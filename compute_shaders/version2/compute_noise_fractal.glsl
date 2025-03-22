#ifndef COMPUTE_NOISE_FRACTAL
#define COMPUTE_NOISE_FRACTAL
float evaluateNoise(vec3 pos, float terrainHeight)
{
    float h = 1;
    float G = exp2(-h);
    float f = 1;
    float a = 1;
    float t = 0;
    
    for (int i = 0; i < 4; i++)
    {
        t += a * snoise(f * ((pos / (NoiseLayers.Array[0].CaveScale) / ((int(terrainHeight) > Params.OceanHeight) ? 1 : 6))));
        f *= 2.0;
        a *= G;
    }
    return t;
}

float fractalNoise(vec2 pos, int noisePosition)
{
    float v = 0;
    float amplitude_scale = 1;
    
    NoiseLayer b = NoiseLayers.Array[noisePosition];
    vec3 p1 = vec3(pos.xy, Params.NoiseSeed);
    for (int i = 0; i < NoiseLayers.Array[noisePosition].Octaves; i++)
    {
        v += snoise(vec3(p1.xy / b.Frequency, Params.NoiseSeed)) * amplitude_scale;

        p1.xy *= b.Lacunarity;
        
        amplitude_scale *= b.Persistence;
    }
    v = v * v;
    return clamp(v, 0.0, 1.0);
}

HeightAndNoise sampleHeightAtPoint(vec2 pos)
{
    float height = 0;

    float strongestWeight = 0;

    uint count = 0;
    uint noiseIndex = 0;
    float heightWeight;
    int i = 0;
    
    float weightH = fractalNoise(pos, i);
    
    height = clamp(weightH * max(6,Params.MaxWorldHeight-10), 0, max(16,Params.MaxWorldHeight));

    HeightAndNoise hb;
    hb.Height = uint(round(height));
    hb.NoiseIndex = noiseIndex;
    return hb;
}
#endif