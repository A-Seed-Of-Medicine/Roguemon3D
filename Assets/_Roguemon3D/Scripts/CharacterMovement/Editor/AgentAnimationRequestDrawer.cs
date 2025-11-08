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
    }
}
