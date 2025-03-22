using Godot;
using System;

[GlobalClass]
public partial class ChunkGenParamsResource : Resource
{
    [Export] public Vector3 SeedOffset {get;set;} = new Vector3(0.0f,0.0f,0.0f);
    [Export] public int CSP {get;set;} = ChunkManager.CSP;
    [Export] public int CSP3 {get;set;} = ChunkManager.CSP3;
    public int NumChunksToCompute  = 1;
    [Export] public int MaxWorldHeight {get;set;} = 250;
    [Export] public int StoneBlockID {get;set;} = BlockManager.StoneBlockId;
    [Export] public int OceanHeight {get;set;} = 10;
    [Export] public int NoiseLayerCount {get;set;} = 1;
    [Export] public int NoiseSeed {get;set;} = 0; 
    [Export] public float NoiseScale {get;set;} = 1.0f;
    [Export] public float CaveNoiseScale {get;set;} = 550.0f;
    [Export] public float CaveThreshold {get;set;} = 0.75f;
    [Export] public bool GenerateCaves {get;set;} = true;
    [Export] public bool ForceFloor {get;set;} = true;
}