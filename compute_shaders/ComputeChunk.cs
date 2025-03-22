using Godot;
using Godot.NativeInterop;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

public class MeshArrayDataPacket
{
    public Vector3[] Vertices = [];
    public Vector3[] Normals = [];
    public Vector2[] UVs = [];
    public MeshArrayDataPacket(){}
}

public static class ComputeChunk
{
    // struct to hold the parameters for the compute shader
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ChunkParamsStruct
    {
        public System.Numerics.Vector4 SeedOffset;
        public int CSP;
        public int CSP3;
        public int NumChunksToCompute;
        public int MaxWorldHeight;
        public int StoneBlockID;
        public int OceanHeight;
        public int NoiseLayerCount;
        public int NoiseSeed;
        public float NoiseScale;
        public float CaveNoiseScale;
        public float CaveThreshold;
        public bool GenerateCaves;
        public bool ForceFloor;
        private readonly int _padding1;
        private readonly int _padding2;
        private readonly int _padding3;
        
        // THIS FIELD IS ADDED SEPARATELY WHEN PARAMETERS ARE FILLED
        // it is a variable sized array, and cannot be blittable
        // avoid non-blittalbe type in struct, instead we append this to the buffer in GenerateParameterBufferBytes 
        // public System.Numerics.Vector3[] ChunkPositions;

        public ChunkParamsStruct(ChunkGenParamsResource c)
        {
            SeedOffset = new System.Numerics.Vector4(c.SeedOffset.X, c.SeedOffset.Y, c.SeedOffset.Z, 0.0f);
            CSP = c.CSP;
            CSP3 = c.CSP3;
            NumChunksToCompute = c.NumChunksToCompute;
            MaxWorldHeight = c.MaxWorldHeight;
            StoneBlockID = c.StoneBlockID;
            OceanHeight = c.OceanHeight;
            NoiseLayerCount = c.NoiseLayerCount;
            NoiseSeed = c.NoiseSeed;
            NoiseScale = c.NoiseScale;
            CaveNoiseScale = c.CaveNoiseScale;
            CaveThreshold = c.CaveThreshold;
            GenerateCaves = c.GenerateCaves;
            ForceFloor = c.ForceFloor;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct MeshParamsStruct
    {
        public int CHUNK_SIZE;
        public int CSP;
        public int CSP3;
        public int MaxVerts;
        public int NumChunksToCompute;
        private readonly int padding1;
        private readonly int padding2;
        private readonly int padding3;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct NoiseLayerStruct
    {
        public float Gain;
        public float Frequency;
        public float Lacunarity;
        public float Persistence;
        public int Octaves;
        public float CaveScale;
        public float CaveThreshold;
        public int SurfaceVoxelId;
        public int SubSurfaceVoxelId;
        private readonly int _padding1;
        private readonly int _padding2;
        private readonly int _padding3;

        public NoiseLayerStruct(){}

        public NoiseLayerStruct(NoiseLayerResource noiseLayer)
        {
            Gain = noiseLayer.Gain;
            Frequency = noiseLayer.Frequency;
            Lacunarity = noiseLayer.Lacunarity;
            Persistence = noiseLayer.Persistence;
            Octaves = noiseLayer.Octaves;
            CaveScale = noiseLayer.CaveScale;
            CaveThreshold = noiseLayer.CaveThreshold;
            SurfaceVoxelId = noiseLayer.SurfaceVoxelId;
            SubSurfaceVoxelId = noiseLayer.SubSurfaceVoxelId;
        }
    }



    private class MeshCallables(Dictionary<Vector3I, MeshArrayDataPacket> meshdict)
    {
        private static readonly int vec3_size = Marshal.SizeOf<Vector3>();
        private static readonly int vec2_size = Marshal.SizeOf<Vector2>();
        public List<(int,Vector3I)> VertexCountsPerChunk = [];
        public Dictionary<Vector3I, MeshArrayDataPacket> meshDict = meshdict;

        public void ReadbackVertexData(byte[] vertex_readback)
        {
            for (int i=0; i<VertexCountsPerChunk.Count; i++)
            {
                var vert_counts = VertexCountsPerChunk[i].Item1;
                if (vert_counts == 0) continue; // skip empty chunks
                var chunk_position = VertexCountsPerChunk[i].Item2;

                var verts_array = MemoryMarshal.Cast<byte, Vector3>(vertex_readback.AsSpan(i*MAX_VERTS_PER_CHUNK*vec3_size, vert_counts*vec3_size)).ToArray();
                
                if (meshDict.TryGetValue(chunk_position, out var packet))
                {
                    packet.Vertices = verts_array;
                }
                else
                {
                    meshDict.TryAdd(chunk_position, new MeshArrayDataPacket { Vertices = verts_array });
                }
            }
        }

        public void ReadbackNormalsData(byte[] normals_readback)
        {
            for (int i=0; i<VertexCountsPerChunk.Count; i++)
            {
                var vert_counts = VertexCountsPerChunk[i].Item1;
                if (vert_counts == 0) continue; // skip empty chunks
                var chunk_position = VertexCountsPerChunk[i].Item2;

                var norms_array = MemoryMarshal.Cast<byte, Vector3>(normals_readback.AsSpan(i*MAX_VERTS_PER_CHUNK*vec3_size, vert_counts*vec3_size)).ToArray();
                if (meshDict.TryGetValue(chunk_position, out var packet))
                {
                    packet.Normals = norms_array;
                }
                else
                {
                    meshDict.TryAdd(chunk_position, new MeshArrayDataPacket { Normals = norms_array });
                }
            }
        }

        public void ReadbackUVData(byte[] uvs_readback)
        {
            for (int i=0; i<VertexCountsPerChunk.Count; i++)
            {
                var vert_counts = VertexCountsPerChunk[i].Item1;
                if (vert_counts == 0) continue; // skip empty chunks
                var chunk_position = VertexCountsPerChunk[i].Item2;
                var uvs_array = MemoryMarshal.Cast<byte, Vector2>(uvs_readback.AsSpan(i*MAX_VERTS_PER_CHUNK*vec2_size, vert_counts*vec2_size)).ToArray();
                if (meshDict.TryGetValue(chunk_position, out var packet))
                {
                    packet.UVs = uvs_array;
                }
                else
                {
                    meshDict.TryAdd(chunk_position, new MeshArrayDataPacket { UVs = uvs_array });
                }
            }
        }
    }

    private static readonly RDShaderFile _shader_file = ResourceLoader.Load<RDShaderFile>("res://compute_shaders/chunkgen.glsl");
    private static readonly RDShaderFile _mesh_shader_file = ResourceLoader.Load<RDShaderFile>("res://compute_shaders/meshing/compute_meshing.glsl");

    public static readonly RenderingDevice LocalRenderingDevice = RenderingServer.CreateLocalRenderingDevice();

    public static int MaxWorldHeight = 250;
    public static float CaveNoiseScale = 550.0f;
    public static float CaveThreshold = 0.75f;
    public static int OceanHeight = 64;
    public static bool GenerateCaves = true;
    public static bool ForceFloor = true;
    const int MAX_VERTS_PER_CHUNK = 6*6*ChunkManager.CHUNK_SIZE*ChunkManager.CHUNK_SIZE*ChunkManager.CHUNK_SIZE/2;

    /*
            Gain = 0.5f,
            Frequency = 0.01f,
            Lacunarity = 2.0f,
            Persistence = 0.5f,
            Octaves = 5,
            CaveScale = 0.5f,
            CaveThreshold = 0.8f,
            SurfaceVoxelId = BlockManager.GrassBlockId,
            SubSurfaceVoxelId = BlockManager.DirtBlockId
    */
    /*
            Gain = 0.7f,
            Frequency = 700f,
            Lacunarity = 3.3f,
            Persistence = 0.27f,
            Octaves = 5,
            CaveScale = 550f,
            CaveThreshold = 0.75f,
            SurfaceVoxelId = BlockManager.GrassBlockId,
            SubSurfaceVoxelId = BlockManager.DirtBlockId
    */

    private static NoiseLayerStruct[] _noise_layers = [
        new NoiseLayerStruct
        {
            Gain = 0.7f,
            Frequency = 700f,
            Lacunarity = 3.3f,
            Persistence = 0.27f,
            Octaves = 5,
            CaveScale = 25f,
            CaveThreshold = 0.0f,
            SurfaceVoxelId = BlockManager.GrassBlockId,
            SubSurfaceVoxelId = BlockManager.DirtBlockId
        },
        new NoiseLayerStruct
        {
            Gain = 0.002f,
            Frequency = 0.005f,
            Lacunarity = 3.0f,
            Persistence = 0.5f,
            Octaves = 4,
            CaveScale = 0.5f,
            CaveThreshold = 0.8f,
            SurfaceVoxelId = BlockManager.GrassBlockId,
            SubSurfaceVoxelId = BlockManager.DirtBlockId
        }
    ];

    public static void SetNoiseLayer(int index,NoiseLayerResource noiseLayer)
    {
        _noise_layers[index] = new NoiseLayerStruct(noiseLayer);
    }

    private static ChunkParamsStruct input_params = new() 
    {
        SeedOffset = new(0.0f,0.0f,0.0f,0.0f),
        CSP = ChunkManager.CSP,
        CSP3 = ChunkManager.CSP3,
        NumChunksToCompute = 1,
        MaxWorldHeight = ChunkManager.CHUNK_SIZE*ChunkManager.Instance.YRenderDistance,
        StoneBlockID = BlockManager.StoneBlockId,
        OceanHeight = 64,
        NoiseLayerCount = 1,
        NoiseSeed = 0,
        NoiseScale = 1.0f,
        CaveNoiseScale = 550.0f,
        CaveThreshold = 0.8f,
        GenerateCaves = true,
        ForceFloor = true,
    };
    //private static readonly RenderingDevice _rd = RenderingServer.CreateLocalRenderingDevice();

    //public static ConcurrentDictionary<RenderingDevice,  int> _computeResources = new();

    /*
    private static void BufferGetDataCallback(byte[] array, Vector3I chunkPosition, RenderingDevice _rd, Rid[] rids)
    {
        _rd.Sync();
        GD.Print("Generated chunk at: " + chunkPosition);
        //_rd.Sync();
        var data = new int[ChunkManager.CSP3];
        Buffer.BlockCopy(array, 0, data, 0, array.Length);

        ChunkManager.BLOCKCACHE[chunkPosition] = data;
        var tempstring = "";
        for (int i = 0; i < 20; i++)
        {
            tempstring +=  ChunkManager.BLOCKCACHE[chunkPosition][i] + " ";
        }
        tempstring += "...";
        GD.Print(tempstring);
        
        RenderingServer.CallOnRenderThread(Callable.From(()=>
        {
            foreach (Rid r in rids)
            {
                _rd.FreeRid(r);
            }
            _rd.Free();            
        }));
    }*/

    public static void GenerateMultiChunks(List<Vector3I> chunksToGenerate, bool mesh_on_gpu = false)
    {
        if (!GodotObject.IsInstanceValid(LocalRenderingDevice)) return;
        var stopwatch = Stopwatch.StartNew();
        var _rd = LocalRenderingDevice;
        var _shader_spir_v = _shader_file.GetSpirV();
        
        var _compute_shader = _rd.ShaderCreateFromSpirV(_shader_spir_v);

        var _num_blocks = ChunkManager.CSP3*chunksToGenerate.Count;

        // due to invocations being 8x8x8, min padded chunk size is 8x8x8, and padded chunk size should always be a multiple of 8
        // a good padded chunk size is 32x32x32, which is divided between 4x4x4 workgroups:
        var workgroups = (uint) ChunkManager.CSP / 8;

        var _voxel_buffer_rid = GenerateIntBuffer(_rd, _num_blocks);
        var _voxel_array_uniform = GenerateUniform(_voxel_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 0);

        var paramsBufferBytes = GenerateParameterBufferBytes(chunksToGenerate);
        var _params_buffer_rid = _rd.StorageBufferCreate((uint)paramsBufferBytes.Length, paramsBufferBytes);
        var _params_uniform = GenerateUniform(_params_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 1);

        var noiseBufferBytes = GenerateNoiseLayerBufferBytes();
        var _noise_buffer_rid = _rd.StorageBufferCreate((uint)noiseBufferBytes.Length, noiseBufferBytes);
        var _noise_uniform = GenerateUniform(_noise_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 2);

        var atomicCounterBytes = new byte[sizeof(uint)];
        var _atomic_counter_rid = _rd.StorageBufferCreate((uint)atomicCounterBytes.Length, atomicCounterBytes);
        var _atomic_counter_uniform = GenerateUniform(_atomic_counter_rid, RenderingDevice.UniformType.StorageBuffer, 3);
        
        var test_buffer_size = 10;
        var test_buffer = new byte[test_buffer_size*sizeof(int)];
        var test_buffer_rid = _rd.StorageBufferCreate((uint)test_buffer.Length, test_buffer);
        var test_buffer_unifom = GenerateUniform(test_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 4);

        var _bindings = new Godot.Collections.Array<RDUniform> { _voxel_array_uniform, _params_uniform, _noise_uniform, _atomic_counter_uniform, test_buffer_unifom };

        var _uniform_set = _rd.UniformSetCreate(_bindings, _compute_shader, 0);

        var _compute_pipeline = _rd.ComputePipelineCreate(_compute_shader);
        var _compute_list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(_compute_list, _compute_pipeline);
        _rd.ComputeListBindUniformSet(_compute_list, _uniform_set, 0);
        _rd.ComputeListDispatch(_compute_list, workgroups * (uint)chunksToGenerate.Count, workgroups, workgroups);
        //_rd.ComputeListAddBarrier(_compute_list);
        _rd.ComputeListEnd();
        _rd.Submit();

        var rids = new Rid[] {_uniform_set, _voxel_buffer_rid, _params_buffer_rid, _noise_buffer_rid, _atomic_counter_rid, _compute_pipeline, _compute_shader, test_buffer_rid};

        _rd.Sync();
        var voxel_array_readback = _rd.BufferGetData(_voxel_buffer_rid);

        bool allZeroes = true;
        var chunk_bytes = Marshal.SizeOf<int>()*ChunkManager.CSP3;
        for (int i=0; i<chunksToGenerate.Count; i++)
        {
            var chunkData = new int[ChunkManager.CSP3];
            MemoryMarshal.Cast<byte, int>(voxel_array_readback.AsSpan(i*chunk_bytes,chunk_bytes)).CopyTo(chunkData);

            var chunkPosition = chunksToGenerate[i];
            ChunkManager.BLOCKCACHE[chunkPosition] = chunkData;
            if (!chunkData.All(x => x == 0)) allZeroes = false;
        }
        if (allZeroes) GD.Print("DATA IS ALL ZEROES");

        stopwatch.Stop();

        GD.Print($"compute shader generating chunks time elapsed: {stopwatch.ElapsedMilliseconds} ms");

        if (mesh_on_gpu)
        {
            // // trying to process any more than 21 chunks at once on the GPU will exceed max buffer sizes (buffers go up to 128MB, 21 chunks of MAX VERTEX each will be 122.472 mb)
            // int chunk_batch_stride = 9;//21;//chunksToGenerate.Count;
            // GD.Print("total chunks to mesh: ", chunksToGenerate.Count);
            // GD.Print("chunk batch size: ", chunk_batch_stride, $" i<({chunksToGenerate.Count/chunk_batch_stride})");

            // for (int i=0; i<Mathf.Ceil(chunksToGenerate.Count/(float)chunk_batch_stride);i++)
            // {
            //     var chunk_batch = chunksToGenerate.Skip(i*chunk_batch_stride).Take(chunk_batch_stride).ToList();
            //     var voxel_batch = voxel_array_readback.AsSpan(i*chunk_batch_stride*chunk_bytes, chunk_batch.Count*chunk_bytes).ToArray();
            //     //GD.Print($"meshing {chunk_batch.Count}, ({i*chunk_batch_stride}-{i*chunk_batch_stride+chunk_batch.Count}) chunks: ", chunk_batch.Select(x => x.ToString()).ToArray().Join(","));
            //     MeshMultiChunks(chunk_batch, voxel_batch);
            // }

            MeshMultiChunks(chunksToGenerate, voxel_array_readback);
        }
        else
        {
            ChunkManager.UpdateMeshCacheData();
        }

        FreeRids(_rd, rids);
        //_rd.Free();
    }

    public async static void MeshMultiChunks(List<Vector3I> chunksToMesh, byte[] voxel_data)
    {
        if (!GodotObject.IsInstanceValid(LocalRenderingDevice)) return;
        var stopwatch = Stopwatch.StartNew();

        var _rd = LocalRenderingDevice;//RenderingServer.CreateLocalRenderingDevice();
        var _shader_spir_v = _mesh_shader_file.GetSpirV();
        var _compute_shader = _rd.ShaderCreateFromSpirV(_shader_spir_v);

        // due to meshed chunk size being 30x30x30, invocations being 6x6x6, workgroups are 30/6 = 5
        var workgroups = (uint) ChunkManager.CHUNK_SIZE / 6;

        var paramsBufferBytes = GenerateMeshParameterBufferBytes(chunksToMesh);
        var _params_buffer_rid = _rd.StorageBufferCreate((uint)paramsBufferBytes.Length, paramsBufferBytes);
        var _params_uniform = GenerateUniform(_params_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 1);

        var texture_array_coords_buffer = MemoryMarshal.AsBytes(BlockManager.AllBlocksTextureArrayPositions.AsSpan()).ToArray();
        var texture_array_coords_buffer_rid = _rd.StorageBufferCreate((uint)texture_array_coords_buffer.Length, texture_array_coords_buffer);
        var texture_array_coords_uniform = GenerateUniform(texture_array_coords_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 2);
        
        var vec3_size = Marshal.SizeOf<Vector3>();
        var vec2_size = Marshal.SizeOf<Vector2>();

        // keep track of RIDs in the order they were added, so we can free them later
        var _compute_rids_to_free = new List<Rid>(){_params_buffer_rid, texture_array_coords_buffer_rid};
        
        // stores rids for each BATCH of processed chunks, so we can read back the vertex data
        List<(Rid,Rid,Rid,Rid,List<Vector3I>)> chunk_batch_list = [];

        // trying to process any more than 21 chunks at once on the GPU will exceed max buffer sizes (buffers go up to 128MB, 21 chunks with MAX VERTEX each will be 122.472 mb)
        int chunk_batch_stride = 9;//chunksToGenerate.Count;
        var bytes_per_chunk = ChunkManager.CSP3*sizeof(int);
        //GD.Print("total chunks to mesh: ", chunksToMesh.Count);
        // GD.Print("chunk batch size: ", chunk_batch_stride, $" i<({chunksToMesh.Count/chunk_batch_stride})");

        var _compute_pipeline = _rd.ComputePipelineCreate(_compute_shader);
        for (int i=0; i<Mathf.Ceil(chunksToMesh.Count/(float)chunk_batch_stride);i++)
        {
            var chunk_batch = chunksToMesh.Skip(i*chunk_batch_stride).Take(chunk_batch_stride).ToList();
            var voxel_batch = voxel_data.AsSpan(i*chunk_batch_stride*bytes_per_chunk, chunk_batch.Count*bytes_per_chunk).ToArray();

            var max_verts = MAX_VERTS_PER_CHUNK * chunk_batch.Count;

            var _voxel_buffer_rid = _rd.StorageBufferCreate((uint)voxel_batch.Length, voxel_batch);
            var _voxel_array_uniform = GenerateUniform(_voxel_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 0);

            var atomicCounterBytes = new byte[chunksToMesh.Count * sizeof(uint)];
            var _atomic_counter_rid = _rd.StorageBufferCreate((uint)atomicCounterBytes.Length, atomicCounterBytes);
            var _atomic_counter_uniform = GenerateUniform(_atomic_counter_rid, RenderingDevice.UniformType.StorageBuffer, 3);

            var vertex_buffer = new byte[max_verts*vec3_size];
            var vertex_buffer_rid = _rd.StorageBufferCreate((uint)vertex_buffer.Length, vertex_buffer);
            var vertex_buffer_uniform = GenerateUniform(vertex_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 4);

            var normals_buffer = new byte[max_verts*vec3_size];
            var normals_buffer_rid = _rd.StorageBufferCreate((uint)normals_buffer.Length, normals_buffer);
            var normals_buffer_uniform = GenerateUniform(normals_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 5);

            var uvs_buffer = new byte[max_verts*vec2_size];
            var uvs_buffer_rid = _rd.StorageBufferCreate((uint)uvs_buffer.Length, uvs_buffer);
            var uvs_buffer_uniform = GenerateUniform(uvs_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 6);

            var _bindings = new Godot.Collections.Array<RDUniform> {
                _voxel_array_uniform, _params_uniform, texture_array_coords_uniform,
                _atomic_counter_uniform, vertex_buffer_uniform, normals_buffer_uniform, uvs_buffer_uniform
            };

            var _uniform_set = _rd.UniformSetCreate(_bindings, _compute_shader, 0);

            //GD.Print($"meshing {chunk_batch.Count}, ({i*chunk_batch_stride}-{i*chunk_batch_stride+chunk_batch.Count}) chunks: ", chunk_batch.Select(x => x.ToString()).ToArray().Join(","));
            
            
            var _compute_list = _rd.ComputeListBegin();
            _rd.ComputeListBindComputePipeline(_compute_list, _compute_pipeline);
            _rd.ComputeListBindUniformSet(_compute_list, _uniform_set, 0);
            _rd.ComputeListDispatch(_compute_list, workgroups * (uint)chunk_batch.Count, workgroups, workgroups);
            _rd.ComputeListEnd();
            _compute_rids_to_free.AddRange([_voxel_buffer_rid, _atomic_counter_rid, vertex_buffer_rid, normals_buffer_rid, uvs_buffer_rid]);
            chunk_batch_list.Add((vertex_buffer_rid, normals_buffer_rid, uvs_buffer_rid, _atomic_counter_rid, chunk_batch));
        }
        _compute_rids_to_free.Add(_compute_pipeline);

        _rd.Submit();
        //await ChunkManager.Instance.ToSignal(ChunkManager.Instance.GetTree(), "physics_frame");
        //await ChunkManager.Instance.ToSignal(ChunkManager.Instance.GetTree(), "physics_frame");
        _rd.Sync();

        var meshDict = new Dictionary<Vector3I, MeshArrayDataPacket>();
        
        // read back data for each batch of chunks
        foreach (var (vert_buffer_rid, norm_buffer_rid, uv_buffer_rid, counter_rid, chunk_list) in chunk_batch_list)
        {
            var atomic_counter_readback = _rd.BufferGetData(counter_rid);
            var vert_counts_readback = MemoryMarshal.Cast<byte, int>(atomic_counter_readback.AsSpan()).ToArray();

            var mesh_callable = new MeshCallables(meshDict)
            {
                VertexCountsPerChunk = [.. chunk_list.Select(x => (vert_counts_readback[chunk_list.IndexOf(x)], x))]
            };

            var vert_callable = Callable.From((byte[] data)=> mesh_callable.ReadbackVertexData(data));
            var norm_callable = Callable.From((byte[] data)=> mesh_callable.ReadbackNormalsData(data));
            var uv_callable = Callable.From((byte[] data)=> mesh_callable.ReadbackUVData(data));

            var vert_err = _rd.BufferGetDataAsync(vert_buffer_rid, vert_callable);
            var norm_err =_rd.BufferGetDataAsync(norm_buffer_rid, norm_callable);
            var uvs_err = _rd.BufferGetDataAsync(uv_buffer_rid, uv_callable);
        }

        // do more work to force the async readbacks to finish
        // without this, it does not work...for some reason...
        _rd.BufferGetData(_params_buffer_rid);

        foreach (var (chunk_position, mesh_data_packet) in meshDict)
        {
            if (ChunkManager.MESHCACHE.TryGetValue(chunk_position, out var chunk))
            {
                var meshinstance = chunk.MeshInstance;
                var mesh = (ArrayMesh)meshinstance.Mesh;
                mesh.ClearSurfaces();

                if (mesh_data_packet.Vertices.Length == 0)
                {
                    chunk.CollisionShape.Shape = null;
                    continue;
                }

                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = mesh_data_packet.Vertices;
                arrays[(int)Mesh.ArrayType.Normal] = mesh_data_packet.Normals;
                arrays[(int)Mesh.ArrayType.TexUV] = mesh_data_packet.UVs;

                var xform = new Transform3D(Basis.Identity, (Vector3)chunk_position*ChunkManager.CHUNK_SIZE);
                
                Callable.From(() => {
                    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                    chunk.GlobalTransform = xform;
                }).CallDeferred();
                
            }
            else GD.Print("Chunk not found in MESHCACHE: ", chunk_position);           
        }

        stopwatch.Stop();
        GD.Print($"compute shader building Meshes time elapsed: {stopwatch.ElapsedMilliseconds} ms");

        _compute_rids_to_free.Add(_compute_shader);
        FreeRids(_rd, _compute_rids_to_free);
        //_rd.Free();
    }

    public static void MeshMultiChunks_SINGLE_PIPELINE(List<Vector3I> chunksToMesh, byte[] voxel_data)
    {
        //var stopwatch = Stopwatch.StartNew();

        var _rd = RenderingServer.CreateLocalRenderingDevice();
        var _shader_spir_v = _mesh_shader_file.GetSpirV();
        var _compute_shader = _rd.ShaderCreateFromSpirV(_shader_spir_v);

        // due to meshed chunk size being 30x30x30, invocations being 6x6x6, workgroups are 30/6 = 5
        var workgroups = (uint) ChunkManager.CHUNK_SIZE / 6;

        var _voxel_buffer_rid = _rd.StorageBufferCreate((uint)voxel_data.Length, voxel_data);
        var _voxel_array_uniform = GenerateUniform(_voxel_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 0);
        // GD.Print("voxel_data length: ", voxel_data.Length);
        // GD.Print("voxel_data length/4/CSP3: ", voxel_data.Length/4/ChunkManager.CSP3);
        // GD.Print("chunksToMesh.Count: ", chunksToMesh.Count);
        // GD.Print("voxel_data: [0..10]: ", MemoryMarshal.Cast<byte, int>(voxel_data.AsSpan()).ToArray().Take(10).ToArray().Join(","));

        var paramsBufferBytes = GenerateMeshParameterBufferBytes(chunksToMesh);
        var _params_buffer_rid = _rd.StorageBufferCreate((uint)paramsBufferBytes.Length, paramsBufferBytes);
        var _params_uniform = GenerateUniform(_params_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 1);

        var texture_array_coords_buffer = MemoryMarshal.AsBytes(BlockManager.AllBlocksTextureArrayPositions.AsSpan()).ToArray();
        var texture_array_coords_buffer_rid = _rd.StorageBufferCreate((uint)texture_array_coords_buffer.Length, texture_array_coords_buffer);
        var texture_array_coords_uniform = GenerateUniform(texture_array_coords_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 2);

        var atomicCounterBytes = new byte[chunksToMesh.Count * sizeof(uint)];
        var _atomic_counter_rid = _rd.StorageBufferCreate((uint)atomicCounterBytes.Length, atomicCounterBytes);
        var _atomic_counter_uniform = GenerateUniform(_atomic_counter_rid, RenderingDevice.UniformType.StorageBuffer, 3);
        
        var max_verts = MAX_VERTS_PER_CHUNK * chunksToMesh.Count;
        var vec3_size = Marshal.SizeOf<Vector3>();
        var vec2_size = Marshal.SizeOf<Vector2>();
        
        var vertex_buffer = new byte[max_verts*vec3_size];
        var vertex_buffer_rid = _rd.StorageBufferCreate((uint)vertex_buffer.Length, vertex_buffer);
        var vertex_buffer_uniform = GenerateUniform(vertex_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 4);

        var normals_buffer = new byte[max_verts*vec3_size];
        var normals_buffer_rid = _rd.StorageBufferCreate((uint)normals_buffer.Length, normals_buffer);
        var normals_buffer_uniform = GenerateUniform(normals_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 5);

        var uvs_buffer = new byte[max_verts*vec2_size];
        var uvs_buffer_rid = _rd.StorageBufferCreate((uint)uvs_buffer.Length, uvs_buffer);
        var uvs_buffer_uniform = GenerateUniform(uvs_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 6);

        // GD.Print("vertex_buffer length: ", vertex_buffer.Length);
        // GD.Print("normals_buffer length: ", normals_buffer.Length);
        // GD.Print("uvs_buffer length: ", uvs_buffer.Length);

        var _bindings = new Godot.Collections.Array<RDUniform> {
            _voxel_array_uniform, _params_uniform, texture_array_coords_uniform,
            _atomic_counter_uniform, vertex_buffer_uniform, normals_buffer_uniform, uvs_buffer_uniform
        };

        var _uniform_set = _rd.UniformSetCreate(_bindings, _compute_shader, 0);

        /*
        GD.Print("Running compute shader");

        // trying to process any more than 21 chunks at once on the GPU will exceed max buffer sizes (buffers go up to 128MB, 21 chunks with MAX VERTEX each will be 122.472 mb)
        int chunk_batch_stride = 9;//chunksToGenerate.Count;
        var bytes_per_chunk = ChunkManager.CSP3*sizeof(int);
        GD.Print("total chunks to mesh: ", chunksToMesh.Count);
        GD.Print("chunk batch size: ", chunk_batch_stride, $" i<({chunksToMesh.Count/chunk_batch_stride})");

        for (int i=0; i<Mathf.Ceil(chunksToMesh.Count/(float)chunk_batch_stride);i++)
        {
            var chunk_batch = chunksToMesh.Skip(i*chunk_batch_stride).Take(chunk_batch_stride).ToList();
            var voxel_batch = voxel_data.AsSpan(i*chunk_batch_stride*bytes_per_chunk, chunk_batch.Count*bytes_per_chunk).ToArray();
            GD.Print($"meshing {chunk_batch.Count}, ({i*chunk_batch_stride}-{i*chunk_batch_stride+chunk_batch.Count}) chunks: ", chunk_batch.Select(x => x.ToString()).ToArray().Join(","));
            MeshMultiChunks(chunk_batch, voxel_batch);
        }
        */

        GD.Print("Running compute shader");
        var _compute_pipeline = _rd.ComputePipelineCreate(_compute_shader);
        var _compute_list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(_compute_list, _compute_pipeline);
        _rd.ComputeListBindUniformSet(_compute_list, _uniform_set, 0);
        _rd.ComputeListDispatch(_compute_list, workgroups * (uint)chunksToMesh.Count, workgroups, workgroups);
        _rd.ComputeListEnd();
        _rd.Submit();
        _rd.Sync();

        var rids = new Rid[]
            {_uniform_set, _voxel_buffer_rid, _params_buffer_rid,  _atomic_counter_rid,
            vertex_buffer_rid, normals_buffer_rid, uvs_buffer_rid, texture_array_coords_buffer_rid,
            _compute_pipeline, _compute_shader};

        var atomic_counter_readback = _rd.BufferGetData(_atomic_counter_rid);
        var vert_counts_readback = MemoryMarshal.Cast<byte, uint>(atomic_counter_readback.AsSpan()).ToArray();
        //GD.Print("vert_counts_readback: ", MemoryMarshal.Cast<byte, int>(atomic_counter_readback.AsSpan()).ToArray().Take(10).ToArray().Join(","));

        Dictionary<Vector3I, MeshArrayDataPacket> meshDict = [];

        var vert_lambda = (byte[] vertex_readback) =>
        {
            //GD.Print("vertex_buffer length: ", vertex_buffer.Length/vec3_size);
            //GD.Print("vertex_readback length: ", vertex_readback.Length/vec3_size);
            //GD.Print("vertex buffer reading stride (MAX_VERTS_PER_CHUNK): ", MAX_VERTS_PER_CHUNK);
            for (int i=0; i<chunksToMesh.Count; i++)
            {
                if (vert_counts_readback[i] == 0) continue; // skip empty chunks
                var pos = chunksToMesh[i];

                // GD.Print("reding back verts buffer between index: ", i*MAX_VERTS_PER_CHUNK, " and ", i*MAX_VERTS_PER_CHUNK + (int)vert_counts_readback[i], $" (verts count: {(int)vert_counts_readback[i]})");
                // if (i*MAX_VERTS_PER_CHUNK*vec3_size + (int)vert_counts_readback[i]*vec3_size >= vertex_buffer.Length)
                // {
                //     GD.Print($"ERROR: index out of bounds for vertex buffer readback");
                // }
                var verts_array = MemoryMarshal.Cast<byte, Vector3>(vertex_readback.AsSpan(i*MAX_VERTS_PER_CHUNK*vec3_size, (int)vert_counts_readback[i]*vec3_size)).ToArray();
                
                if (meshDict.TryGetValue(pos, out var packet))
                {
                    packet.Vertices = verts_array;
                }
                else
                {
                    meshDict.TryAdd(pos, new MeshArrayDataPacket { Vertices = verts_array });
                }
            }
            var cast = MemoryMarshal.Cast<byte, Vector3>(vertex_readback.AsSpan(0, (int)vert_counts_readback[0]*vec3_size)).ToArray();
            //GD.Print("verts[0] readback: ", cast.Take(10).ToArray().Join(","));
        };

        var norm_lambda = (byte[] normals_readback) =>
        {
            for (int i=0; i<chunksToMesh.Count; i++)
            {
                if (vert_counts_readback[i] == 0) continue;
                var pos = chunksToMesh[i];
                var norms_array = MemoryMarshal.Cast<byte, Vector3>(normals_readback.AsSpan(i*MAX_VERTS_PER_CHUNK*vec3_size, (int)vert_counts_readback[i]*vec3_size)).ToArray();
                if (meshDict.TryGetValue(pos, out var packet))
                {
                    packet.Normals = norms_array;
                }
                else
                {
                    meshDict.TryAdd(pos, new MeshArrayDataPacket { Normals = norms_array });
                }
            }
            var cast = MemoryMarshal.Cast<byte, Vector3>(normals_readback.AsSpan(0, (int)vert_counts_readback[0]*vec3_size)).ToArray();
            //GD.Print("normals[0] readback: ", cast.Take(10).ToArray().Join(","));
        };

        var uv_lambda = (byte[] uvs_readback) =>
        {
            for (int i=0; i<chunksToMesh.Count; i++)
            {
                if (vert_counts_readback[i] == 0) continue;
                var pos = chunksToMesh[i];
                var uvs_array = MemoryMarshal.Cast<byte, Vector2>(uvs_readback.AsSpan(i*MAX_VERTS_PER_CHUNK*vec2_size, (int)vert_counts_readback[i]*vec2_size)).ToArray();
                if (meshDict.TryGetValue(pos, out var packet))
                {
                    packet.UVs = uvs_array;
                }
                else
                {
                    meshDict.TryAdd(pos, new MeshArrayDataPacket { UVs = uvs_array });
                }
            }
            var cast = MemoryMarshal.Cast<byte, Vector2>(uvs_readback.AsSpan(0, (int)vert_counts_readback[0]*vec3_size)).ToArray();
            // GD.Print("uvs[0] readback: ", cast.Take(10).ToArray().Join(","));
        };

        var vert_err = _rd.BufferGetDataAsync(vertex_buffer_rid, Callable.From(vert_lambda));
        var norm_err =_rd.BufferGetDataAsync(normals_buffer_rid, Callable.From(norm_lambda));
        var uvs_err = _rd.BufferGetDataAsync(uvs_buffer_rid, Callable.From(uv_lambda));

        // GD.Print("vertex readback async called: ", vert_err);
        // GD.Print("normals readback async called: ",norm_err);
        // GD.Print("uvs readback async called: ",uvs_err);

        // do more work to force the async readbacks to finish
        atomic_counter_readback = _rd.BufferGetData(_atomic_counter_rid);

        // update chunk meshes
        for (int i=0; i<chunksToMesh.Count; i++)
        {
            var pos = chunksToMesh[i];
            if (ChunkManager.MESHCACHE.TryGetValue(pos, out var chunk))
            {
                var meshinstance = chunk.MeshInstance;
                var mesh = (ArrayMesh)meshinstance.Mesh;
                mesh.ClearSurfaces();
                if (vert_counts_readback[i] == 0)
                {
                    chunk.CollisionShape.Shape = null;
                    continue;
                }
                if (meshDict.TryGetValue(pos, out var packet))
                {
                    var arrays = new Godot.Collections.Array();
                    arrays.Resize((int)Mesh.ArrayType.Max);
                    arrays[(int)Mesh.ArrayType.Vertex] = packet.Vertices;
                    arrays[(int)Mesh.ArrayType.Normal] = packet.Normals;
                    arrays[(int)Mesh.ArrayType.TexUV] = packet.UVs;
                    //GD.Print(packet.Vertices.Take(10).ToArray().Join(","));

                    var xform = new Transform3D(Basis.Identity, (Vector3)pos*ChunkManager.CHUNK_SIZE);
                    
                    Callable.From(() => {
                        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                        chunk.GlobalTransform = xform;
                    }).CallDeferred();
                }
            }
        }
        //stopwatch.Stop();
        //GD.Print($"Building Meshes time elapsed: {stopwatch.ElapsedMilliseconds} ms");

        FreeRids(_rd, rids);
        _rd.Free();
    }

    private static void ReadBackNoiseLayerBuffer(RenderingDevice _rd, Rid _storageBuffer)
    {
        // Read back buffer from GPU
        byte[] outputBytes = _rd.BufferGetData(_storageBuffer);

        // Deserialize back into struct
        var outputParams = MemoryMarshal.Read<NoiseLayerStruct>(outputBytes);
        // Extract the float array (from structSize to end)
        //var noise_layer_array = new NoiseLayerStruct[1];
        //MemoryMarshal.Cast<byte, NoiseLayerStruct>(outputBytes.AsSpan()[Marshal.SizeOf<NoiseLayerStruct>()..]).CopyTo(noise_layer_array);
        
        // Print values for debugging
        GD.Print("DEBUG: reading back noise layer buffer...");
        GD.Print($"Gain: {outputParams.Gain}");
        GD.Print($"Frequency: {outputParams.Frequency}");
        GD.Print($"Lacunarity: {outputParams.Lacunarity}");
        GD.Print($"Persistence: {outputParams.Persistence}");
        GD.Print($"Octaves: {outputParams.Octaves}");
        GD.Print($"CaveScale: {outputParams.CaveScale}");
        GD.Print($"CaveThreshold: {outputParams.CaveThreshold}");
        GD.Print($"SurfaceVoxelId: {outputParams.SurfaceVoxelId}");
        GD.Print($"SubSurfaceVoxelId: {outputParams.SubSurfaceVoxelId}");
    }

    private static void ReadBackParamsBuffer(RenderingDevice _rd, Rid _storageBuffer)
    {
        // Read back buffer from GPU
        byte[] outputBytes = _rd.BufferGetData(_storageBuffer);

        // Deserialize back into struct
        var outputParams = MemoryMarshal.Read<ChunkParamsStruct>(outputBytes);
        // Extract the float array (from structSize to end)
        var vec4_array = new System.Numerics.Vector4[outputParams.NumChunksToCompute];
        MemoryMarshal.Cast<byte, System.Numerics.Vector4>(outputBytes.AsSpan(Marshal.SizeOf<ChunkParamsStruct>())).CopyTo(vec4_array);
        
        // Print values for debugging
        GD.Print("DEBUG: reading back params buffer...");
        GD.Print($"CSP: {outputParams.CSP}");
        GD.Print($"CSP3: {outputParams.CSP3}");
        GD.Print($"GenerateCaves: {outputParams.GenerateCaves}");
        GD.Print($"ForceFloor: {outputParams.ForceFloor}");
        GD.Print($"MaxWorldHeight: {outputParams.MaxWorldHeight}");
        GD.Print($"StoneBlockID: {outputParams.StoneBlockID}");
        GD.Print($"OceanHeight: {outputParams.OceanHeight}");
        GD.Print($"NoiseScale: {outputParams.NoiseScale}");
        GD.Print($"CaveNoiseScale: {outputParams.CaveNoiseScale}");
        GD.Print($"CaveThreshold: {outputParams.CaveThreshold}");
        GD.Print($"SeedOffset (VEC4): {outputParams.SeedOffset}");
        GD.Print($"NumChunksToCompute: {outputParams.NumChunksToCompute}");
        GD.Print($"VAR VEC4 Array: {string.Join(",", vec4_array)}");
    }

    public static void PrintDebugVariables()
    {
        GD.Print("MemoryMarshal size of ChunkParamsStruct in bytes: " + Marshal.SizeOf<ChunkParamsStruct>());
        GD.Print("MemoryMarshal size of System.Numerics.Vector4 in bytes: " + Marshal.SizeOf<System.Numerics.Vector4>());
        
        GD.Print("MemoryMarshal size of input params boolean in bytes: " + Marshal.SizeOf<bool>());
        GD.Print("sizeof(bool): " + sizeof(bool));

        /*
        int offset = 0;
        GD.Print($"SeedOffset: {offset}"); offset += 16;
        GD.Print($"CSP: {offset}"); offset += 4;
        GD.Print($"CSP3: {offset}"); offset += 4;
        GD.Print($"NumChunksToCompute: {offset}"); offset += 4;
        GD.Print($"MaxWorldHeight: {offset}"); offset += 4;
        GD.Print($"StoneBlockID: {offset}"); offset += 4;
        GD.Print($"OceanHeight: {offset}"); offset += 4;
        GD.Print($"NoiseLayerCount: {offset}"); offset += 4;
        GD.Print($"NoiseSeed: {offset}"); offset += 4;
        GD.Print($"NoiseScale: {offset}"); offset += 4;
        GD.Print($"CaveNoiseScale: {offset}"); offset += 4;
        GD.Print($"CaveThreshold: {offset}"); offset += 4;
        GD.Print($"GenerateCaves: {offset}"); offset += 4;
        GD.Print($"ForceFloor: {offset}"); offset += 4;
        GD.Print($"Padding1: {offset}"); offset += 4;
        GD.Print($"Padding2: {offset}"); offset += 4;
        GD.Print($"Padding3: {offset}"); offset += 4;
        GD.Print($"ChunkPositions START: {offset}");
        */
    }

    public static void GenerateMultiChunksSequentially(List<Vector3I> chunksToGenerate)
    {
        var stopwatch = Stopwatch.StartNew();
        GD.Print("Running compute shader");

        var _rd = RenderingServer.CreateLocalRenderingDevice();
        var _shader_spir_v = _shader_file.GetSpirV();
        var _compute_shader = _rd.ShaderCreateFromSpirV(_shader_spir_v);

        var noiseBufferBytes = GenerateNoiseLayerBufferBytes();
        var _noise_buffer_rid = _rd.StorageBufferCreate((uint)noiseBufferBytes.Length, noiseBufferBytes);
        var _noise_uniform = GenerateUniform(_noise_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 2);

        var _compute_pipeline = _rd.ComputePipelineCreate(_compute_shader);
        var workgroups = (uint)ChunkManager.CSP / 8;
        var rids = new Rid[chunksToGenerate.Count][];
        for (int i=0; i<chunksToGenerate.Count; i++)
        {
            var chunkPosition = chunksToGenerate[i];
            var paramsBufferBytes = GenerateParameterBufferBytes([chunkPosition]);
            var _params_buffer_rid = _rd.StorageBufferCreate((uint)paramsBufferBytes.Length, paramsBufferBytes);
            var _params_uniform = GenerateUniform(_params_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 1);

            var _num_blocks = ChunkManager.CSP3;
            var _voxel_buffer_rid = GenerateIntBuffer(_rd, _num_blocks);
            var _voxel_array_uniform = GenerateUniform(_voxel_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 0);

            var atomicCounterBytes = new byte[sizeof(uint)];
            var _atomic_counter_rid = _rd.StorageBufferCreate((uint)atomicCounterBytes.Length, atomicCounterBytes);
            var _atomic_counter_uniform = GenerateUniform(_atomic_counter_rid, RenderingDevice.UniformType.StorageBuffer, 3);

            var test_buffer = new byte[8*sizeof(int)];
            var test_buffer_rid = _rd.StorageBufferCreate((uint)test_buffer.Length, test_buffer);
            var test_buffer_unifom = GenerateUniform(test_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 4);

            var _bindings = new Godot.Collections.Array<RDUniform> { _voxel_array_uniform, _params_uniform, _noise_uniform, _atomic_counter_uniform, test_buffer_unifom };
            var _uniform_set = _rd.UniformSetCreate(_bindings, _compute_shader, 0);
            
            var _compute_list = _rd.ComputeListBegin();
            _rd.ComputeListBindComputePipeline(_compute_list, _compute_pipeline);
            _rd.ComputeListBindUniformSet(_compute_list, _uniform_set, 0);
            _rd.ComputeListDispatch(_compute_list, workgroups, workgroups, workgroups);
            //_rd.ComputeListAddBarrier(_compute_list);
            _rd.ComputeListEnd();
            rids[i] = [_uniform_set, _voxel_buffer_rid, _params_buffer_rid, _atomic_counter_rid, test_buffer_rid];
        }
        
        _rd.Submit();
        _rd.Sync();

        foreach (Rid[] rid_array in rids)
        {
            var _voxel_buffer_rid = rid_array[1];
            var _params_buffer_rid = rid_array[2];
            var voxel_bytes = _rd.BufferGetData(_voxel_buffer_rid);
            var params_bytes = _rd.BufferGetData(_params_buffer_rid);
            
            var data = new int[ChunkManager.CSP3];
            Buffer.BlockCopy(voxel_bytes, 0, data, 0, voxel_bytes.Length);

            var outputParams = MemoryMarshal.Read<ChunkParamsStruct>(params_bytes);
            // Extract the float array (from structSize to end)
            var vec4_array = new System.Numerics.Vector4[outputParams.NumChunksToCompute];
            MemoryMarshal.Cast<byte, System.Numerics.Vector4>(params_bytes.AsSpan(Marshal.SizeOf<ChunkParamsStruct>())).CopyTo(vec4_array);
            var chunkPosition = new Vector3I((int)vec4_array[0].X,(int)vec4_array[0].Y,(int)vec4_array[0].Z);

            //GD.Print("array length: " + array.Length);
            //GD.Print("array length/sizeof(int): " + array.Length/sizeof(int));
            //GD.Print("data length: " + data.Length);
            if (data.All(x => x == 0)) GD.Print("data is all zeroes");

            ChunkManager.BLOCKCACHE[chunkPosition] = data;
        }

        /*
        GD.Print($"Generated chunk at: " + chunkPosition);
        var tempstring = "[";
        for (int i = 0; i < 20; i++)
        {
            tempstring += ChunkManager.BLOCKCACHE[chunkPosition][i] + ",";
        }
        tempstring += "...]";
        GD.Print(tempstring);
        */

        FreeRids(_rd, rids);
        
        _rd.FreeRid(_noise_buffer_rid);
        if (_compute_pipeline.IsValid) _rd.FreeRid(_compute_pipeline);
        _rd.FreeRid(_compute_shader);

        stopwatch.Stop();
        GD.Print($"GenerateMultiChunksSequentially time elapsed: {stopwatch.ElapsedMilliseconds} ms");
    }

    public static void GenerateBlocks(Vector3I chunkPosition)
    {
        var _rd = RenderingServer.CreateLocalRenderingDevice();
        var _shader_spir_v = _shader_file.GetSpirV();
        var _compute_shader = _rd.ShaderCreateFromSpirV(_shader_spir_v);

        var _num_blocks = ChunkManager.CSP3;
        var _voxel_buffer_rid = GenerateIntBuffer(_rd, _num_blocks);
        var _voxel_array_uniform = GenerateUniform(_voxel_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 0);

        var paramsBufferBytes = GenerateParameterBufferBytes([chunkPosition]);
        var _params_buffer_rid = _rd.StorageBufferCreate((uint)paramsBufferBytes.Length, paramsBufferBytes);
        var _params_uniform = GenerateUniform(_params_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 1);

        var noiseBufferBytes = GenerateNoiseLayerBufferBytes();
        var _noise_buffer_rid = _rd.StorageBufferCreate((uint)noiseBufferBytes.Length, noiseBufferBytes);
        var _noise_uniform = GenerateUniform(_noise_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 2);

        var atomicCounterBytes = new byte[sizeof(uint)];
        var _atomic_counter_rid = _rd.StorageBufferCreate((uint)atomicCounterBytes.Length, atomicCounterBytes);
        var _atomic_counter_uniform = GenerateUniform(_atomic_counter_rid, RenderingDevice.UniformType.StorageBuffer, 3);

        var test_buffer = new byte[8*sizeof(int)];
        var test_buffer_rid = _rd.StorageBufferCreate((uint)test_buffer.Length, test_buffer);
        var test_buffer_unifom = GenerateUniform(test_buffer_rid, RenderingDevice.UniformType.StorageBuffer, 4);

        var _bindings = new Godot.Collections.Array<RDUniform> { _voxel_array_uniform, _params_uniform, _noise_uniform, _atomic_counter_uniform, test_buffer_unifom };

        var _uniform_set = _rd.UniformSetCreate(_bindings, _compute_shader, 0);
        
        var workgroups = (uint)ChunkManager.CSP / 8;
        var _compute_pipeline = _rd.ComputePipelineCreate(_compute_shader);
        var _compute_list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(_compute_list, _compute_pipeline);
        _rd.ComputeListBindUniformSet(_compute_list, _uniform_set, 0);
        _rd.ComputeListDispatch(_compute_list, workgroups, workgroups, workgroups);
        //_rd.ComputeListAddBarrier(_compute_list);
        _rd.ComputeListEnd();
        _rd.Submit();

        var rids = new Rid[] {_uniform_set, _voxel_buffer_rid, _params_buffer_rid, _noise_buffer_rid, _atomic_counter_rid, _compute_pipeline, _compute_shader};

        _rd.Sync();
        var array = _rd.BufferGetData(_voxel_buffer_rid);
        
        var data = new int[ChunkManager.CSP3];
        Buffer.BlockCopy(array, 0, data, 0, array.Length);

        //GD.Print("array length: " + array.Length);
        //GD.Print("array length/sizeof(int): " + array.Length/sizeof(int));
        //GD.Print("data length: " + data.Length);
        if (data.All(x => x == 0)) GD.Print("data is all zeroes");

        ChunkManager.BLOCKCACHE[chunkPosition] = data;
        /*
        GD.Print($"Generated chunk at: " + chunkPosition);
        var tempstring = "[";
        for (int i = 0; i < 20; i++)
        {
            tempstring += ChunkManager.BLOCKCACHE[chunkPosition][i] + ",";
        }
        tempstring += "...]";
        GD.Print(tempstring);
        */

        FreeRids(_rd, rids);
        /*

        var lambda = new Action<byte[]> ((array) =>
            {
                GD.Print("Generated chunk at: " + chunkPosition);
                var data = new int[ChunkManager.CSP3];
                Buffer.BlockCopy(array, 0, data, 0, array.Length);

                ChunkManager.BLOCKCACHE[chunkPosition] = data;
                var tempstring = "";
                for (int i = 0; i < 20; i++)
                {
                    tempstring += ChunkManager.BLOCKCACHE[chunkPosition][i] + " ";
                }
                tempstring += "...";
                GD.Print(tempstring);

                RenderingServer.CallOnRenderThread(Callable.From(() =>
                {
                    foreach (Rid r in rids)
                    {
                        _rd.FreeRid(r);
                    }
                    _rd.Free();
                }));
            }
        );

        var lambdatest = new Action<string>(GD.Print);

        var calltest = Callable.From(lambdatest);

        var callable = Callable.From(lambda);
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            var err = _rd.BufferGetDataAsync(_voxel_buffer_rid, callable, 0, ChunkManager.CSP3*sizeof(int));
            GD.Print(err);
        }));

        calltest.CallDeferred("this is a test");*/
    }

    public static void FreeLocalRenderingDevice()
    {
        if (GodotObject.IsInstanceValid(LocalRenderingDevice))
        {
            LocalRenderingDevice.Free();
        }
    }

    private static void FreeRids(RenderingDevice _rd, Rid[] rids)
    {
        foreach (Rid r in rids)
        {  
            if(r.IsValid) _rd.FreeRid(r); 
        }
    }

    private static void FreeRids(RenderingDevice _rd, List<Rid> rids)
    {
        foreach (Rid r in rids)
        {
            //GD.Print("attemping to free: ", r, " and rid is valid: ", r.IsValid);
            if(r.IsValid) _rd.FreeRid(r); 
        }
    }    
    private static void FreeRids(RenderingDevice _rd, Rid[][] rids)
    {
        foreach (Rid[] rid_array in rids)
        {
            foreach (Rid r in rid_array) if(r.IsValid) _rd.FreeRid(r);
        }
        //_rd.Free();
    }

    public static void FreeRenderingDevice(RenderingDevice _rd = null)
    {
        if (_rd == null) return;
        _rd.Sync();
        _rd.Free();
    }

    private static Rid GenerateIntBuffer(RenderingDevice _rd, int size)
    {
        var dataBufferBytes = new byte[size * sizeof(int)];
        return _rd.StorageBufferCreate((uint)dataBufferBytes.Length, dataBufferBytes);
    }

    private static RDUniform GenerateUniform(Rid dataBuffer, RenderingDevice.UniformType type, int binding)
    {
        var dataUniform = new RDUniform
        {
            UniformType = type,
            Binding = binding
        };
        dataUniform.AddId(dataBuffer);
        return dataUniform;
    }

    private static byte[] GenerateNoiseLayerBufferBytes()
    {
        var struct_size = Marshal.SizeOf<NoiseLayerStruct>();
        var data_bytes = new byte[_noise_layers.Length * struct_size]; // Allocate space for struct + vec3 array

        // Copy the struct data into the start of the byte array
        for (int i=0; i< _noise_layers.Length; i++)
        {
            MemoryMarshal.AsBytes(new Span<NoiseLayerStruct>(ref _noise_layers[i])).CopyTo(data_bytes.AsSpan()[(i*struct_size)..]);
        }

        return data_bytes;
    }

    private static byte[] GenerateParameterBufferBytes(List<Vector3I> chunkPositions)
    {
        Vector3 seedOffset = new(0, 0, 0);
        var chunk_positions = new System.Numerics.Vector4[chunkPositions.Count];
        for (var i=0; i<chunkPositions.Count; i++)
        {
            chunk_positions[i] = new System.Numerics.Vector4(chunkPositions[i].X, chunkPositions[i].Y, chunkPositions[i].Z,0.0f);
        }

        input_params.SeedOffset = new System.Numerics.Vector4(seedOffset.X, seedOffset.Y, seedOffset.Z, 0.0f);
        input_params.NumChunksToCompute = chunkPositions.Count;
        input_params.MaxWorldHeight = MaxWorldHeight;
        input_params.CaveNoiseScale = CaveNoiseScale;
        input_params.CaveThreshold = CaveThreshold;
        input_params.OceanHeight = OceanHeight;
        input_params.GenerateCaves = GenerateCaves;
        input_params.ForceFloor = ForceFloor;

        // Get the struct size
        // note that MemoryMarshal interprets bools as 4 bytes for interoperability,
        // this makes it preferable to the sizeof(bool) operator which = 1 byte
        // and makes padding with the gpu harder (glsl pads bools to 4 bytes)
        var struct_size = Marshal.SizeOf<ChunkParamsStruct>();
        
        var data_bytes = new byte[struct_size + (chunkPositions.Count * Marshal.SizeOf<System.Numerics.Vector4>())]; // Allocate space for struct + vec3 array

        // Copy the struct data into the start of the byte array
        
        MemoryMarshal.AsBytes(new Span<ChunkParamsStruct>(ref input_params)).CopyTo(data_bytes.AsSpan());

        // Serialize the vec3 (padded to vec4) array and append it to the byte array
        var vec4_array_bytes = MemoryMarshal.AsBytes<System.Numerics.Vector4>(chunk_positions).ToArray();
        vec4_array_bytes.CopyTo(data_bytes.AsSpan(struct_size));

        //GD.Print($"Generated parameter buffer bytes {data_bytes.Length}");

        return data_bytes;
    }

    private static byte[] GenerateMeshParameterBufferBytes(List<Vector3I> chunksToMesh)
    {
        var chunk_positions = new System.Numerics.Vector4[chunksToMesh.Count];
        for (var i=0; i<chunksToMesh.Count; i++)
        {
            chunk_positions[i] = new System.Numerics.Vector4(chunksToMesh[i].X, chunksToMesh[i].Y, chunksToMesh[i].Z,0.0f);
        }

        var mesh_params = new MeshParamsStruct
        {

            CHUNK_SIZE = ChunkManager.CHUNK_SIZE,
            CSP = ChunkManager.CSP,
            CSP3 = ChunkManager.CSP3,
            MaxVerts = MAX_VERTS_PER_CHUNK,
            NumChunksToCompute = chunksToMesh.Count,
        };

        var struct_size = Marshal.SizeOf<MeshParamsStruct>();
        
        var data_bytes = new byte[struct_size + (chunksToMesh.Count * Marshal.SizeOf<System.Numerics.Vector4>())]; // Allocate space for struct + vec3 array

        MemoryMarshal.AsBytes(new Span<MeshParamsStruct>(ref mesh_params)).CopyTo(data_bytes.AsSpan());

        var vec4_array_bytes = MemoryMarshal.AsBytes<System.Numerics.Vector4>(chunk_positions).ToArray();
        vec4_array_bytes.CopyTo(data_bytes.AsSpan(struct_size));

        return data_bytes;
    }
}
