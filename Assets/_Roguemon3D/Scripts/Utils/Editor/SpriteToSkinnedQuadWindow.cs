#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;                 // SpriteDataAccessExtensions
using UnityEngine.U2D.Animation;
using Object = UnityEngine.Object;

namespace SpriteTo3DTools
{
    /// <summary>
    /// Editor window for converting a 2D skinned Sprite
    /// (from PSDImporter or TextureImporter) into a 3D SkinnedMeshRenderer object.
    /// For PSB files with Character Rig enabled, it can convert the entire rig
    /// into a single SkinnedMeshRenderer.
    /// </summary>
    public class SpriteToSkinnedQuadWindow : EditorWindow
    {
        [SerializeField] private UnityEngine.Object _sourceAsset;
        [SerializeField] private int _selectedSpriteIndex;
        [SerializeField] private bool _createMeshAsset = true;
        [SerializeField] private bool _createMaterialAsset = true;
        [SerializeField] private bool _createPrefab = true;
        [SerializeField] private DefaultAsset _outputFolder;

        // PSB / Character Rig info
        [SerializeField] private bool _convertEntirePsb = true;

        // Optional shader override for generated materials
        [SerializeField] private Shader _overrideShader;

        private readonly List<Sprite> _sprites = new List<Sprite>();
        private string[] _spriteNames = Array.Empty<string>();

        private GameObject _psbCharacterPrefab;
        private bool _psbHasCharacterRig;

        [MenuItem("Tools/2.5D/Sprite Skin → 3D Skinned Quad")]
        public static void ShowWindow()
        {
            var window = GetWindow<SpriteToSkinnedQuadWindow>();
            window.titleContent = new GUIContent("Sprite → Skinned Quad");
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _sourceAsset = EditorGUILayout.ObjectField(
                new GUIContent(
                    "Source Asset",
                    "Texture2D, Sprite, or PSB asset imported as Sprite.\n" +
                    "For PSDImporter: you can select either the PSB file (recommended) or the generated prefab."),
                _sourceAsset,
                typeof(UnityEngine.Object),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                RefreshSpriteList();
            }

            if (_sprites.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Select a Texture2D, Sprite, or PSB asset that contains sprites " +
                    "with Skinning Editor data (bones, geometry, weights).",
                    MessageType.Info);
            }
            else
            {
                _selectedSpriteIndex = Mathf.Clamp(_selectedSpriteIndex, 0, _sprites.Count - 1);
                _selectedSpriteIndex = EditorGUILayout.Popup("Sprite", _selectedSpriteIndex, _spriteNames);
            }

            if (_psbHasCharacterRig)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("PSB Character Rig", EditorStyles.boldLabel);
                _convertEntirePsb = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Convert full PSB character rig",
                        "If enabled, converts the entire PSDImporter-generated character prefab into\n" +
                        "a single SkinnedMeshRenderer using the existing bone hierarchy."),
                    _convertEntirePsb);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

            _outputFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Output Folder",
                    "Optional project folder where Mesh/Material/Prefab assets will be saved.\n" +
                    "If empty, the source asset's folder is used."),
                _outputFolder,
                typeof(DefaultAsset),
                false);

            _overrideShader = (Shader)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Shader",
                    "Optional shader for generated materials. If null, 'Sprites/Default' is used.\n" +
                    "The sprite texture is assigned to '_BaseMap' and 'mainTexture'."),
                _overrideShader,
                typeof(Shader),
                false);

            _createMeshAsset = EditorGUILayout.ToggleLeft("Create Mesh Asset(s)", _createMeshAsset);
            _createMaterialAsset = EditorGUILayout.ToggleLeft("Create Material Asset(s)", _createMaterialAsset);
            _createPrefab = EditorGUILayout.ToggleLeft("Create Prefab Asset", _createPrefab);

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_sourceAsset == null || (_sprites.Count == 0 && !_psbHasCharacterRig)))
            {
                if (GUILayout.Button("Generate 3D Skinned Quad"))
                {
                    Generate();
                }
            }
        }

        private void RefreshSpriteList()
        {
            _sprites.Clear();
            _spriteNames = Array.Empty<string>();
            _selectedSpriteIndex = 0;
            _psbCharacterPrefab = null;
            _psbHasCharacterRig = false;

            if (_sourceAsset == null)
                return;

            string path = AssetDatabase.GetAssetPath(_sourceAsset);
            if (string.IsNullOrEmpty(path))
                return;

            var allAtPath = AssetDatabase.LoadAllAssetsAtPath(path);

            // Collect sprites at this path.
            foreach (var obj in allAtPath)
            {
                if (obj is Sprite sprite)
                    _sprites.Add(sprite);
            }

            // If the asset itself is a Sprite (e.g. single-sprite import),
            // ensure it is present.
            if (_sprites.Count == 0 && _sourceAsset is Sprite directSprite)
            {
                _sprites.Add(directSprite);
            }

            _spriteNames = _sprites.Select(s => s.name).ToArray();

            // Try to find a character rig prefab for PSB imports.
            foreach (var obj in allAtPath)
            {
                if (obj is GameObject go)
                {
                    // Heuristic: treat the first prefab that has SpriteRenderer children as the character rig.
                    if (go.GetComponentInChildren<SpriteRenderer>(true) != null)
                    {
                        _psbCharacterPrefab = go;
                        break;
                    }
                }
            }

            if (_psbCharacterPrefab != null)
            {
                // Consider it a "character rig" if it has at least one SpriteSkin.
                _psbHasCharacterRig = _psbCharacterPrefab.GetComponentInChildren<SpriteSkin>(true) != null;
            }
        }

        private void Generate()
        {
            if (_sourceAsset == null)
                return;

            string sourcePath = AssetDatabase.GetAssetPath(_sourceAsset);
            if (string.IsNullOrEmpty(sourcePath))
                return;

            string outputDir = GetOutputDirectory(sourcePath);

            // If we have a PSB character prefab and the user wants the whole rig, do that.
            if (_psbHasCharacterRig && _convertEntirePsb && _psbCharacterPrefab != null)
            {
                GenerateFromPsbCharacter(outputDir);
                return;
            }

            // Fallback: single-sprite conversion (TextureImporter, individual Sprite, or PSB sub-sprite).
            if (_sprites.Count == 0)
                return;

            Sprite sprite = _sprites[_selectedSpriteIndex];
            if (sprite == null)
            {
                EditorUtility.DisplayDialog("Invalid Sprite", "Selected sprite is null.", "OK");
                return;
            }

            if (!SpriteToSkinnedQuadUtility.HasSkinningData(sprite))
            {
                EditorUtility.DisplayDialog(
                    "No Skinning Data",
                    "The selected sprite does not contain bone/weight data.\n\n" +
                    "Open it in the 2D Skinning Editor and ensure it has bones, " +
                    "geometry and weights before converting.",
                    "OK");
                return;
            }

            GameObject quadRoot;
            Mesh mesh;
            Material material;

            try
            {
                quadRoot = SpriteToSkinnedQuadUtility.BuildFromSprite(
                    sprite,
                    _overrideShader,
                    out mesh,
                    out material);
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to generate skinned quad from sprite " + sprite.name + ":\n" + ex);
                EditorUtility.DisplayDialog("Error", "Failed to generate skinned quad. See console for details.", "OK");
                return;
            }

            Undo.RegisterCreatedObjectUndo(quadRoot, "Create Skinned Quad");
            Selection.activeObject = quadRoot;
            EditorGUIUtility.PingObject(quadRoot);

            if (!string.IsNullOrEmpty(outputDir))
            {
                var meshes = mesh != null ? new[] { mesh } : Array.Empty<Mesh>();
                var materials = material != null ? new[] { material } : Array.Empty<Material>();
                SaveAssets(quadRoot, meshes, materials, sprite.name, outputDir);
            }
        }

        private void GenerateFromPsbCharacter(string outputDir)
        {
            GameObject rigRoot;
            Mesh mesh;
            Material mat;

            try
            {
                rigRoot = SpriteToSkinnedQuadUtility.BuildSingleMeshFromPsbCharacterPrefab(
                    _psbCharacterPrefab,
                    _overrideShader,
                    out mesh,
                    out mat);
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to generate single skinned mesh from PSB character prefab " +
                               _psbCharacterPrefab.name + ":\n" + ex);
                EditorUtility.DisplayDialog("Error", "Failed to generate single skinned mesh. See console for details.", "OK");
                return;
            }

            Undo.RegisterCreatedObjectUndo(rigRoot, "Create Single Skinned PSB Rig");
            Selection.activeObject = rigRoot;
            EditorGUIUtility.PingObject(rigRoot);

            if (!string.IsNullOrEmpty(outputDir))
            {
                var meshes = mesh != null ? new[] { mesh } : Array.Empty<Mesh>();
                var materials = mat != null ? new[] { mat } : Array.Empty<Material>();
                SaveAssets(rigRoot, meshes, materials, _sourceAsset.name, outputDir);
            }
        }

        private string GetOutputDirectory(string assetPath)
        {
            if (_outputFolder != null)
            {
                string folderPath = AssetDatabase.GetAssetPath(_outputFolder);
                if (AssetDatabase.IsValidFolder(folderPath))
                    return folderPath;
            }

            if (!string.IsNullOrEmpty(assetPath))
            {
                string dir = Path.GetDirectoryName(assetPath);
                if (!string.IsNullOrEmpty(dir))
                    return dir.Replace("\\", "/");
            }

            return "Assets";
        }

        private void SaveAssets(
            GameObject root,
            IEnumerable<Mesh> meshes,
            IEnumerable<Material> materials,
            string baseName,
            string outputDir)
        {
            if (string.IsNullOrEmpty(outputDir))
                outputDir = "Assets";

            AssetDatabase.StartAssetEditing();
            try
            {
                if (_createMeshAsset && meshes != null)
                {
                    foreach (var mesh in meshes)
                    {
                        if (mesh == null)
                            continue;

                        string fileName = !string.IsNullOrEmpty(mesh.name)
                            ? baseName + "_" + mesh.name
                            : baseName + "_Mesh";

                        string meshPath = AssetDatabase.GenerateUniqueAssetPath(
                            Path.Combine(outputDir, fileName + ".asset"));
                        AssetDatabase.CreateAsset(mesh, meshPath);
                    }
                }

                if (_createMaterialAsset && materials != null)
                {
                    foreach (var mat in materials)
                    {
                        if (mat == null)
                            continue;

                        string fileName = !string.IsNullOrEmpty(mat.name)
                            ? baseName + "_" + mat.name
                            : baseName + "_Mat";

                        string matPath = AssetDatabase.GenerateUniqueAssetPath(
                            Path.Combine(outputDir, fileName + ".mat"));
                        AssetDatabase.CreateAsset(mat, matPath);
                    }
                }

                if (_createPrefab && root != null)
                {
                    string prefabPath = AssetDatabase.GenerateUniqueAssetPath(
                        Path.Combine(outputDir, baseName + "_SkinnedQuad.prefab"));
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
            }
        }
    }

    /// <summary>
    /// Static utility that does the actual Sprite → SkinnedMesh conversion.
    /// </summary>
    internal static class SpriteToSkinnedQuadUtility
    {
        /// <summary>
        /// Checks if the sprite has bones + BlendWeight vertex channel.
        /// </summary>
        public static bool HasSkinningData(Sprite sprite)
        {
            if (sprite == null)
                return false;

            var bones = sprite.GetBones();
            if (bones == null || bones.Length == 0)
                return false;

            if (!SpriteDataAccessExtensions.HasVertexAttribute(sprite, VertexAttribute.BlendWeight))
                return false;

            var weightsSlice = sprite.GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight);
            return weightsSlice.Length > 0;
        }

        /// <summary>
        /// Builds a skinned quad GameObject hierarchy from a single skinned Sprite.
        /// Returns:
        ///   - root GameObject with SkinnedMeshRenderer
        ///   - created Mesh and Material (via out parameters)
        /// </summary>
        public static GameObject BuildFromSprite(
            Sprite sprite,
            Shader shader,
            out Mesh mesh,
            out Material material)
        {
            if (sprite == null)
                throw new ArgumentNullException(nameof(sprite));

            if (!SpriteDataAccessExtensions.HasVertexAttribute(sprite, VertexAttribute.Position))
                throw new InvalidOperationException(
                    $"Sprite '{sprite.name}' has no Position vertex attribute. " +
                    "Make sure it has a generated mesh from the Skinning Editor.");

            var posSlice = sprite.GetVertexAttribute<Vector3>(VertexAttribute.Position);
            int vertexCount = posSlice.Length;

            if (vertexCount <= 0)
                throw new InvalidOperationException(
                    $"Sprite '{sprite.name}' reports zero vertices. " +
                    "Check that it has mesh geometry in the Skinning Editor.");

            // UVs
            NativeSlice<Vector2> uvSlice = default;
            if (SpriteDataAccessExtensions.HasVertexAttribute(sprite, VertexAttribute.TexCoord0))
            {
                uvSlice = sprite.GetVertexAttribute<Vector2>(VertexAttribute.TexCoord0);
                if (uvSlice.Length != vertexCount)
                {
                    Debug.LogWarning(
                        $"Sprite '{sprite.name}' UV count ({uvSlice.Length}) does not match " +
                        $"position count ({vertexCount}). Using the minimum length.");
                    vertexCount = Mathf.Min(vertexCount, uvSlice.Length);
                }
            }

            // Bone weights
            if (!SpriteDataAccessExtensions.HasVertexAttribute(sprite, VertexAttribute.BlendWeight))
                throw new InvalidOperationException(
                    $"Sprite '{sprite.name}' has no BlendWeight vertex attribute. " +
                    "It is not skinned or has no weights.");

            var weightSlice = sprite.GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight);
            if (weightSlice.Length == 0)
                throw new InvalidOperationException(
                    $"Sprite '{sprite.name}' BlendWeight channel is empty. " +
                    "Check that the sprite has weights in the Skinning Editor.");

            if (weightSlice.Length != vertexCount)
            {
                Debug.LogWarning(
                    $"Sprite '{sprite.name}' bone-weight count ({weightSlice.Length}) does not match " +
                    $"position count ({vertexCount}). Using the minimum length.");
                vertexCount = Mathf.Min(vertexCount, weightSlice.Length);
            }

            // Indices / triangles
            var indices16 = sprite.GetIndices();
            var triangles = new int[indices16.Length];
            for (int i = 0; i < indices16.Length; i++)
                triangles[i] = indices16[i];

            // Bones from the sprite
            var spriteBones = sprite.GetBones();
            if (spriteBones == null || spriteBones.Length == 0)
                throw new InvalidOperationException(
                    $"Sprite '{sprite.name}' has no SpriteBones. " +
                    "Rig it in the Skinning Editor first.");

            // Copy vertex data into managed arrays
            var vertices = new Vector3[vertexCount];
            var uv = new Vector2[vertexCount];
            var boneWeights = new BoneWeight[vertexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = posSlice[i];

                if (uvSlice.Length > 0)
                    uv[i] = uvSlice[i];

                boneWeights[i] = weightSlice[i];
            }

            // Normals
            Vector3[] normals;
            if (SpriteDataAccessExtensions.HasVertexAttribute(sprite, VertexAttribute.Normal))
            {
                var normalSlice = sprite.GetVertexAttribute<Vector3>(VertexAttribute.Normal);
                if (normalSlice.Length < vertexCount)
                {
                    Debug.LogWarning(
                        $"Sprite '{sprite.name}' normal count ({normalSlice.Length}) " +
                        $"less than vertex count ({vertexCount}). Missing normals will default to -Z.");
                }

                normals = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                    normals[i] = i < normalSlice.Length ? normalSlice[i] : Vector3.back;
            }
            else
            {
                normals = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                    normals[i] = Vector3.back;
            }

            // Build Mesh
            mesh = new Mesh
            {
                name = sprite.name + "_SkinnedQuadMesh"
            };

            if (vertexCount > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.normals = normals;
            mesh.boneWeights = boneWeights;

            // Root object + SkinnedMeshRenderer
            var rootGO = new GameObject(sprite.name + "_SkinnedQuad");
            var smr = rootGO.AddComponent<SkinnedMeshRenderer>();

            // Build bone transforms from SpriteBone hierarchy
            var bones = new Transform[spriteBones.Length];

            for (int i = 0; i < spriteBones.Length; i++)
            {
                var sb = spriteBones[i];
                var boneGO = new GameObject(string.IsNullOrEmpty(sb.name) ? $"Bone_{i}" : sb.name);
                var t = boneGO.transform;

                int parentId = sb.parentId;
                if (parentId >= 0 && parentId < spriteBones.Length && bones[parentId] != null)
                    t.SetParent(bones[parentId], false);
                else
                    t.SetParent(rootGO.transform, false);

                t.localPosition = sb.position;
                t.localRotation = sb.rotation;
                t.localScale = Vector3.one;

                bones[i] = t;
            }

            // Bind poses
            Matrix4x4[] bindPoses = null;
            try
            {
                var bindPoseNative = sprite.GetBindPoses();
                if (bindPoseNative.IsCreated && bindPoseNative.Length == bones.Length)
                {
                    bindPoses = new Matrix4x4[bones.Length];
                    for (int i = 0; i < bones.Length; i++)
                        bindPoses[i] = bindPoseNative[i];
                }
            }
            catch (MissingMethodException)
            {
                // GetBindPoses not available; ignore and use fallback.
            }

            if (bindPoses == null)
            {
                bindPoses = new Matrix4x4[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                    bindPoses[i] = bones[i].worldToLocalMatrix * rootGO.transform.localToWorldMatrix;
            }

            mesh.bindposes = bindPoses;
            mesh.RecalculateBounds();

            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = rootGO.transform;
            smr.updateWhenOffscreen = true;

            // Material using the sprite's texture and optional shader override.
            var shaderToUse = shader != null ? shader : Shader.Find("Sprites/Default");
            material = new Material(shaderToUse)
            {
                name = sprite.name + "_SkinnedQuadMat"
            };

            if (sprite.texture != null)
            {
                material.SetTexture("_BaseMap", sprite.texture);
                material.mainTexture = sprite.texture;
            }

            smr.sharedMaterial = material;

            return rootGO;
        }

        /// <summary>
        /// Build a single SkinnedMeshRenderer rig from a PSDImporter-generated
        /// character prefab. All parts are baked into one Mesh sharing the same
        /// bone hierarchy, so animations still work but there is only one renderer.
        /// Triangles are appended in SpriteRenderer.sortingOrder order so that
        /// Sprites/Default (ZWrite Off) reproduces 2D layering.
        ///
        /// The returned GameObject contains:
        ///   - the original bone hierarchy Transforms
        ///   - a single SkinnedMeshRenderer on the root object
        ///   - no leftover sprite-part transforms (Head, CapeFront, Skirt, etc.)
        /// </summary>
        public static GameObject BuildSingleMeshFromPsbCharacterPrefab(
            GameObject characterPrefab,
            Shader shader,
            out Mesh combinedMesh,
            out Material combinedMaterial)
        {
            if (characterPrefab == null)
                throw new ArgumentNullException(nameof(characterPrefab));

            // Instantiate a plain clone so we don't carry prefab override data.
            var instance = Object.Instantiate(characterPrefab);
            instance.name = characterPrefab.name + "_SingleSkinned";

            var spriteSkins = instance.GetComponentsInChildren<SpriteSkin>(true);
            var spriteRenderers = instance.GetComponentsInChildren<SpriteRenderer>(true);

            if (spriteSkins.Length == 0)
                throw new InvalidOperationException(
                    "PSB character prefab has no SpriteSkin components.");

            // Root bone: assume all SpriteSkins share the same rootBone.
            Transform rootBone = spriteSkins[0].rootBone != null
                ? spriteSkins[0].rootBone
                : instance.transform;

            // Gather parts (SpriteSkin + SpriteRenderer) and sort by sortingOrder
            var parts = new List<Part>();
            foreach (var skin in spriteSkins)
            {
                var sr = skin.GetComponent<SpriteRenderer>();
                if (sr == null || sr.sprite == null)
                    continue;

                if (!HasSkinningData(sr.sprite))
                    continue;

                parts.Add(new Part
                {
                    sprite = sr.sprite,
                    spriteSkin = skin,
                    spriteRenderer = sr,
                    transform = sr.transform,
                    sortingOrder = sr.sortingOrder
                });
            }

            if (parts.Count == 0)
                throw new InvalidOperationException(
                    "No skinned sprite parts found on PSB character prefab.");

            // Sort by order in layer, so later parts draw "on top"
            parts.Sort((a, b) => a.sortingOrder.CompareTo(b.sortingOrder));

            // Build global bone list and mapping from Transform -> index
            var globalBones = new List<Transform>();
            var boneIndexMap = new Dictionary<Transform, int>();

            // Mesh data accumulators
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            var boneWeights = new List<BoneWeight>();
            var triangles = new List<int>();

            int vertexOffset = 0;

            foreach (var part in parts)
            {
                var sprite = part.sprite;
                var skin = part.spriteSkin;
                var t = part.transform;

                // Build local mapping from this SpriteSkin's bones -> global indices
                var localBones = skin.boneTransforms;
                if (localBones == null || localBones.Length == 0)
                    continue;

                var localToGlobalIndex = new int[localBones.Length];
                for (int i = 0; i < localBones.Length; i++)
                {
                    var boneT = localBones[i];
                    if (boneT == null)
                        boneT = rootBone;

                    if (!boneIndexMap.TryGetValue(boneT, out int gIndex))
                    {
                        gIndex = globalBones.Count;
                        globalBones.Add(boneT);
                        boneIndexMap.Add(boneT, gIndex);
                    }

                    localToGlobalIndex[i] = gIndex;
                }

                // Read sprite mesh data
                if (!SpriteDataAccessExtensions.HasVertexAttribute(sprite, VertexAttribute.Position))
                    throw new InvalidOperationException(
                        $"Sprite '{sprite.name}' has no Position vertex attribute.");

                var posSlice = sprite.GetVertexAttribute<Vector3>(VertexAttribute.Position);
                int vertexCount = posSlice.Length;
                if (vertexCount <= 0)
                    continue;

                NativeSlice<Vector2> uvSlice = default;
                if (SpriteDataAccessExtensions.HasVertexAttribute(sprite, VertexAttribute.TexCoord0))
                {
                    uvSlice = sprite.GetVertexAttribute<Vector2>(VertexAttribute.TexCoord0);
                    if (uvSlice.Length != vertexCount)
                    {
                        Debug.LogWarning(
                            $"Sprite '{sprite.name}' UV count ({uvSlice.Length}) does not match " +
                            $"position count ({vertexCount}). Using the minimum length.");
                        vertexCount = Mathf.Min(vertexCount, uvSlice.Length);
                    }
                }

                if (!SpriteDataAccessExtensions.HasVertexAttribute(sprite, VertexAttribute.BlendWeight))
                    throw new InvalidOperationException(
                        $"Sprite '{sprite.name}' has no BlendWeight vertex attribute.");

                var weightSlice = sprite.GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight);
                if (weightSlice.Length == 0)
                    throw new InvalidOperationException(
                        $"Sprite '{sprite.name}' BlendWeight channel is empty.");

                if (weightSlice.Length != vertexCount)
                {
                    Debug.LogWarning(
                        $"Sprite '{sprite.name}' bone-weight count ({weightSlice.Length}) does not match " +
                        $"position count ({vertexCount}). Using the minimum length.");
                    vertexCount = Mathf.Min(vertexCount, weightSlice.Length);
                }

                // Indices
                var indices16 = sprite.GetIndices();
                int baseVertex = vertexOffset;

                // Local transform to world, then world to rootBone local, so vertices live in rootBone space.
                Matrix4x4 partLocalToWorld = t.localToWorldMatrix;
                Matrix4x4 worldToRoot = rootBone.worldToLocalMatrix;

                // Positions / UVs / Weights / Normals
                NativeSlice<Vector3> normalSlice = default;
                bool hasNormals = SpriteDataAccessExtensions.HasVertexAttribute(sprite, VertexAttribute.Normal);
                if (hasNormals)
                    normalSlice = sprite.GetVertexAttribute<Vector3>(VertexAttribute.Normal);

                for (int i = 0; i < vertexCount; i++)
                {
                    // position
                    Vector3 pLocal = posSlice[i];
                    Vector3 pWorld = partLocalToWorld.MultiplyPoint3x4(pLocal);
                    Vector3 pRoot = worldToRoot.MultiplyPoint3x4(pWorld);
                    vertices.Add(pRoot);

                    // uv
                    if (uvSlice.Length > 0)
                        uvs.Add(uvSlice[i]);
                    else
                        uvs.Add(Vector2.zero);

                    // normal
                    if (hasNormals && i < normalSlice.Length)
                    {
                        Vector3 nLocal = normalSlice[i];
                        Vector3 nWorld = partLocalToWorld.MultiplyVector(nLocal);
                        Vector3 nRoot = worldToRoot.MultiplyVector(nWorld).normalized;
                        normals.Add(nRoot);
                    }
                    else
                    {
                        normals.Add(Vector3.back);
                    }

                    // bone weights
                    var w = weightSlice[i];
                    var bw = new BoneWeight
                    {
                        weight0 = w.weight0,
                        weight1 = w.weight1,
                        weight2 = w.weight2,
                        weight3 = w.weight3,
                        boneIndex0 = localToGlobalIndex[Mathf.Clamp(w.boneIndex0, 0, localToGlobalIndex.Length - 1)],
                        boneIndex1 = localToGlobalIndex[Mathf.Clamp(w.boneIndex1, 0, localToGlobalIndex.Length - 1)],
                        boneIndex2 = localToGlobalIndex[Mathf.Clamp(w.boneIndex2, 0, localToGlobalIndex.Length - 1)],
                        boneIndex3 = localToGlobalIndex[Mathf.Clamp(w.boneIndex3, 0, localToGlobalIndex.Length - 1)]
                    };
                    boneWeights.Add(bw);
                }

                // triangles appended in layer order (back to front)
                for (int i = 0; i < indices16.Length; i++)
                    triangles.Add(baseVertex + indices16[i]);

                vertexOffset += vertexCount;
            }

            // Build combined mesh
            combinedMesh = new Mesh
            {
                name = characterPrefab.name + "_CombinedSkinnedMesh"
            };

            if (vertices.Count > 65535)
                combinedMesh.indexFormat = IndexFormat.UInt32;

            combinedMesh.SetVertices(vertices);
            combinedMesh.SetUVs(0, uvs);
            combinedMesh.SetNormals(normals);
            combinedMesh.SetTriangles(triangles, 0);
            combinedMesh.boneWeights = boneWeights.ToArray();

            // Bind poses from global bones & rootBone
            var bonesArray = globalBones.ToArray();
            var bindPoses = new Matrix4x4[bonesArray.Length];
            for (int i = 0; i < bonesArray.Length; i++)
            {
                var b = bonesArray[i] != null ? bonesArray[i] : rootBone;
                bindPoses[i] = b.worldToLocalMatrix * rootBone.localToWorldMatrix;
            }

            combinedMesh.bindposes = bindPoses;
            combinedMesh.RecalculateBounds();

            // Single material using the first sprite's texture and optional shader override
            var firstSprite = parts[0].sprite;
            var shaderToUse = shader != null ? shader : Shader.Find("Sprites/Default");

            combinedMaterial = new Material(shaderToUse)
            {
                name = characterPrefab.name + "_CombinedMat"
            };

            if (firstSprite != null && firstSprite.texture != null)
            {
                combinedMaterial.SetTexture("_BaseMap", firstSprite.texture);
                combinedMaterial.mainTexture = firstSprite.texture;
            }

            // Add SkinnedMeshRenderer to the instance root
            var smr = instance.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = combinedMesh;
            smr.bones = bonesArray;
            smr.rootBone = rootBone;
            smr.sharedMaterial = combinedMaterial;
            smr.updateWhenOffscreen = true;

            // Remove SpriteRenderer & SpriteSkin components (bones / transforms remain).
            foreach (var sr in spriteRenderers)
                Object.DestroyImmediate(sr, true);
            foreach (var skin in spriteSkins)
                Object.DestroyImmediate(skin, true);

#if UNITY_EDITOR
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(instance);
#endif

            // Remove now-unused sprite part transforms (Head, CapeFront, Skirt, etc.)
            // while keeping the bone hierarchy intact.
            var keepTransforms = new HashSet<Transform>(bonesArray)
            {
                rootBone,
                instance.transform
            };

            var candidateTransforms = new HashSet<Transform>();
            foreach (var part in parts)
            {
                if (part.transform != null)
                    candidateTransforms.Add(part.transform);
            }

            // First pass: delete known part transforms that are not bones and have no other components
            foreach (var t in candidateTransforms)
            {
                if (t == null || keepTransforms.Contains(t))
                    continue;

                var comps = t.GetComponents<Component>();
                if (comps.Length <= 1) // only Transform
                {
                    Object.DestroyImmediate(t.gameObject);
                }
            }

            // Second pass: recursively prune any empty, non-bone transforms
            bool removed;
            do
            {
                removed = false;
                var allTransforms = instance.GetComponentsInChildren<Transform>(true);
                foreach (var tr in allTransforms)
                {
                    if (tr == null)
                        continue;
                    if (tr == instance.transform)
                        continue;
                    if (keepTransforms.Contains(tr))
                        continue;

                    if (tr.childCount == 0 && tr.GetComponents<Component>().Length == 1)
                    {
                        Object.DestroyImmediate(tr.gameObject);
                        removed = true;
                    }
                }
            } while (removed);

            return instance;
        }

        private struct Part
        {
            public Sprite sprite;
            public SpriteSkin spriteSkin;
            public SpriteRenderer spriteRenderer;
            public Transform transform;
            public int sortingOrder;
        }
    }
}

#endif
