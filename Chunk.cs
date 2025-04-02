using Godot;
using System;

public partial class Chunk : StaticBody3D
{
    [Export] public MeshInstance3D MeshInstance;
    [Export] public CollisionShape3D CollisionShape;

    public Godot.Collections.Array ArrayMeshData {get; private set;}

    public override void _Ready()
    {
        ArrayMeshData = [];
        ArrayMeshData.Resize((int)Mesh.ArrayType.Max);
    }
}