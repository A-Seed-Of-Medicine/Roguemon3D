using UnityEngine;

public static class BaseQuad {
    static Mesh _shared;
    public static Mesh Shared {
        get {
            _shared = new Mesh { name = "BaseQuad" };

            var verts = new [] {
                new Vector3(-0.5f,-0.5f,0f),
                new Vector3( 0.5f,-0.5f,0f),
                new Vector3( 0.5f, 0.5f,0f),
                new Vector3(-0.5f, 0.5f,0f),
            };
            var uvs = new [] {
                new Vector2(0f,0f),
                new Vector2(1f,0f),
                new Vector2(1f,1f),
                new Vector2(0f,1f),
            };
            var tris = new [] { 0,1,2, 0,2,3 }; // consistent winding


            var norms = new [] {
                -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward
            };
            var tangents = new [] {
                new Vector4(1f,0f,0f,-1f),
                new Vector4(1f,0f,0f,-1f),
                new Vector4(1f,0f,0f,-1f),
                new Vector4(1f,0f,0f,-1f),
            };

            _shared.SetVertices(verts);
            _shared.SetUVs(0, uvs);
            _shared.SetNormals(norms);
            _shared.SetTangents(tangents);
            _shared.SetTriangles(tris, 0, true);
            _shared.RecalculateBounds();
            _shared.UploadMeshData(false);
            return _shared;
        }
    }
}