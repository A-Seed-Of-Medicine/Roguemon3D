
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace _PinBoy.Scripts.Utils.Editor
{
    /// <summary>
    /// Editor window that scans a folder for multi-sprite assets and generates
    /// looping sprite animations for each texture automatically.
    /// </summary>
    public class MultiSpriteAnimationCreatorWindow : EditorWindow
    {
        private enum SpriteOrder
        {
            ImportOrder,
            Name,
            Position
        }

        [SerializeField] private DefaultAsset searchFolder;
        [SerializeField] private float frameRate = 12f;
        [SerializeField] private bool includeSubfolders = true;
        [SerializeField] private bool loopTime = true;
        [SerializeField] private SpriteOrder spriteOrder = SpriteOrder.Name;

        private string statusMessage = string.Empty;
        private Vector2 scroll;

        [MenuItem("Tools/Animation/Multi-Sprite Animation Creator")]
        private static void ShowWindow()
        {
            var window = GetWindow<MultiSpriteAnimationCreatorWindow>();
            window.titleContent = new GUIContent("Sprite Animations");
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Multi-Sprite Animation Creator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Select a folder, then automatically build animations for every texture that imports multiple sprites.\n" +
                "Each animation is named after its source texture and will be overwritten if it already exists.",
                MessageType.Info);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                searchFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                    new GUIContent("Search Folder", "Folder to scan for multi-sprite textures."),
                    searchFolder,
                    typeof(DefaultAsset),
                    false);

                includeSubfolders = EditorGUILayout.ToggleLeft(
                    new GUIContent("Include Subfolders", "Look through all nested folders as well."),
                    includeSubfolders);

                frameRate = Mathf.Max(1f, EditorGUILayout.FloatField(
                    new GUIContent("Frame Rate", "Frames per second for generated animations."),
                    frameRate));

                loopTime = EditorGUILayout.ToggleLeft(
                    new GUIContent("Loop", "Whether generated clips loop by default."),
                    loopTime);

                spriteOrder = (SpriteOrder)EditorGUILayout.EnumPopup(
                    new GUIContent("Sprite Order", "How sprites are ordered inside the generated clip."),
                    spriteOrder);
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(searchFolder == null))
            {
                if (GUILayout.Button("Create Animations"))
                {
                    CreateAnimations();
                }
            }

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.Space();
                using (var scrollScope = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.Height(120f)))
                {
                    scroll = scrollScope.scrollPosition;
                    EditorGUILayout.HelpBox(statusMessage, MessageType.None);
                }
            }
        }

        private void CreateAnimations()
        {
            string folderPath = AssetDatabase.GetAssetPath(searchFolder);
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                statusMessage = "Please select a valid project folder.";
                ShowNotification(new GUIContent(statusMessage));
                return;
            }

            string normalizedFolder = NormalizeFolder(folderPath);
            List<MultiSpriteAsset> targets = FindTargets(normalizedFolder).ToList();

            if (targets.Count == 0)
            {
                statusMessage = "No multi-sprite textures found in the selected folder.";
                ShowNotification(new GUIContent(statusMessage));
                return;
            }

            int created = 0;
            int updated = 0;

            foreach (var target in targets)
            {
                AnimationClip clip = BuildClip(target.Sprites, target.AnimationName);
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(target.AnimationPath);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(clip, existing);
                    updated++;
                }
                else
                {
                    AssetDatabase.CreateAsset(clip, target.AnimationPath);
                    created++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            statusMessage =
                $"Processed {targets.Count} texture(s). Created {created} new animation(s), updated {updated}.\n" +
                string.Join("\n", targets.Select(t => $"{t.AnimationName} -> {t.AnimationPath}"));

            ShowNotification(new GUIContent("Animation generation complete."));
        }

        private IEnumerable<MultiSpriteAsset> FindTargets(string folderPath)
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!includeSubfolders)
                {
                    string directory = NormalizeFolder(Path.GetDirectoryName(path) ?? string.Empty);
                    if (!string.Equals(directory, folderPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                List<Sprite> sprites = AssetDatabase.LoadAllAssetRepresentationsAtPath(path)
                    .OfType<Sprite>()
                    .ToList();

                if (sprites.Count <= 1)
                    continue;

                yield return new MultiSpriteAsset(path, OrderSprites(sprites));
            }
        }

        private List<Sprite> OrderSprites(List<Sprite> sprites)
        {
            return spriteOrder switch
            {
                SpriteOrder.Name => sprites
                    .OrderBy(s => s.name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                SpriteOrder.Position => sprites
                    .OrderByDescending(s => s.rect.y)
                    .ThenBy(s => s.rect.x)
                    .ToList(),
                _ => sprites
            };
        }

        private AnimationClip BuildClip(IReadOnlyList<Sprite> sprites, string animationName)
        {
            var clip = new AnimationClip
            {
                frameRate = Mathf.Max(1f, frameRate),
                name = animationName
            };

            float frameTime = 1f / clip.frameRate;
            var keyframes = new ObjectReferenceKeyframe[sprites.Count];
            for (int i = 0; i < sprites.Count; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = i * frameTime,
                    value = sprites[i]
                };
            }

            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = string.Empty,
                propertyName = "m_Sprite"
            };

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loopTime;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            return clip;
        }

        private static string NormalizeFolder(string folderPath)
        {
            return folderPath.Replace("\\", "/").TrimEnd('/');
        }

        private class MultiSpriteAsset
        {
            public MultiSpriteAsset(string texturePath, IReadOnlyList<Sprite> sprites)
            {
                TexturePath = texturePath;
                AnimationName = Path.GetFileNameWithoutExtension(texturePath);
                AnimationPath = Path.Combine(Path.GetDirectoryName(texturePath) ?? string.Empty,
                    $"{AnimationName}.anim").Replace("\\", "/");
                Sprites = sprites.ToList();
            }

            public string TexturePath { get; }
            public string AnimationPath { get; }
            public string AnimationName { get; }
            public List<Sprite> Sprites { get; }
        }
    }
}
