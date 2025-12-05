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
            Rect foldoutRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);

            if (property.isExpanded)
            {
                float spacing = EditorGUIUtility.standardVerticalSpacing;
                float y = foldoutRect.y + EditorGUIUtility.singleLineHeight + spacing;
                EditorGUI.indentLevel++;

                Rect NextRect(float height)
                {
                    Rect rect = new Rect(position.x, y, position.width, height);
                    y += height + spacing;
                    return rect;
                }

                void Header(string text)
                {
                    Rect headerRect = NextRect(EditorGUIUtility.singleLineHeight);
                    EditorGUI.LabelField(headerRect, text, EditorStyles.boldLabel);
                }

                void DrawProperty(SerializedProperty prop, string displayName = null, bool includeChildren = true, bool disabled = false)
                {
                    GUIContent content = string.IsNullOrEmpty(displayName) ? null : new GUIContent(displayName);
                    float height = EditorGUI.GetPropertyHeight(prop, content, includeChildren);
                    Rect rect = NextRect(height);
                    using (new EditorGUI.DisabledScope(disabled))
                    {
                        EditorGUI.PropertyField(rect, prop, content, includeChildren);
                    }
                }

                Header("Identity");
                EditorGUI.indentLevel++;
                DrawProperty(property.FindPropertyRelative("id"));
                DrawProperty(property.FindPropertyRelative("action"));
                DrawProperty(property.FindPropertyRelative("magnitudeMultiplier"));
                DrawProperty(property.FindPropertyRelative("triggerWhenNoTarget"));
                DrawProperty(property.FindPropertyRelative("allowRepeatedHits"));
                DrawProperty(property.FindPropertyRelative("stunImmune"));
                EditorGUI.indentLevel--;

                Header("Timing");
                EditorGUI.indentLevel++;
                DrawProperty(property.FindPropertyRelative("windup"));
                DrawProperty(property.FindPropertyRelative("active"));
                DrawProperty(property.FindPropertyRelative("recovery"));
                DrawProperty(property.FindPropertyRelative("comboResetDelay"));
                DrawProperty(property.FindPropertyRelative("transitionWindowOpen"));
                DrawProperty(property.FindPropertyRelative("transitionWindowClose"));
                EditorGUI.indentLevel--;

                Header("Movement");
                EditorGUI.indentLevel++;
                DrawProperty(property.FindPropertyRelative("movementLocks"), "Movement Locks");
                DrawProperty(property.FindPropertyRelative("lockAim"));
                DrawProperty(property.FindPropertyRelative("zeroVelocityOnStart"));
                DrawProperty(property.FindPropertyRelative("missNudgeImpulse"));
                DrawProperty(property.FindPropertyRelative("missNudgeDelay"));
                DrawProperty(property.FindPropertyRelative("applyNudgeWhenHit"));
                EditorGUI.indentLevel--;

                Header("Charge");
                EditorGUI.indentLevel++;
                SerializedProperty chargeSettings = property.FindPropertyRelative("chargeSettings");
                DrawProperty(chargeSettings.FindPropertyRelative("chargeWindup"));
                DrawProperty(chargeSettings.FindPropertyRelative("minimumChargeTime"));
                DrawProperty(chargeSettings.FindPropertyRelative("maximumChargeTime"));
                DrawProperty(chargeSettings.FindPropertyRelative("movementLocks"), "Charge Movement Locks");
                EditorGUI.indentLevel--;

                Header("Hit Detection");
                EditorGUI.indentLevel++;
                DrawProperty(property.FindPropertyRelative("hitDetectorPrefab"));
                DrawProperty(property.FindPropertyRelative("parentHitDetectorToPivot"));
                DrawProperty(property.FindPropertyRelative("hitDetectorPositionOffset"));
                DrawProperty(property.FindPropertyRelative("hitDetectorRotationOffset"));
                DrawProperty(property.FindPropertyRelative("fallbackDirection"));
                EditorGUI.indentLevel--;

                Header("Branches");
                EditorGUI.indentLevel++;
                DrawProperty(property.FindPropertyRelative("transitions"));
                EditorGUI.indentLevel--;

                Header("Hit Stop");
                EditorGUI.indentLevel++;
                SerializedProperty hitStopOnHit = property.FindPropertyRelative("hitStopOnHit");
                DrawProperty(property.FindPropertyRelative("hitStopOnExecute"), "On Execute");
                DrawProperty(hitStopOnHit, "On Hit");
                bool showMultiply = hitStopOnHit.floatValue > 0f;
                DrawProperty(property.FindPropertyRelative("multiplyHitStopPerHit"), "Multiply Per Hit", includeChildren: false, disabled: !showMultiply);
                EditorGUI.indentLevel--;

                Header("Animation");
                EditorGUI.indentLevel++;
                SerializedProperty usePhaseAnimations = property.FindPropertyRelative("usePhaseAnimations");
                SerializedProperty animation = property.FindPropertyRelative("animation");
                SerializedProperty windupAnimation = property.FindPropertyRelative("windupAnimation");
                SerializedProperty activeAnimation = property.FindPropertyRelative("activeAnimation");
                SerializedProperty recoveryAnimation = property.FindPropertyRelative("recoveryAnimation");
                SerializedProperty scaleWindupToDuration = property.FindPropertyRelative("scaleWindupAnimationToStepDuration");
                SerializedProperty scaleActiveToDuration = property.FindPropertyRelative("scaleActiveAnimationToStepDuration");
                SerializedProperty scaleRecoveryToDuration = property.FindPropertyRelative("scaleRecoveryAnimationToStepDuration");
                SerializedProperty crossFade = property.FindPropertyRelative("animationCrossFade");
                SerializedProperty speedMultiplier = property.FindPropertyRelative("animationSpeedMultiplier");
                SerializedProperty scaleToDuration = property.FindPropertyRelative("scaleAnimationSpeedToStepDuration");
                SerializedProperty overrideSpeed = property.FindPropertyRelative("overrideAnimationSpeed");

                DrawProperty(usePhaseAnimations, "Use Phase Animations", includeChildren: false);
                bool usingPhases = usePhaseAnimations.boolValue;
                if (usingPhases)
                {
                    DrawProperty(windupAnimation, "Windup Animation");
                    DrawProperty(scaleWindupToDuration, "Scale Windup To Step Duration", includeChildren: false);
                    DrawProperty(activeAnimation, "Active Animation");
                    DrawProperty(scaleActiveToDuration, "Scale Active To Step Duration", includeChildren: false);
                    DrawProperty(recoveryAnimation, "Recovery Animation");
                    DrawProperty(scaleRecoveryToDuration, "Scale Recovery To Step Duration", includeChildren: false);
                }
                else
                {
                    DrawProperty(animation);
                    DrawProperty(scaleToDuration, "Scale Speed To Step Duration", includeChildren: false);
                }

                bool hasAnimationClip = usingPhases
                    ? HasAnyAnimationClip(windupAnimation) || HasAnyAnimationClip(activeAnimation) || HasAnyAnimationClip(recoveryAnimation)
                    : HasAnyAnimationClip(animation);
                DrawProperty(crossFade, "Cross Fade", disabled: !hasAnimationClip);
                DrawProperty(overrideSpeed, "Force Override Speed", includeChildren: false);
                bool phaseScaled = scaleWindupToDuration.boolValue || scaleActiveToDuration.boolValue || scaleRecoveryToDuration.boolValue || scaleToDuration.boolValue;
                bool enableMultiplier = (usingPhases ? phaseScaled : scaleToDuration.boolValue) || overrideSpeed.boolValue;
                DrawProperty(speedMultiplier, "Speed Multiplier", includeChildren: false, disabled: !enableMultiplier);
                EditorGUI.indentLevel--;

                Header("VFX");
                EditorGUI.indentLevel++;
                DrawProperty(property.FindPropertyRelative("vfx"));
                EditorGUI.indentLevel--;

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

            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float headerHeight = EditorGUIUtility.singleLineHeight;

            float AddHeader()
            {
                height += headerHeight + spacing;
                return headerHeight;
            }

            float AddProperty(SerializedProperty prop, string displayName = null)
            {
                GUIContent content = string.IsNullOrEmpty(displayName) ? null : new GUIContent(displayName);
                float h = EditorGUI.GetPropertyHeight(prop, content, true);
                height += h + spacing;
                return h;
            }

            float AddPropertyDisabled(SerializedProperty prop, string displayName = null)
            {
                return AddProperty(prop, displayName);
            }

            // Identity
            AddHeader();
            AddProperty(property.FindPropertyRelative("id"));
            AddProperty(property.FindPropertyRelative("action"));
            AddProperty(property.FindPropertyRelative("magnitudeMultiplier"));
            AddProperty(property.FindPropertyRelative("triggerWhenNoTarget"));
            AddProperty(property.FindPropertyRelative("allowRepeatedHits"));
            AddProperty(property.FindPropertyRelative("stunImmune"));

            // Timing
            AddHeader();
            AddProperty(property.FindPropertyRelative("windup"));
            AddProperty(property.FindPropertyRelative("active"));
            AddProperty(property.FindPropertyRelative("recovery"));
            AddProperty(property.FindPropertyRelative("comboResetDelay"));
            AddProperty(property.FindPropertyRelative("transitionWindowOpen"));
            AddProperty(property.FindPropertyRelative("transitionWindowClose"));

            // Movement
            AddHeader();
            AddProperty(property.FindPropertyRelative("movementLocks"));
            AddProperty(property.FindPropertyRelative("lockAim"));
            AddProperty(property.FindPropertyRelative("zeroVelocityOnStart"));
            AddProperty(property.FindPropertyRelative("missNudgeImpulse"));
            AddProperty(property.FindPropertyRelative("missNudgeDelay"));
            AddProperty(property.FindPropertyRelative("applyNudgeWhenHit"));

            // Charge
            AddHeader();
            SerializedProperty chargeSettings = property.FindPropertyRelative("chargeSettings");
            AddProperty(chargeSettings.FindPropertyRelative("chargeWindup"));
            AddProperty(chargeSettings.FindPropertyRelative("minimumChargeTime"));
            AddProperty(chargeSettings.FindPropertyRelative("maximumChargeTime"));
            AddProperty(chargeSettings.FindPropertyRelative("movementLocks"));

            // Hit Detection
            AddHeader();
            AddProperty(property.FindPropertyRelative("hitDetectorPrefab"));
            AddProperty(property.FindPropertyRelative("parentHitDetectorToPivot"));
            AddProperty(property.FindPropertyRelative("hitDetectorPositionOffset"));
            AddProperty(property.FindPropertyRelative("hitDetectorRotationOffset"));
            AddProperty(property.FindPropertyRelative("fallbackDirection"));

            // Branches
            AddHeader();
            AddProperty(property.FindPropertyRelative("transitions"));

            // Hit Stop
            AddHeader();
            SerializedProperty hitStopOnHit = property.FindPropertyRelative("hitStopOnHit");
            AddProperty(property.FindPropertyRelative("hitStopOnExecute"));
            AddProperty(hitStopOnHit);
            if (hitStopOnHit.floatValue > 0f)
            {
                AddPropertyDisabled(property.FindPropertyRelative("multiplyHitStopPerHit"));
            }
            else
            {
                // Still reserve space for the disabled field so layout remains predictable
                AddPropertyDisabled(property.FindPropertyRelative("multiplyHitStopPerHit"));
            }

            // Animation
            AddHeader();
            SerializedProperty animation = property.FindPropertyRelative("animation");
            SerializedProperty windupAnimation = property.FindPropertyRelative("windupAnimation");
            SerializedProperty activeAnimation = property.FindPropertyRelative("activeAnimation");
            SerializedProperty recoveryAnimation = property.FindPropertyRelative("recoveryAnimation");
            SerializedProperty scaleWindupToDuration = property.FindPropertyRelative("scaleWindupAnimationToStepDuration");
            SerializedProperty scaleActiveToDuration = property.FindPropertyRelative("scaleActiveAnimationToStepDuration");
            SerializedProperty scaleRecoveryToDuration = property.FindPropertyRelative("scaleRecoveryAnimationToStepDuration");
            SerializedProperty crossFade = property.FindPropertyRelative("animationCrossFade");
            SerializedProperty speedMultiplier = property.FindPropertyRelative("animationSpeedMultiplier");
            SerializedProperty scaleToDuration = property.FindPropertyRelative("scaleAnimationSpeedToStepDuration");
            SerializedProperty overrideSpeed = property.FindPropertyRelative("overrideAnimationSpeed");

            AddProperty(property.FindPropertyRelative("usePhaseAnimations"));
            if (property.FindPropertyRelative("usePhaseAnimations").boolValue)
            {
                AddProperty(windupAnimation);
                AddProperty(scaleWindupToDuration);
                AddProperty(activeAnimation);
                AddProperty(scaleActiveToDuration);
                AddProperty(recoveryAnimation);
                AddProperty(scaleRecoveryToDuration);
            }
            else
            {
                AddProperty(animation);
                AddProperty(scaleToDuration);
            }
            AddProperty(crossFade);
            AddProperty(overrideSpeed);
            AddProperty(speedMultiplier);

            // VFX
            AddHeader();
            AddProperty(property.FindPropertyRelative("vfx"));

            // remove trailing spacing added after last property
            height -= spacing;
            return height;
        }

        static bool HasAnyAnimationClip(SerializedProperty animationProperty)
        {
            if (animationProperty == null)
            {
                return false;
            }

            return animationProperty.FindPropertyRelative("singleClip").objectReferenceValue != null ||
                   animationProperty.FindPropertyRelative("northClip").objectReferenceValue != null ||
                   animationProperty.FindPropertyRelative("southClip").objectReferenceValue != null ||
                   animationProperty.FindPropertyRelative("eastClip").objectReferenceValue != null ||
                   animationProperty.FindPropertyRelative("westClip").objectReferenceValue != null ||
                   animationProperty.FindPropertyRelative("northEastClip").objectReferenceValue != null ||
                   animationProperty.FindPropertyRelative("southEastClip").objectReferenceValue != null ||
                   animationProperty.FindPropertyRelative("northWestClip").objectReferenceValue != null ||
                   animationProperty.FindPropertyRelative("southWestClip").objectReferenceValue != null;
        }
    }
}
