using System;
using System.Collections.Generic;
using Godot;

[Tool]
public partial class Grass : MultiMeshInstance3D {
    //public static readonly ShaderMaterial GrassBladeMaterial = ResourceLoader.Load("res://shaders/grass/multimesh_grass_shader.tres") as ShaderMaterial;

    public Vector3 PlayerPosition { get; set; }
    public Vector3 ChunkPosition { get; set; }

    public List<(Transform3D,Color)> Spawns {get; private set;}

    public override void _Ready() {
        //MaterialOverride = GrassBladeMaterial;
    }

    [Export] public float Density
    {
        get => _density;
        set
        {
            if (value < 1.0f) value = 1.0f;
            _density = value;
            //if (Engine.IsEditorHint()) Rebuild(); // Automatically call Rebuild when Span is changed in the editor
        }
    }
    private float _density = 5000.0f;

    [Export] public Vector2 BladeWidth
    {
        get => _width;
        set
        {
            _width = value;
            //if (Engine.IsEditorHint()) Rebuild(); // Automatically call Rebuild when Width is changed in the editor
        }
    }
    private Vector2 _width = new (0.01f, 0.02f);

    [Export] public Vector2 BladeHeight
    {
        get => _height;
        set
        {
            _height = value;
            //if (Engine.IsEditorHint()) Rebuild(); // Automatically call Rebuild when Height is changed in the editor
        }
    }
    private Vector2 _height = new (0.04f, 0.08f);   

    [Export] public Vector2 SwayYawDegrees
    {
        get => _swayYawDegrees;
        set
        {
            _swayYawDegrees = value;
            //if (Engine.IsEditorHint()) Rebuild(); // Automatically call Rebuild when SwayYawDegrees is changed in the editor
        }
    }
    private Vector2 _swayYawDegrees = new (0.0f, 10.0f);

    [Export] public Vector2 SwayPitchDegrees
    {
        get => _swayPitchDegrees;
        set
        {
            _swayPitchDegrees = value;
            //if (Engine.IsEditorHint()) Rebuild(); // Automatically call Rebuild when SwayPitchDegrees is changed in the editor
        }
    }
    private Vector2 _swayPitchDegrees = new (0.04f, 0.08f);

    [Export] public Mesh TerrainMesh {
        get => _terrainMesh;
        set
        {
            _terrainMesh = value;
            //if (Engine.IsEditorHint()) Rebuild(); // Automatically call Rebuild when TerrainMesh is changed in the editor
        }
    }
    private Mesh _terrainMesh;

    public Grass() {
        //if (Engine.IsEditorHint()) Rebuild();
    }

    public static List<(Transform3D, Color)> GenSpawns(Grass grass) {
        var spawns =  GrassFactory.Generate(
            grass.TerrainMesh,
            grass.Density,
            grass.BladeWidth,
            grass.BladeHeight,
            grass.SwayYawDegrees,
            grass.SwayPitchDegrees
        );
        grass.Spawns = spawns;
        return spawns;
    }

    public static MultiMesh GenMultiMesh(Grass grass) {
        var mm = new MultiMesh
            {
                InstanceCount = 0,
                Mesh = MeshFactory.SimpleGrass(),
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                UseColors = false
            };
        var spawns = GenSpawns(grass);
	    mm.InstanceCount = spawns.Count;
        for (int i=0;i<mm.InstanceCount;i++) {
            var spawn = spawns[i];
            mm.SetInstanceTransform(i, spawn.Item1);
            mm.SetInstanceCustomData(i, spawn.Item2);
        }
        return mm;
    }

    public static MultiMesh Rebuild(Grass grass) {
        var spawns = GenSpawns(grass);
        var mm = new MultiMesh
            {
                InstanceCount = 0,
                Mesh = MeshFactory.SimpleGrass(),
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                UseColors = false
            };
        mm.InstanceCount = spawns.Count;
        if (spawns.Count == 0) return mm;

        for (int i=0;i<spawns.Count;i++) {
            var spawn = spawns[i];
            mm.SetInstanceTransform(i, spawn.Item1);
            mm.SetInstanceCustomData(i, spawn.Item2);
        }
        return mm;
    }

    public void Rebuild(List<(Transform3D,Color)> spawns = null) {
        /*
        if (TerrainMesh == null) return;
        Multimesh ??= new MultiMesh
            {
                InstanceCount = 0,
                Mesh = MeshFactory.SimpleGrass(),
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                UseColors = false
            };
        */
        spawns = GrassFactory.Generate(TerrainMesh, Density, BladeWidth, BladeHeight, SwayYawDegrees, SwayPitchDegrees);
        Multimesh.InstanceCount = spawns.Count;
        if (spawns.Count == 0) return;

        for (int i=0;i<spawns.Count;i++) {
            var spawn = spawns[i];
            Multimesh.SetInstanceTransform(i, spawn.Item1);
            Multimesh.SetInstanceCustomData(i, spawn.Item2);
        }
    }

    internal void SetSpawns(List<(Transform3D, Color)> spawns)
    {
        Spawns = spawns;
    }
}