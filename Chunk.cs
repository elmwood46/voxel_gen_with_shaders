using Godot;
using System;

public partial class Chunk : StaticBody3D
{
    [Export] public MeshInstance3D MeshInstance;
    [Export] public CollisionShape3D CollisionShape;
}