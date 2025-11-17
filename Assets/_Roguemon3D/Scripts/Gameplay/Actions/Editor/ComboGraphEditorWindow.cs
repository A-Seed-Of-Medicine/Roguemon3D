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
        SerializedObject serializedAction;
        SerializedProperty requiresAimProperty;
        SerializedProperty entryStepsProperty;
        SerializedProperty stepsProperty;

        int selectedStepIndex;
        Vector2 entryScroll;
        Vector2 stepScroll;
        bool entryFoldout = true;

        readonly Dictionary<int, StepFoldoutState> foldoutStates = new();

        [MenuItem("Window/Gameplay/Combo Graph Editor")] 
        static void ShowWindow()
        {
            GetWindow<ComboGraphEditorWindow>("Combo Graph Editor").Show();
        }

        public static void Open(CharacterComboAction action)
        {
            ComboGraphEditorWindow window = GetWindow<ComboGraphEditorWindow>("Combo Graph Editor");
            window.SetTarget(action);
            window.Focus();
        }

        void OnEnable()
        {
            if (targetAction != null)
            {
                CreateSerializedObject();
            }
        }

        void OnDisable()
        {
            serializedAction = null;
            requiresAimProperty = null;
            entryStepsProperty = null;
            stepsProperty = null;
        }

        void OnGUI()
        {
            DrawTargetSelector();

            if (targetAction == null)
            {
                EditorGUILayout.HelpBox("Assign a CharacterComboAction component to begin editing its combo graph.", MessageType.Info);
                return;
            }

            if (serializedAction == null)
            {
                CreateSerializedObject();
            }

            serializedAction.UpdateIfRequiredOrScript();

            GUILayout.Space(SectionSpacing);
            EditorGUILayout.PropertyField(requiresAimProperty);

            GUILayout.Space(SectionSpacing);
            DrawEntrySteps();

            GUILayout.Space(SectionSpacing);
            DrawStepTabs();

            serializedAction.ApplyModifiedProperties();
        }

        void DrawTargetSelector()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Combo Controller", EditorStyles.boldLabel);
                CharacterComboAction selected = (CharacterComboAction)EditorGUILayout.ObjectField("Target", targetAction, typeof(CharacterComboAction), true);
                if (selected != targetAction)
                {
                    SetTarget(selected);
                }
            }
        }

        void SetTarget(CharacterComboAction action)
        {
            targetAction = action;
            if (targetAction == null)
            {
                serializedAction = null;
                requiresAimProperty = null;
                entryStepsProperty = null;
                stepsProperty = null;
                selectedStepIndex = 0;
                foldoutStates.Clear();
            }
            else
            {
                CreateSerializedObject();
            }
        }

        void CreateSerializedObject()
        {
            if (targetAction == null)
            {
                return;
            }

            serializedAction = new SerializedObject(targetAction);
            requiresAimProperty = serializedAction.FindProperty("requiresAimInput");
            entryStepsProperty = serializedAction.FindProperty("entrySteps");
            stepsProperty = serializedAction.FindProperty("steps");
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
                DrawStepInspector(step, selectedStepIndex);
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

        void DrawStepInspector(SerializedProperty step, int index)
        {
            StepFoldoutState state = GetFoldoutState(index);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(step.FindPropertyRelative("id"));
                EditorGUILayout.PropertyField(step.FindPropertyRelative("action"));
                EditorGUILayout.PropertyField(step.FindPropertyRelative("magnitudeMultiplier"));
                EditorGUILayout.PropertyField(step.FindPropertyRelative("triggerWhenNoTarget"));
                EditorGUILayout.PropertyField(step.FindPropertyRelative("allowRepeatedHits"));
                EditorGUILayout.PropertyField(step.FindPropertyRelative("stunImmune"));
            }

            GUILayout.Space(SectionSpacing);

            state.Timing = EditorGUILayout.BeginFoldoutHeaderGroup(state.Timing, "Timing");
            if (state.Timing)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(step.FindPropertyRelative("windup"));
                    EditorGUILayout.PropertyField(step.FindPropertyRelative("active"));
                    EditorGUILayout.PropertyField(step.FindPropertyRelative("recovery"));
                    EditorGUILayout.PropertyField(step.FindPropertyRelative("comboResetDelay"));
                    EditorGUILayout.PropertyField(step.FindPropertyRelative("transitionWindowOpen"));
                    EditorGUILayout.PropertyField(step.FindPropertyRelative("transitionWindowClose"));
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            GUILayout.Space(SectionSpacing);

            state.Movement = EditorGUILayout.BeginFoldoutHeaderGroup(state.Movement, "Movement");
            if (state.Movement)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(step.FindPropertyRelative("lockMovement"));
                    EditorGUILayout.PropertyField(step.FindPropertyRelative("zeroVelocityOnStart"));
                    EditorGUILayout.PropertyField(step.FindPropertyRelative("missNudgeImpulse"));
                    EditorGUILayout.PropertyField(step.FindPropertyRelative("missNudgeDelay"));
                    EditorGUILayout.PropertyField(step.FindPropertyRelative("applyNudgeWhenHit"));
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            GUILayout.Space(SectionSpacing);

            state.HitDetection = EditorGUILayout.BeginFoldoutHeaderGroup(state.HitDetection, "Hit Detection");
            if (state.HitDetection)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    // Draw arrays without their own header groups
                    DrawArrayNoHeader(step.FindPropertyRelative("hitColliders"), "Hit Colliders");

                    EditorGUILayout.PropertyField(step.FindPropertyRelative("targetLayers"));
                    EditorGUILayout.PropertyField(step.FindPropertyRelative("includeTriggerColliders"));

                    DrawArrayNoHeader(step.FindPropertyRelative("allegianceMask"), "Allegiance Mask");

                    EditorGUILayout.PropertyField(step.FindPropertyRelative("fallbackDirection"));
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();


            GUILayout.Space(SectionSpacing);

            state.Transitions = EditorGUILayout.BeginFoldoutHeaderGroup(state.Transitions, $"Transitions ({step.FindPropertyRelative("transitions").arraySize})");
            if (state.Transitions)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    DrawTransitions(step.FindPropertyRelative("transitions"));
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            GUILayout.Space(SectionSpacing);

            state.Vfx = EditorGUILayout.BeginFoldoutHeaderGroup(state.Vfx, "VFX");
            if (state.Vfx)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(step.FindPropertyRelative("vfx"));
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            GUILayout.Space(SectionSpacing);

            state.HitStop = EditorGUILayout.BeginFoldoutHeaderGroup(state.HitStop, "Hit Stop");
            if (state.HitStop)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    SerializedProperty executeStop = step.FindPropertyRelative("hitStopOnExecute");
                    SerializedProperty hitStopOnHit = step.FindPropertyRelative("hitStopOnHit");
                    SerializedProperty multiply = step.FindPropertyRelative("multiplyHitStopPerHit");

                    EditorGUILayout.PropertyField(executeStop, new GUIContent("On Execute"));
                    EditorGUILayout.PropertyField(hitStopOnHit, new GUIContent("On Hit"));

                    using (new EditorGUI.DisabledScope(hitStopOnHit.floatValue <= 0f))
                    {
                        EditorGUILayout.PropertyField(multiply, new GUIContent("Multiply Per Hit"));
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            GUILayout.Space(SectionSpacing);

            state.Animation = EditorGUILayout.BeginFoldoutHeaderGroup(state.Animation, "Animation");
            if (state.Animation)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    SerializedProperty animation = step.FindPropertyRelative("animation");
                    SerializedProperty crossFade = step.FindPropertyRelative("animationCrossFade");
                    SerializedProperty speedMultiplier = step.FindPropertyRelative("animationSpeedMultiplier");
                    SerializedProperty scaleToDuration = step.FindPropertyRelative("scaleAnimationSpeedToStepDuration");
                    SerializedProperty overrideSpeed = step.FindPropertyRelative("overrideAnimationSpeed");

                    EditorGUILayout.PropertyField(animation);

                    using (new EditorGUI.DisabledScope(!HasAnyAnimationClip(animation)))
                    {
                        EditorGUILayout.PropertyField(crossFade, new GUIContent("Cross Fade"));
                    }

                    EditorGUILayout.PropertyField(scaleToDuration, new GUIContent("Scale Speed To Step Duration"));
                    EditorGUILayout.PropertyField(overrideSpeed, new GUIContent("Force Override Speed"));

                    bool enableMultiplier = scaleToDuration.boolValue || overrideSpeed.boolValue;
                    using (new EditorGUI.DisabledScope(!enableMultiplier))
                    {
                        EditorGUILayout.PropertyField(speedMultiplier, new GUIContent("Speed Multiplier"));
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            foldoutStates[index] = state;
        }

        void DrawTransitions(SerializedProperty transitions)
        {
            for (int i = 0; i < transitions.arraySize; i++)
            {
                SerializedProperty transition = transitions.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(transition.FindPropertyRelative("input"));
                    EditorGUILayout.PropertyField(transition.FindPropertyRelative("nextStepId"));
                    EditorGUILayout.PropertyField(transition.FindPropertyRelative("queueUntilWindow"));
                    EditorGUILayout.PropertyField(transition.FindPropertyRelative("transitionDelay"));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Remove", GUILayout.Width(80f)))
                        {
                            transitions.DeleteArrayElementAtIndex(i);
                            break;
                        }
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Add Transition", GUILayout.Width(140f)))
                {
                    transitions.InsertArrayElementAtIndex(transitions.arraySize);
                    SerializedProperty newTransition = transitions.GetArrayElementAtIndex(transitions.arraySize - 1);
                    newTransition.FindPropertyRelative("input").enumValueIndex = 0;
                    newTransition.FindPropertyRelative("nextStepId").stringValue = string.Empty;
                    newTransition.FindPropertyRelative("queueUntilWindow").boolValue = true;
                    newTransition.FindPropertyRelative("transitionDelay").floatValue = 0f;
                }
                GUILayout.FlexibleSpace();
            }
        }
        
        static void DrawArrayNoHeader(SerializedProperty arrayProp, string label)
        {
            if (arrayProp == null || !arrayProp.isArray) return;

            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
           // int newSize = Mathf.Max(0, EditorGUILayout.IntField("Size", arrayProp.arraySize));
            //if (newSize != arrayProp.arraySize) arrayProp.arraySize = newSize;
            // New Horizontal layout for buttons
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Element", GUILayout.Width(120f)))
                {
                    arrayProp.InsertArrayElementAtIndex(arrayProp.arraySize);
                }

                if (GUILayout.Button("Remove Last Element", GUILayout.Width(150f)))
                {
                    if (arrayProp.arraySize > 0)
                    {
                        arrayProp.DeleteArrayElementAtIndex(arrayProp.arraySize - 1);
                    }
                }
            }

            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                var elem = arrayProp.GetArrayElementAtIndex(i);
                // Draw each element without creating another header group
                EditorGUILayout.PropertyField(elem, new GUIContent($"Element {i}"), true);
            }
            EditorGUI.indentLevel--;
        }


        void AddStep()
        {
            stepsProperty.InsertArrayElementAtIndex(stepsProperty.arraySize);
            SerializedProperty step = stepsProperty.GetArrayElementAtIndex(stepsProperty.arraySize - 1);
            ResetStep(step);
            selectedStepIndex = stepsProperty.arraySize - 1;
            foldoutStates.Clear();
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
            foldoutStates.Clear();
        }

        void RemoveStep(int index)
        {
            if (stepsProperty.arraySize == 0)
            {
                return;
            }

            stepsProperty.DeleteArrayElementAtIndex(index);
            selectedStepIndex = Mathf.Clamp(selectedStepIndex, 0, stepsProperty.arraySize - 1);
            foldoutStates.Clear();
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
            step.FindPropertyRelative("zeroVelocityOnStart").boolValue = true;
            step.FindPropertyRelative("missNudgeImpulse").floatValue = 0f;
            step.FindPropertyRelative("missNudgeDelay").floatValue = 0f;
            step.FindPropertyRelative("applyNudgeWhenHit").boolValue = false;
            SerializedProperty colliders = step.FindPropertyRelative("hitColliders");
            ClearArray(colliders);
            step.FindPropertyRelative("targetLayers").intValue = Physics.DefaultRaycastLayers;
            step.FindPropertyRelative("includeTriggerColliders").boolValue = true;
            SerializedProperty allegiance = step.FindPropertyRelative("allegianceMask");
            ClearArray(allegiance);
            step.FindPropertyRelative("fallbackDirection").vector3Value = Vector3.forward;
            SerializedProperty transitions = step.FindPropertyRelative("transitions");
            ClearArray(transitions);
            step.FindPropertyRelative("vfx").objectReferenceValue = null;
            ResetAnimation(step.FindPropertyRelative("animation"));
            step.FindPropertyRelative("hitStopOnExecute").floatValue = 0f;
            step.FindPropertyRelative("hitStopOnHit").floatValue = 0f;
            step.FindPropertyRelative("multiplyHitStopPerHit").boolValue = true;
            step.FindPropertyRelative("animationCrossFade").floatValue = 0.1f;
            step.FindPropertyRelative("animationSpeedMultiplier").floatValue = 1f;
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

        StepFoldoutState GetFoldoutState(int index)
        {
            if (!foldoutStates.TryGetValue(index, out StepFoldoutState state))
            {
                state = new StepFoldoutState
                {
                    Timing = true,
                    Movement = true,
                    HitDetection = true,
                    Transitions = true,
                    Vfx = true,
                    HitStop = true,
                    Animation = true
                };
                foldoutStates[index] = state;
            }

            return state;
        }

        struct StepFoldoutState
        {
            public bool Timing;
            public bool Movement;
            public bool HitDetection;
            public bool Transitions;
            public bool Vfx;
            public bool HitStop;
            public bool Animation;
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
