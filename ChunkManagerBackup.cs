/*
using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

public enum SlopeType {
    None=0,
    Side=1,
    Corner=2,
    InvCorner=3
}

public enum ChunkLOD {
    NoLOD = 1,
    Half = 2,
    Quarter = 4,
    Eighth = 8,
    Sixteenth = 16
}

public struct GreedyQuad
{
    public int col; // column offset
    public int row; // row offset
    public int delta_row; // width of quad
    public int delta_col; // height of quad

    public GreedyQuad(int col, int row, int w, int h)
    {
        this.col = col;
        this.row = row;
        this.delta_row = w;
        this.delta_col = h;
    }
}

public partial class ChunkManager : Node3D
{
    public static readonly ConcurrentDictionary<Vector3I, int[]> BLOCKCACHE = new();
    public static readonly ConcurrentDictionary<Vector3I, (Rid, Mesh)> MESHCACHE = new();
    public const int CHUNK_SIZE = 30;
    public const int CSP = CHUNK_SIZE + 2;
    public const int CSP2 = CSP * CSP;
    public const int CSP3 = CSP2 * CSP;
	public int RenderDistance = 5;
	public int YRenderDistance = 1;
    const float INVSQRT2 = 0.70710678118f;
    public const int BLOCK_DAMAGE_BITS_OFFSET = 16;
	public const int BLOCK_SLOPE_BITS_OFFSET = 24;
    private static readonly Godot.Vector3 SlopedNormalNegZ = new(0, INVSQRT2, -INVSQRT2);
    private static readonly Godot.Vector3 SlopedCornerNormalNegZ = new(INVSQRT2, INVSQRT2, -INVSQRT2);

    [Export] public MeshInstance3D ChunkMesh {get; set;}
    private Vector3I _prevChunkPosition = new(int.MinValue,int.MinValue,int.MinValue);

    private static readonly Vector3I[] CUBE_VERTS =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(0, 1, 0),
            new(1, 1, 0),
            new(0, 0, 1),
            new(1, 0, 1),
            new(0, 1, 1),
            new(1, 1, 1)
        };

    // vertices for a square face of the above, cube depending on axis
    // axis has 2 entries for each coordinate - y, x, z and alternates between -/+
    // axis 0 = down, 1 = up, 2 = right, 3 = left, 4 = front (-z is front in godot), 5 = back
    private static readonly int[,] CUBE_AXIS =
        {
            {0, 4, 5, 1}, // bottom
            {2, 3, 7, 6}, // top
            {6, 4, 0, 2}, // left
            {3, 1, 5, 7}, // right
            {2, 0, 1, 3}, // front
            {7, 5, 4, 6}  // back
        };

    public static ChunkManager Instance;

    public ChunkManager() {
        Instance = this;
    }

    public override void _Ready()
    {
        UpdateRenderDistance(true);
    }

    public void ClearAndInitializeMeshCache()
    {
        MESHCACHE.Clear();
        BLOCKCACHE.Clear();
        CantorPairing.Clear();
        // setup LOD meshes
		var row_len = RenderDistance;
		var halfWidth = Mathf.FloorToInt(RenderDistance / 2f);
		var placeholder_node = new Node3D();
		AddChild(placeholder_node);
		for (int x=-row_len/2; x <= row_len/2; x++)
		{
			for (int z=-row_len/2; z <= row_len/2; z++)
			{
				for (int y=0; y<YRenderDistance; y++)
				{
					// if (x >= -halfWidth && x < RenderDistance-halfWidth
					// && z >= -halfWidth && z < RenderDistance-halfWidth
					// && y >= 0 && y < Y_CHUNKS) {
					// 	continue;
					// }
					// Create a visual instance (for 3D).
					Rid instance = RenderingServer.InstanceCreate();
					// Set the scenario from the world, this ensures it
					// appears with the same objects as the scene.
					Rid scenario = placeholder_node.GetWorld3D().Scenario;
					RenderingServer.InstanceSetScenario(instance, scenario);

					var pos = new Vector3I(x,y,z);
					MESHCACHE.TryAdd(pos, new (instance, new Mesh()));
				}
			}
		}
		RemoveChild(placeholder_node);
    }

    public void UpdateRenderDistance(bool slider_value_changed)
    {
        if (!slider_value_changed) return;
        var xz_slider = GetNode<Slider>("%XZRenderSlider");
        var y_slider = GetNode<Slider>("%YRenderSlider");
        var xz_label = GetNode<Label>("%XZRenderLabel");
        var y_label = GetNode<Label>("%YRenderLabel");
        RenderDistance = Mathf.FloorToInt(xz_slider.Value);
        YRenderDistance = Mathf.FloorToInt(y_slider.Value);
        y_label.Text = $"Y Render Distance: {YRenderDistance}";
        xz_label.Text = $"XZ Render Distance: {RenderDistance}";
    }

    public void NoiseValueChanged(float value)
    {
        var gain = GetNode<SpinBox>("%Gain");
        var frequency = GetNode<SpinBox>("%Frequency");
        var lacun = GetNode<SpinBox>("%Lacunarity");
        var persistence = GetNode<SpinBox>("%Persistence");
        var octaves = GetNode<SpinBox>("%Octaves");
        var cave_scale = GetNode<SpinBox>("%CaveScale");
        var cave_thresh = GetNode<SpinBox>("%CaveThreshold");

        var noise_layer = new NoiseLayer()
        {
            Gain = (float)gain.Value,
            Frequency = (float)frequency.Value,
            Lacunarity = (float)lacun.Value,
            Persistence = (float)persistence.Value,
            Octaves = (int)octaves.Value,
            CaveScale = (float)cave_scale.Value,
            CaveThreshold = (float)cave_thresh.Value
        };

        ComputeChunk.SetNoiseLayer(0, noise_layer);
    }

    public void ChunkValueChanged(float value)
    {
        var max_world = GetNode<SpinBox>("%MaxWorldHeight");
        var ocean_height = GetNode<SpinBox>("%OceanHeight");
        var cave_scale = GetNode<SpinBox>("%CaveNoiseScale");
        var cave_thresh = GetNode<SpinBox>("%ChunkCaveThreshold");
        var gen_caves = GetNode<SpinBox>("%GenerateCaves");
        var force_floor = GetNode<SpinBox>("%ForceFloor");
        ComputeChunk.OceanHeight = (int)ocean_height.Value;
        ComputeChunk.MaxWorldHeight = (int)max_world.Value;
        ComputeChunk.CaveNoiseScale = (float)cave_scale.Value;
        ComputeChunk.CaveThreshold = (float)cave_thresh.Value;
        ComputeChunk.GenerateCaves = gen_caves.Value == 1.0;
        ComputeChunk.ForceFloor = force_floor.Value == 1.0;
    }

    public override void _Process(double delta) {
        var chunkpos = (Vector3I)ChunkMesh.GlobalPosition;
        if (_prevChunkPosition != chunkpos && ChunkMesh is not null) {
            //GD.Print($"prev chunk position {_prevChunkPosition}, chunk position {chunkpos}");
            _prevChunkPosition = chunkpos;
            //UpdateMeshesProcess();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        GetNode<Label>("%FPSLabel").Text = $"FPS: {Engine.GetFramesPerSecond()}";
    }

    public static void SampleGenerate(Vector3I chunk_index)
    {
        var k = new int[CSP3];
        for (int x=0;x<CSP;x++)
        {
            for (int y=0;y<CSP;y++)
            {
                for (int z=0;z<CSP;z++)
                {
                    k[x + y*CSP2 + z*CSP] = Random.Shared.NextSingle() > 0.5f ? BlockManager.StoneBlockId : 0;
                }
            }
        }
        BLOCKCACHE[chunk_index] = k;
    }

    public void UpdateMeshesTest(bool sequential = false)
    {
        ClearAndInitializeMeshCache();
        List<Vector3I> chunk_positions = [];
        foreach (var (meshpos, (instance_rid, _)) in MESHCACHE)
        {
            var pos = meshpos + (Vector3I)ChunkMesh.GlobalPosition;
            if (!CantorPairing.Contains(pos))
            {
                CantorPairing.Add(pos);
                chunk_positions.Add(pos);
            }
        }


        if (sequential)
        {
            ComputeChunk.GenerateMultiChunksSequentially(chunk_positions);
        }
        else
        {
            //RenderingServer.CallOnRenderThread(Callable.From(() =>
            //{
                ComputeChunk.GenerateMultiChunks(chunk_positions);
            //}));
        }

        var stopwatch = Stopwatch.StartNew();
        UpdateMeshCacheData();
        stopwatch.Stop();
        GD.Print($"UpdateMeshCacheData time elapsed: {stopwatch.ElapsedMilliseconds} ms");

        //GD.Print($"Blockcache size: {BLOCKCACHE.Count}, Meshcache size: {MESHCACHE.Count}");

    }

    // free the rendering device when closing the scene
    public override void _ExitTree()
    {
        ComputeChunk.FreeRenderingDevice();
    }

    public void UpdateMeshesProcess()
    {
        
        
        foreach (var (meshpos, (instance_rid, _)) in MESHCACHE)
        {
            var pos = meshpos + (Vector3I)ChunkMesh.GlobalPosition;
            if (!CantorPairing.Contains(pos))
            {
                CantorPairing.Add(pos);
                ComputeChunk.GenerateBlocks(pos);
                //SampleGenerate(pos);
            }
        }

        UpdateMeshCacheData();

        
        //var t = new Timer(){WaitTime = 0.5f, Autostart = true, OneShot = true};
        //t.Timeout += () => {
        //    IMPLEMENT_LAMBDA_HERE;
        //    t.QueueFree();
        //};
        // AddChild(t);
    }

    public static void UpdateMeshCacheData()
    {
        var update_lod_meshes = new ConcurrentBag<(Vector3I, (Rid, Mesh))>();

        foreach (var (meshpos, (instance_rid, _)) in MESHCACHE)
        {
            var pos = meshpos + (Vector3I)Instance.ChunkMesh.GlobalPosition;

            // do multithreaded greedy meshing of LOD meshes
            var lod = ChunkLOD.Sixteenth;
            var use_block_lod = true;// (dist_sq > 40000); // 40000 = 200 blocks
            //var new_arraymesh = LODBuildChunkMesh(pos,lod,use_block_lod);
            var new_arraymesh = BuildChunkMesh(pos);
            var new_mesh_rid = new_arraymesh.GetRid();
            update_lod_meshes.Add((meshpos, (instance_rid, new_arraymesh)));

            var xform = new Transform3D(Basis.Identity, (Godot.Vector3)pos*CHUNK_SIZE);
            RenderingServer.CallOnRenderThread(Callable.From(()=>UpdateInstanceRidData(instance_rid, new_mesh_rid, xform)));
        }

        foreach (var (meshpos, (rid, mesh)) in update_lod_meshes)
        {
            MESHCACHE[meshpos] = (rid,mesh);
        }
    }

    public static void UpdateInstanceRidData(Rid instance, Rid new_mesh_rid, Transform3D new_transform)
    {
        RenderingServer.InstanceSetBase(instance, new_mesh_rid);
        RenderingServer.InstanceSetTransform(instance, new_transform);
    }
}
*/