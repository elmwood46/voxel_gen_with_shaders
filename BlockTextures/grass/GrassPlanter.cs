using Godot;
using System;


[Tool]
public partial class GrassPlanter : MultiMeshInstance3D
{
    #region fields and properties
    [Export] public float Span
    {
        get => _span;
        set
        {
            _span = value;
            Rebuild(); // Automatically call Rebuild when Span is changed in the editor
        }
    }
    private float _span = 5.0f;

    [Export] public int Count
    {
        get => _count;
        set
        {
            _count = value;
            Rebuild(); // Automatically call Rebuild when Count is changed in the editor
        }
    }
    private int _count = 5000;

    [Export] public Vector2 BladeWidth
    {
        get => _width;
        set
        {
            _width = value;
            Rebuild(); // Automatically call Rebuild when Width is changed in the editor
        }
    }
    private Vector2 _width = new (0.01f, 0.02f);

    [Export] public Vector2 BladeHeight
    {
        get => _height;
        set
        {
            _height = value;
            Rebuild(); // Automatically call Rebuild when Height is changed in the editor
        }
    }
    private Vector2 _height = new (0.04f, 0.08f);

    [Export] public Vector2 SwayYawDegrees
    {
        get => _swayYawDegrees;
        set
        {
            _swayYawDegrees = value;
            Rebuild(); // Automatically call Rebuild when SwayYawDegrees is changed in the editor
        }
    }
    private Vector2 _swayYawDegrees = new (0.0f, 10.0f);

    [Export] public Vector2 SwayPitchDegrees
    {
        get => _swayPitchDegrees;
        set
        {
            _swayPitchDegrees = value;
            Rebuild(); // Automatically call Rebuild when SwayPitchDegrees is changed in the editor
        }
    }
    private Vector2 _swayPitchDegrees = new (0.0f, 10.0f);

    [Export] public Mesh TerrainMesh {get; set;}

    private RandomNumberGenerator rng = new ();
#endregion

    public GrassPlanter() {
        rng.Randomize(); // Ensures unique seed for randomness
        Rebuild();
    }

    public override void _Ready() {
        Rebuild();
    }

    public Tuple<Transform3D,Color>[] ComputeSpawns() {
        if (TerrainMesh == null) {
            GD.PrintErr("TerrainMesh is not set!");
            return null;
        }
        var surface = TerrainMesh.SurfaceGetArrays(0);
        Vector3[] positions = (Vector3[])surface[(int)Mesh.ArrayType.Vertex];
        Tuple<Transform3D,Color>[] spawns = new Tuple<Transform3D,Color>[positions.Length];
        int idx = 0;
        foreach (Vector3 pos in positions) {
            var q1 = new Quaternion(Vector3.Up, Mathf.DegToRad(rng.RandfRange(0f, 359f)));
            var transform = new Transform3D(new Basis(q1), pos);
            // store the random grass sway values in the custom data field (color is just used as a vec4 here)
            var custom_params = new Color( 
                rng.RandfRange(BladeWidth.X,BladeWidth.Y),
                rng.RandfRange(BladeHeight.X,BladeHeight.Y),
                rng.RandfRange(SwayYawDegrees.X,SwayYawDegrees.Y),
                rng.RandfRange(SwayPitchDegrees.X,SwayPitchDegrees.Y)
            );
            spawns[idx] = new (transform,custom_params);
            idx++;
        }
        return spawns;
    }

    public void Rebuild() {
        if (Multimesh == null) {
            Multimesh = new MultiMesh();
        }
        Multimesh.InstanceCount = 0;
        var spawns = ComputeSpawns();
        if (spawns == null) return; 
        Multimesh.Mesh = MeshFactory.SimpleGrass();
        Multimesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        Multimesh.UseCustomData = true;
        Multimesh.InstanceCount = Count;
        for (int index=0; index<Multimesh.InstanceCount;index++) {
            if (index < spawns.Length) {
                var spawn_instance = spawns[index];
                Multimesh.SetInstanceTransform(index, spawn_instance.Item1);
                Multimesh.SetInstanceCustomData(index, spawn_instance.Item2);
            } else break;
        }
    }
}