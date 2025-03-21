#[compute]
#version 450

// each chunk is 32x32x32 blocks
// we run one thread per block, doing 8x8x8 blocks per workgroup, and have 64 (4x4x4) workgroups per chunk
// when computing multiple chunks, stack workgroups in the x direction 

layout(local_size_x = 8, local_size_y = 8, local_size_z = 8) in;

// constants
const vec3 VEC3_ONE = vec3(1.0, 1.0, 1.0);
const uvec3 U_VEC3_ONE = uvec3(1, 1, 1);

// structs
struct NoiseLayer {
    float Gain;
    float Frequency;
    float Lacunarity;
    float Persistence;
    int Octaves;

    float CaveScale;
    float CaveThreshold;

    int SurfaceVoxelID;
    int SubSurfaceVoxelID;
};

struct HeightAndNoise {
    uint Height;
    uint NoiseIndex;
};

// input block information
layout(set=0, binding = 0, std430) restrict buffer writeonly vox_buffer {
    int Array[];
} Voxels;

// input chunk information
layout(set=0, binding = 1, std430) restrict buffer readonly chunk_params {
    vec4 SeedOffset;

    int CSP;
    int CSP3;
    int NumChunksToCompute;
    int MaxWorldHeight;
    int StoneBlockID;
    int OceanHeight;
    int NoiseLayerCount;
    int NoiseSeed;

    float NoiseScale;
    float CaveNoiseScale;
    float CaveThreshold;

    bool GenerateCaves;
    bool ForceFloor;

    // Ensure 16-byte alignment
    int MaxVerts;
    int _padding2;
    int _padding3;

     //MUST START ON MULTIPLE OF 16 BYTES
    vec4[] ChunkPositions;
} Params;

layout(set=0, binding = 2, std430) restrict buffer readonly noise_buffer {
    NoiseLayer Array[];
} NoiseLayers;

#include "compute_noise.glsl"
#include "compute_noise_fractal.glsl"
#include "meshing/voxel_values.glsl"

void main() {
    int vox_aray_idx = glob_invocation_to_vox_array_index();
    int set_block = 0;

    vec3 seeded_global_pos = global_block_pos() + Params.SeedOffset.xyz;
    vec2 posXZ = seeded_global_pos.xz;
    uint voxel_y = uint(seeded_global_pos.y);

    HeightAndNoise hb = sampleHeightAtPoint(posXZ);
    uint terrainHeight = hb.Height;
    NoiseLayer selectedNoise = NoiseLayers.Array[hb.NoiseIndex];

    if (voxel_y > terrainHeight)
    {
        Voxels.Array[vox_aray_idx] = set_block;
        return;
    }

    bool isSurfaceBlock = voxel_y >= terrainHeight - 3;
    set_block = isSurfaceBlock ? selectedNoise.SurfaceVoxelID : selectedNoise.SubSurfaceVoxelID;

    if (Params.GenerateCaves && evaluateNoise(seeded_global_pos, terrainHeight) > selectedNoise.CaveThreshold)
    {
        set_block = 0;
    }

    if (voxel_y <= 1 && Params.ForceFloor) set_block = selectedNoise.SurfaceVoxelID;

    Voxels.Array[vox_aray_idx] = set_block;
}