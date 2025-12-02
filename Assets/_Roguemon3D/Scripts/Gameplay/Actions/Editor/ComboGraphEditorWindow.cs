using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Actions;

namespace _PinBoy.Scripts.Gameplay.Actions.Editor
{
    /// <summary>
    /// GraphToolkit based editor window for visualizing and editing CharacterComboDefinition graphs.
    /// </summary>
    public class ComboGraphEditorWindow : EditorWindow
    {
        const string WindowTitle = "Combo Graph Editor";

        CharacterComboAction targetAction;
        CharacterComboDefinition targetDefinition;
        SerializedObject serializedDefinition;

        CharacterComboGraphView graphView;
        VisualElement inspectorPanel;
        ObjectField actionField;
        ObjectField definitionField;
        Label emptySelectionLabel;

        [MenuItem("Tools/Gameplay/Combo Graph Editor")]
        public static void ShowWindow()
        {
            ComboGraphEditorWindow window = GetWindow<ComboGraphEditorWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.Show();
        }

        public static void Open(CharacterComboAction action)
        {
            ComboGraphEditorWindow window = GetWindow<ComboGraphEditorWindow>();
            window.SetTargetAction(action);
            window.Focus();
        }

        public static void Open(CharacterComboDefinition definition)
        {
            ComboGraphEditorWindow window = GetWindow<ComboGraphEditorWindow>();
            window.SetTargetDefinition(definition);
            window.Focus();
        }

        void OnEnable()
        {
            ConstructUI();
            Undo.undoRedoPerformed += HandleUndoRedo;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedo;
        }

        void ConstructUI()
        {
            rootVisualElement.Clear();

            Toolbar toolbar = BuildToolbar();
            rootVisualElement.Add(toolbar);

            TwoPaneSplitView split = new TwoPaneSplitView(0, 480, TwoPaneSplitViewOrientation.Horizontal);
            graphView = new CharacterComboGraphView();
            split.Add(graphView);

            inspectorPanel = new ScrollView();
            inspectorPanel.style.minWidth = 380;
            split.Add(inspectorPanel);

            rootVisualElement.Add(split);

            emptySelectionLabel = new Label("Select a combo step, entry, or edit the definition settings.")
            {
                style = { unityTextAlign = TextAnchor.MiddleCenter, marginTop = 12, marginBottom = 12 }
            };

            graphView.StepSelected += ShowStepInspector;
            graphView.EntrySelected += ShowEntryInspector;
            graphView.NothingSelected += ShowDefinitionInspector;

            ShowDefinitionInspector();
        }

        Toolbar BuildToolbar()
        {
            Toolbar toolbar = new Toolbar();

            actionField = new ObjectField("Action")
            {
                objectType = typeof(CharacterComboAction),
                allowSceneObjects = true,
                style = { width = 250 }
            };
            actionField.RegisterValueChangedCallback(evt => SetTargetAction(evt.newValue as CharacterComboAction));
            toolbar.Add(actionField);

            definitionField = new ObjectField("Combo Definition")
            {
                objectType = typeof(CharacterComboDefinition),
                allowSceneObjects = false,
                style = { width = 250 }
            };
            definitionField.RegisterValueChangedCallback(evt => SetTargetDefinition(evt.newValue as CharacterComboDefinition));
            toolbar.Add(definitionField);

            toolbar.Add(new ToolbarSpacer() { style = { flexGrow = 1f } });

            Button addEntryButton = new(() => AddEntry()) { text = "Add Entry" };
            toolbar.Add(addEntryButton);

            Button addStepButton = new(() => AddStep()) { text = "Add Step" };
            toolbar.Add(addStepButton);

            Button rebuildButton = new(() => graphView.RefreshGraph()) { text = "Refresh" };
            toolbar.Add(rebuildButton);

            return toolbar;
        }

        void SetTargetAction(CharacterComboAction action)
        {
            targetAction = action;
            actionField.SetValueWithoutNotify(action);
            if (action != null)
            {
                SetTargetDefinition(action.ComboDefinition);
            }
        }

        void SetTargetDefinition(CharacterComboDefinition definition)
        {
            targetDefinition = definition;
            definitionField.SetValueWithoutNotify(definition);
            serializedDefinition = definition != null ? new SerializedObject(definition) : null;
            graphView?.SetDefinition(serializedDefinition);
            ShowDefinitionInspector();
        }

        void HandleUndoRedo()
        {
            serializedDefinition?.UpdateIfRequiredOrScript();
            graphView?.RefreshGraph();
            ShowDefinitionInspector();
        }

        void ShowDefinitionInspector()
        {
            inspectorPanel.Clear();
            if (serializedDefinition == null)
            {
                inspectorPanel.Add(new HelpBox("Assign a CharacterComboDefinition or CharacterComboAction to begin.", HelpBoxMessageType.Info));
                return;
            }

            serializedDefinition.UpdateIfRequiredOrScript();

            inspectorPanel.Add(new Label("Definition Settings") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            PropertyField requiresAim = new(serializedDefinition.FindProperty("requiresAimInput"), "Requires Aim Input");
            requiresAim.Bind(serializedDefinition);
            inspectorPanel.Add(requiresAim);

            PropertyField queuedLifetime = new(serializedDefinition.FindProperty("queuedInputLifetime"), "Queued Input Lifetime");
            queuedLifetime.Bind(serializedDefinition);
            inspectorPanel.Add(queuedLifetime);

            PropertyField entries = new(serializedDefinition.FindProperty("entrySteps"), "Entry Steps");
            entries.Bind(serializedDefinition);
            inspectorPanel.Add(entries);

            PropertyField steps = new(serializedDefinition.FindProperty("steps"), "Steps");
            steps.Bind(serializedDefinition);
            inspectorPanel.Add(steps);

            inspectorPanel.Add(emptySelectionLabel);
        }

        void ShowStepInspector(SerializedProperty stepProperty)
        {
            inspectorPanel.Clear();
            if (stepProperty == null)
            {
                ShowDefinitionInspector();
                return;
            }

            inspectorPanel.Add(new Label($"Step: {stepProperty.FindPropertyRelative("id").stringValue}")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold }
            });
            PropertyField field = new(stepProperty);
            inspectorPanel.Add(field);
            field.Bind(serializedDefinition);
        }

        void ShowEntryInspector(SerializedProperty entryProperty)
        {
            inspectorPanel.Clear();
            if (entryProperty == null)
            {
                ShowDefinitionInspector();
                return;
            }

            inspectorPanel.Add(new Label("Entry") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            PropertyField field = new(entryProperty);
            inspectorPanel.Add(field);
            field.Bind(serializedDefinition);
        }

        void AddStep()
        {
            if (serializedDefinition == null)
            {
                return;
            }

            serializedDefinition.UpdateIfRequiredOrScript();
            SerializedProperty steps = serializedDefinition.FindProperty("steps");
            steps.InsertArrayElementAtIndex(steps.arraySize);
            SerializedProperty newStep = steps.GetArrayElementAtIndex(steps.arraySize - 1);
            ResetStep(newStep, steps.arraySize - 1);
            serializedDefinition.ApplyModifiedProperties();
            graphView.RefreshGraph();
        }

        void AddEntry()
        {
            if (serializedDefinition == null)
            {
                return;
            }

            serializedDefinition.UpdateIfRequiredOrScript();
            SerializedProperty entries = serializedDefinition.FindProperty("entrySteps");
            entries.InsertArrayElementAtIndex(entries.arraySize);
            SerializedProperty entry = entries.GetArrayElementAtIndex(entries.arraySize - 1);
            entry.FindPropertyRelative("input").enumValueIndex = 0;
            entry.FindPropertyRelative("stepId").stringValue = string.Empty;
            entry.FindPropertyRelative("graphPosition").vector2Value = new Vector2(60f, entries.arraySize * 120f);
            serializedDefinition.ApplyModifiedProperties();
            graphView.RefreshGraph();
        }

        void ResetStep(SerializedProperty step, int index)
        {
            step.FindPropertyRelative("id").stringValue = $"step_{index + 1}";
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
            step.FindPropertyRelative("lockMovementInRecovery").boolValue = true;
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
            while (transitions.arraySize > 0)
            {
                transitions.DeleteArrayElementAtIndex(transitions.arraySize - 1);
            }
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
            step.FindPropertyRelative("scaleAnimationSpeedToStepDuration").boolValue = false;
            step.FindPropertyRelative("scaleWindupAnimationToStepDuration").boolValue = false;
            step.FindPropertyRelative("scaleActiveAnimationToStepDuration").boolValue = false;
            step.FindPropertyRelative("scaleRecoveryAnimationToStepDuration").boolValue = false;
            step.FindPropertyRelative("overrideAnimationSpeed").boolValue = false;
            step.FindPropertyRelative("graphPosition").vector2Value = new Vector2(420f + (index % 4) * 240f, 100f + (index / 4) * 180f);
        }

        static void ResetAnimation(SerializedProperty animation)
        {
            if (animation == null)
            {
                return;
            }

            animation.FindPropertyRelative("directionMode").enumValueIndex = (int)CharacterMovement.AgentAnimationRequest.DirectionMode.Single;
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
