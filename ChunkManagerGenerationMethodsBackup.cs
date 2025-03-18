using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

public partial class ChunkManager : Node3D
{
    #region greedy mesh
    public static void LODGreedyChunkMesh(Dictionary<int, Dictionary<int, UInt32[]>>[] data, Vector3I chunk_index, int subchunk, ChunkLOD LOD, bool useLODBlockType)
    {
        // we expect blocks generated before attempting to mesh
        if (!BLOCKCACHE.TryGetValue(chunk_index, out var chunk_blocks)) {
            GD.Print($"Couldn't find chunk index {chunk_index} in block cache");
            return;
        }

        var _lod = (int)LOD;
        var _lod_chunk_size = CHUNK_SIZE;// Mathf.CeilToInt(CHUNK_SIZE / (float)_lod);
        var _lod_csp = _lod_chunk_size+2;
        var _lod_csp2 = _lod_csp*_lod_csp;
        var _lod_csp3 = _lod_csp*_lod_csp2;

        var axis_cols = new UInt32[_lod_csp3 * 3];
        var col_face_masks = new UInt32[_lod_csp3 * 6];
        var slope_blocks = new Dictionary<int, UInt32[]>();

        // generate binary 0 1 voxel representation for each axis
        // central chunk loop
        int dx, dy, dz;
        dx = dy = dz = 0;
        var delta = new Vector3I(dx, dy, dz);
        Vector3I prev_delta;
        var targ_chunk = chunk_blocks;

        if (true)
        {
            for (int x = 0; x < CSP; x++)
            {
                for (int y = 0; y < CSP; y++)
                {
                    for (int z = 0; z < CSP; z++)
                    {
                        // read padded blocks from neighboring chunks
                        prev_delta = delta;
                        dx = x > CHUNK_SIZE ? 1 : x < 1 ? -1 : 0;
                        dy = y > CHUNK_SIZE ? 1 : y < 1 ? -1 : 0;
                        dz = z > CHUNK_SIZE ? 1 : z < 1 ? -1 : 0;
                        delta = new Vector3I(dx, dy, dz);

                        if (prev_delta != delta)
                        {
                            if (!BLOCKCACHE.TryGetValue(chunk_index + delta, out var new_chunk))
                            {
                                new_chunk = chunk_blocks;
                            }
                            targ_chunk = new_chunk;
                        }

                        var islocal = targ_chunk == chunk_blocks;

                        var block_pos = new Vector3I(x, y, z) - (islocal ? Vector3I.Zero : delta * _lod_chunk_size);
                        var idx = BlockIndex(block_pos);
                        idx += subchunk * _lod_csp3; // move up one subchunk
                        var blockinfo = targ_chunk[idx];

                        // HACK set blockinfo to zero to prevent sloped air blocks bug
                        if (IsBlockEmpty(blockinfo)) blockinfo = 0;
                        chunk_blocks[BlockIndex(new Vector3I(x, y, z))] = blockinfo;

                        if (LOD == ChunkLOD.NoLOD && IsBlockSloped(blockinfo))
                        {
                            if (dx != 0 || dy != 0 || dz != 0) continue;  // dont add sloped blocks if we are in padded space, this causes overlap in world space
                            // add sloped blocks and IDs to a separate list
                            idx = BlockIndex(new Vector3I(x, y, z));
                            if (!slope_blocks.TryGetValue(idx, out _))
                            {
                                slope_blocks.Add(idx, [(uint)blockinfo]);
                            }
                        }
                        else if (!IsBlockEmpty(blockinfo))
                        { // if block is solid
                            axis_cols[x + z * _lod_csp] |= (UInt32)1 << y;           // y axis defined by x,z
                            axis_cols[z + y * _lod_csp + _lod_csp2] |= (UInt32)1 << x;    // x axis defined by z,y
                            axis_cols[x + y * _lod_csp + _lod_csp2 * 2] |= (UInt32)1 << z;  // z axis defined by x,y
                        }
                    }
                }
            }
        }
        /*
        else
        {
            for (int x = 0; x < _lod_chunk_size; x++)
            {
                for (int y = 0; y < _lod_chunk_size; y++)
                {
                    for (int z = 0; z < _lod_chunk_size; z++)
                    {
                        var block_pos = new Vector3I(x*_lod, y*_lod, z*_lod)+Vector3I.One;
                        var idx = BlockIndex(block_pos);
                        var blockinfo = chunk_blocks[idx];
                        if (!IsBlockEmpty(blockinfo))
                        { // if block is solid
                            axis_cols[x + z * _lod_csp] |= (UInt32)1 << y;           // y axis defined by x,z
                            axis_cols[z + y * _lod_csp + _lod_csp2] |= (UInt32)1 << x;    // x axis defined by z,y
                            axis_cols[x + y * _lod_csp + _lod_csp2 * 2] |= (UInt32)1 << z;  // z axis defined by x,y
                        }
                    }
                }
            }            
        }
        */

        // add slope blocks to entry zero of the extra "axis"
        // data 1-5 are the cube axes, 6 is the sloped blocks 
        data[6].Add(0, slope_blocks);

        // do face culling for each axis
        for (int axis = 0; axis < 3; axis++)
        {
            for (int i = 0; i < _lod_csp2; i++)
            {
                var col = axis_cols[i + axis * _lod_csp2];
                // sample descending axis and set true when air meets solid
                col_face_masks[_lod_csp2 * axis * 2 + i] = col & ~(col << 1);
                // sample ascending axis and set true when air meets solid
                col_face_masks[_lod_csp2 * (axis * 2 + 1) + i] = col & ~(col >> 1);
            }
        }

        // put the data into the hash maps
        for (int axis = 0; axis < 6; axis++)
        {
            // i and j are coords in the binary plane for the given axis
            // i is column, j is row
            for (int j = 0; j < _lod_chunk_size; j++)
            {
                for (int i = 0; i < _lod_chunk_size; i++)
                {
                    // get column index for col_face_masks
                    // add 1 to i and j because we are skipping the first row and column due to padding
                    var col_idx = (i + 1) + ((j + 1) * _lod_csp) + (axis * _lod_csp2);

                    // removes rightmost and leftmost padded bit (it's outside the chunk)
                    var col = col_face_masks[col_idx] >> 1;
                    col &= ~((UInt32)1 << _lod_chunk_size);

                    // now get y coord of faces (it's their bit location in the UInt64, so trailing zeroes can find it)
                    while (col != 0)
                    {
                        var k = BitOperations.TrailingZeroCount(col);
                        // clear least significant (rightmost) set bit
                        col &= col - 1;

                        var voxel_pos = axis switch
                        {
                            0 or 1 => new Vector3I(i, k, j),  // down, up    (xz -> y axis)
                            2 or 3 => new Vector3I(k, j, i),  // right, left (zy -> x axis)
                            _ => new Vector3I(i, j, k),       // back, front (xy -> z axis)
                        };
                        var blockinfo = chunk_blocks[BlockIndex(voxel_pos + Vector3I.One)];

                        // all LOD blocks have damage of zero to better increase greedy meshing
                        // as LOD increases block types get simplified
                        if (LOD == ChunkLOD.NoLOD && IsBlockDamaged(blockinfo))
                        {
                            // make all damage amounts 0 (preserve damage type)
                            var typeflag = GetBlockDamageTypeFlag(blockinfo);
                            blockinfo = RepackDamageData(blockinfo, typeflag, 0);
                        }
                        else if (LOD == ChunkLOD.Half || LOD == ChunkLOD.Eighth)
                        {
                            var blocktype = GetBlockID(blockinfo);
                            blockinfo = PackAllBlockInfo(blocktype,0,0,0,0,0);
                        }
                        else if (LOD == ChunkLOD.Sixteenth)
                        {
                            var blocktype = BlockManager.BlockID("LODGrass");
                            blockinfo = PackAllBlockInfo(blocktype,0,0,0,0,0);
                        }

                        if (useLODBlockType && LOD != ChunkLOD.Sixteenth)
                        {
                            var species = GetBlockSpecies(blockinfo);
                            var new_block_type  = species switch {
                                BlockSpecies.Dirt => BlockManager.BlockID("LODDirt"),
                                BlockSpecies.Stone => BlockManager.BlockID("LODStone"),
                                BlockSpecies.Leaves => BlockManager.BlockID("LODLeaves"),
                                _ => BlockManager.BlockID("LODGrass")
                            };
                            blockinfo = PackAllBlockInfo(new_block_type,0,0,0,0,0);
                        }

                        if (!data[axis].TryGetValue(blockinfo, out Dictionary<int, UInt32[]> planeSet))
                        {
                            planeSet = new();
                            data[axis].Add(blockinfo, planeSet);
                        }

                        var k_ymod = k + CHUNK_SIZE * subchunk;
                        if (!planeSet.TryGetValue(k_ymod, out UInt32[] data_entry))
                        {
                            data_entry = new UInt32[_lod_chunk_size];
                            planeSet.Add(k_ymod, data_entry);
                        }
                        data_entry[j] |= (UInt32)1 << i;     // push the "row" bit into the "column" UInt32
                        planeSet[k_ymod] = data_entry;
                    }
                }
            }
        }
    }
    #endregion

    #region build mesh
    public static Mesh LODBuildChunkMesh(Vector3I chunk_index, ChunkLOD LOD, bool chunkBlockLOD)
    {
        static int get_surface_tool_index(int blockinfo, int axis)
        {
            var blockId = GetBlockID(blockinfo);
            if (blockId == BlockManager.LavaBlockId)
            {
                return ChunkMeshData.LAVA_SURFACE;
            }
            else if (blockId == BlockManager.BlockID("GoldOre"))
            {
                return ChunkMeshData.GOLD_SURFACE;
            }
            else if (axis == 1 && !IsBlockDamaged(blockinfo) && blockId == BlockManager.BlockID("Grass"))
            {
                return ChunkMeshData.GRASS_SURFACE;
            }
            else
            {
                return ChunkMeshData.CHUNK_SURFACE;
            }
        }

        // data is an array of dictionaries, one for each axis
        // each dictionary is a hash map of block types to a set binary planes
        // we need to group by block type like this so we can batch the meshing and texture blocks correctly
        Dictionary<int, Dictionary<int, UInt32[]>>[] data = new Dictionary<int, Dictionary<int, UInt32[]>>[7];
        short i;
        for (i = 0; i < 6; i++) data[i] = []; // initialize the hash maps for each axis value
        data[i] = []; // an extra one for sloped blocks

        // add all SUBCHUNKS
        for (i = 0; i < 1; i++) LODGreedyChunkMesh(data, chunk_index, i, LOD, chunkBlockLOD);

        // construct mesh
        var surfToolArray = new SurfaceTool[ChunkMeshData.ALL_SURFACES];
        for (var s = 0; s < ChunkMeshData.ALL_SURFACES; s++)
        {
            surfToolArray[s] = new();
            surfToolArray[s].Begin(Mesh.PrimitiveType.Triangles);
        }

        for (int axis = 0; axis < 6; axis++)
        {
            foreach (var (blockinfo, planeSet) in data[axis])
            {
                foreach (var (k_chunked, binary_plane) in planeSet)
                {
                    var blockId = GetBlockID(blockinfo);

                    // sloped blocks are not greedy meshed
                    var greedy_quads = LODGreedyMeshBinaryPlane(binary_plane, LOD);

                    var k = k_chunked % CHUNK_SIZE;
                    var subchunk = k_chunked / CHUNK_SIZE;

                    foreach (GreedyQuad quad in greedy_quads)
                    {
                        Vector3I quad_offset, quad_delta; // row and col, width and height
                        Godot.Vector2 uv_offset;

                        quad_offset = axis switch
                        {
                            // row, col -> axis
                            0 => new Vector3I(quad.col, k, quad.row), // down, up    (xz -> y axis)
                            1 => new Vector3I(quad.col, k + 1, quad.row),
                            2 => new Vector3I(k, quad.row, quad.col), // left, right (zy -> x axis)
                            3 => new Vector3I(k + 1, quad.row, quad.col),
                            4 => new Vector3I(quad.col, quad.row, k), // back, front (xy -> z axis)
                            _ => new Vector3I(quad.col, quad.row, k + 1)  // remember -z is forward in godot, we are still in chunk space so we add 1
                        };

                        quad_delta = axis switch
                        {
                            // row, col -> axis
                            0 or 1 => new Vector3I(quad.delta_col, 0, quad.delta_row),  // down, up    (xz -> y axis)
                            2 or 3 => new Vector3I(0, quad.delta_row, quad.delta_col),  // right, left (zy -> x axis)
                            _ => new Vector3I(quad.delta_col, quad.delta_row, 0),       // back, front (xy -> z axis)
                        };

                        uv_offset = axis switch
                        {
                            0 => new Godot.Vector2(quad_delta.X, quad_delta.Z), // down, up    (xz -> y axis)
                            1 => new Godot.Vector2(quad_delta.Z, quad_delta.X), // for some reason y is flipped on the top face???? :( 
                            2 or 3 => new Godot.Vector2(quad_delta.Z, quad_delta.Y), // right, left (zy -> x axis)
                            _ => new Godot.Vector2(quad_delta.X, quad_delta.Y),      // back, front (xy -> z axis)
                        };

                        // offset vertical by the current subchunk
                        // note that subchunking isnt even implemented because it turned out slower than just multithreading everything
                        // so SUBCHUNKS should be fixed at 1 and this always adds 0
                        quad_offset += Vector3I.Up * subchunk * CHUNK_SIZE;

                        // construct vertices and normals for mesh
                        Godot.Vector3[] verts = new Godot.Vector3[4];
                        for (i = 0; i < 4; i++)
                        {
                            verts[i] = quad_offset + (Godot.Vector3)CUBE_VERTS[CUBE_AXIS[axis, i]] * quad_delta;
                        }

                        Godot.Vector3[] triangle1 = { verts[0], verts[1], verts[2] };
                        Godot.Vector3[] triangle2 = { verts[0], verts[2], verts[3] };
                        Godot.Vector3 normal = axis switch
                        {
                            0 => Godot.Vector3.Down, // -y
                            1 => Godot.Vector3.Up,   // +y
                            2 => Godot.Vector3.Left, // -x
                            3 => Godot.Vector3.Right, // +x
                            4 => Godot.Vector3.Forward, // -z is forward in godot
                            _ => Godot.Vector3.Back     // +z
                        };
                        Godot.Vector3[] normals = { normal, normal, normal };


                        var uvA = Godot.Vector2.Zero;
                        var uvB = new Godot.Vector2(0, 1);
                        var uvC = Godot.Vector2.One;
                        var uvD = new Godot.Vector2(1, 0);
                        var uvTriangle1 = new Godot.Vector2[] { uvA, uvB, uvC };
                        var uvTriangle2 = new Godot.Vector2[] { uvA, uvC, uvD };

                        var surfidx = get_surface_tool_index(blockinfo, axis);
                        if (surfidx == ChunkMeshData.LAVA_SURFACE)
                        {
                            // lava surface has no metadata
                            surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, normals: normals);
                            surfToolArray[surfidx].AddTriangleFan(triangle2, uvTriangle2, normals: normals);
                        }
                        else
                        {
                            var blockDamage = GetBlockDamageData(blockinfo);
                            var block_face_texture_idx = BlockManager.BlockTextureArrayPositions(blockId)[axis];
                            var notacolour = new Color(block_face_texture_idx, uv_offset.X, uv_offset.Y, blockDamage) * (1 / 255f);
                            var metadata = new Color[] { notacolour, notacolour, notacolour };
                            surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
                            surfToolArray[surfidx].AddTriangleFan(triangle2, uvTriangle2, colors: metadata, normals: normals);
                        }
                    }
                }
            }
        }

        // sloped blocks are not greedy meshed, but constucted seperately
        // their data is stored in the 7th dictionary
        if (data[6].Count > 0) foreach (var (block_idx, blockdata) in data[6][0])
        {
            var blockinfo = (int)blockdata[0];
            var blockId = GetBlockID(blockinfo);
            var slopeType = GetBlockSlopeType(blockinfo);
            var flipSlope = GetBlockSlopeFlip(blockinfo);

            // two types of slope, regular slope (id:1) or angled (7 face) corner slope (id:2)
            // all blocks in this set are sloped so it's either going to be 1 or 2
            var regularSlope = slopeType == (int)SlopeType.Side;
            var cornerSlope = slopeType == (int)SlopeType.Corner;
            var invCornerSlope = slopeType == (int)SlopeType.InvCorner;
            float rotation_angle = GetBlockSlopeRotation(blockinfo);
            //rotation_angle += Mathf.Pi/2;
            while (rotation_angle > Mathf.Pi * 2) rotation_angle -= Mathf.Pi * 2;
            // DEBUG no flip slope
            //if (flipSlope && !regularSlope) rotation_degrees -= 90f;
            /*
                var x = chunk_idx % CHUNK_SIZE;
                var z = (chunk_idx / CHUNK_SIZE) % CHUNK_SIZE;
                var y = chunk_idx / CHUNKSQ;*/
            Vector3I pos = BlockIndexToVector(block_idx);//new(x,y,z);//BlockIndexToVector(chunk_idx);
            pos -= Vector3I.One; // remove padding

            for (int axis = 0; axis < 6; axis++)
            {
                // regular slope - skip front face because it's a ramp
                if (regularSlope && axis == 4) continue;

                //pos += quad_offset;

                var blockDamage = GetBlockDamageData(blockinfo);
                var block_face_texture_idx = BlockManager.BlockTextureArrayPositions(blockId)[axis];
                var notacolour = new Color(block_face_texture_idx, 1.0f, 1.0f, blockDamage) * (1 / 255f);
                var metadata = new Color[] { notacolour, notacolour, notacolour };

                Godot.Vector3[] verts = new Godot.Vector3[4];

                for (i = 0; i < 4; i++)
                {
                    // get local vertex coords
                    verts[i] = (Godot.Vector3)CUBE_VERTS[CUBE_AXIS[axis, i]] - Godot.Vector3.One * 0.5f;

                    // shift down top face into a slope, for regular slope
                    if (regularSlope && axis == 1 && (i == 0 || i == 1)) verts[i] -= Godot.Vector3.Up;
                    if (cornerSlope && axis == 1 && (i == 0 || i == 1 || i == 2)) verts[i] -= Godot.Vector3.Up; // else shift corner down by 1 for corner slopes
                    if (invCornerSlope && axis == 1 && i == 1) verts[i] -= Godot.Vector3.Up; // else shift corner down by 1


                    verts[i] = verts[i].Rotated(Godot.Vector3.Up, rotation_angle);
                    if (flipSlope) verts[i] = verts[i].Rotated(Godot.Vector3.Forward, Mathf.Pi);
                    verts[i] += (Godot.Vector3)pos + Godot.Vector3.One * 0.5f;
                }

                Godot.Vector3[] triangle1 = { verts[0], verts[1], verts[2] };
                Godot.Vector3[] triangle2 = { verts[0], verts[2], verts[3] };
                Godot.Vector3 normal = axis switch
                {
                    0 => Godot.Vector3.Down,    // -y
                    1 => Godot.Vector3.Up,      // +y
                    2 => Godot.Vector3.Left,    // -x
                    3 => Godot.Vector3.Right,   // +x
                    4 => Godot.Vector3.Forward, // -z is forward in godot
                    _ => Godot.Vector3.Back     // +z
                };
                if (flipSlope) normal = normal.Rotated(Godot.Vector3.Forward, Mathf.Pi);
                normal = normal.Rotated(Godot.Vector3.Up, rotation_angle);

                Godot.Vector3[] normals = { normal, normal, normal };

                var uvA = Godot.Vector2.Zero;
                var uvB = new Godot.Vector2(0, 1);
                var uvC = Godot.Vector2.One;
                var uvD = new Godot.Vector2(1, 0);
                var uvTriangle1 = new Godot.Vector2[] { uvA, uvB, uvC };
                var uvTriangle2 = new Godot.Vector2[] { uvA, uvC, uvD };

                var surfidx = get_surface_tool_index(blockinfo, axis);
                switch (axis)
                {
                    case 1: // top face - modify normals
                        if (invCornerSlope) surfToolArray[surfidx].AddTriangleFan(triangle2, uvTriangle2, colors: metadata, normals: normals);

                        var normrotate = SlopedNormalNegZ;
                        if (cornerSlope || invCornerSlope) normrotate = SlopedCornerNormalNegZ;
                        if (flipSlope) normrotate = normrotate.Rotated(Godot.Vector3.Forward, Mathf.Pi);
                        normrotate = normrotate.Rotated(Godot.Vector3.Up, rotation_angle);
                        normals = new Godot.Vector3[] { normrotate, normrotate, normrotate };
                        if (regularSlope || invCornerSlope) surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);

                        if (!invCornerSlope) surfToolArray[surfidx].AddTriangleFan(triangle2, uvTriangle2, colors: metadata, normals: normals);
                        break;
                    case 2: // side face, only add one of the triangles
                        if (regularSlope || cornerSlope) surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
                        else if (invCornerSlope)
                        {
                            surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
                            surfToolArray[surfidx].AddTriangleFan(triangle2, uvTriangle2, colors: metadata, normals: normals);
                        }
                        break;
                    case 3: // obverse side face, only add one of the triangles and adjust its vertices accordingly
                        triangle1 = new Godot.Vector3[] { verts[1], verts[2], verts[3] };

                        //if (invCornerSlope) uvTriangle1 = new Godot.Vector2[] { uvC, uvB, uvA };
                        if (regularSlope || invCornerSlope)
                        {
                            uvTriangle1 = new Godot.Vector2[] { uvC, uvB, uvA };
                            surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
                        }
                        break;
                    case 4: // facing -z, front, corner slopes only add one triangle, else normal
                        if (regularSlope)
                        {
                            surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
                            surfToolArray[surfidx].AddTriangleFan(triangle2, uvTriangle2, colors: metadata, normals: normals);
                        }
                        else if (invCornerSlope) surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
                        break;
                    case 5:
                        if (regularSlope || invCornerSlope)
                        {
                            surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
                            surfToolArray[surfidx].AddTriangleFan(triangle2, uvTriangle2, colors: metadata, normals: normals);
                        }
                        if (cornerSlope)
                        {
                            triangle1 = new Godot.Vector3[] { verts[1], verts[2], verts[3] };
                            uvTriangle1 = new Godot.Vector2[] { uvC, uvB, uvA };
                            surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
                        }
                        break;
                    default: // bottom face is always drawn, corner slopes only have 1 triangle
                        if (cornerSlope)
                        {
                            surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
                        }
                        else
                        {
                            surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
                            surfToolArray[surfidx].AddTriangleFan(triangle2, uvTriangle2, colors: metadata, normals: normals);
                        }
                        break;
                }
            }
        }

        // index grass surface
        surfToolArray[ChunkMeshData.GRASS_SURFACE].Index();
        surfToolArray[ChunkMeshData.CHUNK_SURFACE].Index();
        surfToolArray[ChunkMeshData.LAVA_SURFACE].Index();
        surfToolArray[ChunkMeshData.GOLD_SURFACE].Index();
        var surfaces = new ArrayMesh[ChunkMeshData.ALL_SURFACES];
        surfaces[ChunkMeshData.CHUNK_SURFACE] = surfToolArray[ChunkMeshData.CHUNK_SURFACE].Commit();
        surfaces[ChunkMeshData.LAVA_SURFACE] = surfToolArray[ChunkMeshData.LAVA_SURFACE].Commit();
        surfaces[ChunkMeshData.GRASS_SURFACE] = surfToolArray[ChunkMeshData.GRASS_SURFACE].Commit();
        surfaces[ChunkMeshData.GOLD_SURFACE] = surfToolArray[ChunkMeshData.GOLD_SURFACE].Commit();

        // TODO set mesh LOD collision shapes
        var noCollisions = true;//new Vector3I(chunk_index.X,0,chunk_index.Z).DistanceSquaredTo(new Vector3I(playerChunkX, 0, playerChunkZ)) > (8*8);
        var chunkMesh = new ChunkMeshData(surfaces, noCollisions);
        return chunkMesh.GetUnifiedSurfaces();
    }
    #endregion

    #region binary plane
    private static List<GreedyQuad> LODGreedyMeshBinaryPlane(UInt32[] data, ChunkLOD LOD)
    { // modify this so chunks are 30 and padded 1 on each side to 32        
        var _lod = (int)LOD;
        var _lod_chunk_size = CHUNK_SIZE;// Mathf.CeilToInt(CHUNK_SIZE / (float)_lod);
       
        List<GreedyQuad> greedy_quads = new();
        int data_length = data.Length;
        for (int j = 0; j < data_length; j++)
        { // j selects a row from the data[j]
            var i = 0; // i  traverses the bits in current row j
            while (i < _lod_chunk_size)
            {
                i += BitOperations.TrailingZeroCount(data[j] >> i);
                if (i >= _lod_chunk_size) continue;
                var h = BitOperations.TrailingZeroCount(~(data[j] >> i)); // count trailing ones from i upwards
                UInt32 h_as_mask = 0; // create a mask of h bits
                for (int xx = 0; xx < h; xx++) h_as_mask |= (UInt32)1 << xx;
                var mask = h_as_mask << i; // a mask of h bits starting at i
                var w = 1;
                while (j + w < data_length)
                {
                    var next_row_h = (data[j + w] >> i) & h_as_mask; // check next row across
                    if (next_row_h != h_as_mask) break; // if we can't expand aross the row, break
                    data[j + w] &= ~mask;  // if we can, we clear bits from next row so they won't be processed again
                    w++;
                }
                greedy_quads.Add(new GreedyQuad { row = j, col = i, delta_row = w, delta_col = h });
                i += h; // jump past the ones to check if there are any more in this column
            }
        }
        return greedy_quads;
    }
    #endregion

    private static bool IsBlockSloped(int blockinfo) => false;

    private static bool IsBlockDamaged(int blockinfo) => false;

    public static bool IsBlockEmpty(int blockInfo) {
        return GetBlockID(blockInfo) == 0;
    }

    private static int GetBlockID(int blockinfo)
    {
        return blockinfo & 0xffff;
    }

    public static int BlockIndex(Vector3I blockPaddedPosition) {
        return blockPaddedPosition.X + blockPaddedPosition.Z*CSP + blockPaddedPosition.Y*CSP2;
    }

	/// <summary>
	/// Converts a block index to a block coordinate in a chunk.
	/// </summary>
	/// <param name="blockIndex"></param>
	/// <returns>A Vector3I coordinate repsenting a given block in the chunk at the provided index.</returns>
	public static Vector3I BlockIndexToVector(int blockIndex) {
		return new Vector3I(blockIndex%CSP,blockIndex/CSP2,blockIndex/CSP%CSP);
	}

    public static int PackAllBlockInfo(int blockType, int damageType, int damageAmount, int slopeType, int slopeRotation, int blockflip) {
        return blockType;
    }

    public static BlockSpecies GetBlockSpecies(int blockinfo) {
        return BlockManager.Instance.Blocks[GetBlockID(blockinfo)].Species;
    }

    public static int GetBlockDamageData(int blockInfo) {
        return (blockInfo>>BLOCK_DAMAGE_BITS_OFFSET) & 0xff;
    }


    public static int GetBlockDamageInteger(int blockInfo) {
        return GetBlockDamageData(blockInfo)&0x1f;
    }

    public static int GetBlockDamageTypeFlag(int blockInfo) {
        return GetBlockDamageData(blockInfo) >> 5;
    }

    public static int GetBlockSlopeData(int blockInfo) {
        return (blockInfo >> BLOCK_SLOPE_BITS_OFFSET)&0xff;
    }

    public static int GetBlockSlopeType(int blockInfo) {
        return GetBlockSlopeData(blockInfo) & 0b11;
    }

    public static float GetBlockSlopeRotation(int blockInfo) {
        return GetBlockSlopeRotationBits(blockInfo)*Mathf.Pi/2;
    }

	public static int GetBlockSlopeRotationBits(int blockInfo) {
		return (GetBlockSlopeData(blockInfo) >> 2) & 0b11;
	}

	public static bool GetBlockSlopeFlip(int blockinfo) {
		return ((GetBlockSlopeData(blockinfo) >> 4)&1) == 1;
	}

    public static int RepackDamageData(int blockInfo, int damageTypeFlag, int damageAmount) {
        return RepackDamageData(blockInfo,PackDamageData(damageTypeFlag, damageAmount));
    }

	public static int RepackDamageData(int blockinfo, int packedDamageData) {
		return (blockinfo & ~(0xff<<BLOCK_DAMAGE_BITS_OFFSET)) | (packedDamageData<<BLOCK_DAMAGE_BITS_OFFSET);
	}

    public static int RepackSlopeData(int blockInfo, int slopeType, int slopeRotation, int slopeflip) {
        return RepackSlopeData(blockInfo,PackSlopeData(slopeType, slopeRotation, slopeflip));
    }

	public static int RepackSlopeData(int blockinfo, int packedSlopeData) {
		return (blockinfo & ~(0xff<<BLOCK_SLOPE_BITS_OFFSET)) | (packedSlopeData<<BLOCK_SLOPE_BITS_OFFSET);
	}

    public static int PackDamageData(int damageTypeFlag, int damageAmount) {
        return (damageTypeFlag<<5) | damageAmount;
    }

    public static int PackSlopeData(int slopeType, int slopeRotation, int slopeFlip) {
        return slopeType | slopeRotation << 2 | slopeFlip << 4;
    }    
}