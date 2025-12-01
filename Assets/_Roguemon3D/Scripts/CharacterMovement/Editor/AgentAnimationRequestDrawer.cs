using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace _PinBoy.Scripts.CharacterMovement.Editor
{
    [CustomPropertyDrawer(typeof(AgentAnimationRequest))]
    public class AgentAnimationRequestDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            // Resolve properties once
            var modeProp    = property.FindPropertyRelative("directionMode");
            var mirrorProp  = property.FindPropertyRelative("mirrorLeftRight");
            var single      = property.FindPropertyRelative("singleClip");
            var north       = property.FindPropertyRelative("northClip");
            var south       = property.FindPropertyRelative("southClip");
            var east        = property.FindPropertyRelative("eastClip");
            var west        = property.FindPropertyRelative("westClip");
            var ne          = property.FindPropertyRelative("northEastClip");
            var se          = property.FindPropertyRelative("southEastClip");
            var nw          = property.FindPropertyRelative("northWestClip");
            var sw          = property.FindPropertyRelative("southWestClip");
            var crossFade   = property.FindPropertyRelative("crossFade");
            var overrideSp  = property.FindPropertyRelative("overrideSpeed");
            var speed       = property.FindPropertyRelative("playbackSpeed");

            // Foldout with summary
            var foldRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(
                foldRect,
                property.isExpanded,
                BuildSummaryLabel(label, modeProp, mirrorProp, single),
                true
            );

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                var y = foldRect.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

                Rect next(float h)
                {
                    var r = new Rect(position.x, y, position.width, h);
                    y += h + EditorGUIUtility.standardVerticalSpacing;
                    return r;
                }

                // convenience drawer that includes children
                void DrawWithChildren(SerializedProperty p, string lbl = null)
                {
                    var gc = string.IsNullOrEmpty(lbl) ? GUIContent.none : new GUIContent(lbl);
                    float h = EditorGUI.GetPropertyHeight(p, gc, true);
                    EditorGUI.PropertyField(next(h), p, gc, true);
                }

                // Controls
                DrawWithChildren(modeProp, "Direction Mode");
                DrawWithChildren(mirrorProp, "Mirror Left/Right");

                // Clips by mode
                switch ((AgentAnimationRequest.DirectionMode)modeProp.enumValueIndex)
                {
                    case AgentAnimationRequest.DirectionMode.Single:
                        DrawWithChildren(single, "Clip");
                        break;

                    case AgentAnimationRequest.DirectionMode.FourWay:
                        DrawWithChildren(north, "North");
                        DrawWithChildren(south, "South");
                        DrawWithChildren(east,  "East");
                        if (!mirrorProp.boolValue) DrawWithChildren(west, "West");
                        break;

                    case AgentAnimationRequest.DirectionMode.EightWay:
                        DrawWithChildren(north, "North");
                        DrawWithChildren(south, "South");
                        DrawWithChildren(east,  "East");
                        DrawWithChildren(ne,    "North-East");
                        DrawWithChildren(se,    "South-East");
                        if (!mirrorProp.boolValue)
                        {
                            DrawWithChildren(west, "West");
                            DrawWithChildren(nw,   "North-West");
                            DrawWithChildren(sw,   "South-West");
                        }
                        break;
                }

                // Playback options
                DrawWithChildren(crossFade, "Cross Fade");
                DrawWithChildren(overrideSp, "Override Speed");
                if (overrideSp.boolValue) DrawWithChildren(speed, "Playback Speed");

                // Bulk assignment helper
                var buttonRect = next(EditorGUIUtility.singleLineHeight);
                if (GUI.Button(buttonRect, "Assign Animations"))
                {
                    AgentAnimationAssignmentWindow.Open(property);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // Always at least one line for the foldout
            float total = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded) return total;

            float s = EditorGUIUtility.standardVerticalSpacing;

            var modeProp    = property.FindPropertyRelative("directionMode");
            var mirrorProp  = property.FindPropertyRelative("mirrorLeftRight");
            var single      = property.FindPropertyRelative("singleClip");
            var north       = property.FindPropertyRelative("northClip");
            var south       = property.FindPropertyRelative("southClip");
            var east        = property.FindPropertyRelative("eastClip");
            var west        = property.FindPropertyRelative("westClip");
            var ne          = property.FindPropertyRelative("northEastClip");
            var se          = property.FindPropertyRelative("southEastClip");
            var nw          = property.FindPropertyRelative("northWestClip");
            var sw          = property.FindPropertyRelative("southWestClip");
            var crossFade   = property.FindPropertyRelative("crossFade");
            var overrideSp  = property.FindPropertyRelative("overrideSpeed");
            var speed       = property.FindPropertyRelative("playbackSpeed");

            float Add(SerializedProperty p, string lbl = null)
            {
                var gc = string.IsNullOrEmpty(lbl) ? GUIContent.none : new GUIContent(lbl);
                float h = EditorGUI.GetPropertyHeight(p, gc, true);
                total += h + s;
                return h;
            }

            // Controls
            Add(modeProp, "Direction Mode");
            Add(mirrorProp, "Mirror Left/Right");

            // Clips by mode
            switch ((AgentAnimationRequest.DirectionMode)modeProp.enumValueIndex)
            {
                case AgentAnimationRequest.DirectionMode.Single:
                    Add(single, "Clip");
                    break;

                case AgentAnimationRequest.DirectionMode.FourWay:
                    Add(north, "North");
                    Add(south, "South");
                    Add(east,  "East");
                    if (!mirrorProp.boolValue) Add(west, "West");
                    break;

                case AgentAnimationRequest.DirectionMode.EightWay:
                    Add(north, "North");
                    Add(south, "South");
                    Add(east,  "East");
                    Add(ne,    "North-East");
                    Add(se,    "South-East");
                    if (!mirrorProp.boolValue)
                    {
                        Add(west, "West");
                        Add(nw,   "North-West");
                        Add(sw,   "South-West");
                    }
                    break;
            }

            // Playback options
            Add(crossFade, "Cross Fade");
            Add(overrideSp, "Override Speed");
            if (overrideSp.boolValue) Add(speed, "Playback Speed");

            // Assign button
            total += EditorGUIUtility.singleLineHeight + s;

            return total;
        }

        // Builds the foldout title with a short summary. Never reads objectReference from non-object fields.
        static GUIContent BuildSummaryLabel(GUIContent baseLabel,
                                            SerializedProperty modeProp,
                                            SerializedProperty mirrorProp,
                                            SerializedProperty singleClipProp)
        {
            string summary = string.Empty;

            var mode = (AgentAnimationRequest.DirectionMode)modeProp.enumValueIndex;
            switch (mode)
            {
                case AgentAnimationRequest.DirectionMode.Single:
                    summary = singleClipProp.propertyType == SerializedPropertyType.ObjectReference
                        ? SafeObjectLabel(singleClipProp)
                        : "Single";
                    break;

                case AgentAnimationRequest.DirectionMode.FourWay:
                    summary = mirrorProp.boolValue ? "4-Way (Mirror)" : "4-Way";
                    break;

                case AgentAnimationRequest.DirectionMode.EightWay:
                    summary = mirrorProp.boolValue ? "8-Way (Mirror)" : "8-Way";
                    break;
            }

            if (string.IsNullOrEmpty(summary)) return baseLabel;

            var text = string.IsNullOrEmpty(baseLabel.text)
                ? summary
                : $"{baseLabel.text} [{summary}]";

            // Preserve tooltip and image if any
            return new GUIContent(text, baseLabel.image, baseLabel.tooltip);
        }

        // Safe label for ObjectReference only
        static string SafeObjectLabel(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference)
                return "Value";

            return property.objectReferenceValue != null
                ? property.objectReferenceValue.name
                : "None";
        }

        sealed class AgentAnimationAssignmentWindow : EditorWindow
        {
            SerializedObject serializedObject;
            string propertyPath;
            string folderAssetPath;
            Vector2 scrollPosition;

            const string DialogTitle = "Assign Agent Animations";

            public static void Open(SerializedProperty property)
            {
                if (property == null || property.serializedObject == null)
                {
                    Debug.LogWarning("No property available to assign animations.");
                    return;
                }

                var window = CreateInstance<AgentAnimationAssignmentWindow>();
                window.serializedObject = property.serializedObject;
                window.propertyPath = property.propertyPath;
                window.titleContent = new GUIContent(DialogTitle);
                window.minSize = new Vector2(350f, 200f);
                window.ShowUtility();
            }

            void OnGUI()
            {
                using var scope = new EditorGUI.DisabledScope(serializedObject == null);
                if (serializedObject == null)
                {
                    EditorGUILayout.HelpBox("No serialized object to modify.", MessageType.Error);
                    return;
                }

                serializedObject.Update();
                SerializedProperty property = serializedObject.FindProperty(propertyPath);
                if (property == null)
                {
                    EditorGUILayout.HelpBox("The animation request could not be found.", MessageType.Error);
                    return;
                }

                EditorGUILayout.LabelField(DialogTitle, EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Select a folder to scan for animation clips that include direction suffixes like _North, _NE, _South, etc.", MessageType.Info);

                DrawFolderSelector();

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(folderAssetPath)))
                {
                    if (GUILayout.Button("Assign From Folder"))
                    {
                        AssignFromFolder(folderAssetPath, property);
                    }
                }
            }

            void DrawFolderSelector()
            {
                using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPosition))
                {
                    scrollPosition = scroll.scrollPosition;
                    EditorGUILayout.LabelField("Selected Folder", string.IsNullOrEmpty(folderAssetPath) ? "None" : folderAssetPath, EditorStyles.wordWrappedLabel);
                    if (GUILayout.Button("Choose Folder"))
                    {
                        string selected = EditorUtility.OpenFolderPanel("Select Animation Folder", Application.dataPath, string.Empty);
                        if (!string.IsNullOrEmpty(selected))
                        {
                            folderAssetPath = ConvertToAssetPath(selected);
                            if (string.IsNullOrEmpty(folderAssetPath))
                            {
                                EditorUtility.DisplayDialog(DialogTitle, "Please select a folder inside the project Assets directory.", "OK");
                            }
                        }
                    }
                }
            }

            static string ConvertToAssetPath(string absolutePath)
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string fullPath = Path.GetFullPath(absolutePath);
                if (!fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string relative = fullPath.Substring(projectRoot.Length + 1).Replace('\\', '/');
                return relative;
            }

            void AssignFromFolder(string assetFolderPath, SerializedProperty property)
            {
                Undo.RecordObjects(serializedObject.targetObjects, "Assign Agent Animations");

                var clips = LoadClips(assetFolderPath);
                var result = MapClipsToRequest(clips, property);

                serializedObject.ApplyModifiedProperties();

                string summary = result.foundAny
                    ? $"Assigned {result.assignedCount} clip(s) from '{assetFolderPath}'."
                    : "No matching clips were found to assign.";
                EditorUtility.DisplayDialog(DialogTitle, summary, "OK");
            }

            static IReadOnlyList<AnimationClip> LoadClips(string assetFolderPath)
            {
                List<AnimationClip> clips = new();
                string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { assetFolderPath });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (clip)
                    {
                        clips.Add(clip);
                    }
                }

                return clips;
            }

            static (int assignedCount, bool foundAny) MapClipsToRequest(IReadOnlyList<AnimationClip> clips, SerializedProperty property)
            {
                if (clips.Count == 0)
                {
                    return (0, false);
                }

                var bindings = new List<DirectionBinding>
                {
                    new("northEastClip", "NORTHEAST", "NE"),
                    new("southEastClip", "SOUTHEAST", "SE"),
                    new("southWestClip", "SOUTHWEST", "SW"),
                    new("northWestClip", "NORTHWEST", "NW"),
                    new("northClip", "NORTH", "N"),
                    new("southClip", "SOUTH", "S"),
                    new("eastClip", "EAST", "E"),
                    new("westClip", "WEST", "W"),
                };

                var modeProp = property.FindPropertyRelative("directionMode");
                int assigned = 0;
                bool found = false;

                foreach (AnimationClip clip in clips)
                {
                    string upperName = clip.name.ToUpperInvariant();
                    foreach (DirectionBinding binding in bindings)
                    {
                        if (!binding.Matches(upperName))
                        {
                            continue;
                        }

                        SerializedProperty targetProp = property.FindPropertyRelative(binding.PropertyName);
                        if (targetProp != null)
                        {
                            targetProp.objectReferenceValue = clip;
                            assigned++;
                        }

                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    bool hasDiagonal = HasClip(property, "northEastClip") || HasClip(property, "southEastClip") || HasClip(property, "southWestClip") || HasClip(property, "northWestClip");
                    bool hasCardinal = HasClip(property, "northClip") || HasClip(property, "southClip") || HasClip(property, "eastClip") || HasClip(property, "westClip");

                    if (hasDiagonal)
                    {
                        modeProp.enumValueIndex = (int)AgentAnimationRequest.DirectionMode.EightWay;
                    }
                    else if (hasCardinal)
                    {
                        modeProp.enumValueIndex = (int)AgentAnimationRequest.DirectionMode.FourWay;
                    }
                }

                return (assigned, found);
            }

            static bool HasClip(SerializedProperty property, string relativeName)
            {
                SerializedProperty prop = property.FindPropertyRelative(relativeName);
                return prop != null && prop.objectReferenceValue != null;
            }

            readonly struct DirectionBinding
            {
                public string PropertyName { get; }
                readonly string[] tokens;

                public DirectionBinding(string propertyName, params string[] tokens)
                {
                    PropertyName = propertyName;
                    this.tokens = tokens;
                }

                public bool Matches(string upperName)
                {
                    foreach (string token in tokens)
                    {
                        if (NameHasToken(upperName, token))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                static bool NameHasToken(string upperName, string token)
                {
                    string withUnderscore = "_" + token;
                    return upperName.EndsWith(token, StringComparison.Ordinal)
                           || upperName.Contains(withUnderscore, StringComparison.Ordinal)
                           || upperName.StartsWith(token + "_", StringComparison.Ordinal);
                }
            }
        }
    }
}
