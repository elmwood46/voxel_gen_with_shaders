#ifndef SHARED_DATA
#define SHARED_DATA
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

layout(set=0, binding = 1, std430 ) restrict buffer readonly chunk_position_buffer {
    vec3 Position;
} Uniform;

// input chunk information
layout(set=0, binding = 2, std430) restrict buffer readonly chunk_params {
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
    int _padding1;
    int _padding2;
    int _padding3;

     //MUST START ON MULTIPLE OF 16 BYTES
    vec4[] ChunkPositions;
} Params;

layout(set=0, binding = 3, std430) restrict buffer noise_buffer {
    NoiseLayer Array[];
} NoiseLayers;
#endif