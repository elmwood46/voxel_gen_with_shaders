using Godot;
using System;
using System.Collections.Generic;

public static class GrassFactory 
{
    // random barycentric coordinates
    static Vector3 RandBcc() {
        var u = Random.Shared.NextSingle();
        var v = Random.Shared.NextSingle();
        if (u+v >= 1.0f) {
            u = 1.0f - u;
            v = 1.0f - v;
        }
        return new Vector3(u,v,1.0f-(u+v));
    }

    static Vector3 FromBccVector3(Vector3 bcc, Vector3 a, Vector3 b, Vector3 c) {
        return a*bcc.X + b*bcc.Y + c*bcc.Z;
    }
    
    static Vector3 GetOrthogonal(Vector3 v) {
        float x = Mathf.Abs(v.X);
        float y = Mathf.Abs(v.Y);
        float z = Mathf.Abs(v.Z);
        var other = Vector3.Forward;
        if ((x > y) && (x > z)) {
            other = Vector3.Right;
        } else if (y > z) {
            other = Vector3.Up;
        }
        return v.Cross(other);
    }

    static Quaternion ShortestArc(Vector3 from, Vector3 to) {
        float dot = from.Dot(to);
        if (dot > 0.999999f) 
            return Quaternion.Identity; // identity quat - no rotation
        if (dot < -0.999999f) 
            return new Quaternion(GetOrthogonal(from), Mathf.Pi).Normalized();
        var axis = from.Cross(to);
        return new Quaternion(axis.X, axis.Y, axis.Z, 1 + dot).Normalized();
    }

    static float TriangleArea(Vector3 a, Vector3 b, Vector3 c) {
        float ab = a.DistanceTo(b);
        float bc = b.DistanceTo(c);
        float ca = c.DistanceTo(a);
        float s = (ab+bc+ca)/2.0f;
        return Mathf.Sqrt(s*(s-ab)*(s-bc)*(s-ca)); 
    }

    public static List<(Transform3D, Color)> Generate (Mesh mesh,float density,Vector2 bladewidth,Vector2 bladeheight,Vector2 degyaw,Vector2 degpitch)
    {
        if (mesh == null || mesh.GetSurfaceCount() == 0) {
            //GD.PrintErr("TerrainMesh is not set!");
            return new List<(Transform3D, Color)>();
        }

        // HACK for testing
       // density = 120;

        var surface = mesh.SurfaceGetArrays(0);
        var positions = (Vector3[])surface[(int)Mesh.ArrayType.Vertex];
        var indices = (int[])surface[(int)Mesh.ArrayType.Index];
        var normals = (Vector3[])surface[(int)Mesh.ArrayType.Normal];
        //GD.Print($"Array lengths: positions={positions.Length}, indices={indices.Length}, normals={normals.Length}");
        var spawns = new List<(Transform3D, Color)>();

        //var player_dist = player_pos.DistanceTo(chunk_pos+Vector3.One * (Chunk.VOXEL_SCALE * Chunk.CHUNK_SIZE*1.5f)); 
        
        // distance to chunk centre
        // drop off very fast - powers of four
       // var dropoff = Mathf.Pow(2,-player_dist/Mathf.RoundToInt(Chunk.CHUNK_SIZE*1.5f*Chunk.VOXEL_SCALE)); // halve density every 1.5 chunks
        //density *= dropoff;
        //if (density < 50.0f) return spawns;
        var radyaw = (Mathf.DegToRad(degyaw.X),Mathf.DegToRad(degyaw.Y));
        var radpitch = (Mathf.DegToRad(degpitch.X),Mathf.DegToRad(degpitch.Y));
        for (int i=0;i<indices.Length;i+=3) {
            var j = indices[i];
			var k = indices[i + 1];
			var l = indices[i + 2];
			var area = TriangleArea(
				positions[j],
				positions[k],
				positions[l]
			);
            
            var bladesPerFace = (int)Mathf.Round(area * density);
            // HACK - set sloped blocks to generate more grass blades
            if (normals[j].Dot(Vector3.Up) < 0.8f) bladesPerFace*=3;
            
            for (int blade=0;blade<bladesPerFace;blade++) {
                var uvw = RandBcc();
                var pos = FromBccVector3(uvw,positions[j],positions[k],positions[l]);

                // LOD less fast - powers of two
                //player_dist = player_pos.DistanceTo(pos+chunk_pos);
                //dropoff = Mathf.Pow(2,-player_dist/(Chunk.CHUNK_SIZE*1.5f*Chunk.VOXEL_SCALE));
                //if (blade > (int)bladesPerFace*dropoff) continue;

                // made everything up for blocky world
                var normal = Vector3.Up;///FromBccVector3(uvw,normals[j],normals[k],normals[l]);
                var q1 = new Quaternion(Vector3.Up,Mathf.Tau*GD.Randf());
                var q2 = ShortestArc(Vector3.Up,normal);
                var transform = new Transform3D(new Basis(q2*q1),pos); // make grass blade snap to surface normal
                var customParams = new Color( 
                    (float)GD.RandRange(bladewidth.X,bladewidth.Y),
                    (float)GD.RandRange(bladeheight.X,bladeheight.Y),
                    (float)GD.RandRange(radyaw.Item1,radyaw.Item2),
                    (float)GD.RandRange(radpitch.Item1,radpitch.Item2)
                );
                spawns.Add(new (transform, customParams));
            }
        }
        //GD.Print("generated ",spawns.Count," grass blades");
        return spawns;
    }
}