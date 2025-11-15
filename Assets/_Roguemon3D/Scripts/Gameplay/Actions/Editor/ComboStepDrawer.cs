using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Actions.Editor
{
    [CustomPropertyDrawer(typeof(CharacterComboAction.ComboStep))]
    public class ComboStepDrawer : PropertyDrawer
    {
        const float SectionSpacing = 4f;

        class FoldoutState
        {
            public bool Identity = true;
            public bool Timing = true;
            public bool Movement = true;
            public bool HitDetection = true;
            public bool HitStop = true;
            public bool Transitions = true;
            public bool Vfx = true;
            public bool Animation = true;
        }

        static readonly Dictionary<string, FoldoutState> FoldoutCache = new();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect foldoutRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);

            if (property.isExpanded)
            {
                float y = foldoutRect.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                EditorGUI.indentLevel++;

                FoldoutState state = GetFoldoutState(property);

                y = DrawSection(position, y, property, "Identity", ref state.Identity, DrawIdentitySection);
                y += SectionSpacing;
                y = DrawSection(position, y, property, "Timing", ref state.Timing, DrawTimingSection);
                y += SectionSpacing;
                y = DrawSection(position, y, property, "Movement", ref state.Movement, DrawMovementSection);
                y += SectionSpacing;
                y = DrawSection(position, y, property, "Hit Detection", ref state.HitDetection, DrawHitDetectionSection);
                y += SectionSpacing;
                y = DrawSection(position, y, property, "Hit Stop", ref state.HitStop, DrawHitStopSection);
                y += SectionSpacing;
                y = DrawSection(position, y, property, "Transitions", ref state.Transitions, DrawTransitionsSection);
                y += SectionSpacing;
                y = DrawSection(position, y, property, "VFX", ref state.Vfx, DrawVfxSection);
                y += SectionSpacing;
                y = DrawSection(position, y, property, "Animation", ref state.Animation, DrawAnimationSection);

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            height += EditorGUIUtility.standardVerticalSpacing;

            FoldoutState state = GetFoldoutState(property);

            height += GetSectionHeight(property, state.Identity, IdentityProperties) + SectionSpacing;
            height += GetSectionHeight(property, state.Timing, TimingProperties) + SectionSpacing;
            height += GetSectionHeight(property, state.Movement, MovementProperties) + SectionSpacing;
            height += GetSectionHeight(property, state.HitDetection, HitDetectionProperties) + SectionSpacing;
            height += GetSectionHeight(property, state.HitStop, HitStopProperties) + SectionSpacing;
            height += GetSectionHeight(property, state.Transitions, TransitionsProperties) + SectionSpacing;
            height += GetSectionHeight(property, state.Vfx, VfxProperties) + SectionSpacing;
            height += GetSectionHeight(property, state.Animation, AnimationProperties);

            return height;
        }

        static FoldoutState GetFoldoutState(SerializedProperty property)
        {
            string key = property.propertyPath;
            if (!FoldoutCache.TryGetValue(key, out FoldoutState state))
            {
                state = new FoldoutState();
                FoldoutCache[key] = state;
            }

            return state;
        }

        float DrawSection(Rect position, float y, SerializedProperty root, string label, ref bool expanded, System.Action<Rect, SerializedProperty, ref float> drawer)
        {
            Rect headerRect = EditorGUI.IndentedRect(new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight));
            expanded = EditorGUI.Foldout(headerRect, expanded, label, true);
            y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            if (expanded)
            {
                EditorGUI.indentLevel++;
                drawer(position, root, ref y);
                EditorGUI.indentLevel--;
            }

            return y;
        }

        void DrawIdentitySection(Rect position, SerializedProperty root, ref float y)
        {
            DrawProperty(position, root.FindPropertyRelative("id"), ref y);
            DrawProperty(position, root.FindPropertyRelative("action"), ref y);
            DrawProperty(position, root.FindPropertyRelative("magnitudeMultiplier"), ref y);
            DrawProperty(position, root.FindPropertyRelative("triggerWhenNoTarget"), ref y);
            DrawProperty(position, root.FindPropertyRelative("allowRepeatedHits"), ref y);
            DrawProperty(position, root.FindPropertyRelative("stunImmune"), ref y);
        }

        void DrawTimingSection(Rect position, SerializedProperty root, ref float y)
        {
            DrawProperty(position, root.FindPropertyRelative("windup"), ref y);
            DrawProperty(position, root.FindPropertyRelative("active"), ref y);
            DrawProperty(position, root.FindPropertyRelative("recovery"), ref y);
            DrawProperty(position, root.FindPropertyRelative("comboResetDelay"), ref y);
            DrawProperty(position, root.FindPropertyRelative("transitionWindowOpen"), ref y);
            DrawProperty(position, root.FindPropertyRelative("transitionWindowClose"), ref y);
        }

        void DrawMovementSection(Rect position, SerializedProperty root, ref float y)
        {
            DrawProperty(position, root.FindPropertyRelative("lockMovement"), ref y);
            DrawProperty(position, root.FindPropertyRelative("zeroVelocityOnStart"), ref y);
            DrawProperty(position, root.FindPropertyRelative("missNudgeImpulse"), ref y);
            DrawProperty(position, root.FindPropertyRelative("missNudgeDelay"), ref y);
            DrawProperty(position, root.FindPropertyRelative("applyNudgeWhenHit"), ref y);
        }

        void DrawHitDetectionSection(Rect position, SerializedProperty root, ref float y)
        {
            DrawProperty(position, root.FindPropertyRelative("hitColliders"), ref y, true);
            DrawProperty(position, root.FindPropertyRelative("targetLayers"), ref y);
            DrawProperty(position, root.FindPropertyRelative("includeTriggerColliders"), ref y);
            DrawProperty(position, root.FindPropertyRelative("allegianceMask"), ref y, true);
            DrawProperty(position, root.FindPropertyRelative("fallbackDirection"), ref y);
        }

        void DrawHitStopSection(Rect position, SerializedProperty root, ref float y)
        {
            SerializedProperty hitStopOnExecute = root.FindPropertyRelative("hitStopOnExecute");
            SerializedProperty hitStopOnHit = root.FindPropertyRelative("hitStopOnHit");
            SerializedProperty multiplyHitStopPerHit = root.FindPropertyRelative("multiplyHitStopPerHit");

            DrawProperty(position, hitStopOnExecute, ref y);
            DrawProperty(position, hitStopOnHit, ref y);

            using (new EditorGUI.DisabledScope(hitStopOnHit.floatValue <= 0f))
            {
                DrawProperty(position, multiplyHitStopPerHit, ref y);
            }
        }

        void DrawTransitionsSection(Rect position, SerializedProperty root, ref float y)
        {
            DrawProperty(position, root.FindPropertyRelative("transitions"), ref y, true);
        }

        void DrawVfxSection(Rect position, SerializedProperty root, ref float y)
        {
            DrawProperty(position, root.FindPropertyRelative("vfx"), ref y);
        }

        void DrawAnimationSection(Rect position, SerializedProperty root, ref float y)
        {
            SerializedProperty animationProp = root.FindPropertyRelative("animation");
            SerializedProperty crossFadeProp = root.FindPropertyRelative("animationCrossFade");
            SerializedProperty scaleSpeedProp = root.FindPropertyRelative("scaleAnimationSpeedToStepDuration");
            SerializedProperty overrideSpeedProp = root.FindPropertyRelative("overrideAnimationSpeed");
            SerializedProperty speedMultiplierProp = root.FindPropertyRelative("animationSpeedMultiplier");

            DrawProperty(position, animationProp, ref y, true);
            DrawProperty(position, crossFadeProp, ref y);
            DrawProperty(position, scaleSpeedProp, ref y);
            DrawProperty(position, overrideSpeedProp, ref y);

            bool enableSpeed = scaleSpeedProp.boolValue || overrideSpeedProp.boolValue;
            using (new EditorGUI.DisabledScope(!enableSpeed))
            {
                DrawProperty(position, speedMultiplierProp, ref y);
            }
        }

        void DrawProperty(Rect position, SerializedProperty property, ref float y, bool includeChildren = false)
        {
            if (property == null)
            {
                return;
            }

            float height = EditorGUI.GetPropertyHeight(property, includeChildren);
            Rect rect = new Rect(position.x, y, position.width, height);
            EditorGUI.PropertyField(rect, property, includeChildren);
            y += height + EditorGUIUtility.standardVerticalSpacing;
        }

        static readonly string[] IdentityProperties =
        {
            "id",
            "action",
            "magnitudeMultiplier",
            "triggerWhenNoTarget",
            "allowRepeatedHits",
            "stunImmune"
        };

        static readonly string[] TimingProperties =
        {
            "windup",
            "active",
            "recovery",
            "comboResetDelay",
            "transitionWindowOpen",
            "transitionWindowClose"
        };

        static readonly string[] MovementProperties =
        {
            "lockMovement",
            "zeroVelocityOnStart",
            "missNudgeImpulse",
            "missNudgeDelay",
            "applyNudgeWhenHit"
        };

        static readonly string[] HitDetectionProperties =
        {
            "hitColliders",
            "targetLayers",
            "includeTriggerColliders",
            "allegianceMask",
            "fallbackDirection"
        };

        static readonly string[] HitStopProperties =
        {
            "hitStopOnExecute",
            "hitStopOnHit",
            "multiplyHitStopPerHit"
        };

        static readonly string[] TransitionsProperties =
        {
            "transitions"
        };

        static readonly string[] VfxProperties =
        {
            "vfx"
        };

        static readonly string[] AnimationProperties =
        {
            "animation",
            "animationCrossFade",
            "scaleAnimationSpeedToStepDuration",
            "overrideAnimationSpeed",
            "animationSpeedMultiplier"
        };

        float GetSectionHeight(SerializedProperty root, bool expanded, IReadOnlyList<string> propertyNames)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!expanded)
            {
                return height;
            }

            float spacing = EditorGUIUtility.standardVerticalSpacing;
            height += spacing;

            foreach (string propertyName in propertyNames)
            {
                SerializedProperty child = root.FindPropertyRelative(propertyName);
                if (child == null)
                {
                    continue;
                }

                bool includeChildren = child.propertyType == SerializedPropertyType.Generic || child.isArray;
                height += EditorGUI.GetPropertyHeight(child, includeChildren) + spacing;
            }

            return height;
        }
    }
}
