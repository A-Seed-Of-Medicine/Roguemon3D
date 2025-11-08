using UnityEditor;
using UnityEngine;

namespace _PinBoy.Scripts.CharacterMovement.Editor
{
    [CustomPropertyDrawer(typeof(AgentAnimationRequest))]
    public class AgentAnimationRequestDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float total = lineHeight;

            if (!property.isExpanded)
            {
                return total;
            }

            SerializedProperty modeProp = property.FindPropertyRelative("directionMode");
            SerializedProperty mirrorProp = property.FindPropertyRelative("mirrorLeftRight");
            var mode = (AgentAnimationRequest.DirectionMode)modeProp.enumValueIndex;

            int clipLines = mode switch
            {
                AgentAnimationRequest.DirectionMode.Single => 1,
                AgentAnimationRequest.DirectionMode.FourWay => mirrorProp.boolValue ? 3 : 4,
                AgentAnimationRequest.DirectionMode.EightWay => mirrorProp.boolValue ? 5 : 8,
                _ => 1
            };

            int lines = 1; // Direction mode
            if (mode != AgentAnimationRequest.DirectionMode.Single)
            {
                lines += 1; // Mirror toggle
            }

            lines += clipLines;
            lines += 1; // Cross fade
            lines += 1; // Override speed
            lines += 1; // Playback speed

            total += spacing;
            total += lines * lineHeight;
            total += Mathf.Max(0, lines - 1) * spacing;
            return total;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            SerializedProperty modeProp = property.FindPropertyRelative("directionMode");
            SerializedProperty mirrorProp = property.FindPropertyRelative("mirrorLeftRight");
            SerializedProperty singleProp = property.FindPropertyRelative("singleClip");
            SerializedProperty northProp = property.FindPropertyRelative("northClip");
            SerializedProperty southProp = property.FindPropertyRelative("southClip");
            SerializedProperty eastProp = property.FindPropertyRelative("eastClip");
            SerializedProperty westProp = property.FindPropertyRelative("westClip");
            SerializedProperty northEastProp = property.FindPropertyRelative("northEastClip");
            SerializedProperty southEastProp = property.FindPropertyRelative("southEastClip");
            SerializedProperty northWestProp = property.FindPropertyRelative("northWestClip");
            SerializedProperty southWestProp = property.FindPropertyRelative("southWestClip");
            SerializedProperty crossFadeProp = property.FindPropertyRelative("crossFade");
            SerializedProperty overrideSpeedProp = property.FindPropertyRelative("overrideSpeed");
            SerializedProperty playbackSpeedProp = property.FindPropertyRelative("playbackSpeed");

            Rect foldoutRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            GUIContent foldoutLabel = new GUIContent(BuildSummaryLabel(label, modeProp, mirrorProp, singleProp));
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, foldoutLabel, true);

            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            EditorGUI.indentLevel++;
            float y = foldoutRect.yMax + EditorGUIUtility.standardVerticalSpacing;
            float width = position.width;
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            Rect fieldRect = new Rect(position.x, y, width, lineHeight);
            EditorGUI.PropertyField(fieldRect, modeProp);
            y += lineHeight + spacing;

            var mode = (AgentAnimationRequest.DirectionMode)modeProp.enumValueIndex;
            if (mode != AgentAnimationRequest.DirectionMode.Single)
            {
                fieldRect.y = y;
                EditorGUI.PropertyField(fieldRect, mirrorProp);
                y += lineHeight + spacing;
            }

            switch (mode)
            {
                case AgentAnimationRequest.DirectionMode.Single:
                    y = DrawClipField(position.x, width, y, singleProp, "Clip");
                    break;
                case AgentAnimationRequest.DirectionMode.FourWay:
                    y = DrawClipField(position.x, width, y, southProp, "South / Down Clip");
                    y = DrawClipField(position.x, width, y, northProp, "North / Up Clip");
                    y = DrawClipField(position.x, width, y, eastProp, "East / Right Clip");
                    if (!mirrorProp.boolValue)
                    {
                        y = DrawClipField(position.x, width, y, westProp, "West / Left Clip");
                    }
                    break;
                case AgentAnimationRequest.DirectionMode.EightWay:
                    y = DrawClipField(position.x, width, y, southProp, "South / Down Clip");
                    y = DrawClipField(position.x, width, y, southEastProp, "South-East Clip");
                    y = DrawClipField(position.x, width, y, eastProp, "East / Right Clip");
                    y = DrawClipField(position.x, width, y, northEastProp, "North-East Clip");
                    y = DrawClipField(position.x, width, y, northProp, "North / Up Clip");
                    if (!mirrorProp.boolValue)
                    {
                        y = DrawClipField(position.x, width, y, northWestProp, "North-West Clip");
                        y = DrawClipField(position.x, width, y, westProp, "West / Left Clip");
                        y = DrawClipField(position.x, width, y, southWestProp, "South-West Clip");
                    }
                    break;
            }

            y = DrawFloatField(position.x, width, y, crossFadeProp, "Cross Fade");

            Rect overrideRect = new Rect(position.x, y, width, lineHeight);
            EditorGUI.PropertyField(overrideRect, overrideSpeedProp);
            y += lineHeight + spacing;

            using (new EditorGUI.DisabledScope(!overrideSpeedProp.boolValue))
            {
                y = DrawFloatField(position.x, width, y, playbackSpeedProp, "Playback Speed");
            }

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        static float DrawClipField(float x, float width, float y, SerializedProperty property, string label)
        {
            Rect rect = new Rect(x, y, width, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(rect, property, new GUIContent(label));
            return y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        }

        static float DrawFloatField(float x, float width, float y, SerializedProperty property, string label)
        {
            Rect rect = new Rect(x, y, width, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(rect, property, new GUIContent(label));
            return y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        }

        static string BuildSummaryLabel(GUIContent baseLabel, SerializedProperty modeProp, SerializedProperty mirrorProp, SerializedProperty singleProp)
        {
            var mode = (AgentAnimationRequest.DirectionMode)modeProp.enumValueIndex;
            string summary = mode switch
            {
                AgentAnimationRequest.DirectionMode.Single => ObjectLabel(singleProp),
                AgentAnimationRequest.DirectionMode.FourWay => mirrorProp.boolValue ? "4-Way (Mirror)" : "4-Way",
                AgentAnimationRequest.DirectionMode.EightWay => mirrorProp.boolValue ? "8-Way (Mirror)" : "8-Way",
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(summary))
            {
                return baseLabel.text;
            }

            return string.IsNullOrEmpty(baseLabel.text)
                ? summary
                : $"{baseLabel.text} [{summary}]";
        }

        static string ObjectLabel(SerializedProperty property)
        {
            return property.objectReferenceValue != null ? property.objectReferenceValue.name : "None";
        }
    }
}
