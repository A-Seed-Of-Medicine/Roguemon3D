using System;
using System.Collections.Generic;
using Unity.VisualScripting;
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
    
    public int triggerLayerMask = 2;

    [Tooltip("Thickness used for planar shapes (Quad / NineSliceQuad / Radial).")]
    [Min(0.0001f)] public float triggerHeight = 0.25f;

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
    
    public List<Collider> colliders;
    public ParticleSystem particleSystem;
    public ParticleSystem[] subEmitterSystems = Array.Empty<ParticleSystem>();

    private Mesh mesh;

    /// <summary>Returns the generated trigger volume root (child Transform), or null if none exists.</summary>
    public Transform triggerVolumeRoot;

    private void OnEnable()
    {
        SetupParticleSystemParameters();
        GenerateMesh(true);
    }

    private void OnValidate()
    {
        if (!Application.isEditor)
            return;

        if (autoUpdate)
            GenerateMesh(true);
    }

    [ContextMenu("Generate Mesh")]
    public void GenerateMesh(bool destroyImmediate = false)
    {
        mesh = new Mesh();
        mesh.name = "P" + shape.ToString();

        switch (shape)
        {
            case ShapeType.Quad:
                GenerateQuad(mesh);
                break;
            case ShapeType.Radial:
                GenerateRadial(mesh);
                break;
            case ShapeType.Cylinder:
                GenerateCylinder(mesh);
                break;
            case ShapeType.Sphere:
                GenerateSphere(mesh);
                break;
            case ShapeType.NineSliceQuad:
                GenerateNineSliceQuad(mesh);
                break;
        }

        mesh.RecalculateNormals();
        UpdateParticleMesh();
        //UpdateTriggerVolume(destroyImmediate);
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

        int[] triangles = new int[6] { 0, 2, 1, 2, 3, 1 };

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
        {
            uvs[i] = RotateUV(uvs[i], uvRotation);
        }

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
            float tR = (float)r / rings;
            float radius = Mathf.Lerp(r0, r1, tR);

            for (int s = 0; s <= segments; s++)
            {
                float tS = (float)s / segments;
                float a = tS * angleRad;

                float x = Mathf.Cos(a) * radius;
                float y = Mathf.Sin(a) * radius;

                vertices[vert] = new Vector3(x, y, 0);

                float u = tS;
                float v = (radius - r0) / (r1 - r0);
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
        {
            uvs[i] = RotateUV(uvs[i], uvRotation);
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
    }

    private void GenerateCylinder(Mesh mesh)
    {
        int seg = Mathf.Max(3, cylinderSegments);
        int rings = Mathf.Max(1, cylinderRings);

        int vertexCount = (rings + 1) * (seg + 1);
        int triangleCount = rings * seg * 6;

        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        int[] triangles = new int[triangleCount];

        int vert = 0, tri = 0;

        for (int r = 0; r <= rings; r++)
        {
            float tR = (float)r / rings;

            float radius = cylinderProfile.Evaluate(tR) * cylinderRadius;

            float y = Mathf.Lerp(-cylinderHeight * 0.5f, cylinderHeight * 0.5f, tR);

            for (int s = 0; s <= seg; s++)
            {
                float tS = (float)s / seg;
                float angle = tS * Mathf.PI * 2f;

                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;

                vertices[vert] = new Vector3(x, z, y);
                uvs[vert] = new Vector2(tS, tR) * tiling;

                if (r < rings && s < seg)
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
        {
            uvs[i] = RotateUV(uvs[i], uvRotation);
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
    }

    private void GenerateSphere(Mesh mesh)
    {
        int seg = Mathf.Max(3, sphereSegments);
        int rings = Mathf.Max(2, sphereRings);

        int vertexCount = (rings + 1) * (seg + 1);
        int triangleCount = rings * seg * 6;

        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        int[] triangles = new int[triangleCount];

        int vert = 0;
        int tri = 0;

        for (int r = 0; r <= rings; r++)
        {
            float v = (float)r / rings;
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

                if (r < rings && s < seg)
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
        {
            uvs[i] = RotateUV(uvs[i], uvRotation);
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
    }

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

        var shape = ps.shape;
        shape.enabled = false;

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
        ParticleSystemRenderer psr = particleSystem.GetComponent<ParticleSystemRenderer>();
        if (psr && mesh)
            psr.mesh = mesh;
        
        foreach (var subPs in subEmitterSystems)
        {
            if (!subPs || subPs == particleSystem)
                continue;

            var subPsr = subPs.GetComponent<ParticleSystemRenderer>();
            if (subPsr)
                subPsr.mesh = mesh;
        }
    }

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

#if UNITY_EDITOR
    [ContextMenu("Reassign Particle Mesh")]
    public void ReassignParticleMesh()
    {
        var psr = GetComponent<ParticleSystemRenderer>();
        if (psr != null && mesh != null)
        {
            psr.mesh = mesh;
            Debug.Log($"Mesh reassigned to ParticleSystemRenderer on '{gameObject.name}'.");
        }
        else
        {
            Debug.LogWarning($"Could not reassign mesh on '{gameObject.name}'. Mesh or PSR missing.");
        }
    }
#endif

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

        // Reset to a stable baseline so downstream systems can reliably "sweep" this transform.
        root.localPosition = Vector3.zero;
        root.localScale = Vector3.one;
        root.localRotation = alignTriggerVolumeToParticleRotation ? GetParticleMeshLocalRotation() : Quaternion.identity;
        
        ClearTriggerVolume(root, destroyImmediate);

        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (!root)
                return;
            switch (shape)
            {
                case ShapeType.Quad:
                case ShapeType.NineSliceQuad:
                    BuildPlanarBoxTrigger(root, GetPlanarSizeFromMesh(), triggerHeight, triggerSizeOffset);
                    break;

                case ShapeType.Radial:
                    BuildRadialTrigger(root);
                    break;

                case ShapeType.Cylinder:
                    BuildCylinderTrigger(root);
                    break;

                case ShapeType.Sphere:
                    BuildSphereTrigger(root);
                    break;
            }
        };
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

        // Remove all colliders on the root (root is dedicated to this trigger volume).
        var colliders = root.GetComponents<Collider>();
        foreach (var c in colliders)
            DestroySafe(c, destroyImmediate);
        this.colliders = new List<Collider>();
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
        colliders.Add(bc);
    }

    private void BuildSphereTrigger(Transform root)
    {
        float r = Mathf.Max(0.0001f, sphereRadius + triggerSizeOffset);

        var sc = root.gameObject.AddComponent<SphereCollider>();
        sc.isTrigger = triggerVolumeIsTrigger;
        sc.center = Vector3.zero;
        sc.radius = r;
        colliders.Add(sc);
    }

    private void BuildRadialTrigger(Transform root)
    {
        float angleRad = Mathf.Clamp(angle, 0.1f, 360f) * Mathf.Deg2Rad;

        float r0 = Mathf.Min(innerRadius, outerRadius);
        float r1 = Mathf.Max(innerRadius, outerRadius);

        // Expand outer radius outward, and pull inner radius inward (to enlarge the total area).
        float inner = Mathf.Max(0f, r0 - triggerSizeOffset);
        float outer = Mathf.Max(inner + 0.0001f, r1 + triggerSizeOffset);

        float thickness = Mathf.Max(0.0001f, triggerHeight + triggerSizeOffset * 2f);

        int seg = radialTriggerSegmentsOverride > 0 ? radialTriggerSegmentsOverride : Mathf.Max(3, segments);
        seg = Mathf.Clamp(seg, 3, 256);

        float delta = angleRad / seg;

        for (int i = 0; i < seg; i++)
        {
            float a0 = delta * i;
            float a1 = delta * (i + 1);

            var prism = BuildRadialWedgePrism(inner, outer, a0, a1, thickness);
            var mc = root.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = prism;
            mc.convex = true;
            mc.isTrigger = triggerVolumeIsTrigger;
            colliders.Add(mc);
        }
    }

    private void BuildCylinderTrigger(Transform root)
    {
        int seg = Mathf.Clamp(Mathf.Max(3, cylinderSegments), 3, 128);
        int slices = cylinderTriggerSlicesOverride > 0 ? cylinderTriggerSlicesOverride : Mathf.Max(1, cylinderRings);
        slices = Mathf.Clamp(slices, 1, 128);

        float halfH = Mathf.Max(0.0001f, cylinderHeight * 0.5f + triggerSizeOffset);

        for (int i = 0; i < slices; i++)
        {
            float t0 = (float)i / slices;
            float t1 = (float)(i + 1) / slices;

            float z0 = Mathf.Lerp(-halfH, halfH, t0);
            float z1 = Mathf.Lerp(-halfH, halfH, t1);

            float r0 = Mathf.Max(0.0001f, cylinderProfile.Evaluate(t0) * cylinderRadius + triggerSizeOffset);
            float r1 = Mathf.Max(0.0001f, cylinderProfile.Evaluate(t1) * cylinderRadius + triggerSizeOffset);

            var sliceMesh = BuildFrustumPrism(r0, r1, z0, z1, seg);
            var mc = root.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = sliceMesh;
            mc.convex = true;
            mc.isTrigger = triggerVolumeIsTrigger;
            colliders.Add(mc);
        }
    }

    private Vector2 GetPlanarSizeFromMesh()
    {
        if (mesh == null)
            return quadSize;

        // Mesh is built in XY for planar shapes, so "planar size" is x/y in local space.
        var b = mesh.bounds.size;
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

    private static Mesh BuildRadialWedgePrism(float inner, float outer, float a0, float a1, float thickness)
    {
        float z0 = -thickness * 0.5f;
        float z1 = thickness * 0.5f;

        Vector3 p0 = new Vector3(Mathf.Cos(a0) * inner, Mathf.Sin(a0) * inner, z0);
        Vector3 p1 = new Vector3(Mathf.Cos(a1) * inner, Mathf.Sin(a1) * inner, z0);
        Vector3 p2 = new Vector3(Mathf.Cos(a1) * outer, Mathf.Sin(a1) * outer, z0);
        Vector3 p3 = new Vector3(Mathf.Cos(a0) * outer, Mathf.Sin(a0) * outer, z0);

        Vector3 p4 = new Vector3(p0.x, p0.y, z1);
        Vector3 p5 = new Vector3(p1.x, p1.y, z1);
        Vector3 p6 = new Vector3(p2.x, p2.y, z1);
        Vector3 p7 = new Vector3(p3.x, p3.y, z1);

        // 8 verts, 12 triangles (2 per face * 6 faces)
        var vertices = new Vector3[8] { p0, p1, p2, p3, p4, p5, p6, p7 };
        var triangles = new int[]
        {
            // Bottom (reverse winding to face -Z)
            0, 2, 1, 0, 3, 2,

            // Top (faces +Z)
            4, 5, 6, 4, 6, 7,

            // Inner wall (0-1)
            0, 1, 5, 0, 5, 4,

            // Outer wall (3-2)
            3, 7, 6, 3, 6, 2,

            // Side wall at a1 (1-2)
            1, 2, 6, 1, 6, 5,

            // Side wall at a0 (0-3)
            0, 4, 7, 0, 7, 3
        };

        var mesh = new Mesh();
        mesh.hideFlags = HideFlags.DontSave;
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildFrustumPrism(float r0, float r1, float z0, float z1, int seg)
    {
        seg = Mathf.Max(3, seg);

        var vertices = new List<Vector3>((seg + 1) * 2 + 2);
        var triangles = new List<int>(seg * 12);

        // Bottom ring
        for (int i = 0; i <= seg; i++)
        {
            float t = (float)i / seg;
            float a = t * Mathf.PI * 2f;
            vertices.Add(new Vector3(Mathf.Cos(a) * r0, Mathf.Sin(a) * r0, z0));
        }

        int topStart = vertices.Count;

        // Top ring
        for (int i = 0; i <= seg; i++)
        {
            float t = (float)i / seg;
            float a = t * Mathf.PI * 2f;
            vertices.Add(new Vector3(Mathf.Cos(a) * r1, Mathf.Sin(a) * r1, z1));
        }

        int bottomCenter = vertices.Count;
        vertices.Add(new Vector3(0f, 0f, z0));

        int topCenter = vertices.Count;
        vertices.Add(new Vector3(0f, 0f, z1));

        // Sides
        for (int i = 0; i < seg; i++)
        {
            int b0 = i;
            int b1 = i + 1;
            int t0 = topStart + i;
            int t1 = topStart + i + 1;

            triangles.Add(b0); triangles.Add(t0); triangles.Add(b1);
            triangles.Add(b1); triangles.Add(t0); triangles.Add(t1);
        }

        // Bottom cap (faces -Z)
        for (int i = 0; i < seg; i++)
        {
            int b0 = i;
            int b1 = i + 1;
            triangles.Add(bottomCenter); triangles.Add(b1); triangles.Add(b0);
        }

        // Top cap (faces +Z)
        for (int i = 0; i < seg; i++)
        {
            int t0 = topStart + i;
            int t1 = topStart + i + 1;
            triangles.Add(topCenter); triangles.Add(t0); triangles.Add(t1);
        }

        var mesh = new Mesh();
        mesh.hideFlags = HideFlags.DontSave;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void DestroySafe(Object obj, bool immediate = false)
    {
        if (obj == null)
            return;

        if (!immediate)
            Destroy(obj);
        else
            UnityEditor.EditorApplication.delayCall+=()=>
            {
                DestroyImmediate(obj, true);
            };
    }
}
