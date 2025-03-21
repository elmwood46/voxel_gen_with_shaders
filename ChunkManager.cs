using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;

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
    public static readonly ConcurrentDictionary<Vector3I, Chunk> MESHCACHE = new();
    public static readonly PackedScene CHUNK_SCENE = GD.Load<PackedScene>("res://chunk_scene.tscn");
    public const int CHUNK_SIZE = 30;
    public const int CSP = CHUNK_SIZE + 2;
    public const int CSP2 = CSP * CSP;
    public const int CSP3 = CSP2 * CSP;
    public static readonly Vector3I Dimensions = new(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE);
    const int SUBCHUNKS = 1;
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
        foreach (var (_, chunk) in MESHCACHE)
        {
            chunk.QueueFree();
        }
        MESHCACHE.Clear();
        BLOCKCACHE.Clear();
        CantorPairing.Clear();
        // setup LOD meshes
		var row_len = RenderDistance;
		var halfWidth = Mathf.FloorToInt(RenderDistance / 2f);

		for (int x=-row_len/2; x <= row_len/2; x++)
		{
			for (int z=-row_len/2; z <= row_len/2; z++)
			{
				for (int y=0; y<YRenderDistance; y++)
				{
                    var chunk = CHUNK_SCENE.Instantiate<Chunk>();
                    AddChild(chunk);
					var pos = new Vector3I(x,y,z);

                    chunk.GlobalTransform = new Transform3D(Basis.Identity, pos*CHUNK_SIZE);
                    chunk.MeshInstance.Mesh = new ArrayMesh();
                    MESHCACHE.TryAdd(pos,chunk);
				}
			}
		}
    }

    public static void PrintDebugVariables()
    {
        ComputeChunk.PrintDebugVariables();
    }

    public void UpdateRenderDistance(bool slider_value_changed)
    {
        Callable.From(()=>{
            if (!slider_value_changed) return;
            var xz_slider = GetNode<Slider>("%XZRenderSlider");
            var y_slider = GetNode<Slider>("%YRenderSlider");
            var xz_label = GetNode<Label>("%XZRenderLabel");
            var y_label = GetNode<Label>("%YRenderLabel");
            RenderDistance = Mathf.FloorToInt(xz_slider.Value);
            YRenderDistance = Mathf.FloorToInt(y_slider.Value);
            y_label.Text = $"Y Render Distance: {YRenderDistance}";
            xz_label.Text = $"XZ Render Distance: {RenderDistance}";
        }).CallDeferred();
    }

    public void NoiseValueChanged(float value)
    {
        Callable.From(()=>{
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
        }).CallDeferred();
    }

    public void ChunkValueChanged(float value)
    {
        Callable.From(()=>{
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
        }).CallDeferred();
    }

    public static bool MeshOnGpu = true;
    public void ToggledGpuMeshing(bool value)
    {
        MeshOnGpu = value;
    }

    public override void _PhysicsProcess(double delta)
    {
        GetNode<Label>("%FPSLabel").Text = $"FPS: {Engine.GetFramesPerSecond()}";
    }

    public void UpdateMeshesTest(bool sequential = false)
    {
        ClearAndInitializeMeshCache();
        List<Vector3I> chunk_positions = [];
        foreach (var (meshpos, _) in MESHCACHE)
        {
            var pos = meshpos + (Vector3I)ChunkMesh.GlobalPosition;
            if (!CantorPairing.Contains(pos))
            {
                CantorPairing.Add(pos);
                chunk_positions.Add(pos);
            }
        }
        GD.Print("_____________________________________________");
        if (sequential)
        {
            ComputeChunk.GenerateMultiChunksSequentially(chunk_positions);
            var stopwatch = Stopwatch.StartNew();
            UpdateMeshCacheData();
            stopwatch.Stop();
            GD.Print($"UpdateMeshCacheData time elapsed: {stopwatch.ElapsedMilliseconds} ms");
        }
        else
        {
            ComputeChunk.GenerateMultiChunks(chunk_positions,MeshOnGpu);
        }
        GD.Print("_____________________________________________\n");
    }

    // free the rendering device when closing the scene
    public override void _ExitTree()
    {
        ComputeChunk.FreeRenderingDevice();
    }

    public async static void UpdateMeshCacheData()
    {
        var stopwatch = Stopwatch.StartNew();
        var task_list = new List<Task>();
        foreach (var (meshpos, chunk)  in MESHCACHE)
        {
            task_list.Add(Task.Run(() => {
            // do multithreaded greedy meshing of LOD meshes
                var new_arraymesh = BuildChunkMesh(meshpos);
                var xform = new Transform3D(Basis.Identity, (Godot.Vector3)meshpos*CHUNK_SIZE);
                var trimesh_shape = new_arraymesh.CreateTrimeshShape();

                Callable.From(()=>{
                    chunk.MeshInstance.Mesh = new_arraymesh;
                    chunk.CollisionShape.Shape = trimesh_shape;
                    chunk.Transform = xform;
                }).CallDeferred();
                return Task.CompletedTask;
            }));
        }
        await Task.WhenAll(task_list);
        stopwatch.Stop();
        GD.Print($"UpdateMeshCacheData time elapsed: {stopwatch.ElapsedMilliseconds} ms");
        return;
    }
}