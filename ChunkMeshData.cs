using System.Collections.Generic;
using Godot;

public class ChunkMeshData {
    public const byte CHUNK_SURFACE = 0;
    public const byte GRASS_SURFACE = 1;
    public const byte LAVA_SURFACE = 2;
    public const byte GOLD_SURFACE = 3;
    public const byte ALL_SURFACES = 4; // must be equal to the number of surfaces

    private readonly ArrayMesh[] _surfaces = new ArrayMesh[ALL_SURFACES + 1];
    private readonly ConcavePolygonShape3D _trimesh_shape;

    public ChunkMeshData(Dictionary<int,Dictionary<int,List<float>>> serializedData) {
        _surfaces[CHUNK_SURFACE] = new ArrayMesh();
        _surfaces[GRASS_SURFACE] = new ArrayMesh();
        _surfaces[LAVA_SURFACE] = new ArrayMesh();
        _surfaces[GOLD_SURFACE] = new ArrayMesh();
        _surfaces[ALL_SURFACES] = new ArrayMesh();
        UnifySurfaces();
        _trimesh_shape = _surfaces[ALL_SURFACES].GetSurfaceCount() > 0 ?_surfaces[ALL_SURFACES].CreateTrimeshShape() : new ConcavePolygonShape3D();
    }

    public ChunkMeshData(ArrayMesh[] input_surfaces, bool noCollisions = false) {
        _surfaces[CHUNK_SURFACE] = input_surfaces[CHUNK_SURFACE];
        _surfaces[GRASS_SURFACE] = input_surfaces[GRASS_SURFACE];
        _surfaces[LAVA_SURFACE] = input_surfaces[LAVA_SURFACE];
        _surfaces[GOLD_SURFACE] = input_surfaces[GOLD_SURFACE];
        _surfaces[ALL_SURFACES] = new ArrayMesh();
        UnifySurfaces();
        if (noCollisions) _trimesh_shape = null;
        else _trimesh_shape = _surfaces[ALL_SURFACES].GetSurfaceCount() > 0 ?_surfaces[ALL_SURFACES].CreateTrimeshShape() : new ConcavePolygonShape3D();
    }

    public bool HasSurfaceOfType(byte type) {
        return _surfaces[type]?.GetSurfaceCount() > 0;
    }

    public ArrayMesh GetSurface(byte type) {
        return _surfaces[type];
    }

    public ArrayMesh GetUnifiedSurfaces() {
        return _surfaces[ALL_SURFACES];
    }

    public ConcavePolygonShape3D GetTrimeshShape() {
        return _trimesh_shape;
    }

    private void UnifySurfaces() {
        _surfaces[ALL_SURFACES].ClearSurfaces();
        for (byte type = 0; type < ALL_SURFACES; type++) {
            if (HasSurfaceOfType(type)) {
                var surface = _surfaces[type];
                var material = type switch {
                    CHUNK_SURFACE => BlockManager.Instance.ChunkMaterial,
                    GRASS_SURFACE => BlockManager.Instance.ChunkMaterial,
                    LAVA_SURFACE => BlockManager.Instance.LavaShader,
                    GOLD_SURFACE => BlockManager.Instance.ChunkMaterialDamagePulse,
                    _ => BlockManager.Instance.ChunkMaterial
                };
                _surfaces[ALL_SURFACES].AddSurfaceFromArrays(
                    Mesh.PrimitiveType.Triangles,
                    surface.SurfaceGetArrays(0)
                );
                _surfaces[ALL_SURFACES].SurfaceSetMaterial(
                    _surfaces[ALL_SURFACES].GetSurfaceCount() - 1,
                    material
                );
            }
        }
    }

    public ArrayMesh GetNavigableSurfaces()
    {
        var ret = new ArrayMesh();
        for (byte type = 0; type < ALL_SURFACES; type++) {
            if (type != LAVA_SURFACE && HasSurfaceOfType(type)) {
                var surface = _surfaces[type];
                ret.AddSurfaceFromArrays(
                    Mesh.PrimitiveType.Triangles,
                    surface.SurfaceGetArrays(0)
                );
            }
        }
        return ret;
    }
}
