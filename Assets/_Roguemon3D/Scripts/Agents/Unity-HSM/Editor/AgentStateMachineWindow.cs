using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using _PinBoy.Scripts.CharacterMovement;
using HSM;

namespace _PinBoy.Scripts.Agents.UnityHSM.Editor
{
    public class AgentStateMachineWindow : EditorWindow
    {
        static readonly GUIContent WindowTitle = new("Agent State Machine");
        static readonly Color ActiveStateColorPro = new(0.2f, 0.45f, 0.25f, 0.65f);
        static readonly Color ActiveStateColorPersonal = new(0.55f, 0.85f, 0.55f, 0.65f);
        static readonly Color SelectedAgentColor = new(0.3f, 0.55f, 0.85f, 0.85f);
        static readonly BindingFlags ChildBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        const double AgentRefreshInterval = 1.0;

        readonly Dictionary<State, bool> foldoutStates = new();
        readonly HashSet<State> drawVisited = new();
        readonly Dictionary<Type, FieldInfo[]> childFieldCache = new();

        AgentController selectedAgent;
        AgentController[] cachedAgents = Array.Empty<AgentController>();
        Vector2 agentScroll;
        Vector2 stateScroll;
        double nextAgentRefresh;
        bool followSceneSelection = true;
        bool showAgentList = true;

        //[MenuItem("Tools/Gameplay/Agent State Machine Debugger")]
        public static void Open()
        {
            var window = GetWindow<AgentStateMachineWindow>();
            window.titleContent = WindowTitle;
            window.Show();
        }

        void OnEnable()
        {
            titleContent = WindowTitle;
            RefreshAgentCache(true);
            EditorApplication.update += HandleEditorUpdate;
            Selection.selectionChanged += HandleSelectionChanged;
        }

        void OnDisable()
        {
            EditorApplication.update -= HandleEditorUpdate;
            Selection.selectionChanged -= HandleSelectionChanged;
        }

        void HandleEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup >= nextAgentRefresh)
            {
                RefreshAgentCache();
            }

            if (!selectedAgent && followSceneSelection)
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
            if (active == null)
            {
                return;
            }

            AgentController controller = active.GetComponentInParent<AgentController>();
            if (controller != null)
            {
                SetSelectedAgent(controller);
            }
        }

        void RefreshAgentCache(bool force = false)
        {
            if (!force && EditorApplication.timeSinceStartup < nextAgentRefresh)
            {
                return;
            }

            nextAgentRefresh = EditorApplication.timeSinceStartup + AgentRefreshInterval;

            var allAgents = Resources.FindObjectsOfTypeAll<AgentController>();
            cachedAgents = allAgents
                .Where(agent => agent != null && !EditorUtility.IsPersistent(agent) && agent.gameObject.scene.IsValid())
                .Distinct()
                .OrderBy(agent => agent.gameObject.scene.name)
                .ThenBy(agent => agent.name)
                .ToArray();

            if (selectedAgent && !cachedAgents.Contains(selectedAgent))
            {
                SetSelectedAgent(null);
            }

            Repaint();
        }

        void SetSelectedAgent(AgentController controller)
        {
            if (selectedAgent == controller)
            {
                return;
            }

            selectedAgent = controller;
            foldoutStates.Clear();
            drawVisited.Clear();
            Repaint();
        }

        void OnGUI()
        {
            DrawAgentSelection();
            EditorGUILayout.Space();

            if (!selectedAgent)
            {
                EditorGUILayout.HelpBox("Select an AgentController from the scene to inspect its state machine.", MessageType.Info);
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to view live state machine information.", MessageType.Warning);
            }

            DrawAgentOverview(selectedAgent);
            EditorGUILayout.Space();
            DrawStateHierarchy(selectedAgent);
        }

        void DrawAgentSelection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Agent Selection", EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();
                AgentController agent = (AgentController)EditorGUILayout.ObjectField("Agent", selectedAgent, typeof(AgentController), true);
                if (EditorGUI.EndChangeCheck())
                {
                    SetSelectedAgent(agent);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    followSceneSelection = EditorGUILayout.ToggleLeft("Follow Scene Selection", followSceneSelection);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
                    {
                        RefreshAgentCache(true);
                    }
                }

                showAgentList = EditorGUILayout.ToggleLeft("Show Scene Agents", showAgentList);
                if (!showAgentList)
                {
                    return;
                }

                if (cachedAgents.Length == 0)
                {
                    EditorGUILayout.HelpBox("No AgentController instances found in the open scenes.", MessageType.Info);
                    return;
                }

                agentScroll = EditorGUILayout.BeginScrollView(agentScroll, GUILayout.MinHeight(Mathf.Min(200f, 24f * cachedAgents.Length + 8f)));
                foreach (AgentController controller in cachedAgents)
                {
                    if (!controller)
                    {
                        continue;
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string sceneName = controller.gameObject.scene.IsValid() ? controller.gameObject.scene.name : "<No Scene>";
                        EditorGUILayout.LabelField(sceneName, EditorStyles.miniLabel, GUILayout.Width(110f));

                        Color previous = GUI.color;
                        if (controller == selectedAgent)
                        {
                            GUI.color = SelectedAgentColor;
                        }

                        if (GUILayout.Button(controller.name, GUILayout.ExpandWidth(true)))
                        {
                            SetSelectedAgent(controller);
                            Selection.activeGameObject = controller.gameObject;
                            EditorGUIUtility.PingObject(controller);
                        }

                        GUI.color = previous;

                        if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(50f)))
                        {
                            EditorGUIUtility.PingObject(controller);
                            Selection.activeGameObject = controller.gameObject;
                        }
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        void DrawAgentOverview(AgentController agent)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("State Machine Overview", EditorStyles.boldLabel);

                if (agent.Machine == null || agent.AgentRoot == null)
                {
                    EditorGUILayout.HelpBox("The state machine has not been initialised yet.", MessageType.Info);
                    return;
                }

                string path = string.IsNullOrEmpty(agent.ActiveStatePath) ? "<Unknown>" : agent.ActiveStatePath;
                EditorGUILayout.LabelField("Active State Path", path);

                State activeLeaf = agent.ActiveLeafState;
                EditorGUILayout.LabelField("Active Leaf", FormatStateName(activeLeaf));

                int depth = activeLeaf != null ? activeLeaf.PathToRoot().Count() : 0;
                EditorGUILayout.LabelField("Active Depth", depth.ToString());

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawCompactStat("Grounded", agent.grounded.ToString());
                    DrawCompactStat("Action Running", agent.IsPerformingAction.ToString());
                    DrawCompactStat("Movement Locked", agent.IsMovementLocked.ToString());
                }

                TransitionSequencer sequencer = agent.Machine.Sequencer;
                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField("Transition Sequencer", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Status", sequencer.IsTransitioning ? "Transitioning" : "Idle");

                if (sequencer.IsTransitioning)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.LabelField("From", FormatStateName(sequencer.ActiveTransitionFrom));
                        EditorGUILayout.LabelField("To", FormatStateName(sequencer.ActiveTransitionTo));
                    }
                }

                if (sequencer.PendingTransition.HasValue)
                {
                    (State from, State to) pending = sequencer.PendingTransition.Value;
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.LabelField("Pending", $"{FormatStateName(pending.from)} → {FormatStateName(pending.to)}");
                    }
                }

                if (sequencer.LastCompletedTransition.HasValue)
                {
                    (State from, State to) completed = sequencer.LastCompletedTransition.Value;
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.LabelField("Last Completed", $"{FormatStateName(completed.from)} → {FormatStateName(completed.to)}");
                    }
                }

                if (sequencer.LastRequestedFrom != null || sequencer.LastRequestedTo != null)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.LabelField("Last Requested", $"{FormatStateName(sequencer.LastRequestedFrom)} → {FormatStateName(sequencer.LastRequestedTo)}");
                    }
                }

                if (activeLeaf != null)
                {
                    EditorGUILayout.LabelField("Active Path", EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        foreach (State state in activeLeaf.PathToRoot().Reverse())
                        {
                            EditorGUILayout.LabelField(FormatStateName(state));
                        }
                    }
                }
            }
        }

        void DrawStateHierarchy(AgentController agent)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("State Hierarchy", EditorStyles.boldLabel);

                if (agent.Machine?.Root == null)
                {
                    EditorGUILayout.HelpBox("State hierarchy is unavailable until the machine is initialised.", MessageType.Info);
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Expand All", EditorStyles.miniButtonLeft, GUILayout.Width(90f)))
                    {
                        SetAllFoldouts(agent.Machine.Root, true);
                    }

                    if (GUILayout.Button("Collapse All", EditorStyles.miniButtonRight, GUILayout.Width(90f)))
                    {
                        SetAllFoldouts(agent.Machine.Root, false);
                    }
                }

                State leaf = agent.Machine.Root.Leaf();
                HashSet<State> activeStates = new();
                if (leaf != null)
                {
                    foreach (State state in leaf.PathToRoot())
                    {
                        activeStates.Add(state);
                    }
                }

                drawVisited.Clear();
                stateScroll = EditorGUILayout.BeginScrollView(stateScroll, GUILayout.MinHeight(220f));
                DrawStateRecursive(agent.Machine.Root, activeStates, 0);
                EditorGUILayout.EndScrollView();
            }
        }

        void DrawStateRecursive(State state, HashSet<State> activeStates, int depth)
        {
            if (state == null || !drawVisited.Add(state))
            {
                return;
            }

            bool isActive = activeStates.Contains(state);
            string label = FormatStateName(state);
            if (isActive)
            {
                label += " (Active)";
            }

            int previousIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = depth;
            Rect controlRect = EditorGUILayout.GetControlRect();
            Rect indentedRect = EditorGUI.IndentedRect(controlRect);
            if (isActive)
            {
                EditorGUI.DrawRect(indentedRect, EditorGUIUtility.isProSkin ? ActiveStateColorPro : ActiveStateColorPersonal);
            }

            bool expanded = GetFoldoutState(state, isActive);
            expanded = EditorGUI.Foldout(controlRect, expanded, label, true);
            SetFoldoutState(state, expanded);
            EditorGUI.indentLevel = previousIndent;

            if (!expanded)
            {
                return;
            }

            List<State> children = EnumerateChildStates(state).ToList();

            EditorGUI.indentLevel = depth + 1;
            EditorGUILayout.LabelField("Parent", FormatStateName(state.Parent));
            EditorGUILayout.LabelField("Active Child", FormatStateName(state.ActiveChild));
            EditorGUILayout.LabelField("Children", children.Count > 0 ? string.Join(", ", children.Select(FormatStateName)) : "None");

            IReadOnlyList<IActivity> activities = state.Activities;
            if (activities.Count > 0)
            {
                EditorGUILayout.LabelField("Activities");
                using (new EditorGUI.IndentLevelScope())
                {
                    foreach (IActivity activity in activities)
                    {
                        if (activity == null)
                        {
                            EditorGUILayout.LabelField("<null>");
                            continue;
                        }

                        EditorGUILayout.LabelField(activity.GetType().Name, activity.Mode.ToString());
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("Activities", "None");
            }

            EditorGUI.indentLevel = previousIndent;

            foreach (State child in children)
            {
                DrawStateRecursive(child, activeStates, depth + 1);
            }
        }

        IEnumerable<State> EnumerateChildStates(State state)
        {
            if (state == null)
            {
                yield break;
            }

            Type type = state.GetType();
            if (!childFieldCache.TryGetValue(type, out FieldInfo[] fields))
            {
                fields = type
                    .GetFields(ChildBindingFlags)
                    .Where(field => typeof(State).IsAssignableFrom(field.FieldType) && field.Name != nameof(State.Parent))
                    .ToArray();
                childFieldCache[type] = fields;
            }

            foreach (FieldInfo field in fields)
            {
                if (field.GetValue(state) is State child && ReferenceEquals(child.Parent, state))
                {
                    yield return child;
                }
            }
        }

        void SetAllFoldouts(State root, bool expanded)
        {
            foreach (State state in EnumerateAllStates(root))
            {
                if (state == null)
                {
                    continue;
                }

                foldoutStates[state] = expanded;
            }
        }

        IEnumerable<State> EnumerateAllStates(State root)
        {
            if (root == null)
            {
                yield break;
            }

            var stack = new Stack<State>();
            var visited = new HashSet<State>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                State current = stack.Pop();
                if (current == null || !visited.Add(current))
                {
                    continue;
                }

                yield return current;

                foreach (State child in EnumerateChildStates(current))
                {
                    stack.Push(child);
                }
            }
        }

        bool GetFoldoutState(State state, bool defaultValue)
        {
            if (state == null)
            {
                return false;
            }

            if (!foldoutStates.TryGetValue(state, out bool expanded))
            {
                expanded = defaultValue;
                foldoutStates.Add(state, expanded);
            }

            return expanded;
        }

        void SetFoldoutState(State state, bool expanded)
        {
            if (state == null)
            {
                return;
            }

            foldoutStates[state] = expanded;
        }

        static void DrawCompactStat(string label, string value)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(140f)))
            {
                EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
                EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            }
        }

        static string FormatStateName(State state)
        {
            return state != null ? state.GetType().Name : "None";
        }
    }
}
