#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UtilityAI;
using _PinBoy.Scripts.CharacterMovement;

namespace _PinBoy.Scripts.Agents.UtilityAI.Editor
{
    public class BrainEditor : EditorWindow
    {
        static readonly GUIContent WindowTitle = new("Brain Editor");
        static readonly Color SelectedBrainColor = new(0.3f, 0.55f, 0.85f, 0.85f);
        const double BrainRefreshInterval = 1.0;
        const float LeftPanelWidth = 330f;

        readonly Dictionary<AIAction, EvaluationSnapshot> evaluationCache = new();

        Brain selectedBrain;
        Brain[] cachedBrains = Array.Empty<Brain>();
        SerializedObject brainSerializedObject;
        SerializedProperty actionsProperty;
        Vector2 leftScroll;
        Vector2 rightScroll;
        double nextBrainRefresh;
        bool followSceneSelection = true;
        int selectedActionIndex;
        string agentSelectionWarning;

        struct EvaluationSnapshot
        {
            public float Utility;
            public Transform Target;
            public int TargetsInRange;
            public bool IsCurrentBest;
            public float Time;
        }

        [MenuItem("Tools/Utility AI/Brain Editor")]
        public static void Open()
        {
            var window = GetWindow<BrainEditor>();
            window.titleContent = WindowTitle;
            window.Show();
        }

        void OnEnable()
        {
            titleContent = WindowTitle;
            RefreshBrainCache(true);
            EditorApplication.update += HandleEditorUpdate;
            Selection.selectionChanged += HandleSelectionChanged;
        }

        void OnDisable()
        {
            EditorApplication.update -= HandleEditorUpdate;
            Selection.selectionChanged -= HandleSelectionChanged;
            SetSelectedBrain(null);
        }

        void HandleEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup >= nextBrainRefresh)
            {
                RefreshBrainCache();
            }

            if (!selectedBrain && followSceneSelection)
            {
                TryAdoptSelection();
            }

            if (EditorApplication.isPlaying)
            {
                Repaint();
            }
        }

        void HandleSelectionChanged()
        {
            if (!followSceneSelection)
            {
                return;
            }

            TryAdoptSelection();
        }

        void TryAdoptSelection()
        {
            var active = Selection.activeGameObject;
            if (!active)
            {
                return;
            }

            Brain brain = active.GetComponentInParent<Brain>();
            if (!brain)
            {
                AgentController controller = active.GetComponentInParent<AgentController>();
                if (controller)
                {
                    brain = controller.GetComponentInChildren<Brain>(true);
                }
            }

            if (!brain)
            {
                brain = active.GetComponentInChildren<Brain>(true);
            }

            if (brain)
            {
                SetSelectedBrain(brain);
            }
        }

        void RefreshBrainCache(bool force = false)
        {
            if (!force && EditorApplication.timeSinceStartup < nextBrainRefresh)
            {
                return;
            }

            nextBrainRefresh = EditorApplication.timeSinceStartup + BrainRefreshInterval;

            var allBrains = Resources.FindObjectsOfTypeAll<Brain>();
            cachedBrains = allBrains
                .Where(brain => brain != null && !EditorUtility.IsPersistent(brain) && brain.gameObject.scene.IsValid())
                .Distinct()
                .OrderBy(brain => brain.gameObject.scene.name)
                .ThenBy(brain => brain.name)
                .ToArray();

            if (selectedBrain && !cachedBrains.Contains(selectedBrain))
            {
                SetSelectedBrain(null);
            }

            Repaint();
        }

        void SetSelectedBrain(Brain brain)
        {
            if (selectedBrain == brain)
            {
                return;
            }

            if (selectedBrain)
            {
                selectedBrain.ActionEvaluated -= HandleActionEvaluated;
            }

            selectedBrain = brain;
            evaluationCache.Clear();
            selectedActionIndex = 0;
            agentSelectionWarning = null;

            if (selectedBrain)
            {
                brainSerializedObject = new SerializedObject(selectedBrain);
                actionsProperty = brainSerializedObject.FindProperty("actions");
                selectedBrain.ActionEvaluated += HandleActionEvaluated;
            }
            else
            {
                brainSerializedObject = null;
                actionsProperty = null;
            }

            Repaint();
        }

        void HandleActionEvaluated(Brain.ActionEvaluation evaluation)
        {
            if (evaluation.Action == null)
            {
                return;
            }

            evaluationCache[evaluation.Action] = new EvaluationSnapshot
            {
                Utility = evaluation.Utility,
                Target = evaluation.EvaluatedTarget,
                TargetsInRange = evaluation.TargetsInRange,
                IsCurrentBest = evaluation.IsCurrentBest,
                Time = evaluation.Time
            };

            Repaint();
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLeftPanel();
                DrawRightPanel();
            }
        }

        void DrawLeftPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(LeftPanelWidth)))
            {
                DrawBrainSelection();
                EditorGUILayout.Space();
                DrawBrainList();
                EditorGUILayout.Space();
                DrawRuntimeDebugging();
            }
        }

        void DrawBrainSelection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Brain Selection", EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();
                Brain brainField = (Brain)EditorGUILayout.ObjectField("Brain", selectedBrain, typeof(Brain), true);
                if (EditorGUI.EndChangeCheck())
                {
                    SetSelectedBrain(brainField);
                }

                AgentController currentAgent = GetControllerForBrain(selectedBrain);
                EditorGUI.BeginChangeCheck();
                AgentController agentField = (AgentController)EditorGUILayout.ObjectField("Agent", currentAgent, typeof(AgentController), true);
                if (EditorGUI.EndChangeCheck())
                {
                    agentSelectionWarning = null;
                    Brain brainFromAgent = agentField ? agentField.GetComponentInChildren<Brain>(true) : null;
                    if (brainFromAgent)
                    {
                        SetSelectedBrain(brainFromAgent);
                    }
                    else if (agentField != null)
                    {
                        agentSelectionWarning = "Selected AgentController has no Brain component in its hierarchy.";
                    }
                }

                followSceneSelection = EditorGUILayout.ToggleLeft("Follow Scene Selection", followSceneSelection);

                if (GUILayout.Button("Refresh List", GUILayout.Width(120f)))
                {
                    RefreshBrainCache(true);
                }

                if (!string.IsNullOrEmpty(agentSelectionWarning))
                {
                    EditorGUILayout.HelpBox(agentSelectionWarning, MessageType.Warning);
                }
            }
        }

        void DrawBrainList()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Scene Brains", EditorStyles.boldLabel);

                if (cachedBrains.Length == 0)
                {
                    EditorGUILayout.HelpBox("No Brain instances found in the open scenes.", MessageType.Info);
                    return;
                }

                float listHeight = Mathf.Min(220f, 26f * cachedBrains.Length + 8f);
                leftScroll = EditorGUILayout.BeginScrollView(leftScroll, GUILayout.Height(listHeight));
                foreach (Brain brain in cachedBrains)
                {
                    if (!brain)
                    {
                        continue;
                    }

                    string sceneName = brain.gameObject.scene.IsValid() ? brain.gameObject.scene.name : "<No Scene>";
                    string label = BuildBrainLabel(brain);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(sceneName, EditorStyles.miniLabel, GUILayout.Width(110f));

                        Color previous = GUI.color;
                        if (brain == selectedBrain)
                        {
                            GUI.color = SelectedBrainColor;
                        }

                        if (GUILayout.Button(label, GUILayout.ExpandWidth(true)))
                        {
                            SetSelectedBrain(brain);
                            Selection.activeGameObject = brain.gameObject;
                            EditorGUIUtility.PingObject(brain);
                        }

                        GUI.color = previous;

                        if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(50f)))
                        {
                            EditorGUIUtility.PingObject(brain);
                        }
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        void DrawRuntimeDebugging()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Utility Debugging", EditorStyles.boldLabel);

                if (!selectedBrain)
                {
                    EditorGUILayout.HelpBox("Select a Brain to view utility evaluations.", MessageType.Info);
                    return;
                }

                if (!EditorApplication.isPlaying)
                {
                    EditorGUILayout.HelpBox("Enter Play Mode to view live utility evaluations.", MessageType.Warning);
                    return;
                }

                if (selectedBrain.context == null)
                {
                    EditorGUILayout.HelpBox("Context has not been initialized yet.", MessageType.Info);
                    return;
                }

                if (selectedBrain.actions.Count == 0)
                {
                    EditorGUILayout.HelpBox("No utility actions configured on this Brain.", MessageType.Info);
                    return;
                }

                foreach (AIAction action in selectedBrain.actions)
                {
                    if (action == null)
                    {
                        continue;
                    }

                    bool hasSnapshot = evaluationCache.TryGetValue(action, out EvaluationSnapshot snapshot);
                    string actionName = ObjectNames.NicifyVariableName(action.GetType().Name);

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(actionName, EditorStyles.boldLabel);
                            if (hasSnapshot && snapshot.IsCurrentBest)
                            {
                                EditorGUILayout.LabelField("Current Best", EditorStyles.miniBoldLabel, GUILayout.Width(100f));
                            }
                        }

                        if (!hasSnapshot)
                        {
                            EditorGUILayout.LabelField("Awaiting evaluation...", EditorStyles.miniLabel);
                            continue;
                        }

                        EditorGUILayout.LabelField("Utility", snapshot.Utility.ToString("0.###"));
                        EditorGUILayout.LabelField("Target", snapshot.Target ? snapshot.Target.name : "None");
                        EditorGUILayout.LabelField("Targets In Range", snapshot.TargetsInRange.ToString());
                        EditorGUILayout.LabelField("Last Tick", snapshot.Time.ToString("0.00s"));
                    }
                }
            }
        }

        void DrawRightPanel()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                if (!selectedBrain)
                {
                    EditorGUILayout.HelpBox("Select a Brain on the left to edit its actions.", MessageType.Info);
                    return;
                }

                if (brainSerializedObject == null)
                {
                    brainSerializedObject = new SerializedObject(selectedBrain);
                    actionsProperty = brainSerializedObject.FindProperty("actions");
                }

                brainSerializedObject.Update();
                DrawActionToolbar();

                if (actionsProperty.arraySize == 0)
                {
                    EditorGUILayout.HelpBox("Add a Utility Action to start editing.", MessageType.Info);
                    brainSerializedObject.ApplyModifiedProperties();
                    return;
                }

                selectedActionIndex = Mathf.Clamp(selectedActionIndex, 0, actionsProperty.arraySize - 1);

                string[] actionTabs = BuildActionTabs(actionsProperty);
                selectedActionIndex = GUILayout.Toolbar(selectedActionIndex, actionTabs);

                rightScroll = EditorGUILayout.BeginScrollView(rightScroll);
                SerializedProperty actionProperty = actionsProperty.GetArrayElementAtIndex(selectedActionIndex);
                EditorGUILayout.PropertyField(actionProperty, GUIContent.none, true);
                EditorGUILayout.EndScrollView();

                brainSerializedObject.ApplyModifiedProperties();
                TrimEvaluationCache();
            }
        }

        void DrawActionToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("AI Actions", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Add", EditorStyles.miniButton))
                {
                    ShowAddMenu();
                }

                using (new EditorGUI.DisabledScope(actionsProperty.arraySize == 0))
                {
                    if (GUILayout.Button("Remove", EditorStyles.miniButton))
                    {
                        RemoveAction(selectedActionIndex);
                    }
                }

                using (new EditorGUI.DisabledScope(selectedActionIndex <= 0))
                {
                    if (GUILayout.Button("Up", EditorStyles.miniButton))
                    {
                        MoveAction(selectedActionIndex, selectedActionIndex - 1);
                    }
                }

                using (new EditorGUI.DisabledScope(selectedActionIndex < 0 || selectedActionIndex >= actionsProperty.arraySize - 1))
                {
                    if (GUILayout.Button("Down", EditorStyles.miniButton))
                    {
                        MoveAction(selectedActionIndex, selectedActionIndex + 1);
                    }
                }
            }
        }

        void ShowAddMenu()
        {
            var menu = new GenericMenu();
            bool hasEntries = false;
            foreach (var type in TypeCache.GetTypesDerivedFrom(typeof(AIAction)))
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition || type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                hasEntries = true;
                menu.AddItem(new GUIContent(type.Name), false, () => AddAction(type));
            }

            if (!hasEntries)
            {
                menu.AddDisabledItem(new GUIContent("No AIAction implementations found"));
            }

            menu.ShowAsContext();
        }

        void AddAction(Type actionType)
        {
            brainSerializedObject.Update();
            actionsProperty.arraySize++;
            SerializedProperty element = actionsProperty.GetArrayElementAtIndex(actionsProperty.arraySize - 1);
            element.managedReferenceValue = Activator.CreateInstance(actionType);
            brainSerializedObject.ApplyModifiedProperties();
            selectedActionIndex = actionsProperty.arraySize - 1;
            TrimEvaluationCache();
            EditorUtility.SetDirty(selectedBrain);
        }

        void RemoveAction(int index)
        {
            if (index < 0 || index >= actionsProperty.arraySize)
            {
                return;
            }

            brainSerializedObject.Update();
            actionsProperty.DeleteArrayElementAtIndex(index);
            brainSerializedObject.ApplyModifiedProperties();
            selectedActionIndex = Mathf.Clamp(selectedActionIndex, 0, actionsProperty.arraySize - 1);
            TrimEvaluationCache();
            EditorUtility.SetDirty(selectedBrain);
        }

        void MoveAction(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= actionsProperty.arraySize)
            {
                return;
            }

            if (toIndex < 0 || toIndex >= actionsProperty.arraySize)
            {
                return;
            }

            brainSerializedObject.Update();
            actionsProperty.MoveArrayElement(fromIndex, toIndex);
            brainSerializedObject.ApplyModifiedProperties();
            selectedActionIndex = toIndex;
            TrimEvaluationCache();
            EditorUtility.SetDirty(selectedBrain);
        }

        string[] BuildActionTabs(SerializedProperty actions)
        {
            int count = actions.arraySize;
            string[] tabs = new string[count];
            for (int i = 0; i < count; i++)
            {
                SerializedProperty element = actions.GetArrayElementAtIndex(i);
                string label = GetActionLabel(element, i);
                tabs[i] = label;
            }

            return tabs;
        }

        string GetActionLabel(SerializedProperty element, int index)
        {
            if (element == null)
            {
                return $"Action {index + 1}";
            }

            var action = element.managedReferenceValue as AIAction;
            if (action == null)
            {
                return $"Action {index + 1}";
            }

            return ObjectNames.NicifyVariableName(action.GetType().Name);
        }

        void TrimEvaluationCache()
        {
            if (!selectedBrain)
            {
                evaluationCache.Clear();
                return;
            }

            var validActions = new HashSet<AIAction>(selectedBrain.actions.Where(action => action != null));
            var invalidKeys = evaluationCache.Keys.Where(action => !validActions.Contains(action)).ToList();
            foreach (AIAction action in invalidKeys)
            {
                evaluationCache.Remove(action);
            }
        }

        AgentController GetControllerForBrain(Brain brain)
        {
            if (!brain)
            {
                return null;
            }

            if (brain.controller)
            {
                return brain.controller;
            }

            return brain.GetComponentInParent<AgentController>();
        }

        string BuildBrainLabel(Brain brain)
        {
            AgentController controller = GetControllerForBrain(brain);
            if (controller)
            {
                return $"{controller.name} ({brain.name})";
            }

            return brain.name;
        }
    }
}
#endif
