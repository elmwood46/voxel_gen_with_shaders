// this function assumes we already have a voxel which
// we know we need to mesh (not empty), and have calculated
// the position and neighbouring 6 voxels for it .
// note that solid_neighbours[6] are ordered according to CUBEFACECHECKS[6] in voxel_values.glsl

#ifndef MESH_FUNCTION
#define MESH_FUNCTION

void _do_meshing_for_voxel(int block_type_to_set)
{  
    int loop_faces = 6;
    uvec3 local_voxel_position = u_local_block_pos() - U_VEC3_ONE;
    if (local_voxel_position.x < 0 || local_voxel_position.x == 30
        || local_voxel_position.y < 0 || local_voxel_position.y == 30
        || local_voxel_position.z < 0 || local_voxel_position.z == 30)
    {
        loop_faces = 0;
    }
    
    bool solid_neighbours[6];
    for (int i=0; i<loop_faces; i++)
    {
        solid_neighbours[i] = _calculate_block_type(global_block_pos() + CUBEFACECHECKS[i]) != 0;
    }

    int chunk_idx = glob_invocation_to_chunk_idx();

    vec3 faceVertices[4];
    vec2 faceUVs[4];

    for (int i = 0; i < loop_faces; i++)
    {
        // Check if there's a solid block against this face, and skip if so
        if (solid_neighbours[i]) continue;
        
        uint vertCount = atomicAdd(AtomicCounter.ChunkVertCounts[chunk_idx], 6);

        for (int j = 0; j < 4; j++)
        {
            faceVertices[j] =  vec3(local_voxel_position).xyz + CUBEVERTICES[CUBEVERTEXINDEX[i][j]].xyz;
            faceUVs[j] = CUBEUVS[j];
        }
        
        for (int k = 0; k < 6; k++)
        {
            for (int w=0;w<3;w++)
            {
                Vertices.Array[(chunk_idx*Params.MaxVerts + vertCount + k)*3 + w] = faceVertices[CUBETRIS[i][k]][w];
                Normals.Array[(chunk_idx*Params.MaxVerts + vertCount + k)*3 + w] = CUBEFACECHECKS[i][w];
                if (w<2) TexUVS.Array[(chunk_idx*Params.MaxVerts + vertCount + k)*2 + w]  = faceUVs[CUBETRIS[i][k]][w];//vec2(i, BlockTextureArrayCoords.Array[voxel_id*6+i]);
            }
        }
    }
}

#endif