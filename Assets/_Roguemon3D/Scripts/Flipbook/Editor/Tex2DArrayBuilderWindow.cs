// Assets/Editor/Tex2DArrayBuilderWindow.cs
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    public class Tex2DArrayBuilderWindow : EditorWindow {
        DefaultAsset sourceFolder;
        bool mipmaps = true;

        [MenuItem("Tools/Sprites/Build Texture2DArray")]
        static void Open() => GetWindow<Tex2DArrayBuilderWindow>("Build Texture2DArray");

        void OnGUI() {
            GUILayout.Label("Build Texture2DArray from a folder of equally-sized sprites", EditorStyles.boldLabel);
            sourceFolder = (DefaultAsset)EditorGUILayout.ObjectField("Source Folder", sourceFolder, typeof(DefaultAsset), false);
            mipmaps = EditorGUILayout.Toggle("Generate Mipmaps", mipmaps);

            using (new EditorGUI.DisabledScope(sourceFolder == null)) {
                if (GUILayout.Button("Build Array")) Build();
            }
        }

        void Build() {
            var path = AssetDatabase.GetAssetPath(sourceFolder);
            var texPaths = AssetDatabase.FindAssets("t:Texture2D", new[] { path })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p) // define a stable layer order
                .ToArray();

            if (texPaths.Length == 0) { Debug.LogWarning("No textures found."); return; }

            var texes = texPaths.Select(p => AssetDatabase.LoadAssetAtPath<Texture2D>(p)).ToArray();
            int w = texes[0].width, h = texes[0].height, depth = texes.Length;

            // sanity check
            if (texes.Any(t => t.width != w || t.height != h)) {
                EditorUtility.DisplayDialog("Error", "All textures must have the same dimensions.", "OK");
                return;
            }

            // Choose a format that matches import (you can force e.g., RGBA32 if unsure)
            var fmt = texes[0].format;
            var array = new Texture2DArray(w, h, depth, fmt, mipmaps, false) {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            for (int i = 0; i < depth; i++) {
                Graphics.CopyTexture(texes[i], 0, 0, array, i, 0);
            }
            array.Apply(false, true);

            var savePath = EditorUtility.SaveFilePanelInProject("Save Texture2DArray", "SpriteArray", "asset", "Select save location");
            if (!string.IsNullOrEmpty(savePath)) {
                AssetDatabase.CreateAsset(array, savePath);
                AssetDatabase.SaveAssets();
                Selection.activeObject = array;
            }
        }
    }
}
#endif
