using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using _PinBoy.Scripts.Gameplay.Actions;

namespace _PinBoy.Scripts.Gameplay.Actions.Editor
{
    [CustomPropertyDrawer(typeof(CharacterComboAction.ComboStep))]
    public class ComboStepDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            bool useDefaultAnimation = property.FindPropertyRelative("useDefaultAnimationSettings")?.boolValue ?? false;
            bool useDefaultPhaseFx = property.FindPropertyRelative("useDefaultPhaseFx")?.boolValue ?? false;

            SerializedProperty iterator = property.Copy();
            SerializedProperty end = iterator.GetEndProperty();
            bool enterChildren = true;
            float y = position.y;

            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;

                if (ShouldSkip(iterator, useDefaultAnimation, useDefaultPhaseFx))
                {
                    continue;
                }

                float height = EditorGUI.GetPropertyHeight(iterator, true);
                Rect fieldRect = new(position.x, y, position.width, height);
                EditorGUI.PropertyField(fieldRect, iterator, true);
                y += height + EditorGUIUtility.standardVerticalSpacing;

                if (iterator.name == "useDefaultAnimationSettings")
                {
                    useDefaultAnimation = iterator.boolValue;
                }
                else if (iterator.name == "useDefaultPhaseFx")
                {
                    useDefaultPhaseFx = iterator.boolValue;
                }
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            bool useDefaultAnimation = property.FindPropertyRelative("useDefaultAnimationSettings")?.boolValue ?? false;
            bool useDefaultPhaseFx = property.FindPropertyRelative("useDefaultPhaseFx")?.boolValue ?? false;

            SerializedProperty iterator = property.Copy();
            SerializedProperty end = iterator.GetEndProperty();
            bool enterChildren = true;
            float height = 0f;

            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;

                if (ShouldSkip(iterator, useDefaultAnimation, useDefaultPhaseFx))
                {
                    continue;
                }

                height += EditorGUI.GetPropertyHeight(iterator, true) + EditorGUIUtility.standardVerticalSpacing;
            }

            return Mathf.Max(0f, height - EditorGUIUtility.standardVerticalSpacing);
        }

        static bool ShouldSkip(SerializedProperty property, bool useDefaultAnimation, bool useDefaultPhaseFx)
        {
            if (useDefaultAnimation && AnimationFieldNames.Contains(property.name))
            {
                return true;
            }

            if (useDefaultPhaseFx && PhaseFxFieldNames.Contains(property.name))
            {
                return true;
            }

            return false;
        }

        static readonly HashSet<string> AnimationFieldNames = new()
        {
            "usePhaseAnimations",
            "animation",
            "windupAnimation",
            "activeAnimation",
            "recoveryAnimation",
            "animationCrossFade",
            "animationSpeedMultiplier",
            "scaleAnimationSpeedToStepDuration",
            "scaleWindupAnimationToStepDuration",
            "scaleActiveAnimationToStepDuration",
            "scaleRecoveryAnimationToStepDuration",
            "overrideAnimationSpeed"
        };

        static readonly HashSet<string> PhaseFxFieldNames = new()
        {
            "windupFx",
            "activeFx",
            "recoveryFx"
        };
    }
}
