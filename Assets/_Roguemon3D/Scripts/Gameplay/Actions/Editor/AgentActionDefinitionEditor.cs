using System;
using _PinBoy.Scripts.Gameplay.Effects;
using _PinBoy.Scripts.Gameplay.Effects.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Actions.Editor
{
    [CustomEditor(typeof(AgentActionDefinition))]
    sealed class AgentActionDefinitionEditor : UnityEditor.Editor
    {
        SerializedProperty actionNameProp;
        SerializedProperty durationProp;
        SerializedProperty effectDelayProp;
        SerializedProperty lockMovementProp;
        SerializedProperty movementLockDurationProp;
        SerializedProperty zeroVelocityOnLockProp;
        SerializedProperty faceTargetOnStartProp;
        SerializedProperty faceAimDirectionProp;
        SerializedProperty effectsProp;
        SerializedProperty baseMagnitudeProp;
        SerializedProperty vfxPrefabProp;
        SerializedProperty vfxAnchorProp;
        SerializedProperty vfxTimingProp;
        SerializedProperty parentVfxProp;
        SerializedProperty vfxOffsetProp;
        SerializedProperty vfxLifetimeProp;

        ReorderableList effectsList;

        void OnEnable()
        {
            actionNameProp = serializedObject.FindProperty("actionName");
            durationProp = serializedObject.FindProperty("duration");
            effectDelayProp = serializedObject.FindProperty("effectDelay");
            lockMovementProp = serializedObject.FindProperty("lockMovement");
            movementLockDurationProp = serializedObject.FindProperty("movementLockDuration");
            zeroVelocityOnLockProp = serializedObject.FindProperty("zeroVelocityOnLock");
            faceTargetOnStartProp = serializedObject.FindProperty("faceTargetOnStart");
            faceAimDirectionProp = serializedObject.FindProperty("faceAimDirectionWhenNoTarget");
            effectsProp = serializedObject.FindProperty("effects");
            baseMagnitudeProp = serializedObject.FindProperty("baseMagnitude");
            vfxPrefabProp = serializedObject.FindProperty("vfxPrefab");
            vfxAnchorProp = serializedObject.FindProperty("vfxAnchor");
            vfxTimingProp = serializedObject.FindProperty("vfxTiming");
            parentVfxProp = serializedObject.FindProperty("parentVfxToAnchor");
            vfxOffsetProp = serializedObject.FindProperty("vfxOffset");
            vfxLifetimeProp = serializedObject.FindProperty("vfxLifetime");

            CreateEffectsList();
        }

        void CreateEffectsList()
        {
            effectsList = new ReorderableList(serializedObject, effectsProp, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Effects"),
                onAddCallback = OnAddEffect,
                onRemoveCallback = list => ReorderableList.defaultBehaviours.DoRemoveButton(list)
            };

            effectsList.drawElementCallback = (rect, index, active, focused) =>
            {
                if (effectsProp == null || index < 0 || index >= effectsProp.arraySize)
                {
                    return;
                }

                SerializedProperty element = effectsProp.GetArrayElementAtIndex(index);
                float elementHeight = EditorGUI.GetPropertyHeight(element, true);
                rect.height = elementHeight;
                EditorGUI.PropertyField(rect, element, new GUIContent($"Effect {index + 1}"), true);
            };

            effectsList.elementHeightCallback = index =>
            {
                if (effectsProp == null || index < 0 || index >= effectsProp.arraySize)
                {
                    return EditorGUIUtility.singleLineHeight;
                }

                SerializedProperty element = effectsProp.GetArrayElementAtIndex(index);
                return EditorGUI.GetPropertyHeight(element, true) + EditorGUIUtility.standardVerticalSpacing;
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawCoreSettings();
            EditorGUILayout.Space();
            DrawEffectsSection();
            EditorGUILayout.Space();
            DrawVfxSettings();

            serializedObject.ApplyModifiedProperties();
        }

        void DrawCoreSettings()
        {
            EditorGUILayout.LabelField("Core Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(actionNameProp);
            EditorGUILayout.PropertyField(durationProp);
            EditorGUILayout.PropertyField(effectDelayProp);
            EditorGUILayout.PropertyField(lockMovementProp);
            using (new EditorGUI.IndentLevelScope())
            {
                using (new EditorGUI.DisabledScope(!lockMovementProp.boolValue))
                {
                    EditorGUILayout.PropertyField(movementLockDurationProp);
                    EditorGUILayout.PropertyField(zeroVelocityOnLockProp);
                }
            }

            EditorGUILayout.PropertyField(faceTargetOnStartProp);
            using (new EditorGUI.IndentLevelScope())
            {
                using (new EditorGUI.DisabledScope(faceTargetOnStartProp.boolValue))
                {
                    EditorGUILayout.PropertyField(faceAimDirectionProp);
                }
            }
        }

        void DrawEffectsSection()
        {
            EditorGUILayout.LabelField("Effects", EditorStyles.boldLabel);

            if (!EffectEditorUtility.HasEffectTypes)
            {
                EditorGUILayout.HelpBox("No Effect implementations were found in the project.", MessageType.Warning);
            }

            effectsList.DoLayoutList();
            EditorGUILayout.PropertyField(baseMagnitudeProp);
        }

        void DrawVfxSettings()
        {
            EditorGUILayout.LabelField("VFX", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(vfxPrefabProp);
            EditorGUILayout.PropertyField(vfxAnchorProp);
            EditorGUILayout.PropertyField(vfxTimingProp);
            EditorGUILayout.PropertyField(parentVfxProp);
            EditorGUILayout.PropertyField(vfxOffsetProp);
            EditorGUILayout.PropertyField(vfxLifetimeProp);
        }

        void OnAddEffect(ReorderableList list)
        {
            GenericMenu menu = new GenericMenu();
            if (!EffectEditorUtility.HasEffectTypes)
            {
                menu.AddDisabledItem(new GUIContent("No Effect types available"));
            }
            else
            {
                for (int i = 0; i < EffectEditorUtility.EffectTypes.Count; i++)
                {
                    Type type = EffectEditorUtility.EffectTypes[i];
                    string label = EffectEditorUtility.GetFriendlyName(type);
                    menu.AddItem(new GUIContent(label), false, () => AddEffectInstance(type));
                }
            }

            menu.ShowAsContext();
        }

        void AddEffectInstance(Type type)
        {
            EffectEditorUtility.AddEffectInstance(effectsProp, type);
            serializedObject.Update();
            if (effectsList != null && effectsProp != null)
            {
                effectsList.index = Mathf.Max(0, effectsProp.arraySize - 1);
            }
        }
    }
}
