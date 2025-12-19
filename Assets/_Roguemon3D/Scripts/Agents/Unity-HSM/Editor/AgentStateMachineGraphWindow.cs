using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Actions;
using HSM;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace _PinBoy.Scripts.Agents.UnityHSM.Editor
{
    public class AgentStateMachineGraphWindow : EditorWindow
    {
        const double AgentRefreshInterval = 1.0f;
        static readonly GUIContent WindowTitle = new("Agent State Graph");

        readonly List<AgentController> cachedAgents = new();
        ObjectField agentField;
        ToolbarToggle followSelectionToggle;
        ListView agentList;
        Label statusLabel;
        AgentStateMachineGraphView graphView;
        double nextAgentRefresh;
        AgentController selectedAgent;

        [MenuItem("Tools/Gameplay/Agent State Graph")]
        public static void Open()
        {
            var window = GetWindow<AgentStateMachineGraphWindow>();
            window.titleContent = WindowTitle;
            window.Show();
        }

        void OnEnable()
        {
            titleContent = WindowTitle;
            CreateLayout();
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

            if (!selectedAgent && followSelectionToggle.value)
            {
                TryAdoptSelection();
            }

            if (EditorApplication.isPlaying && selectedAgent != null)
            {
                graphView?.UpdateRuntimeState(selectedAgent);
            }
        }

        void HandleSelectionChanged()
        {
            if (followSelectionToggle.value)
            {
                TryAdoptSelection();
            }
        }

        void CreateLayout()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexGrow = 1f;

            var splitView = new TwoPaneSplitView(0, 280f, TwoPaneSplitViewOrientation.Horizontal)
            {
                style = { flexGrow = 1f }
            };

            splitView.Add(CreateInspectorPane());
            splitView.Add(CreateGraphPane());

            rootVisualElement.Add(splitView);
        }

        VisualElement CreateInspectorPane()
        {
            var container = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    paddingLeft = 4,
                    paddingRight = 4,
                    paddingTop = 4,
                    paddingBottom = 4
                }
            };

            var toolbar = new Toolbar();
            followSelectionToggle = new ToolbarToggle { text = "Follow Scene Selection", value = true };
            toolbar.Add(followSelectionToggle);

            var refreshButton = new ToolbarButton(() => RefreshAgentCache(true)) { text = "Refresh" };
            toolbar.Add(refreshButton);
            container.Add(toolbar);

            agentField = new ObjectField("Agent")
            {
                objectType = typeof(AgentController),
                allowSceneObjects = true,
                value = selectedAgent
            };
            agentField.RegisterValueChangedCallback(evt => SetSelectedAgent(evt.newValue as AgentController));
            container.Add(agentField);

            statusLabel = new Label
            {
                style = { marginTop = 4f, unityFontStyleAndWeight = FontStyle.Bold }
            };
            container.Add(statusLabel);

            var listHeader = new Label("Scene Agents")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginTop = 4f,
                    marginBottom = 2f
                }
            };
            container.Add(listHeader);

            agentList = new ListView
            {
                showBorder = true,
                showFoldoutHeader = false,
                fixedItemHeight = 18f,
                selectionType = SelectionType.Single,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight
            };
            agentList.makeItem = () => new Label();
            agentList.bindItem = (element, i) =>
            {
                if (element is Label label && i >= 0 && i < cachedAgents.Count)
                {
                    AgentController controller = cachedAgents[i];
                    string sceneName = controller != null && controller.gameObject.scene.IsValid()
                        ? controller.gameObject.scene.name
                        : "<No Scene>";
                    label.text = controller == null ? "<null>" : $"{sceneName} / {controller.name}";
                }
            };
            agentList.itemsChosen += objs =>
            {
                foreach (object obj in objs)
                {
                    int index = agentList.selectedIndex;
                    if (index >= 0 && index < cachedAgents.Count)
                    {
                        SetSelectedAgent(cachedAgents[index]);
                        Selection.activeObject = cachedAgents[index];
                    }
                }
            };
            container.Add(agentList);

            return container;
        }

        VisualElement CreateGraphPane()
        {
            graphView = new AgentStateMachineGraphView
            {
                style = { flexGrow = 1f }
            };

            var container = new VisualElement
            {
                style =
                {
                    flexGrow = 1f,
                    paddingTop = 4,
                    paddingBottom = 4
                }
            };
            container.Add(graphView);
            return container;
        }

        void TryAdoptSelection()
        {
            GameObject active = Selection.activeGameObject;
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

            cachedAgents.Clear();
            cachedAgents.AddRange(Resources.FindObjectsOfTypeAll<AgentController>()
                .Where(agent => agent != null && !EditorUtility.IsPersistent(agent) && agent.gameObject.scene.IsValid())
                .Distinct()
                .OrderBy(agent => agent.gameObject.scene.name)
                .ThenBy(agent => agent.name));

            agentList.itemsSource = cachedAgents;
            agentList.Rebuild();

            if (selectedAgent != null && !cachedAgents.Contains(selectedAgent))
            {
                SetSelectedAgent(null);
            }

            UpdateStatusLabel();
        }

        void SetSelectedAgent(AgentController controller)
        {
            if (selectedAgent == controller)
            {
                return;
            }

            selectedAgent = controller;
            agentField.SetValueWithoutNotify(controller);
            graphView.BuildGraphForAgent(controller);
            UpdateStatusLabel();
            Repaint();
        }

        void UpdateStatusLabel()
        {
            if (selectedAgent == null)
            {
                statusLabel.text = "No Agent selected.";
                return;
            }

            if (selectedAgent.Machine == null || selectedAgent.AgentRoot == null)
            {
                statusLabel.text = "State machine has not been initialised.";
                return;
            }

            string path = string.IsNullOrEmpty(selectedAgent.ActiveStatePath)
                ? "<Unknown>"
                : selectedAgent.ActiveStatePath;
            statusLabel.text = $"Active Path: {path}";
        }
    }

    class AgentStateMachineGraphView : GraphView
    {
        const float HorizontalSpacing = 220f;
        const float VerticalSpacing = 120f;
        const float NodeWidth = 200f;
        const float NodeHeight = 110f;

        readonly Dictionary<State, AgentStateNode> nodeLookup = new();
        readonly Dictionary<State, List<State>> childrenCache = new();
        readonly List<State> activePath = new();

        public AgentStateMachineGraphView()
        {
            style.flexGrow = 1f;
            SetupZoom(0.05f, 4f);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();
        }

        public void BuildGraphForAgent(AgentController agent)
        {
            graphViewChanged -= HandleGraphViewChanged;
            DeleteElements(graphElements.ToList());
            nodeLookup.Clear();
            childrenCache.Clear();
            activePath.Clear();

            if (agent == null)
            {
                AddPlaceholder("Select an AgentController to inspect its state machine.");
                return;
            }

            if (agent.Machine?.Root == null)
            {
                AddPlaceholder("State machine has not been initialised for this agent.");
                return;
            }

            PopulateGraph(agent.Machine.Root);
            UpdateRuntimeState(agent);
            FrameAll();
        }

        void PopulateGraph(State root)
        {
            graphViewChanged += HandleGraphViewChanged;
            List<State> states = AgentStateGraphUtility.EnumerateAllStates(root).ToList();
            foreach (State state in states)
            {
                AddStateNode(state);
            }

            foreach (State state in states)
            {
                foreach (State child in AgentStateGraphUtility.EnumerateChildStates(state))
                {
                    AddStateEdge(state, child);
                }
            }
        }

        void AddStateNode(State state)
        {
            var node = new AgentStateNode(state)
            {
                title = AgentStateGraphUtility.PrettyStateName(state),
                userData = state
            };

            nodeLookup[state] = node;
            AddElement(node);

            int depth = AgentStateGraphUtility.GetDepth(state);
            int index = AgentStateGraphUtility.GetSiblingIndex(state);
            Vector2 position = new(depth * HorizontalSpacing, index * VerticalSpacing);
            node.SetPosition(new Rect(position, new Vector2(NodeWidth, NodeHeight)));
        }

        void AddStateEdge(State parent, State child)
        {
            if (!nodeLookup.TryGetValue(parent, out AgentStateNode parentNode) ||
                !nodeLookup.TryGetValue(child, out AgentStateNode childNode))
            {
                return;
            }

            Edge edge = parentNode.Output.ConnectTo(childNode.Input);
            edge.capabilities &= ~Capabilities.Deletable;
            AddElement(edge);
        }

        GraphViewChange HandleGraphViewChanged(GraphViewChange change)
        {
            if (change.movedElements != null)
            {
                foreach (GraphElement element in change.movedElements)
                {
                    element.capabilities &= ~Capabilities.Movable;
                }
            }

            return change;
        }

        void AddPlaceholder(string message)
        {
            var label = new Label(message)
            {
                style =
                {
                    unityTextAlign = TextAnchor.MiddleCenter,
                    unityFontStyleAndWeight = FontStyle.Italic,
                    fontSize = 13,
                    paddingTop = 8,
                    paddingBottom = 8
                }
            };

            Add(label);
        }

        public void UpdateRuntimeState(AgentController agent)
        {
            if (agent?.Machine?.Root == null)
            {
                return;
            }

            activePath.Clear();
            State activeLeaf = agent.ActiveLeafState;
            if (activeLeaf != null)
            {
                activePath.AddRange(activeLeaf.PathToRoot());
            }

            foreach ((State state, AgentStateNode node) in nodeLookup)
            {
                bool isActive = activePath.Contains(state);
                bool isLeaf = state == activeLeaf;
                node.SetRuntimeState(isActive, isLeaf, agent.IsPerformingAction && state is ActionState);
            }
        }
    }

    class AgentStateNode : Node
    {
        readonly Label descriptionLabel;
        readonly Label activityLabel;
        readonly Color inactiveColor = new(0.18f, 0.18f, 0.18f, 0.75f);
        readonly Color activeColor = new(0.16f, 0.45f, 0.25f, 0.9f);
        readonly Color leafColor = new(0.2f, 0.55f, 0.45f, 0.95f);

        public Port Input { get; }
        public Port Output { get; }

        public AgentStateNode(State state)
        {
            capabilities &= ~Capabilities.Deletable;
            capabilities &= ~Capabilities.Movable;
            capabilities &= ~Capabilities.Ascendable;
            capabilities &= ~Capabilities.Copiable;

            Input = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(State));
            Input.portName = "Parent";
            inputContainer.Add(Input);

            Output = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(State));
            Output.portName = "Children";
            outputContainer.Add(Output);

            descriptionLabel = new Label
            {
                text = AgentStateGraphUtility.DescribeState(state),
                style = { whiteSpace = WhiteSpace.Normal }
            };
            mainContainer.Add(descriptionLabel);

            activityLabel = new Label
            {
                text = AgentStateGraphUtility.DescribeActivities(state),
                style = { color = Color.gray, whiteSpace = WhiteSpace.Normal }
            };
            extensionContainer.Add(activityLabel);

            RefreshExpandedState();
            ApplyInactiveStyle();
        }

        public void SetRuntimeState(bool isActive, bool isLeaf, bool isAction)
        {
            Color background = isLeaf ? leafColor : (isActive ? activeColor : inactiveColor);
            titleContainer.style.backgroundColor = new StyleColor(background);
            descriptionLabel.style.unityFontStyleAndWeight = isAction ? FontStyle.Bold : FontStyle.Normal;
            activityLabel.style.display = isActive ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void ApplyInactiveStyle()
        {
            titleContainer.style.backgroundColor = new StyleColor(inactiveColor);
        }
    }

    static class AgentStateGraphUtility
    {
        static readonly BindingFlags ChildBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        static readonly Dictionary<Type, FieldInfo[]> ChildFieldCache = new();
        static readonly Dictionary<State, int> CachedSiblingIndex = new();

        public static IEnumerable<State> EnumerateAllStates(State root)
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

        public static IEnumerable<State> EnumerateChildStates(State state)
        {
            if (state == null)
            {
                yield break;
            }

            Type type = state.GetType();
            if (!ChildFieldCache.TryGetValue(type, out FieldInfo[] fields))
            {
                fields = type
                    .GetFields(ChildBindingFlags)
                    .Where(field => typeof(State).IsAssignableFrom(field.FieldType) && field.Name != nameof(State.Parent))
                    .ToArray();
                ChildFieldCache[type] = fields;
            }

            int index = 0;
            foreach (FieldInfo field in fields)
            {
                if (field.GetValue(state) is State child && ReferenceEquals(child.Parent, state))
                {
                    CachedSiblingIndex[child] = index++;
                    yield return child;
                }
            }

            IReadOnlyList<State> dynamicChildren = state.DynamicChildren;
            if (dynamicChildren != null)
            {
                foreach (State child in dynamicChildren)
                {
                    if (child != null && ReferenceEquals(child.Parent, state))
                    {
                        CachedSiblingIndex[child] = index++;
                        yield return child;
                    }
                }
            }
        }

        public static int GetDepth(State state)
        {
            return state?.PathToRoot().Count() - 1 ?? 0;
        }

        public static int GetSiblingIndex(State state)
        {
            if (state != null && CachedSiblingIndex.TryGetValue(state, out int index))
            {
                return index;
            }

            return 0;
        }

        public static string PrettyStateName(State state)
        {
            return state == null ? "<null>" : state.GetType().Name;
        }

        public static string DescribeState(State state)
        {
            if (state == null)
            {
                return "<null state>";
            }

            return state.Parent == null ? "Root State" : $"Parent: {PrettyStateName(state.Parent)}";
        }

        public static string DescribeActivities(State state)
        {
            IReadOnlyList<IActivity> activities = state?.Activities;
            if (activities == null || activities.Count == 0)
            {
                return "Activities: none";
            }

            var names = activities.Select(activity => activity == null ? "<null>" : activity.GetType().Name);
            return "Activities: " + string.Join(", ", names);
        }
    }
}
