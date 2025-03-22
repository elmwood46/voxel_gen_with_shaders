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
#include "voxel_values.glsl"

void main() {
    int vox_aray_idx = glob_invocation_to_vox_array_index();

    vec3 globalpos = global_block_pos();

    int set_block_id = 0;

    vec3 seeded_global_pos = globalpos + Params.SeedOffset.xyz;
    vec2 posXZ = seeded_global_pos.xz;
    if (posXZ.x < 0.01 && posXZ.x >= 0 && posXZ.y < 0.01 && posXZ.y >= 0) posXZ.x = 1;
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

    Voxels.Array[vox_aray_idx] = set_block_id;
}