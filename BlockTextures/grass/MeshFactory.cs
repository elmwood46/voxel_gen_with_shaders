using Godot;
using Godot.NativeInterop;
using System;

public static class MeshFactory
{
    public static Mesh SimpleGrass() {
        Vector3[] verts = new Vector3[] {
            new (-0.05f, 0f, 0f),
            new (0.05f, 0f, 0f),
            new (0f, 0.4f, 0f)
        };

        Vector2[] uvs = new Vector2[]
        {
            new (0, 0),
            new (1, 0),
            new(0.5f, 1.0f)
        };

        Vector3[] norms = new Vector3[] {
            new (0f, 1f, 0f),
            new (0f, 1f, 0f),
            new (0f, 1f, 0f)
        };

        // Create the mesh data array
        var meshData = new Godot.Collections.Array();
        meshData.Resize((int)Mesh.ArrayType.Max); // Reserve space for all types

        // Assign data to appropriate slots
        meshData[(int)Mesh.ArrayType.Vertex] = verts;
        meshData[(int)Mesh.ArrayType.TexUV] = uvs;
        meshData[(int)Mesh.ArrayType.Normal] = norms;
        var arrayMesh = new ArrayMesh();
        arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, meshData);
        arrayMesh.CustomAabb = new Aabb(new Vector3(-0.5f,0.0f,-0.5f), new Vector3(1.0f,1.0f,1.0f));

        // add shadow mesh
        /*
        var shadowMeshData = new Godot.Collections.Array();
        shadowMeshData.Resize((int)Mesh.ArrayType.Max); // Reserve space for vertices
        shadowMeshData[(int)Mesh.ArrayType.Vertex] = verts;
        var shadowMesh = new ArrayMesh();
        shadowMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, shadowMeshData);
        arrayMesh.ShadowMesh = shadowMesh;
        */
    
        // for debugging
        //ResourceSaver.Save(arrayMesh, "res://shaders/grass/grass_mesh.tres");

        return arrayMesh;
    }
}