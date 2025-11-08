using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class ArrayFlipbookAVS : MonoBehaviour
{
    [Header("Bank / Clip")]
    public SpriteArrayBank bank;
    public string clipName { get; private set; }

    [Header("Overrides")]
    public bool flipX;
    public float speedMultiplier = 1f;
    public bool playOnAwake = true;
    public bool randomizedStart = false;

    [Header("Targets")]
    [Tooltip("Renderers to receive AVS additional vertex streams")]
    public MeshRenderer[] renderers;
    [Tooltip("MeshFilters that define the base meshes (1:1 with Renderers)")]
    public MeshFilter[] filters;

    SpriteArrayBank.Clip _clip;

    // Per-target data
    [SerializeField, HideInInspector] Mesh[] _avsMeshes;
    [SerializeField, HideInInspector] Vector2[][] _uv2PerTarget; // per-target UV2 arrays

    float _time;
    bool _playing;

    void Awake()
    {
        // If user left arrays empty, try to auto-bind on this GameObject
        if ((filters == null || filters.Length == 0) &&
            (renderers == null || renderers.Length == 0))
        {
            var r = GetComponent<MeshRenderer>();
            var f = GetComponent<MeshFilter>();
            if (r && f)
            {
                renderers = new[] { r };
                filters   = new[] { f };
            }
        }

        // Length guard
        int n = Mathf.Min(renderers?.Length ?? 0, filters?.Length ?? 0);
        if (n == 0) { enabled = false; return; }

        // Ensure base meshes exist
        for (int i = 0; i < n; i++)
        {
            if (filters[i] && filters[i].sharedMesh == null)
                filters[i].sharedMesh = BaseQuad.Shared;
        }

        CreateOrBindAVSAll();
        if (randomizedStart)
            _time = Random.Range(0f, 1f);
        if (bank != null && !string.IsNullOrEmpty(clipName))
            SetClip(clipName, _time, true);

        WriteLayer(CurrentLayerOrDefault());

        _playing = playOnAwake;
    }

    void OnDestroy()
    {
        if (_avsMeshes == null) return;
        for (int i = 0; i < _avsMeshes.Length; i++)
        {
            if (_avsMeshes[i]) Destroy(_avsMeshes[i]);
        }
    }

    void CreateOrBindAVSAll()
    {
        int n = Mathf.Min(renderers.Length, filters.Length);
        _avsMeshes = new Mesh[n];
        _uv2PerTarget = new Vector2[n][];

        for (int i = 0; i < n; i++)
        {
            var mr = renderers[i];
            var mf = filters[i];
            if (!mr || !mf) continue;

            var baseMesh = mf.sharedMesh ?? BaseQuad.Shared;
            int vc = baseMesh.vertexCount;

            var avs = new Mesh { name = $"AVS_Layer_{i}" };
            avs.MarkDynamic();

            // Mirror base positions so we do not change POSITION
            var baseVerts = new List<Vector3>(vc);
            baseMesh.GetVertices(baseVerts);
            avs.SetVertices(baseVerts);

            // Only UV2 (channel 1)
            var uv2 = new Vector2[vc];
            for (int v = 0; v < vc; v++) uv2[v] = Vector2.zero;
            avs.SetUVs(1, uv2);

            // Clear other channels
            avs.SetUVs(0, (List<Vector2>)null);
            avs.colors = null;
            avs.tangents = null;
            avs.normals = null;

            // Bounds from base
            avs.bounds = baseMesh.bounds;

            // No indices/submeshes needed
            avs.subMeshCount = 0;
            avs.UploadMeshData(false);

            // Bind
            mr.additionalVertexStreams = avs;

            // Store
            _avsMeshes[i] = avs;
            _uv2PerTarget[i] = uv2;
        }
    }

    public void SetClip(string newClipName, float startNormalizedTime = 0f, bool overrride = false)
    {
        if (!overrride && clipName == newClipName) return;
        _clip = bank?.GetClip(newClipName);
        clipName = newClipName;
        _time = Mathf.Clamp01(startNormalizedTime) * Mathf.Max(1, _clip?.frameCount ?? 1);
        WriteLayer(CurrentLayerOrDefault());
    }

    public void ApplyPPUScale(SpriteArrayBank b)
    {
        return;
        if (!b || !b.material) return;
        var arr = b.material.GetTexture("_SpriteArray") as Texture2DArray;
        if (!arr) return;
        float wUnits = arr.width / b.pixelsPerUnit;
        float hUnits = arr.height / b.pixelsPerUnit;
        transform.localScale = new Vector3(wUnits, hUnits, wUnits);
    }

    void Update()
    {
        if (_clip == null || !_playing) return;

        _time += Time.deltaTime * (_clip.fps * Mathf.Max(0.001f, speedMultiplier));
        //Debug.Log(gameObject.name + " : "+ _clip.name + " : Time = " + _time + " Loop=" + _clip.loop);
        int layer;
        if (_clip.loop)
        {
            int span = Mathf.Max(1, _clip.frameCount);
            int frameOffset = Mathf.FloorToInt(_time) % span;
            //Debug.Log(gameObject.name + " span: " + frameOffset);
            layer = _clip.firstLayer + frameOffset;
        }
        else
        {
            int frameOffset = Mathf.Min(Mathf.FloorToInt(_time), _clip.frameCount - 1);
            layer = _clip.firstLayer + frameOffset;
        }

        WriteLayer(layer);
    }

    int CurrentLayerOrDefault()
    {
        if (_clip == null) return 0;
        int span = Mathf.Max(1, _clip.frameCount);
        int frameOffset = Mathf.FloorToInt(_time) % span;
        return _clip.firstLayer + frameOffset;
    }

    void WriteLayer(int layer)
    {
        if (_avsMeshes == null || _uv2PerTarget == null) CreateOrBindAVSAll();

        float L = layer;
        int n = _avsMeshes.Length;

        for (int t = 0; t < n; t++)
        {
            var avs = _avsMeshes[t];
            var uv2 = _uv2PerTarget[t];
            if (!avs || uv2 == null) continue;

            int sign = flipX ? -1 : 1;

            // If base mesh changed at runtime, resize UV2
            var targetVC = uv2.Length;
            var filter = (t < filters.Length) ? filters[t] : null;
            var baseMesh = filter ? (filter.sharedMesh ?? BaseQuad.Shared) : null;
            int vc = baseMesh ? baseMesh.vertexCount : targetVC;
            if (vc != targetVC)
            {
                uv2 = new Vector2[vc];
                for (int i = 0; i < vc; i++) uv2[i] = Vector2.zero;
                _uv2PerTarget[t] = uv2;

                // Mirror new positions
                if (baseMesh)
                {
                    var baseVerts = new List<Vector3>(vc);
                    baseMesh.GetVertices(baseVerts);
                    avs.SetVertices(baseVerts);
                    avs.bounds = baseMesh.bounds;
                }
            }

            for (int i = 0; i < uv2.Length; i++)
            {
                var v = uv2[i];
                v.x = L;
                v.y = sign;
                uv2[i] = v;
            }

            avs.SetUVs(1, uv2);
        }
    }

    // Controls
    public void Play() => _playing = true;
    public void Pause() => _playing = false;
    public void Stop() { _playing = false; _time = 0f; WriteLayer(_clip != null ? _clip.firstLayer : 0); }
    public void SetSpeed(float mul) { speedMultiplier = mul; }
    
    public bool IsPlaying() { return _playing; }
}
