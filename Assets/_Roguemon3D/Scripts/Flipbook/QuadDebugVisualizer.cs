// QuadDebugVisualizer.cs
// Drop in /Assets/Editor or /Assets/Scripts. Works in Edit and Play.

using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public class QuadDebugVisualizer : MonoBehaviour {
    [Header("Draw")]
    public bool drawBounds = true;
    public bool drawVertices = true;
    public bool drawNormals = true;
    public bool drawTangents = true;
    [Range(0.01f, 0.25f)] public float handleSize = 0.06f;
    [Range(0.05f, 1f)] public float vectorLen = 0.3f;

    [Header("UV Checker (optional)")]
    public bool applyUVChecker = false;
    [Range(2, 64)] public int checkerTiles = 8;
    public int checkerResolution = 256;

    Mesh _mesh;
    MeshFilter _mf;
    MeshRenderer _mr;

    void OnEnable() {
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
        CacheMesh();
    }

    void Update() {
        CacheMesh();
        if (applyUVChecker) ApplyChecker();
    }

    void CacheMesh() {
        if (_mf == null) _mf = GetComponent<MeshFilter>();
        if (_mf != null) _mesh = Application.isPlaying ? _mf.mesh : _mf.sharedMesh;
    }

    void ApplyChecker() {
        if (_mr == null) return;
        var mat = _mr.sharedMaterial;
        // Log UVs
        for (int i = 0; i < Mathf.Min(4, _mesh.uv.Length); i++) {
            var uv = _mesh.uv[i];
             Debug.Log($"UV {i}: {uv.x:0.###}, {uv.y:0.###}");
        }
        if (mat == null) {
#if UNITY_EDITOR
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.name = "UVChecker_Unlit";
            _mr.sharedMaterial = mat;
#else
            return;
#endif
        }
        var tex = BuildUVChecker(checkerResolution, checkerTiles);
        // URP Unlit base map is "_BaseMap". Also set legacy "_MainTex" for safety.
        mat.SetTexture("_BaseMap", tex);
        mat.SetTexture("_MainTex", tex);
    }

    static Texture2D BuildUVChecker(int res, int tiles) {
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false, true);
        tex.wrapMode = TextureWrapMode.Repeat;
        for (int y = 0; y < res; y++) {
            for (int x = 0; x < res; x++) {
                float u = (float)x / (res - 1);
                float v = (float)y / (res - 1);
                int cx = (int)Mathf.Floor(u * tiles);
                int cy = (int)Mathf.Floor(v * tiles);
                bool check = ((cx + cy) & 1) == 0;
                // R=U, G=V, B=checker
                Color c = new Color(u, v, check ? 1f : 0.25f, 1f);
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply(false, false);
        tex.name = $"UVChecker_{res}_{tiles}";
        return tex;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected() {
        if (_mesh == null) return;

        var t = transform;

        if (drawBounds) {
            Gizmos.color = Color.white;
            var b = _mesh.bounds;
            var center = t.TransformPoint(b.center);
            var size = Vector3.Scale(b.size, AbsVec(t.lossyScale));
            Gizmos.DrawWireCube(center, size);
        }

        var verts = _mesh.vertices;
        var uvs   = _mesh.uv;
        var norms = _mesh.normals;
        var tangs = _mesh.tangents;

        int n = verts.Length;
        for (int i = 0; i < n; i++) {
            var wp = t.TransformPoint(verts[i]);

            if (drawVertices) {
                Gizmos.color = Color.yellow;
                Handles.color = Color.yellow;
                float hs = HandleSize(wp) * handleSize;
                Gizmos.DrawSphere(wp, hs);
                string label = $"v{i}\nuv({(i < uvs.Length ? uvs[i].x.ToString("0.###") : "?")}, {(i < uvs.Length ? uvs[i].y.ToString("0.###") : "?")})";
                Handles.Label(wp + t.up * hs * 2f, label);
            }

            if (drawNormals && norms != null && norms.Length == n) {
                Gizmos.color = Color.cyan;
                var nDir = t.TransformDirection(norms[i]).normalized;
                Debug.Log($"Normal {i}: local {norms[i]} world {nDir}");
                Gizmos.DrawLine(wp, wp + nDir * vectorLen);
            }

            if (drawTangents && tangs != null && tangs.Length == n && norms != null && norms.Length == n) {
                var tDir = new Vector3(tangs[i].x, tangs[i].y, tangs[i].z).normalized;
                var wTan = t.TransformDirection(tDir);
                var wNor = t.TransformDirection(norms[i]).normalized;
                var wBitan = Vector3.Cross(wNor, wTan) * Mathf.Sign(tangs[i].w);
                Debug.Log($"Tangent {i}: local {tDir} world {wTan} norms{norms[i]} bitan {wBitan} (w {tangs[i].w})");
                Gizmos.color = Color.red;   Gizmos.DrawLine(wp, wp + wTan   * vectorLen);
                Gizmos.color = Color.green; Gizmos.DrawLine(wp, wp + wBitan * vectorLen);
            }
        }

        // Triangle centers
        var tris = _mesh.triangles;
        Gizmos.color = new Color(1f, 1f, 0f, 0.75f);
        for (int tIdx = 0; tIdx < tris.Length; tIdx += 3) {
            int a = tris[tIdx], b = tris[tIdx + 1], c = tris[tIdx + 2];
            var p = (verts[a] + verts[b] + verts[c]) / 3f;
            var wp = transform.TransformPoint(p);
            float hs = HandleSize(wp) * handleSize * 0.6f;
            Gizmos.DrawWireSphere(wp, hs);
            Debug.Log($"Tri {tIdx/3}: v{a}, v{b}, v{c}");
        }
    }

    static Vector3 AbsVec(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    static float HandleSize(Vector3 worldPos) {
        return HandleUtility.GetHandleSize(worldPos) * 0.04f;
    }

    // Utilities to swap meshes and verify against Unity's built-in Quad
    [MenuItem("Tools/Quads/Assign Unity Built-in Quad To Selected")]
    static void AssignUnityQuadToSelected() {
        foreach (var obj in Selection.gameObjects) {
            var mf = obj.GetComponent<MeshFilter>();
            if (!mf) continue;
            var temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var unityQuad = temp.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(temp);
            mf.sharedMesh = unityQuad;
        }
    }

    [MenuItem("Tools/Quads/Assign Centered BaseQuad To Selected")]
    static void AssignCenteredBaseQuad() {
        foreach (var obj in Selection.gameObjects) {
            var mf = obj.GetComponent<MeshFilter>();
            if (!mf) continue;
            mf.sharedMesh = MakeCenteredBaseQuad();
        }
    }

    static Mesh MakeCenteredBaseQuad() {
        var m = new Mesh { name = "CenteredBaseQuad" };
        m.vertices = new [] {
            new Vector3(-0.5f,-0.5f,0f),
            new Vector3( 0.5f,-0.5f,0f),
            new Vector3( 0.5f, 0.5f,0f),
            new Vector3(-0.5f, 0.5f,0f),
        };
        m.uv = new [] {
            new Vector2(0f,0f),
            new Vector2(1f,0f),
            new Vector2(1f,1f),
            new Vector2(0f,1f),
        };
        m.triangles = new [] { 0,1,2, 0,2,3 };
        m.normals = new [] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        var tan = new Vector4(1f,0f,0f,1f);
        m.tangents = new [] { tan, tan, tan, tan };
        m.RecalculateBounds();
        return m;
    }
#endif
}
