using Godot;
using System;

public partial class Chunk : StaticBody3D
{
    public const int CHUNK_SIZE = 30;
    public const int CSP = CHUNK_SIZE + 2;
    public const int CSP2 = CSP * CSP;
    public const int CSP3 = CSP2 * CSP;

    [Export] public MeshInstance3D MeshInstance;
    [Export] public CollisionShape3D CollisionShape;

    public Vector3I ChunkPosition {get;set;}

    public void UploadMesh(MeshArrayDataPacket mesh_data_packet)
    {
        var mesh = (ArrayMesh)MeshInstance.Mesh;
        mesh.ClearSurfaces();

        if (mesh_data_packet.Vertices.Length == 0)
        {
            CollisionShape.Shape = null;
            return;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = mesh_data_packet.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = mesh_data_packet.Normals;
        arrays[(int)Mesh.ArrayType.TexUV] = mesh_data_packet.UVs;

        var xform = new Transform3D(Basis.Identity, (Vector3)ChunkPosition*ChunkManager.CHUNK_SIZE);
        
        Callable.From(() => {
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            GlobalTransform = xform;
        }).CallDeferred();
    }

    public void ClearData()
    {
        ((ArrayMesh)MeshInstance.Mesh)?.ClearSurfaces();
        CollisionShape.Shape ??= null;
    }

    public void Deactivate()
    {
        ClearData();
        Visible = false;
    }
}