#[compute]
#version 450

// each chunk is 32x32x32 blocks
// we run one thread per block, doing 8x8x8 blocks per workgroup, and have 64 (4x4x4) workgroups per chunk
// when computing multiple chunks, stack workgroups in the x direction 

layout(local_size_x = 8, local_size_y = 8, local_size_z = 8) in;

const vec3 VEC3_ONE = vec3(1.0, 1.0, 1.0);

#include "shared_data.glsl"
#include "compute_noise.glsl"
#include "compute_noise_fractal.glsl"

// find the chunk of current invocation
int glob_invocation_to_chunk_idx()
{
    return (int(gl_GlobalInvocationID.x)/Params.CSP);
}

// chunk position in world chunk space
vec3 chunk_pos()
{
    return (Params.ChunkPositions[glob_invocation_to_chunk_idx()].xyz);
}

// local block position in local padded chunk space
vec3 local_block_pos()
{
    return (vec3(float(int(gl_GlobalInvocationID.x) % Params.CSP), float(gl_GlobalInvocationID.y), float(gl_GlobalInvocationID.z)));
}

// global block position in world global space
vec3 global_block_pos()
{
    return ((chunk_pos()*float(Params.CSP-2)) + local_block_pos() - VEC3_ONE); // offset by one because chunk padding
}

// convert the current global invocation to a position in the voxel array
int glob_invocation_to_vox_array_index()
{
    return ((glob_invocation_to_chunk_idx()*Params.CSP3) + (int(gl_GlobalInvocationID.x) % Params.CSP)
        + (int(gl_GlobalInvocationID.y) * Params.CSP * Params.CSP)
        + (int(gl_GlobalInvocationID.z) * Params.CSP));
}

/*
void main()
{
    int vox_idx = glob_invocation_to_vox_array_index();
    vec3 pos = global_block_pos();
    vec2 posXZ = pos.xy;
    float pos_y = pos.y;

    float height = snoise(posXZ * Params.NoiseScale) * (Params.MaxWorldHeight);
    uint groundHeight = uint(round(height));
    int vox = 0;

    if (uint(pos_y) > groundHeight)
    {
        Voxels.Array[vox_idx] = 0;
        return;
    }

    bool isSurfaceBlock = uint(pos_y) == groundHeight;

    vox = isSurfaceBlock ? NoiseLayers.Array[0].SurfaceVoxelID : NoiseLayers.Array[0].SubSurfaceVoxelID;

    if (Params.GenerateCaves && snoise(pos * Params.CaveNoiseScale) > Params.CaveThreshold)
    {
        vox = 0;
    }

    if (uint(pos_y) <= 1 && Params.ForceFloor)
        vox = NoiseLayers.Array[0].SubSurfaceVoxelID;

    if (vox != 0) atomicAdd(AtomicCounter.Counter, 1);

    Voxels.Array[vox_idx] = int(pos.x);
}
*/


void main() {
    int vox_aray_idx = glob_invocation_to_vox_array_index();

    vec3 globalpos = global_block_pos();
    
    if (atomicAdd(AtomicCounter.Counter, 1) == 0)
    {
        vec3 chunkpos = chunk_pos();
        vec3 localpos = local_block_pos();
        int i=0;
        Test.Array[i++] = glob_invocation_to_chunk_idx();
        Test.Array[i++] = int(chunkpos.x);
        Test.Array[i++] = int(chunkpos.y);
        Test.Array[i++] = int(chunkpos.z);
        Test.Array[i++] = int(chunk_pos().x);
        Test.Array[i++] = int(chunk_pos().y);
        Test.Array[i++] = int(chunk_pos().z);
        Test.Array[i++] = int(globalpos.x);
        Test.Array[i++] = int(globalpos.y);
        Test.Array[i++] = int(globalpos.z);
    }
    

    int set_block_id = 0;

    vec3 seeded_global_pos = globalpos + Params.SeedOffset.xyz;
    vec2 posXZ = seeded_global_pos.xz;
    uint voxel_y = uint(seeded_global_pos.y);

    HeightAndNoise hb = sampleHeightAtPoint(posXZ);
    uint terrainHeight = hb.Height;
    NoiseLayer selectedNoise = NoiseLayers.Array[hb.NoiseIndex];

    if (voxel_y > terrainHeight)
    {
        Voxels.Array[vox_aray_idx] = 0;
        return;
    }

    bool isSurfaceBlock = voxel_y >= terrainHeight - 3;
    set_block_id = isSurfaceBlock ? selectedNoise.SurfaceVoxelID : selectedNoise.SubSurfaceVoxelID;

    if (Params.GenerateCaves && evaluateNoise(seeded_global_pos, terrainHeight) > selectedNoise.CaveThreshold)
    {
        set_block_id = 0;
    }

    if (voxel_y <= 1 && Params.ForceFloor) set_block_id = selectedNoise.SurfaceVoxelID;

    if (set_block_id != 0) atomicAdd(AtomicCounter.Counter, 1);

    Voxels.Array[vox_aray_idx] = set_block_id;
}