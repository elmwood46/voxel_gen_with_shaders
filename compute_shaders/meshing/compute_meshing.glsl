#[compute]
#version 450

layout(local_size_x = 6, local_size_y = 6, local_size_z = 6) in;

// constants
const vec3 VEC3_ONE = vec3(1.0, 1.0, 1.0);
const uvec3 U_VEC3_ONE = uvec3(1, 1, 1);

// input block information
layout(set=0, binding = 0, std430) restrict buffer readonly vox_buffer {
    int Array[];
} Voxels;

layout(set=0, binding = 1, std430) restrict buffer readonly mesh_params {
    int CHUNK_SIZE;
    int CSP;
    int CSP3;
    int MaxVerts;
    int NumChunksToCompute;
    int padding1;
    int padding2;
    int padding3;
    vec4[] ChunkPositions;
} MeshParams;

layout(set=0, binding = 2, std430) restrict buffer readonly texture_array_coords {
    int Array[];
} BlockTextureArrayCoords;

layout(set=0, binding = 3, std430) restrict buffer atomic_counter_buffer {
    uint[] ChunkVertCounts;
} AtomicCounter;

layout(set=0, binding = 4, std430) restrict buffer writeonly vertex_buffer {
    float Array[]; // actually VEC3 when read back
} Vertices;

layout(set=0, binding = 5, std430) restrict buffer writeonly normals_buffer {
    float Array[]; // actually VEC3 when read back
} Normals;

layout(set=0, binding = 6, std430) restrict buffer writeonly uvs_buffer {
    float Array[]; // actually VEC2 when read back
} TexUVS;

int glob_invocation_to_chunk_idx() // convert global invocation to index of which chunk is being processed
{
    return int(gl_GlobalInvocationID.x)/MeshParams.CHUNK_SIZE;
}

uvec3 u_local_block_pos() // convert global invocation to the local block position in the NON-PADDED (30x30x30) chunk
{
    return uvec3(gl_GlobalInvocationID.x % uint(MeshParams.CHUNK_SIZE), gl_GlobalInvocationID.y, gl_GlobalInvocationID.z);
}

uvec3 padded_chunk_pos(vec3 offset) // convert global invocation to the local block position in the PADDED (32x32x32) chunk
{
    return uvec3(u_local_block_pos() + VEC3_ONE + offset);
}

int flatten_padded_chunk_pos(vec3 offset)
{
    uvec3 padded_pos = padded_chunk_pos(offset);
    return ((glob_invocation_to_chunk_idx()*MeshParams.CSP3) + int(padded_pos.x)
        + (int(padded_pos.y) * MeshParams.CSP * MeshParams.CSP)
        + (int(padded_pos.z) * MeshParams.CSP));
}

const vec3 CUBEVERTICES[8] =
{
    vec3(0, 0, 0), //0
    vec3(1, 0, 0), //1
    vec3(0, 1, 0), //2
    vec3(1, 1, 0), //3

    vec3(0, 0, 1), //4
    vec3(1, 0, 1), //5
    vec3(0, 1, 1), //6
    vec3(1, 1, 1), //7
};

const vec3 CUBEFACECHECKS[6] =
{
    vec3(0, 0, -1), //back
    vec3(0, 0, 1),  //front
    vec3(-1, 0, 0), //left
    vec3(1, 0, 0),  //right
    vec3(0, -1, 0), //bottom
    vec3(0, 1, 0)   //top
};

const int CUBEVERTEXINDEX[6][4] =
{
    { 0, 1, 2, 3 },
    { 4, 5, 6, 7 },
    { 4, 0, 6, 2 },
    { 5, 1, 7, 3 },
    { 0, 1, 4, 5 },
    { 2, 3, 6, 7 },
};

const vec2 CUBEUVS[4] =
{
    vec2(0, 0),
    vec2(0, 1),
    vec2(1, 0),
    vec2(1, 1)
};

/*
// ORIGINAL TRIANGLES (CLOCKWISE WINDING)
const int CUBETRIS[6][6] =
{
    { 0, 2, 3, 0, 3, 1 },
    { 0, 1, 2, 1, 3, 2 },
    { 0, 2, 3, 0, 3, 1 },
    { 0, 1, 2, 1, 3, 2 },
    { 0, 1, 2, 1, 3, 2 },
    { 0, 2, 3, 0, 3, 1 },
};
*/

// MODIFIED TRIANGLES (ANTICLOCKWISE WINDING)
const int CUBETRIS[6][6] =
{
    { 0, 3, 2, 0, 1, 3 },
    { 0, 2, 1, 1, 2, 3 },
    { 0, 3, 2, 0, 1, 3 },
    { 0, 2, 1, 1, 2, 3 },
    { 0, 2, 1, 1, 2, 3 },
    { 0, 3, 2, 0, 1, 3 },
};

void main()
{   
    // do not mesh air blocks
    if (Voxels.Array[flatten_padded_chunk_pos(vec3(0, 0, 0))] == 0) return;

    bool solid_neighbours[6];
    for (int i=0; i<6; i++)
    {
        solid_neighbours[i] = Voxels.Array[flatten_padded_chunk_pos(CUBEFACECHECKS[i])] != 0;
    }

    int chunk_idx = glob_invocation_to_chunk_idx();

    vec3 faceVertices[4];
    vec2 faceUVs[4];

    for (int i = 0; i < 6; i++)
    {
        // Check if there's a solid block against this face, and skip if so
        if (solid_neighbours[i]) continue;

        // check to ensure we do not exceed max vertex count for this chunk
        uint vertCount = atomicAdd(AtomicCounter.ChunkVertCounts[chunk_idx], 6);
        if (vertCount+6 >= MeshParams.MaxVerts) {
            atomicAdd(AtomicCounter.ChunkVertCounts[chunk_idx], -6);
            return;
        }

        for (int j = 0; j < 4; j++)
        {
            faceVertices[j] =  u_local_block_pos() + CUBEVERTICES[CUBEVERTEXINDEX[i][j]].xyz;
            faceUVs[j] = CUBEUVS[j];
        }
        
        for (int k = 0; k < 6; k++)
        {
            for (int w=0;w<3;w++)
            {
                Vertices.Array[(chunk_idx*MeshParams.MaxVerts + vertCount + k)*3 + w] = faceVertices[CUBETRIS[i][k]][w];
                Normals.Array[(chunk_idx*MeshParams.MaxVerts + vertCount + k)*3 + w] = CUBEFACECHECKS[i][w];
                Normals.Array[(chunk_idx*MeshParams.MaxVerts + vertCount + k)*3 + w] = CUBEFACECHECKS[i][w];
                if (w<2) TexUVS.Array[(chunk_idx*MeshParams.MaxVerts + vertCount + k)*2 + w]  = faceUVs[CUBETRIS[i][k]][w];
            }
        }
    }
}