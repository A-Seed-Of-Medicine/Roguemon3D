using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

[ExecuteAlways]
[DisallowMultipleComponent]
public class ProceduralMeshGenerator : MonoBehaviour
{
    public enum ShapeType { Quad, Radial, Cylinder, Sphere, NineSliceQuad }

    [Header("Mesh")]
    public ShapeType shape = ShapeType.Quad;
    public Vector2 tiling = Vector2.one;
    [Range(-180f, 180f)] public float uvRotation = 0f;
    public bool autoUpdate = true;

    // Quad Settings
    [Header("Quad")]
    public Vector2 quadSize = Vector2.one;

    // Quad (9-Slice) Settings
    [Header("Nine Slice Quad")]
    public Vector2 border = new Vector2(0.1f, 0.1f);
    [Range(1, 10)] public int nineSliceSubdiv = 4;

    // Radial Settings
    [Header("Radial")]
    public float innerRadius = 0.001f;
    public float outerRadius = 1f;
    public float angle = 360f;
    public int segments = 32;
    public int rings = 4;

    // Cylinder Settings
    [Header("Cylinder")]
    public float cylinderHeight = 1f;
    public int cylinderSegments = 32;
    public int cylinderRings = 1;
    public float cylinderRadius = 1;
    public AnimationCurve cylinderProfile = AnimationCurve.Linear(0, 1, 1, 1);

    // Sphere Settings
    [Header("Sphere")]
    public int sphereSegments = 32;
    public int sphereRings = 16;
    public float sphereRadius = 1f;

    [Header("Trigger Volume (Collision)")]
    [Tooltip("Creates/updates a child transform with trigger colliders approximating the generated mesh dimensions.")]
    public bool generateTriggerVolume = true;

    [Tooltip("Layer index assigned to the trigger volume root object.")]
    public int triggerLayerMask = 2;

    [Tooltip("Thickness used for planar shapes (Quad / NineSliceQuad / Radial).")]
    [Min(0.0001f)] public float triggerHeight = 0.25f;

    [Tooltip("Height used for generated capsule trigger colliders (Radial / Cylinder).")]
    [Min(0.0001f)] public float triggerCapsuleHeight = 1f;

    [Tooltip("Positive expands, negative shrinks. Applied to radii/width/height, and also expands thickness for planar shapes.")]
    public float triggerSizeOffset = 0f;

    [Tooltip("If 0, uses the mesh generator's segment count for the Radial shape.")]
    [Range(0, 256)] public int radialTriggerSegmentsOverride = 0;

    [Tooltip("If 0, uses cylinderRings. Higher values approximate cylinderProfile more accurately.")]
    [Range(0, 128)] public int cylinderTriggerSlicesOverride = 0;

    [Tooltip("If enabled, the trigger volume transform will match ParticleSystem startRotation (if available).")]
    public bool alignTriggerVolumeToParticleRotation = true;

    [Tooltip("All generated colliders will be set as triggers.")]
    public bool triggerVolumeIsTrigger = true;

    [Tooltip("Name of the generated trigger volume child")]
    public string triggerVolumeName => gameObject.name + "_TriggerVolume";

    public Collider triggerCollider;
    public ParticleSystem particleSystem;
    public ParticleSystem[] subEmitterSystems = Array.Empty<ParticleSystem>();

    /// <summary>Returns the generated trigger volume root (child Transform), or null if none exists.</summary>
    public Transform triggerVolumeRoot;

    // ---------------------------
    // Persistence / Baking fields
    // ---------------------------

    [Header("Baked Assets (optional)")]
    [SerializeField, HideInInspector] private Mesh bakedRenderMesh;
    [SerializeField, HideInInspector] private Mesh bakedTriggerMesh; // only used for Radial MeshCollider trigger
    [SerializeField, HideInInspector] private int bakedRenderHash;
    [SerializeField, HideInInspector] private int bakedTriggerHash;

    // Runtime/preview meshes (per-instance, not saved)
    [NonSerialized] private Mesh runtimeRenderMesh;
    [NonSerialized] private Mesh runtimeTriggerMesh;

    private Mesh ActiveRenderMesh => runtimeRenderMesh != null ? runtimeRenderMesh : bakedRenderMesh;
    private Mesh ActiveTriggerMesh => runtimeTriggerMesh != null ? runtimeTriggerMesh : bakedTriggerMesh;

    // ---------------------------
    // Unity lifecycle
    // ---------------------------

    private void OnEnable()
    {
        EnsureRenderMeshOnEnable();
        SetupParticleSystemParameters();
        UpdateParticleMesh();

        // If you want trigger volume to be present automatically at runtime, uncomment:
        // if (generateTriggerVolume) UpdateTriggerVolume(false);
    }

    private void OnDisable()
    {
        // Avoid leaking runtime meshes in Edit Mode / domain reload.
        if (!Application.isPlaying)
            CleanupRuntimeMeshes(immediate: true);
    }

    private void OnDestroy()
    {
        CleanupRuntimeMeshes(immediate: Application.isPlaying ? false : true);
    }

    private void OnValidate()
    {
        if (!Application.isEditor)
            return;

        if (!autoUpdate)
            return;

        // Editor preview should never mutate baked assets. Always regenerate into a runtime mesh.
        RegenerateMeshes(forceUniqueInstance: true, regenerateTrigger: false);
    }

    // ---------------------------
    // Public API
    // ---------------------------

    /// <summary>
    /// Ensures mesh exists on enable: uses baked mesh if present AND matching current parameters,
    /// otherwise generates a runtime mesh.
    /// </summary>
    private void EnsureRenderMeshOnEnable()
    {
        int currentHash = ComputeRenderMeshHash();

        bool bakedValid = bakedRenderMesh != null && bakedRenderHash == currentHash;
        if (bakedValid)
        {
            // Ensure we are not overriding baked mesh.
            DestroySafe(runtimeRenderMesh, immediate: !Application.isPlaying);
            runtimeRenderMesh = null;
            return;
        }

        // No valid baked mesh: ensure runtime mesh and generate if empty.
        EnsureRuntimeRenderMesh();
        if (runtimeRenderMesh.vertexCount == 0)
            BuildRenderMesh(runtimeRenderMesh);
    }

    /// <summary>
    /// Regenerates the render mesh (and optionally trigger) using current inspector parameters.
    /// If forceUniqueInstance is true, it will never edit baked assets; it will generate into runtime meshes.
    /// </summary>
    public void RegenerateMeshes(bool forceUniqueInstance = true, bool regenerateTrigger = true)
    {
        if (forceUniqueInstance)
        {
            // Ensure we are writing into runtime meshes, not baked assets.
            EnsureRuntimeRenderMesh();
            BuildRenderMesh(runtimeRenderMesh);
        }
        else
        {
            // If not forcing unique, prefer existing active mesh (could be baked).
            var m = ActiveRenderMesh;
            if (m == null)
            {
                EnsureRuntimeRenderMesh();
                m = runtimeRenderMesh;
            }
            BuildRenderMesh(m);
        }

        UpdateParticleMesh();

        if (regenerateTrigger && generateTriggerVolume)
            UpdateTriggerVolume(destroyImmediate: !Application.isPlaying);
    }

    [ContextMenu("Generate Mesh")]
    public void GenerateMesh(bool destroyImmediate = false)
    {
        // Keep the context menu behavior: regenerate using current params, do not mutate baked assets.
        RegenerateMeshes(forceUniqueInstance: true, regenerateTrigger: false);
    }

    [ContextMenu("Regenerate Mesh (Force Runtime Instance)")]
    public void RegenerateMeshForceRuntime()
    {
        RegenerateMeshes(forceUniqueInstance: true, regenerateTrigger: false);
    }

#if UNITY_EDITOR
    [ContextMenu("Bake Render + Trigger Meshes To Asset...")]
    public void BakeMeshesToAsset()
    {
        // Build a fresh render mesh snapshot.
        var renderSnapshot = new Mesh { name = "P" + shape };
        BuildRenderMesh(renderSnapshot);

        // Build a trigger mesh snapshot (only meaningful for Radial MeshCollider triggers).
        Mesh triggerSnapshot = null;
        if (generateTriggerVolume && shape == ShapeType.Radial)
        {
            int seg = Mathf.Clamp(radialTriggerSegmentsOverride > 0 ? radialTriggerSegmentsOverride : segments, 1, 256);

            float a0 = 0f;
            float a1 = Mathf.Deg2Rad * Mathf.Clamp(angle, 0.1f, 360f);
            float thickness = Mathf.Max(0.0001f, triggerHeight + triggerSizeOffset * 2f);

            triggerSnapshot = CreateRadialWedgePrismMesh(
                innerRadius, outerRadius, a0, a1, thickness, seg, triggerSizeOffset,
                dontSaveHideFlags: false
            );
        }

        string path = UnityEditor.EditorUtility.SaveFilePanelInProject(
            "Save Generated Mesh Asset",
            $"{gameObject.name}_{shape}.asset",
            "asset",
            "Choose where to save the baked mesh asset."
        );

        if (string.IsNullOrEmpty(path))
        {
            DestroySafe(renderSnapshot, immediate: true);
            if (triggerSnapshot != null) DestroySafe(triggerSnapshot, immediate: true);
            return;
        }

        // Create the render mesh asset.
        renderSnapshot.hideFlags = HideFlags.None;
        UnityEditor.AssetDatabase.CreateAsset(renderSnapshot, path);

        // Add trigger mesh as sub-asset if present.
        if (triggerSnapshot != null)
        {
            triggerSnapshot.name = $"{gameObject.name}_{shape}_Trigger";
            triggerSnapshot.hideFlags = HideFlags.None;
            UnityEditor.AssetDatabase.AddObjectToAsset(triggerSnapshot, renderSnapshot);
        }

        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.AssetDatabase.Refresh();

        // Assign baked references + hashes.
        bakedRenderMesh = renderSnapshot;
        bakedTriggerMesh = triggerSnapshot;

        bakedRenderHash = ComputeRenderMeshHash();
        bakedTriggerHash = ComputeTriggerMeshHash();

        // Clear runtime overrides so instances use baked mesh (until parameters change).
        CleanupRuntimeMeshes(immediate: true);

        // Apply baked meshes to components (and bake trigger components into prefab/scene object).
        UpdateParticleMesh();
        BakeTriggerComponentsImmediate();

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.EditorUtility.SetDirty(gameObject);
    }

    private void BakeTriggerComponentsImmediate()
    {
        if (!generateTriggerVolume)
            return;

        var root = GetOrCreateTriggerVolumeRoot(createIfMissing: true);
        if (root == null)
            return;

        root.gameObject.SetActive(true);
        root.localPosition = Vector3.zero;
        root.localScale = Vector3.one;
        root.localRotation = alignTriggerVolumeToParticleRotation ? GetParticleMeshLocalRotation() : Quaternion.identity;
        root.gameObject.layer = triggerLayerMask;

        ClearTriggerVolume(root, destroyImmediate: true);

        switch (shape)
        {
            case ShapeType.Quad:
            case ShapeType.NineSliceQuad:
                BuildPlanarBoxTrigger(root, GetPlanarSizeFromMesh(), triggerHeight, triggerSizeOffset);
                break;

            case ShapeType.Radial:
            {
                // Use baked trigger mesh if it matches current trigger params.
                var meshToUse = (bakedTriggerMesh != null && bakedTriggerHash == ComputeTriggerMeshHash())
                    ? bakedTriggerMesh
                    : null;

                if (meshToUse == null)
                {
                    int seg = Mathf.Clamp(radialTriggerSegmentsOverride > 0 ? radialTriggerSegmentsOverride : segments, 1, 256);
                    float a0 = 0f;
                    float a1 = Mathf.Deg2Rad * Mathf.Clamp(angle, 0.1f, 360f);
                    float thickness = Mathf.Max(0.0001f, triggerHeight + triggerSizeOffset * 2f);
                    meshToUse = CreateRadialWedgePrismMesh(innerRadius, outerRadius, a0, a1, thickness, seg, triggerSizeOffset, dontSaveHideFlags: false);
                }

                AttachTriggerMesh(root, meshToUse);
                break;
            }

            case ShapeType.Cylinder:
                BuildCylinderTrigger(root);
                break;

            case ShapeType.Sphere:
                BuildSphereTrigger(root);
                break;
        }
    }
#endif

#if UNITY_EDITOR
    [ContextMenu("Reassign Particle Mesh")]
    public void ReassignParticleMesh()
    {
        RegenerateMeshes(forceUniqueInstance: true, regenerateTrigger: false);
    }
#endif

    // ---------------------------
    // Render Mesh generation
    // ---------------------------

    private void EnsureRuntimeRenderMesh()
    {
        if (runtimeRenderMesh != null)
            return;

        runtimeRenderMesh = new Mesh
        {
            name = "P" + shape,
            hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
        };
    }

    private void BuildRenderMesh(Mesh target)
    {
        if (target == null)
            return;

        target.name = "P" + shape;

        switch (shape)
        {
            case ShapeType.Quad:
                GenerateQuad(target);
                break;
            case ShapeType.Radial:
                GenerateRadial(target);
                break;
            case ShapeType.Cylinder:
                GenerateCylinder(target);
                break;
            case ShapeType.Sphere:
                GenerateSphere(target);
                break;
            case ShapeType.NineSliceQuad:
                GenerateNineSliceQuad(target);
                break;
        }

        target.RecalculateNormals();
        target.RecalculateBounds();
    }

    private void GenerateQuad(Mesh mesh)
    {
        Vector2 halfSize = quadSize * 0.5f;

        Vector3[] vertices = new Vector3[4]
        {
            new Vector3(-halfSize.x, -halfSize.y, 0),
            new Vector3( halfSize.x, -halfSize.y, 0),
            new Vector3(-halfSize.x,  halfSize.y, 0),
            new Vector3( halfSize.x,  halfSize.y, 0)
        };

        int[] triangles = { 0, 2, 1, 2, 3, 1 };

        Vector2[] uvs = new Vector2[4]
        {
            RotateUV(new Vector2(0, 0), uvRotation) * tiling,
            RotateUV(new Vector2(1, 0), uvRotation) * tiling,
            RotateUV(new Vector2(0, 1), uvRotation) * tiling,
            RotateUV(new Vector2(1, 1), uvRotation) * tiling
        };

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
    }

    private void GenerateNineSliceQuad(Mesh mesh)
    {
        float borderX = border.x;
        float borderY = border.y;

        float width = Mathf.Max(quadSize.x, borderX * 2f + 0.0001f);
        float height = Mathf.Max(quadSize.y, borderY * 2f + 0.0001f);

        float halfCenterW = (width - borderX * 2f) * 0.5f;
        float halfCenterH = (height - borderY * 2f) * 0.5f;

        float left = -halfCenterW - borderX;
        float innerLeft = -halfCenterW;
        float innerRight = halfCenterW;
        float right = halfCenterW + borderX;

        float bottom = -halfCenterH - borderY;
        float innerBottom = -halfCenterH;
        float innerTop = halfCenterH;
        float top = halfCenterH + borderY;

        Vector3[] vertices = new Vector3[16];
        Vector2[] uvs = new Vector2[16];

        float borderUV = 0.25f;

        float[] xPos = { left, innerLeft, innerRight, right };
        float[] yPos = { bottom, innerBottom, innerTop, top };

        float[] uVals = { 0f, borderUV, 1f - borderUV, 1f };
        float[] vVals = { 0f, borderUV, 1f - borderUV, 1f };

        int index = 0;
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                vertices[index] = new Vector3(xPos[x], yPos[y], 0f);
                uvs[index] = new Vector2(uVals[x], vVals[y]) * tiling;
                index++;
            }
        }

        int[] triangles = new int[9 * 6];
        int t = 0;
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                int i0 = y * 4 + x;
                int i1 = i0 + 1;
                int i2 = i0 + 4;
                int i3 = i2 + 1;

                triangles[t++] = i0;
                triangles[t++] = i2;
                triangles[t++] = i1;

                triangles[t++] = i1;
                triangles[t++] = i2;
                triangles[t++] = i3;
            }
        }

        for (int i = 0; i < uvs.Length; i++)
            uvs[i] = RotateUV(uvs[i], uvRotation);

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
    }

    private void GenerateRadial(Mesh mesh)
    {
        float angleRad = Mathf.Clamp(angle, 0.1f, 360f) * Mathf.Deg2Rad;
        float r0 = Mathf.Min(innerRadius, outerRadius);
        float r1 = Mathf.Max(innerRadius, outerRadius);

        int vertexCount = (rings + 1) * (segments + 1);
        int triangleCount = rings * segments * 6;

        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        int[] triangles = new int[triangleCount];

        int vert = 0, tri = 0;

        for (int r = 0; r <= rings; r++)
        {
            float tR = (float)r / Mathf.Max(1, rings);
            float radius = Mathf.Lerp(r0, r1, tR);

            for (int s = 0; s <= segments; s++)
            {
                float tS = (float)s / Mathf.Max(1, segments);
                float a = tS * angleRad;

                float x = Mathf.Cos(a) * radius;
                float y = Mathf.Sin(a) * radius;

                vertices[vert] = new Vector3(x, y, 0);

                float u = tS;
                float v = (r1 - r0) > 0.0000001f ? (radius - r0) / (r1 - r0) : 0f;
                uvs[vert] = new Vector2(u, v) * tiling;

                if (r < rings && s < segments)
                {
                    int current = vert;
                    int next = vert + segments + 1;

                    triangles[tri++] = current;
                    triangles[tri++] = current + 1;
                    triangles[tri++] = next;

                    triangles[tri++] = current + 1;
                    triangles[tri++] = next + 1;
                    triangles[tri++] = next;
                }

                vert++;
            }
        }

        for (int i = 0; i < uvs.Length; i++)
            uvs[i] = RotateUV(uvs[i], uvRotation);

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
    }

    private void GenerateCylinder(Mesh mesh)
    {
        int seg = Mathf.Max(3, cylinderSegments);
        int ringCount = Mathf.Max(1, cylinderRings);

        int vertexCount = (ringCount + 1) * (seg + 1);
        int triangleCount = ringCount * seg * 6;

        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        int[] triangles = new int[triangleCount];

        int vert = 0, tri = 0;

        for (int r = 0; r <= ringCount; r++)
        {
            float tR = (float)r / ringCount;
            float radius = cylinderProfile.Evaluate(tR) * cylinderRadius;
            float y = Mathf.Lerp(-cylinderHeight * 0.5f, cylinderHeight * 0.5f, tR);

            for (int s = 0; s <= seg; s++)
            {
                float tS = (float)s / seg;
                float ang = tS * Mathf.PI * 2f;

                float x = Mathf.Cos(ang) * radius;
                float z = Mathf.Sin(ang) * radius;

                vertices[vert] = new Vector3(x, z, y);
                uvs[vert] = new Vector2(tS, tR) * tiling;

                if (r < ringCount && s < seg)
                {
                    int current = vert;
                    int next = vert + seg + 1;

                    triangles[tri++] = current;
                    triangles[tri++] = current + 1;
                    triangles[tri++] = next;

                    triangles[tri++] = current + 1;
                    triangles[tri++] = next + 1;
                    triangles[tri++] = next;
                }

                vert++;
            }
        }

        for (int i = 0; i < uvs.Length; i++)
            uvs[i] = RotateUV(uvs[i], uvRotation);

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
    }

    private void GenerateSphere(Mesh mesh)
    {
        int seg = Mathf.Max(3, sphereSegments);
        int ringCount = Mathf.Max(2, sphereRings);

        int vertexCount = (ringCount + 1) * (seg + 1);
        int triangleCount = ringCount * seg * 6;

        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        int[] triangles = new int[triangleCount];

        int vert = 0;
        int tri = 0;

        for (int r = 0; r <= ringCount; r++)
        {
            float v = (float)r / ringCount;
            float lat = Mathf.PI * v - Mathf.PI / 2f;

            float y = Mathf.Sin(lat);
            float radius = Mathf.Cos(lat);

            for (int s = 0; s <= seg; s++)
            {
                float u = (float)s / seg;
                float lon = u * Mathf.PI * 2f;

                float x = Mathf.Cos(lon) * radius;
                float z = Mathf.Sin(lon) * radius;

                vertices[vert] = new Vector3(x, z, y) * sphereRadius;
                uvs[vert] = new Vector2(u, v) * tiling;

                if (r < ringCount && s < seg)
                {
                    int current = vert;
                    int next = vert + seg + 1;

                    triangles[tri++] = current;
                    triangles[tri++] = current + 1;
                    triangles[tri++] = next;

                    triangles[tri++] = current + 1;
                    triangles[tri++] = next + 1;
                    triangles[tri++] = next;
                }

                vert++;
            }
        }

        for (int i = 0; i < uvs.Length; i++)
            uvs[i] = RotateUV(uvs[i], uvRotation);

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
    }

    // ---------------------------
    // Particle mesh assignment
    // ---------------------------

    private void SetupParticleSystemParameters()
    {
        var ps = GetComponent<ParticleSystem>();
        if (ps == null)
            ps = gameObject.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = false;
        main.startLifetime = 1f;
        main.startSpeed = 0f;
        main.startSize = 1f;

        main.startRotation3D = true;
        main.startRotationX = Mathf.Deg2Rad * 90f;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        var shapeModule = ps.shape;
        shapeModule.enabled = false;

        var psr = GetComponent<ParticleSystemRenderer>();
        if (psr == null)
            psr = gameObject.AddComponent<ParticleSystemRenderer>();

        psr.renderMode = ParticleSystemRenderMode.Mesh;
        psr.alignment = ParticleSystemRenderSpace.Local;

        UpdateParticleMesh();
    }

    private void UpdateParticleMesh()
    {
        particleSystem = GetComponent<ParticleSystem>();
        var psr = particleSystem ? particleSystem.GetComponent<ParticleSystemRenderer>() : null;

        var m = ActiveRenderMesh;
        if (psr && m)
            psr.mesh = m;

        foreach (var subPs in subEmitterSystems)
        {
            if (!subPs || subPs == particleSystem)
                continue;

            var subPsr = subPs.GetComponent<ParticleSystemRenderer>();
            if (subPsr && m)
                subPsr.mesh = m;
        }
    }

    // ---------------------------
    // Trigger Volume (Collision)
    // ---------------------------

    [ContextMenu("Rebuild Trigger Volume")]
    public void UpdateTriggerVolume(bool destroyImmediate = false)
    {
        var root = GetOrCreateTriggerVolumeRoot(generateTriggerVolume);
        if (root == null)
            return;

        root.gameObject.SetActive(generateTriggerVolume);
        if (!generateTriggerVolume)
            return;

        root.localPosition = Vector3.zero;
        root.localScale = Vector3.one;
        root.localRotation = alignTriggerVolumeToParticleRotation ? GetParticleMeshLocalRotation() : Quaternion.identity;
        root.gameObject.layer = triggerLayerMask;

        ClearTriggerVolume(root, destroyImmediate);

        void BuildNow()
        {
            if (!root) return;

            switch (shape)
            {
                case ShapeType.Quad:
                case ShapeType.NineSliceQuad:
                    BuildPlanarBoxTrigger(root, GetPlanarSizeFromMesh(), triggerHeight, triggerSizeOffset);
                    break;

                case ShapeType.Radial:
                {
                    int seg = Mathf.Clamp(radialTriggerSegmentsOverride > 0 ? radialTriggerSegmentsOverride : segments, 1, 256);
                    float a0 = 0f;
                    float a1 = Mathf.Deg2Rad * Mathf.Clamp(angle, 0.1f, 360f);
                    float thickness = Mathf.Max(0.0001f, triggerHeight + triggerSizeOffset * 2f);

                    // If a baked trigger mesh matches current trigger params, reuse it.
                    Mesh trigMesh = (bakedTriggerMesh != null && bakedTriggerHash == ComputeTriggerMeshHash())
                        ? bakedTriggerMesh
                        : null;

                    if (trigMesh == null)
                    {
                        // Build a runtime trigger mesh (DontSave) and attach it.
                        EnsureRuntimeTriggerMesh();
                        DestroySafe(runtimeTriggerMesh, immediate: destroyImmediate);
                        runtimeTriggerMesh = CreateRadialWedgePrismMesh(innerRadius, outerRadius, a0, a1, thickness, seg, triggerSizeOffset, dontSaveHideFlags: true);
                        trigMesh = runtimeTriggerMesh;
                    }

                    AttachTriggerMesh(root, trigMesh);
                    break;
                }

                case ShapeType.Cylinder:
                    BuildCylinderTrigger(root);
                    break;

                case ShapeType.Sphere:
                    BuildSphereTrigger(root);
                    break;
            }
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += BuildNow;
#else
        BuildNow();
#endif
    }

    private void EnsureRuntimeTriggerMesh()
    {
        // No-op placeholder so we have a single place to manage runtimeTriggerMesh lifecycle.
        // The actual mesh is created per-build in CreateRadialWedgePrismMesh to match current params.
    }

    private Transform GetOrCreateTriggerVolumeRoot(bool createIfMissing)
    {
        var existing = triggerVolumeRoot;
        if (existing != null)
            return existing;

        if (!createIfMissing)
            return null;

        var go = new GameObject(triggerVolumeName);
        triggerVolumeRoot = go.transform;
        go.transform.SetParent(transform, false);
        go.layer = triggerLayerMask;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    private void ClearTriggerVolume(Transform root, bool destroyImmediate = false)
    {
        // Destroy any runtime-generated collider meshes to avoid leaking in Edit Mode.
        var meshColliders = root.GetComponents<MeshCollider>();
        foreach (var mc in meshColliders)
        {
            if (mc != null && mc.sharedMesh != null && (mc.sharedMesh.hideFlags & HideFlags.DontSave) != 0)
                DestroySafe(mc.sharedMesh, destroyImmediate);
        }

        var meshFilters = root.GetComponents<MeshFilter>();
        foreach (var mf in meshFilters)
        {
            if (mf != null && mf.sharedMesh != null && (mf.sharedMesh.hideFlags & HideFlags.DontSave) != 0)
                DestroySafe(mf.sharedMesh, destroyImmediate);
            if (mf != null)
                DestroySafe(mf, destroyImmediate);
        }

        var colliders = root.GetComponents<Collider>();
        foreach (var c in colliders)
            DestroySafe(c, destroyImmediate);

        triggerCollider = null;
    }

    private void BuildPlanarBoxTrigger(Transform root, Vector2 planarSize, float height, float sizeOffset)
    {
        float width = Mathf.Max(0.0001f, planarSize.x + sizeOffset * 2f);
        float depth = Mathf.Max(0.0001f, planarSize.y + sizeOffset * 2f);
        float thickness = Mathf.Max(0.0001f, height + sizeOffset * 2f);

        var bc = root.gameObject.AddComponent<BoxCollider>();
        bc.isTrigger = triggerVolumeIsTrigger;
        bc.center = Vector3.zero;
        bc.size = new Vector3(width, depth, thickness);
        triggerCollider = bc;
    }

    private void BuildSphereTrigger(Transform root)
    {
        float r = Mathf.Max(0.0001f, sphereRadius + triggerSizeOffset);

        var sc = root.gameObject.AddComponent<SphereCollider>();
        sc.isTrigger = triggerVolumeIsTrigger;
        sc.center = Vector3.zero;
        sc.radius = r;
        triggerCollider = sc;
    }

    private void BuildCylinderTrigger(Transform root)
    {
        int sampleCount = Mathf.Clamp(
            cylinderTriggerSlicesOverride > 0 ? cylinderTriggerSlicesOverride : Mathf.Max(1, cylinderRings),
            1, 128
        );

        float maxRadius = 0.0001f;
        for (int i = 0; i <= sampleCount; i++)
        {
            float t = (float)i / Mathf.Max(1, sampleCount);
            maxRadius = Mathf.Max(maxRadius, cylinderProfile.Evaluate(t) * cylinderRadius);
        }

        float radius = Mathf.Max(0.0001f, maxRadius + triggerSizeOffset);
        float height = Mathf.Max(triggerCapsuleHeight + triggerSizeOffset * 2f, cylinderHeight + triggerSizeOffset * 2f);

        var capsule = root.gameObject.AddComponent<CapsuleCollider>();
        capsule.isTrigger = triggerVolumeIsTrigger;
        capsule.center = Vector3.zero;
        capsule.direction = 1; // Y axis height
        capsule.radius = radius;
        capsule.height = Mathf.Max(height, capsule.radius * 2f);
        triggerCollider = capsule;
    }

    private void AttachTriggerMesh(Transform root, Mesh triggerMesh)
    {
        if (!root || triggerMesh == null)
            return;

        // Optional visualization / debug: MeshFilter doesn't affect physics.
        var mf = root.GetComponent<MeshFilter>();
        if (!mf) mf = root.gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = triggerMesh;

        var mc = root.GetComponent<MeshCollider>();
        if (!mc) mc = root.gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = triggerMesh;
        mc.convex = true;
        mc.isTrigger = triggerVolumeIsTrigger;
        triggerCollider = mc;
    }

    private Vector2 GetPlanarSizeFromMesh()
    {
        var m = ActiveRenderMesh;
        if (m == null)
            return quadSize;

        var b = m.bounds.size;
        return new Vector2(Mathf.Abs(b.x), Mathf.Abs(b.y));
    }

    private Quaternion GetParticleMeshLocalRotation()
    {
        var ps = GetComponent<ParticleSystem>();
        if (!ps)
            return Quaternion.identity;

        var main = ps.main;
        if (main.startRotation3D)
        {
            float x = main.startRotationX.Evaluate(0f) * Mathf.Rad2Deg;
            float y = main.startRotationY.Evaluate(0f) * Mathf.Rad2Deg;
            float z = main.startRotationZ.Evaluate(0f) * Mathf.Rad2Deg;
            return Quaternion.Euler(x, y, z);
        }

        float rz = main.startRotation.Evaluate(0f) * Mathf.Rad2Deg;
        return Quaternion.Euler(0f, 0f, rz);
    }

    // ---------------------------
    // Radial trigger mesh (multi segment) creation
    // ---------------------------

    private Mesh CreateRadialWedgePrismMesh(
        float inner, float outer, float a0, float a1,
        float thickness, int arcSegments, float sizeOffset,
        bool dontSaveHideFlags
    )
    {
        arcSegments = Mathf.Max(1, arcSegments);

        float rIn = Mathf.Max(0f, Mathf.Min(inner, outer) + sizeOffset);
        float rOut = Mathf.Max(0.0001f, Mathf.Max(inner, outer) + sizeOffset);
        rOut = Mathf.Max(rOut, rIn + 0.0001f);

        float twoPi = Mathf.PI * 2f;
        float span = a1 - a0;
        while (span < 0f) span += twoPi;
        span = Mathf.Clamp(span, 0.001f, twoPi - 0.001f);
        a1 = a0 + span;

        float z0 = -thickness * 0.5f;
        float z1 = thickness * 0.5f;

        Mesh m;
        if (rIn <= 0.00011f)
            m = CreateSolidSectorPrismMesh(rOut, a0, a1, z0, z1, arcSegments);
        else
            m = CreateAnnularSectorPrismMesh(rIn, rOut, a0, a1, z0, z1, arcSegments);

        if (dontSaveHideFlags)
            m.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

        return m;
    }

    private static Mesh CreateSolidSectorPrismMesh(float rOut, float a0, float a1, float z0, float z1, int seg)
    {
        int ringCount = seg + 1;

        int bottomCenter = 0;
        int bottomOuterStart = 1;
        int topCenter = 1 + ringCount;
        int topOuterStart = topCenter + 1;

        var vertices = new Vector3[2 + ringCount * 2];

        vertices[bottomCenter] = new Vector3(0f, 0f, z0);
        vertices[topCenter] = new Vector3(0f, 0f, z1);

        for (int i = 0; i < ringCount; i++)
        {
            float t = (float)i / seg;
            float a = Mathf.Lerp(a0, a1, t);
            float c = Mathf.Cos(a);
            float s = Mathf.Sin(a);

            vertices[bottomOuterStart + i] = new Vector3(c * rOut, s * rOut, z0);
            vertices[topOuterStart + i] = new Vector3(c * rOut, s * rOut, z1);
        }

        var tris = new List<int>(seg * 18);

        // Bottom (-Z)
        for (int i = 0; i < seg; i++)
        {
            int o0 = bottomOuterStart + i;
            int o1 = bottomOuterStart + i + 1;
            tris.Add(bottomCenter); tris.Add(o1); tris.Add(o0);
        }

        // Top (+Z)
        for (int i = 0; i < seg; i++)
        {
            int o0 = topOuterStart + i;
            int o1 = topOuterStart + i + 1;
            tris.Add(topCenter); tris.Add(o0); tris.Add(o1);
        }

        // Outer wall
        for (int i = 0; i < seg; i++)
        {
            int b0 = bottomOuterStart + i;
            int b1 = bottomOuterStart + i + 1;
            int t0 = topOuterStart + i;
            int t1 = topOuterStart + i + 1;

            tris.Add(b0); tris.Add(t0); tris.Add(t1);
            tris.Add(b0); tris.Add(t1); tris.Add(b1);
        }

        // Side wall at start (a0)
        {
            int bC = bottomCenter;
            int tC = topCenter;
            int bO = bottomOuterStart + 0;
            int tO = topOuterStart + 0;

            tris.Add(bC); tris.Add(tC); tris.Add(tO);
            tris.Add(bC); tris.Add(tO); tris.Add(bO);
        }

        // Side wall at end (a1)
        {
            int bC = bottomCenter;
            int tC = topCenter;
            int bO = bottomOuterStart + seg;
            int tO = topOuterStart + seg;

            tris.Add(bC); tris.Add(bO); tris.Add(tO);
            tris.Add(bC); tris.Add(tO); tris.Add(tC);
        }

        var mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh CreateAnnularSectorPrismMesh(float rIn, float rOut, float a0, float a1, float z0, float z1, int seg)
    {
        int ringCount = seg + 1;

        int biStart = 0;
        int boStart = biStart + ringCount;
        int tiStart = boStart + ringCount;
        int toStart = tiStart + ringCount;

        var vertices = new Vector3[ringCount * 4];

        for (int i = 0; i < ringCount; i++)
        {
            float t = (float)i / seg;
            float a = Mathf.Lerp(a0, a1, t);
            float c = Mathf.Cos(a);
            float s = Mathf.Sin(a);

            vertices[biStart + i] = new Vector3(c * rIn, s * rIn, z0);
            vertices[boStart + i] = new Vector3(c * rOut, s * rOut, z0);
            vertices[tiStart + i] = new Vector3(c * rIn, s * rIn, z1);
            vertices[toStart + i] = new Vector3(c * rOut, s * rOut, z1);
        }

        var tris = new List<int>(seg * 30);

        // Bottom (-Z)
        for (int i = 0; i < seg; i++)
        {
            int i0 = biStart + i;
            int i1 = biStart + i + 1;
            int o1 = boStart + i + 1;
            int o0 = boStart + i;

            tris.Add(i0); tris.Add(o1); tris.Add(i1);
            tris.Add(i0); tris.Add(o0); tris.Add(o1);
        }

        // Top (+Z)
        for (int i = 0; i < seg; i++)
        {
            int i0 = tiStart + i;
            int i1 = tiStart + i + 1;
            int o1 = toStart + i + 1;
            int o0 = toStart + i;

            tris.Add(i0); tris.Add(i1); tris.Add(o1);
            tris.Add(i0); tris.Add(o1); tris.Add(o0);
        }

        // Inner wall
        for (int i = 0; i < seg; i++)
        {
            int b0 = biStart + i;
            int b1 = biStart + i + 1;
            int t1 = tiStart + i + 1;
            int t0 = tiStart + i;

            tris.Add(b0); tris.Add(b1); tris.Add(t1);
            tris.Add(b0); tris.Add(t1); tris.Add(t0);
        }

        // Outer wall
        for (int i = 0; i < seg; i++)
        {
            int b0 = boStart + i;
            int b1 = boStart + i + 1;
            int t0 = toStart + i;
            int t1 = toStart + i + 1;

            tris.Add(b0); tris.Add(t0); tris.Add(t1);
            tris.Add(b0); tris.Add(t1); tris.Add(b1);
        }

        // Side wall at start (a0)
        {
            int bI = biStart + 0;
            int bO = boStart + 0;
            int tI = tiStart + 0;
            int tO = toStart + 0;

            tris.Add(bI); tris.Add(bO); tris.Add(tO);
            tris.Add(bI); tris.Add(tO); tris.Add(tI);
        }

        // Side wall at end (a1)
        {
            int bI = biStart + seg;
            int bO = boStart + seg;
            int tI = tiStart + seg;
            int tO = toStart + seg;

            tris.Add(bI); tris.Add(tO); tris.Add(bO);
            tris.Add(bI); tris.Add(tI); tris.Add(tO);
        }

        var mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------------------------
    // Hashing (detect baked validity)
    // ---------------------------

    private int ComputeRenderMeshHash()
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + shape.GetHashCode();
            h = h * 31 + tiling.GetHashCode();
            h = h * 31 + uvRotation.GetHashCode();

            switch (shape)
            {
                case ShapeType.Quad:
                    h = h * 31 + quadSize.GetHashCode();
                    break;

                case ShapeType.NineSliceQuad:
                    h = h * 31 + quadSize.GetHashCode();
                    h = h * 31 + border.GetHashCode();
                    h = h * 31 + nineSliceSubdiv.GetHashCode();
                    break;

                case ShapeType.Radial:
                    h = h * 31 + innerRadius.GetHashCode();
                    h = h * 31 + outerRadius.GetHashCode();
                    h = h * 31 + angle.GetHashCode();
                    h = h * 31 + segments.GetHashCode();
                    h = h * 31 + rings.GetHashCode();
                    break;

                case ShapeType.Cylinder:
                    h = h * 31 + cylinderHeight.GetHashCode();
                    h = h * 31 + cylinderSegments.GetHashCode();
                    h = h * 31 + cylinderRings.GetHashCode();
                    h = h * 31 + cylinderRadius.GetHashCode();
                    h = h * 31 + HashCurve(cylinderProfile);
                    break;

                case ShapeType.Sphere:
                    h = h * 31 + sphereSegments.GetHashCode();
                    h = h * 31 + sphereRings.GetHashCode();
                    h = h * 31 + sphereRadius.GetHashCode();
                    break;
            }

            return h;
        }
    }

    private int ComputeTriggerMeshHash()
    {
        // Only meaningful for the radial MeshCollider trigger mesh.
        unchecked
        {
            int h = 17;
            h = h * 31 + generateTriggerVolume.GetHashCode();
            h = h * 31 + triggerHeight.GetHashCode();
            h = h * 31 + triggerSizeOffset.GetHashCode();
            h = h * 31 + radialTriggerSegmentsOverride.GetHashCode();
            h = h * 31 + shape.GetHashCode();

            if (shape == ShapeType.Radial)
            {
                h = h * 31 + innerRadius.GetHashCode();
                h = h * 31 + outerRadius.GetHashCode();
                h = h * 31 + angle.GetHashCode();
                h = h * 31 + segments.GetHashCode();
            }

            return h;
        }
    }

    private static int HashCurve(AnimationCurve curve)
    {
        if (curve == null)
            return 0;

        unchecked
        {
            int h = 17;
            var keys = curve.keys;
            h = h * 31 + keys.Length;
            for (int i = 0; i < keys.Length; i++)
            {
                var k = keys[i];
                h = h * 31 + k.time.GetHashCode();
                h = h * 31 + k.value.GetHashCode();
                h = h * 31 + k.inTangent.GetHashCode();
                h = h * 31 + k.outTangent.GetHashCode();
            }
            return h;
        }
    }

    // ---------------------------
    // Utilities
    // ---------------------------

    private Vector2 RotateUV(Vector2 uv, float angleDegrees)
    {
        float rad = angleDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);

        uv -= new Vector2(0.5f, 0.5f);
        float x = uv.x * cos - uv.y * sin;
        float y = uv.x * sin + uv.y * cos;
        return new Vector2(x, y) + new Vector2(0.5f, 0.5f);
    }

    private void CleanupRuntimeMeshes(bool immediate)
    {
        if (runtimeRenderMesh != null)
        {
            DestroySafe(runtimeRenderMesh, immediate);
            runtimeRenderMesh = null;
        }

        if (runtimeTriggerMesh != null)
        {
            DestroySafe(runtimeTriggerMesh, immediate);
            runtimeTriggerMesh = null;
        }
    }

    private static void DestroySafe(Object obj, bool immediate = false)
    {
        if (obj == null)
            return;

#if UNITY_EDITOR
        if (immediate)
            DestroyImmediate(obj, true);
        else
            Destroy(obj);
#else
        Destroy(obj);
#endif
    }
}
