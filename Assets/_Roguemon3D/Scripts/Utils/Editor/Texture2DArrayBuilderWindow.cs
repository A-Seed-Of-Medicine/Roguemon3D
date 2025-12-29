// Texture2DArrayBuilderWindow.cs
// Put this file anywhere under an "Editor" folder (e.g. Assets/Editor/Texture2DArrayBuilderWindow.cs)

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

public class Texture2DArrayBuilderWindow : EditorWindow
{
    private enum BuildMethod
    {
        GPU_Blit_ReadPixels_Safe,   // Works even if source textures are not Read/Write enabled; can resample.
        CPU_GetPixels_RequiresReadable
    }

    [SerializeField] private DefaultAsset outputFolder;
    [SerializeField] private string assetName = "NewTexture2DArray";
    [SerializeField] private List<Texture2D> textures = new List<Texture2D>();

    [SerializeField] private TextureFormat outputFormat = TextureFormat.RGBA32;
    [SerializeField] private bool generateMipmaps = true;
    [SerializeField] private bool linear = false;
    [SerializeField] private bool resampleToFirstSize = true;
    [SerializeField] private bool overwriteIfExists = false;
    [SerializeField] private BuildMethod buildMethod = BuildMethod.GPU_Blit_ReadPixels_Safe;

    private ReorderableList _list;

    [MenuItem("Tools/Texture2DArray Builder")]
    public static void Open()
    {
        var w = GetWindow<Texture2DArrayBuilderWindow>("Texture2DArray Builder");
        w.minSize = new Vector2(520, 420);
    }

    private void OnEnable()
    {
        if (textures == null) textures = new List<Texture2D>();

        _list = new ReorderableList(textures, typeof(Texture2D), true, true, true, true);
        _list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Source Textures (Texture2D)");
        _list.elementHeight = EditorGUIUtility.singleLineHeight + 6;

        _list.drawElementCallback = (rect, index, isActive, isFocused) =>
        {
            rect.y += 3;
            rect.height = EditorGUIUtility.singleLineHeight;
            textures[index] = (Texture2D)EditorGUI.ObjectField(rect, $"[{index}]", textures[index], typeof(Texture2D), false);
        };

        // IMPORTANT: prevent ReorderableList from trying to new Texture2D()
        _list.onAddCallback = list =>
        {
            textures.Add(null);
            list.index = textures.Count - 1;
        };
    }


    private void OnGUI()
    {
        EditorGUILayout.Space(6);

        DrawOutputSection();
        EditorGUILayout.Space(8);

        DrawOptionsSection();
        EditorGUILayout.Space(8);

        _list.DoLayoutList();

        EditorGUILayout.Space(10);

        using (new EditorGUI.DisabledScope(!CanBuild(out _)))
        {
            if (GUILayout.Button("Build Texture2DArray Asset", GUILayout.Height(32)))
            {
                Build();
            }
        }
    }

    private void DrawOutputSection()
    {
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

        outputFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Folder", outputFolder, typeof(DefaultAsset), false);
        assetName = EditorGUILayout.TextField("Asset Name", assetName);

        var folderPath = GetFolderPath(outputFolder);
        if (string.IsNullOrEmpty(folderPath))
        {
            EditorGUILayout.HelpBox("Select a valid folder under the project (e.g. Assets/Textures). If none is selected, Assets/ will be used.", MessageType.Info);
        }
        else if (!AssetDatabase.IsValidFolder(folderPath))
        {
            EditorGUILayout.HelpBox("Selected Output Folder is not a valid folder asset. If none is selected, Assets/ will be used.", MessageType.Warning);
        }
    }

    private void DrawOptionsSection()
    {
        EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);

        buildMethod = (BuildMethod)EditorGUILayout.EnumPopup("Build Method", buildMethod);
        outputFormat = (TextureFormat)EditorGUILayout.EnumPopup("Output Format", outputFormat);

        generateMipmaps = EditorGUILayout.Toggle("Generate Mipmaps", generateMipmaps);
        linear = EditorGUILayout.Toggle(new GUIContent("Linear", "If true, creates the Texture2DArray as linear (not sRGB)."), linear);

        resampleToFirstSize = EditorGUILayout.Toggle(
            new GUIContent("Resample To First Size", "If enabled, any texture not matching the first texture's size will be resampled to match."),
            resampleToFirstSize);

        overwriteIfExists = EditorGUILayout.Toggle("Overwrite If Exists", overwriteIfExists);

        if (buildMethod == BuildMethod.CPU_GetPixels_RequiresReadable)
        {
            EditorGUILayout.HelpBox("CPU GetPixels requires each source texture to have Read/Write enabled in its import settings.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("GPU Blit + ReadPixels works even if source textures are not Read/Write enabled and can resample to match size. It is slower than pure GPU copy.", MessageType.Info);
        }
    }

    private bool CanBuild(out string reason)
    {
        reason = null;

        if (string.IsNullOrWhiteSpace(assetName))
        {
            reason = "Asset Name is empty.";
            return false;
        }

        if (textures == null || textures.Count == 0)
        {
            reason = "No textures assigned.";
            return false;
        }

        Texture2D first = null;
        for (int i = 0; i < textures.Count; i++)
        {
            if (textures[i] == null) continue;
            first = textures[i];
            break;
        }

        if (first == null)
        {
            reason = "All texture slots are empty.";
            return false;
        }

        // Validate size matching if resample disabled
        if (!resampleToFirstSize)
        {
            int w = first.width;
            int h = first.height;
            for (int i = 0; i < textures.Count; i++)
            {
                var t = textures[i];
                if (t == null) continue;
                if (t.width != w || t.height != h)
                {
                    reason = "Texture sizes differ and Resample To First Size is disabled.";
                    return false;
                }
            }
        }

        // Validate readability for CPU method
        if (buildMethod == BuildMethod.CPU_GetPixels_RequiresReadable)
        {
            for (int i = 0; i < textures.Count; i++)
            {
                var t = textures[i];
                if (t == null) continue;

                var path = AssetDatabase.GetAssetPath(t);
                if (!string.IsNullOrEmpty(path))
                {
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer != null && !importer.isReadable)
                    {
                        reason = $"Texture '{t.name}' is not Read/Write enabled (required for CPU method).";
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private void Build()
    {
        if (!CanBuild(out var reason))
        {
            EditorUtility.DisplayDialog("Cannot Build", reason ?? "Unknown reason.", "OK");
            return;
        }

        Texture2D first = null;
        for (int i = 0; i < textures.Count; i++)
        {
            if (textures[i] != null) { first = textures[i]; break; }
        }

        if (first == null)
        {
            EditorUtility.DisplayDialog("Cannot Build", "No valid textures.", "OK");
            return;
        }

        int width = first.width;
        int height = first.height;

        // Count non-null textures; preserve order but skip nulls.
        var sources = new List<Texture2D>();
        foreach (var t in textures)
            if (t != null) sources.Add(t);

        int depth = sources.Count;

        var folderPath = GetFolderPath(outputFolder);
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            folderPath = "Assets";

        var assetPath = $"{folderPath}/{assetName}.asset";

        if (overwriteIfExists)
        {
            var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (existing != null)
                AssetDatabase.DeleteAsset(assetPath);
        }

        assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

        var array = new Texture2DArray(width, height, depth, outputFormat, generateMipmaps, linear)
        {
            wrapMode = first.wrapMode,
            filterMode = first.filterMode,
            anisoLevel = first.anisoLevel
        };

        try
        {
            EditorUtility.DisplayProgressBar("Building Texture2DArray", "Copying textures...", 0f);

            for (int slice = 0; slice < depth; slice++)
            {
                var src = sources[slice];
                float p = (depth <= 1) ? 1f : (slice / (float)(depth - 1));
                EditorUtility.DisplayProgressBar("Building Texture2DArray", $"Processing {src.name} ({slice + 1}/{depth})", p);

                if (buildMethod == BuildMethod.CPU_GetPixels_RequiresReadable)
                {
                    CopyViaCPU(src, array, slice, width, height);
                }
                else
                {
                    CopyViaGPUBlitReadPixels(src, array, slice, width, height, resampleToFirstSize);
                }
            }

            // If generateMipmaps is true, updateMipmaps=true will generate mipmaps from base level.
            array.Apply(updateMipmaps: generateMipmaps, makeNoLongerReadable: false);

            AssetDatabase.CreateAsset(array, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorGUIUtility.PingObject(array);
            Selection.activeObject = array;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Build Failed", ex.Message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void CopyViaCPU(Texture2D src, Texture2DArray dst, int slice, int width, int height)
    {
        if (src.width != width || src.height != height)
        {
            throw new InvalidOperationException(
                $"Texture '{src.name}' size {src.width}x{src.height} does not match required size {width}x{height}. " +
                $"Enable 'Resample To First Size' and use the GPU method, or ensure all textures match size.");
        }

        // Set base level; mipmaps will be generated by dst.Apply(updateMipmaps:true) if enabled.
        var pixels = src.GetPixels32(0);
        dst.SetPixels32(pixels, slice, 0);
    }

    private void CopyViaGPUBlitReadPixels(Texture2D src, Texture2DArray dst, int slice, int width, int height, bool allowResample)
    {
        if (!allowResample && (src.width != width || src.height != height))
        {
            throw new InvalidOperationException(
                $"Texture '{src.name}' size {src.width}x{src.height} does not match required size {width}x{height}, and resampling is disabled.");
        }

        // Render the source into an RT of the target size, then read back.
        var prev = RenderTexture.active;

        var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
            linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);

        try
        {
            Graphics.Blit(src, rt);

            RenderTexture.active = rt;
            var tmp = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: linear);
            tmp.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tmp.Apply(false, false);

            dst.SetPixels32(tmp.GetPixels32(0), slice, 0);

            UnityEngine.Object.DestroyImmediate(tmp);
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    private static string GetFolderPath(DefaultAsset folderAsset)
    {
        if (folderAsset == null) return null;
        var path = AssetDatabase.GetAssetPath(folderAsset);
        return path;
    }
}
