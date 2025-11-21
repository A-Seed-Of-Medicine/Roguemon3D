using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Actions;

namespace _PinBoy.Scripts.Gameplay.Actions.Editor
{
    public class ComboGraphEditorWindow : EditorWindow
    {
        const float ToolbarHeight = 24f;
        const float SectionSpacing = 6f;

        CharacterComboAction targetAction;
        CharacterComboDefinition targetDefinition;
        SerializedObject serializedDefinition;
        SerializedProperty requiresAimProperty;
        SerializedProperty queuedInputLifetimeProperty;
        SerializedProperty entryStepsProperty;
        SerializedProperty stepsProperty;

        int selectedStepIndex;
        Vector2 entryScroll;
        Vector2 stepScroll;
        bool entryFoldout = true;

        [MenuItem("Window/Gameplay/Combo Graph Editor")] 
        static void ShowWindow()
        {
            GetWindow<ComboGraphEditorWindow>("Combo Graph Editor").Show();
        }

        public static void Open(CharacterComboAction action)
        {
            ComboGraphEditorWindow window = GetWindow<ComboGraphEditorWindow>("Combo Graph Editor");
            window.SetTargetAction(action);
            window.Focus();
        }

        public static void Open(CharacterComboDefinition definition)
        {
            ComboGraphEditorWindow window = GetWindow<ComboGraphEditorWindow>("Combo Graph Editor");
            window.SetTargetDefinition(definition);
            window.Focus();
        }

        void OnEnable()
        {
            if (targetDefinition != null)
            {
                CreateSerializedObject();
            }
        }

        void OnDisable()
        {
            serializedDefinition = null;
            requiresAimProperty = null;
            queuedInputLifetimeProperty = null;
            entryStepsProperty = null;
            stepsProperty = null;
        }

        void OnGUI()
        {
            DrawTargetSelector();

            if (targetDefinition == null)
            {
                EditorGUILayout.HelpBox("Assign a CharacterComboDefinition asset or a CharacterComboAction with a definition to begin editing its combo graph.", MessageType.Info);
                return;
            }

            if (serializedDefinition == null)
            {
                CreateSerializedObject();
            }

            serializedDefinition.UpdateIfRequiredOrScript();

            GUILayout.Space(SectionSpacing);
            EditorGUILayout.PropertyField(requiresAimProperty);
            EditorGUILayout.PropertyField(queuedInputLifetimeProperty);

            GUILayout.Space(SectionSpacing);
            DrawEntrySteps();

            GUILayout.Space(SectionSpacing);
            DrawStepTabs();

            serializedDefinition.ApplyModifiedProperties();
        }

        void DrawTargetSelector()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Combo Sources", EditorStyles.boldLabel);

                CharacterComboAction selectedAction = (CharacterComboAction)EditorGUILayout.ObjectField("Action", targetAction, typeof(CharacterComboAction), true);
                if (selectedAction != targetAction)
                {
                    SetTargetAction(selectedAction);
                }

                CharacterComboDefinition selectedDefinition = (CharacterComboDefinition)EditorGUILayout.ObjectField("Combo Definition", targetDefinition, typeof(CharacterComboDefinition), false);
                if (selectedDefinition != targetDefinition)
                {
                    SetTargetDefinition(selectedDefinition);
                }

                if (targetAction != null && targetAction.ComboDefinition != targetDefinition)
                {
                    EditorGUILayout.HelpBox("The selected action references a different combo definition. Editing will apply to the definition shown above.", MessageType.Warning);
                }
            }
        }

        void SetTargetAction(CharacterComboAction action)
        {
            targetAction = action;
            if (targetAction != null)
            {
                SetTargetDefinition(targetAction.ComboDefinition);
            }
        }

        void SetTargetDefinition(CharacterComboDefinition definition)
        {
            targetDefinition = definition;
            if (targetDefinition == null)
            {
                ClearSerializedState();
            }
            else
            {
                CreateSerializedObject();
            }
        }

        void ClearSerializedState()
        {
            serializedDefinition = null;
            requiresAimProperty = null;
            queuedInputLifetimeProperty = null;
            entryStepsProperty = null;
            stepsProperty = null;
            selectedStepIndex = 0;
        }

        void CreateSerializedObject()
        {
            if (targetDefinition == null)
            {
                ClearSerializedState();
                return;
            }
            serializedDefinition = new SerializedObject(targetDefinition);
            requiresAimProperty = serializedDefinition.FindProperty("requiresAimInput");
            queuedInputLifetimeProperty = serializedDefinition.FindProperty("queuedInputLifetime");
            entryStepsProperty = serializedDefinition.FindProperty("entrySteps");
            stepsProperty = serializedDefinition.FindProperty("steps");
            selectedStepIndex = Mathf.Clamp(selectedStepIndex, 0, Mathf.Max(0, stepsProperty.arraySize - 1));
        }

        void DrawEntrySteps()
        {
            entryFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(entryFoldout, $"Entry Steps ({entryStepsProperty.arraySize})");
            if (entryFoldout)
            {
                using (var scroll = new EditorGUILayout.ScrollViewScope(entryScroll, GUILayout.MaxHeight(200f)))
                {
                    entryScroll = scroll.scrollPosition;
                    for (int i = 0; i < entryStepsProperty.arraySize; i++)
                    {
                        SerializedProperty entry = entryStepsProperty.GetArrayElementAtIndex(i);
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            EditorGUILayout.PropertyField(entry.FindPropertyRelative("input"));
                            EditorGUILayout.PropertyField(entry.FindPropertyRelative("stepId"));
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                GUILayout.FlexibleSpace();
                                if (GUILayout.Button("Remove", GUILayout.Width(80f)))
                                {
                                    entryStepsProperty.DeleteArrayElementAtIndex(i);
                                    break;
                                }
                            }
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Add Entry", GUILayout.Width(120f)))
                    {
                        entryStepsProperty.InsertArrayElementAtIndex(entryStepsProperty.arraySize);
                        SerializedProperty newEntry = entryStepsProperty.GetArrayElementAtIndex(entryStepsProperty.arraySize - 1);
                        newEntry.FindPropertyRelative("input").enumValueIndex = 0;
                        newEntry.FindPropertyRelative("stepId").stringValue = stepsProperty.arraySize > 0 ?
                            stepsProperty.GetArrayElementAtIndex(0).FindPropertyRelative("id").stringValue : string.Empty;
                    }
                    GUILayout.FlexibleSpace();
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DrawStepTabs()
        {
            EditorGUILayout.LabelField("Combo Steps", EditorStyles.boldLabel);

            if (stepsProperty.arraySize == 0)
            {
                EditorGUILayout.HelpBox("No combo steps defined. Create a step to start building the combo.", MessageType.Info);
                if (GUILayout.Button("Create Step", GUILayout.Height(24f)))
                {
                    AddStep();
                }
                return;
            }

            string[] tabLabels = BuildStepTabLabels();

            int currentIndex = Mathf.Clamp(selectedStepIndex, 0, stepsProperty.arraySize - 1);
            int pressed = GUILayout.Toolbar(currentIndex, tabLabels, GUILayout.Height(ToolbarHeight));
            if (pressed == tabLabels.Length - 1)
            {
                AddStep();
            }
            else
            {
                selectedStepIndex = pressed;
            }

            selectedStepIndex = Mathf.Clamp(selectedStepIndex, 0, stepsProperty.arraySize - 1);
            SerializedProperty step = stepsProperty.GetArrayElementAtIndex(selectedStepIndex);

            using (var scroll = new EditorGUILayout.ScrollViewScope(stepScroll))
            {
                stepScroll = scroll.scrollPosition;
                EditorGUILayout.PropertyField(step, new GUIContent($"Step {selectedStepIndex + 1}"), true);
            }

            GUILayout.Space(SectionSpacing);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Duplicate Step"))
                {
                    DuplicateStep(selectedStepIndex);
                }

                EditorGUI.BeginDisabledGroup(stepsProperty.arraySize <= 1);
                if (GUILayout.Button("Delete Step"))
                {
                    RemoveStep(selectedStepIndex);
                }
                EditorGUI.EndDisabledGroup();
            }
        }

        string[] BuildStepTabLabels()
        {
            string[] labels = new string[stepsProperty.arraySize + 1];
            for (int i = 0; i < stepsProperty.arraySize; i++)
            {
                SerializedProperty step = stepsProperty.GetArrayElementAtIndex(i);
                string id = step.FindPropertyRelative("id").stringValue;
                labels[i] = string.IsNullOrWhiteSpace(id) ? $"Step {i + 1}" : id;
            }

            labels[labels.Length - 1] = "+";
            return labels;
        }

        void AddStep()
        {
            stepsProperty.InsertArrayElementAtIndex(stepsProperty.arraySize);
            SerializedProperty step = stepsProperty.GetArrayElementAtIndex(stepsProperty.arraySize - 1);
            ResetStep(step);
            selectedStepIndex = stepsProperty.arraySize - 1;
        }

        void DuplicateStep(int index)
        {
            if (index < 0 || index >= stepsProperty.arraySize)
            {
                return;
            }

            stepsProperty.InsertArrayElementAtIndex(index + 1);
            SerializedProperty newStep = stepsProperty.GetArrayElementAtIndex(index + 1);
            SerializedProperty id = newStep.FindPropertyRelative("id");
            id.stringValue = GenerateUniqueStepId(id.stringValue);
            selectedStepIndex = index + 1;
        }

        void RemoveStep(int index)
        {
            if (stepsProperty.arraySize == 0)
            {
                return;
            }

            stepsProperty.DeleteArrayElementAtIndex(index);
            selectedStepIndex = Mathf.Clamp(selectedStepIndex, 0, stepsProperty.arraySize - 1);
        }

        string GenerateUniqueStepId(string baseId = "step")
        {
            string sanitized = string.IsNullOrWhiteSpace(baseId) ? "step" : baseId.Trim();
            sanitized = sanitized.Replace(' ', '_');
            HashSet<string> existing = new();
            for (int i = 0; i < stepsProperty.arraySize; i++)
            {
                SerializedProperty step = stepsProperty.GetArrayElementAtIndex(i);
                string id = step.FindPropertyRelative("id").stringValue;
                if (!string.IsNullOrEmpty(id))
                {
                    existing.Add(id);
                }
            }

            string candidate = sanitized;
            int suffix = 1;
            while (existing.Contains(candidate))
            {
                candidate = $"{sanitized}_{suffix++}";
            }

            return candidate;
        }

        void ResetStep(SerializedProperty step)
        {
            step.FindPropertyRelative("id").stringValue = GenerateUniqueStepId();
            step.FindPropertyRelative("action").objectReferenceValue = null;
            step.FindPropertyRelative("magnitudeMultiplier").floatValue = 1f;
            step.FindPropertyRelative("triggerWhenNoTarget").boolValue = false;
            step.FindPropertyRelative("allowRepeatedHits").boolValue = false;
            step.FindPropertyRelative("stunImmune").boolValue = false;
            step.FindPropertyRelative("windup").floatValue = 0.05f;
            step.FindPropertyRelative("active").floatValue = 0.15f;
            step.FindPropertyRelative("recovery").floatValue = 0.25f;
            step.FindPropertyRelative("comboResetDelay").floatValue = 1.2f;
            step.FindPropertyRelative("transitionWindowOpen").floatValue = 0.35f;
            step.FindPropertyRelative("transitionWindowClose").floatValue = 0.9f;
            step.FindPropertyRelative("lockMovement").boolValue = true;
            step.FindPropertyRelative("lockMovementDuringRecovery").boolValue = true;
            step.FindPropertyRelative("lockAim").boolValue = true;
            step.FindPropertyRelative("zeroVelocityOnStart").boolValue = true;
            step.FindPropertyRelative("missNudgeImpulse").floatValue = 0f;
            step.FindPropertyRelative("missNudgeDelay").floatValue = 0f;
            step.FindPropertyRelative("applyNudgeWhenHit").boolValue = false;
            step.FindPropertyRelative("hitDetectorPrefab").objectReferenceValue = null;
            step.FindPropertyRelative("parentHitDetectorToPivot").boolValue = true;
            step.FindPropertyRelative("hitDetectorPositionOffset").vector3Value = Vector3.zero;
            step.FindPropertyRelative("hitDetectorRotationOffset").vector3Value = Vector3.zero;
            step.FindPropertyRelative("fallbackDirection").vector3Value = Vector3.forward;
            SerializedProperty transitions = step.FindPropertyRelative("transitions");
            ClearArray(transitions);
            step.FindPropertyRelative("vfx").objectReferenceValue = null;
            step.FindPropertyRelative("usePhaseAnimations").boolValue = false;
            ResetAnimation(step.FindPropertyRelative("animation"));
            ResetAnimation(step.FindPropertyRelative("windupAnimation"));
            ResetAnimation(step.FindPropertyRelative("activeAnimation"));
            ResetAnimation(step.FindPropertyRelative("recoveryAnimation"));
            step.FindPropertyRelative("hitStopOnExecute").floatValue = 0f;
            step.FindPropertyRelative("hitStopOnHit").floatValue = 0f;
            step.FindPropertyRelative("multiplyHitStopPerHit").boolValue = true;
            step.FindPropertyRelative("animationCrossFade").floatValue = 0.1f;
            step.FindPropertyRelative("animationSpeedMultiplier").floatValue = 1f;
            step.FindPropertyRelative("windupAnimationSpeedMultiplier").floatValue = 1f;
            step.FindPropertyRelative("activeAnimationSpeedMultiplier").floatValue = 1f;
            step.FindPropertyRelative("recoveryAnimationSpeedMultiplier").floatValue = 1f;
            step.FindPropertyRelative("scaleAnimationSpeedToStepDuration").boolValue = false;
            step.FindPropertyRelative("overrideAnimationSpeed").boolValue = false;
        }

        static void ClearArray(SerializedProperty property)
        {
            if (property == null || !property.isArray)
            {
                return;
            }

            while (property.arraySize > 0)
            {
                property.DeleteArrayElementAtIndex(property.arraySize - 1);
            }
        }

        static void ResetAnimation(SerializedProperty animation)
        {
            if (animation == null)
            {
                return;
            }

            animation.FindPropertyRelative("directionMode").enumValueIndex = (int)AgentAnimationRequest.DirectionMode.Single;
            animation.FindPropertyRelative("mirrorLeftRight").boolValue = false;
            animation.FindPropertyRelative("singleClip").objectReferenceValue = null;
            animation.FindPropertyRelative("northClip").objectReferenceValue = null;
            animation.FindPropertyRelative("southClip").objectReferenceValue = null;
            animation.FindPropertyRelative("eastClip").objectReferenceValue = null;
            animation.FindPropertyRelative("westClip").objectReferenceValue = null;
            animation.FindPropertyRelative("northEastClip").objectReferenceValue = null;
            animation.FindPropertyRelative("southEastClip").objectReferenceValue = null;
            animation.FindPropertyRelative("northWestClip").objectReferenceValue = null;
            animation.FindPropertyRelative("southWestClip").objectReferenceValue = null;
            animation.FindPropertyRelative("crossFade").floatValue = 0f;
            animation.FindPropertyRelative("playbackSpeed").floatValue = 1f;
            animation.FindPropertyRelative("overrideSpeed").boolValue = false;
        }
    }
}
