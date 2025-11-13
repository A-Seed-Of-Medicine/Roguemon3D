using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

[DisallowMultipleComponent, RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class ArrayFlipbookAVS : MonoBehaviour
{
    const string ArrayPropertyName = "_SpriteArray";

    [Header("Animation")]
    [SerializeField] public ArrayFlipbookAnimationClip defaultClip;
    public ArrayFlipbookAnimationClip CurrentClip { get; private set; }

    [Header("Overrides")] 
    public bool cameraAligned;
    public bool flipX;
    public float speedMultiplier = 1f;
    public bool playOnAwake = true;
    [Min(0)] public int startFrame;
    public bool randomizedStart;

    [Header("Targets")]
    [Tooltip("Renderers to receive AVS additional vertex streams")]
    public MeshRenderer[] renderers;
    [Tooltip("MeshFilters that define the base meshes (1:1 with Renderers)")]
    public MeshFilter[] filters;
    float anchorYOffset;

    // Per-target data
    [SerializeField, HideInInspector] Mesh[] _avsMeshes;
    [SerializeField, HideInInspector] Vector2[][] _uv2PerTarget; // per-target UV2 arrays

    float _time;
    bool _playing;

    void Awake()
    {
        anchorYOffset = transform.position.y;
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

        if (startFrame > 0)
        {
            if (defaultClip != null && defaultClip.IsValid)
            {
                int frameCount = Mathf.Max(1, defaultClip.FrameCount);
                _time = Mathf.Clamp01(startFrame / (float)frameCount);
            }
            
        }

        if (randomizedStart)
            _time = Random.Range(0f, 1f);

        if (defaultClip != null && defaultClip.IsValid)
        {
            SetClip(defaultClip, _time, true);
        }
        else
        {
            WriteLayer(CurrentLayerOrDefault());
        }

        _playing = playOnAwake;
    }

    private void Start()
    {
        if (CameraManager.Instance)
            CameraManager.Instance.OnCameraPositionUpdated += AlignToCamera;
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

    public void SetClip(ArrayFlipbookAnimationClip clip, float startNormalizedTime = 0f, bool force = false)
    {
        if (!force && ReferenceEquals(CurrentClip, clip))
            return;

        CurrentClip = clip is { IsValid: true } ? clip : null;

        if (CurrentClip != null)
        {
            ApplyClipTexture(CurrentClip);
            ApplyPPUScale(CurrentClip);
            _time = Mathf.Clamp01(startNormalizedTime) * Mathf.Max(1, CurrentClip.FrameCount);
        }
        else
        {
            ClearClipTexture();
            _time = 0f;
        }

        WriteLayer(CurrentLayerOrDefault());
    }

    void ApplyClipTexture(ArrayFlipbookAnimationClip clip)
    {
        if (clip == null || renderers == null)
        {
            return;
        }

        foreach (var renderer in renderers)
        {
            clip.ApplyTexture(renderer, ArrayPropertyName);
        }
    }

    void ClearClipTexture()
    {
        if (renderers == null)
        {
            return;
        }

        foreach (var renderer in renderers)
        {
            if (!renderer)
            {
                continue;
            }

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetTexture(ArrayPropertyName, null);
            renderer.SetPropertyBlock(block);
        }
    }

    void Update()
    {
        if (CurrentClip == null || !_playing)
            return;
        
        _time += Time.deltaTime * (CurrentClip.FramesPerSecond * Mathf.Max(0.001f, speedMultiplier));

        int layer;
        if (CurrentClip.Loop)
        {
            int span = Mathf.Max(1, CurrentClip.FrameCount);
            int frameOffset = Mathf.FloorToInt(_time) % span;
            layer = frameOffset;
        }
        else
        {
            int frameOffset = Mathf.Min(Mathf.FloorToInt(_time), CurrentClip.FrameCount - 1);
            layer = frameOffset;
        }

        WriteLayer(layer);
    }

    int CurrentLayerOrDefault()
    {
        if (CurrentClip == null)
        {
            return 0;
        }

        int span = Mathf.Max(1, CurrentClip.FrameCount);
        int frameOffset = Mathf.FloorToInt(_time) % span;
        return frameOffset;
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

    void ApplyPPUScale(ArrayFlipbookAnimationClip clip)
    {
        if (clip == null || clip.TextureArray == null)
        {
            return;
        }

        float wUnits = clip.TextureArray.width / clip.PixelsPerUnit;
        float hUnits = clip.TextureArray.height / clip.PixelsPerUnit;
        transform.localScale = new Vector3(wUnits, hUnits, wUnits);
    }

    // Controls
    public void Play() => _playing = true;
    public void Pause() => _playing = false;
    public void Stop()
    {
        _playing = false;
        _time = 0f;
        WriteLayer(CurrentClip != null ? 0 : 0);
    }
    public void SetSpeed(float mul) { speedMultiplier = mul; }

    public bool IsPlaying() { return _playing; }

    public void AlignToCamera(Vector3 cameraPosition, Vector3 playerPosition)
    {
        if (!cameraAligned)
            return;
        
        Vector3 dirToCamera = cameraPosition - transform.position;
        dirToCamera.y = 0f;
        if (dirToCamera.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dirToCamera.normalized, Vector3.up);
            transform.rotation = targetRot;
            Vector3 pos = transform.position;
            pos.y = anchorYOffset;
            transform.position = pos;
        }
    }

    void OnValidate()
    {
        if (defaultClip is not { IsValid: true })
            return;
        SetClip(defaultClip, startFrame / (float)defaultClip.FrameCount, true);
    }
}
