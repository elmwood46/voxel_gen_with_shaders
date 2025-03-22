using Godot;
using System;

[GlobalClass]
public partial class NoiseLayerResource : Resource
{
    [Export] public float Gain {get;set;} = 0.7f;
    [Export] public float Frequency {get;set;} = 700f;
    [Export] public float Lacunarity {get;set;} = 3.3f;
    [Export] public float Persistence {get;set;} = 0.27f;
    [Export] public int Octaves {get;set;} = 5;
    [Export] public float CaveScale {get;set;} = 50f;
    [Export] public float CaveThreshold {get;set;} = 0.75f;
    [Export] public int SurfaceVoxelId {get;set;} = 3;
    [Export] public int SubSurfaceVoxelId {get;set;} = 2;
}