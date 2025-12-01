#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace _PinBoy.Scripts.Gameplay.Actions.Editor.GraphToolkit
{
    /// <summary>
    /// GraphToolkit powered editor that visualizes combo branches as a node graph. The window edits a
    /// <see cref="CharacterComboGraphAsset"/> which in turn hydrates <see cref="CharacterComboDefinition"/>.
    /// </summary>
    public class CharacterComboGraphWindow : EditorWindow
    {
        const string WindowTitle = "Character Combo Graph";

        CharacterComboGraphAsset graphAsset;
        CharacterComboDefinition definition;
        CharacterComboAction action;
        ComboGraphView graphView;
        ScrollView inspector;

        [MenuItem("Window/Gameplay/Character Combo Graph (GraphToolkit)")]
        public static void ShowWindow()
        {
            CharacterComboGraphWindow window = GetWindow<CharacterComboGraphWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.Show();
        }

        public static void Open(CharacterComboDefinition comboDefinition)
        {
            CharacterComboGraphWindow window = GetWindow<CharacterComboGraphWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.SetTargets(comboDefinition, null);
            window.Show();
        }

        public static void Open(CharacterComboAction comboAction)
        {
            CharacterComboGraphWindow window = GetWindow<CharacterComboGraphWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.SetTargets(comboAction.ComboDefinition, comboAction);
            window.Show();
        }

        void OnEnable()
        {
            CreateRootUi();
            if (graphAsset != null)
            {
                graphView?.LoadGraph(graphAsset);
            }
        }

        void CreateRootUi()
        {
            rootVisualElement.Clear();

            Toolbar toolbar = new();
            ObjectField actionField = new("Action") { objectType = typeof(CharacterComboAction) };
            actionField.RegisterValueChangedCallback(evt => SetTargets(definition, (CharacterComboAction)evt.newValue));
            toolbar.Add(actionField);

            ObjectField definitionField = new("Definition") { objectType = typeof(CharacterComboDefinition) };
            definitionField.RegisterValueChangedCallback(evt => SetTargets((CharacterComboDefinition)evt.newValue, action));
            toolbar.Add(definitionField);

            ObjectField graphField = new("Graph") { objectType = typeof(CharacterComboGraphAsset) };
            graphField.RegisterValueChangedCallback(evt => SetGraph((CharacterComboGraphAsset)evt.newValue));
            toolbar.Add(graphField);

            toolbar.Add(new ToolbarButton(() => CreateGraphAsset(definitionField)) { text = "Create Graph" });
            toolbar.Add(new ToolbarButton(SynchronizeGraphToDefinition) { text = "Sync to Definition" });
            toolbar.Add(new ToolbarButton(AddStepNode) { text = "Add Step" });
            toolbar.Add(new ToolbarButton(AddEntryNode) { text = "Add Entry" });
            rootVisualElement.Add(toolbar);

            VisualElement content = new() { style = { flexDirection = FlexDirection.Row } };
            graphView = new ComboGraphView(this)
            {
                style =
                {
                    flexGrow = 1f
                }
            };

            inspector = new ScrollView
            {
                style =
                {
                    flexBasis = 320,
                    maxWidth = 360,
                    minWidth = 280,
                    paddingLeft = 8,
                    paddingRight = 8
                }
            };

            graphView.OnSelectionChanged += HandleSelectionChanged;
            content.Add(graphView);
            content.Add(inspector);
            rootVisualElement.Add(content);

            actionField.value = action;
            definitionField.value = definition;
            graphField.value = graphAsset;
        }

        void SetTargets(CharacterComboDefinition newDefinition, CharacterComboAction newAction)
        {
            action = newAction;
            definition = newDefinition ?? action?.ComboDefinition;
            graphAsset = definition?.ComboGraph;
            graphView?.LoadGraph(graphAsset);
        }

        void SetGraph(CharacterComboGraphAsset asset)
        {
            graphAsset = asset;
            if (definition != null && graphAsset != null)
            {
                definition.EditorSetGraph(graphAsset);
            }
            graphView?.LoadGraph(graphAsset);
        }

        void CreateGraphAsset(ObjectField bindingField)
        {
            string path = EditorUtility.SaveFilePanelInProject(WindowTitle, "CharacterComboGraph", "asset", "Choose where to save the combo graph asset.");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            CharacterComboGraphAsset asset = CreateInstance<CharacterComboGraphAsset>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            SetGraph(asset);
            bindingField.value = asset;
        }

        void SynchronizeGraphToDefinition()
        {
            if (graphAsset == null || definition == null)
            {
                return;
            }

            graphAsset.ApplyToDefinition(definition);
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
        }

        void AddStepNode()
        {
            if (graphAsset == null)
            {
                return;
            }

            CharacterComboGraphAsset.StepNode step = graphAsset.CreateStep();
            step.Position = new Vector2(position.width * 0.25f, position.height * 0.25f);
            graphView?.LoadGraph(graphAsset);
            MarkDirty();
        }

        void AddEntryNode()
        {
            if (graphAsset == null)
            {
                return;
            }

            CharacterComboGraphAsset.EntryNode entry = graphAsset.AddEntry();
            entry.Position = new Vector2(position.width * 0.75f, position.height * 0.25f);
            graphView?.LoadGraph(graphAsset);
            MarkDirty();
        }

        void HandleSelectionChanged(IReadOnlyList<GraphElement> selection)
        {
            inspector.Clear();
            if (selection == null || selection.Count == 0)
            {
                return;
            }

            switch (selection[0])
            {
                case ComboStepNode stepNode:
                    DrawStepInspector(stepNode.StepNode);
                    break;
                case ComboEntryNode entryNode:
                    DrawEntryInspector(entryNode.EntryNode);
                    break;
                case ComboEdge edge when edge.EdgeData != null:
                    DrawTransitionInspector(edge.EdgeData);
                    break;
            }
        }

        void DrawStepInspector(CharacterComboGraphAsset.StepNode data)
        {
            inspector.Add(new Label("Step"));
            inspector.Add(new IMGUIContainer(() =>
            {
                EditorGUI.BeginChangeCheck();
                string previousId = data.Id;
                string id = EditorGUILayout.DelayedTextField("Id", data.Id);
                if (EditorGUI.EndChangeCheck())
                {
                    graphAsset?.RenameStep(previousId, id);
                    data.Id = id;
                    graphView?.RelabelStepNode(data);
                    MarkDirty();
                }

                SerializedObject so = new SerializedObject(graphAsset);
                SerializedProperty steps = so.FindProperty("steps");
                int index = graphAsset.Steps.ToList().IndexOf(data);
                if (index >= 0)
                {
                    SerializedProperty stepProp = steps.GetArrayElementAtIndex(index).FindPropertyRelative("step");
                    EditorGUILayout.PropertyField(stepProp, true);
                    if (so.ApplyModifiedProperties())
                    {
                        MarkDirty();
                        graphView?.RefreshTransitions();
                    }
                }
            }));
        }

        void DrawEntryInspector(CharacterComboGraphAsset.EntryNode data)
        {
            inspector.Add(new Label("Entry"));
            inspector.Add(new IMGUIContainer(() =>
            {
                SerializedObject so = new SerializedObject(graphAsset);
                SerializedProperty entries = so.FindProperty("entries");
                int index = graphAsset.Entries.ToList().IndexOf(data);
                if (index >= 0)
                {
                    SerializedProperty entryProp = entries.GetArrayElementAtIndex(index).FindPropertyRelative("entry");
                    EditorGUILayout.PropertyField(entryProp, true);
                    if (so.ApplyModifiedProperties())
                    {
                        MarkDirty();
                        graphView?.LoadGraph(graphAsset);
                    }
                }
            }));
        }

        void DrawTransitionInspector(CharacterComboGraphAsset.TransitionEdge data)
        {
            inspector.Add(new Label("Transition"));
            inspector.Add(new IMGUIContainer(() =>
            {
                EditorGUI.BeginChangeCheck();
                data.Transition.input = (CharacterComboAction.ComboInput)EditorGUILayout.EnumPopup("Input", data.Transition.input);
                data.Transition.queueUntilWindow = EditorGUILayout.Toggle("Queue Until Window", data.Transition.queueUntilWindow);
                data.Transition.transitionDelay = EditorGUILayout.FloatField("Transition Delay", data.Transition.transitionDelay);
                data.Transition.nextStepId = EditorGUILayout.TextField("Next Step", data.ToStepId);
                if (EditorGUI.EndChangeCheck())
                {
                    MarkDirty();
                    graphView?.RefreshTransitions();
                }
            }));
        }

        internal void MarkDirty()
        {
            if (graphAsset != null)
            {
                EditorUtility.SetDirty(graphAsset);
                graphAsset.ApplyToDefinition(definition);
                if (definition != null)
                {
                    EditorUtility.SetDirty(definition);
                }
            }
        }
    }

    class ComboGraphView : GraphView
    {
        readonly CharacterComboGraphWindow window;
        readonly Dictionary<string, ComboStepNode> stepNodes = new();
        CharacterComboGraphAsset graphAsset;

        public event Action<IReadOnlyList<GraphElement>> OnSelectionChanged;

        public ComboGraphView(CharacterComboGraphWindow window)
        {
            this.window = window;
            Insert(0, new GridBackground());
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            graphViewChanged = OnGraphViewChanged;
            this.onSelectionChange += selection => OnSelectionChanged?.Invoke(selection.ToList());
        }

        public void LoadGraph(CharacterComboGraphAsset asset)
        {
            graphAsset = asset;
            DeleteElements(graphElements.ToList());
            stepNodes.Clear();

            if (graphAsset == null)
            {
                return;
            }

            foreach (CharacterComboGraphAsset.StepNode step in graphAsset.Steps)
            {
                AddStepNode(step);
            }

            foreach (CharacterComboGraphAsset.EntryNode entry in graphAsset.Entries)
            {
                AddEntryNode(entry);
            }

            RefreshTransitions();
        }

        public void RefreshTransitions()
        {
            if (graphAsset == null)
            {
                return;
            }

            // Clear existing edges
            foreach (Edge edge in edges.ToList())
            {
                RemoveElement(edge);
            }

            foreach (CharacterComboGraphAsset.EntryNode entry in graphAsset.Entries)
            {
                if (string.IsNullOrEmpty(entry.Entry.stepId))
                {
                    continue;
                }

                ComboEntryNode entryNode = graphElements.OfType<ComboEntryNode>().FirstOrDefault(n => n.EntryNode == entry);
                if (entryNode == null)
                {
                    continue;
                }

                if (stepNodes.TryGetValue(entry.Entry.stepId, out ComboStepNode target))
                {
                    Edge edge = entryNode.Output.ConnectTo(target.Input);
                    AddElement(edge);
                }
            }

            foreach (CharacterComboGraphAsset.TransitionEdge transition in graphAsset.Transitions)
            {
                if (!stepNodes.TryGetValue(transition.FromStepId, out ComboStepNode from) ||
                    !stepNodes.TryGetValue(transition.ToStepId, out ComboStepNode to))
                {
                    continue;
                }

                ComboPort port = from.GetOutput(transition.Transition.input);
                if (port == null)
                {
                    continue;
                }

                ComboEdge edge = port.ConnectTo<ComboEdge>(to.Input);
                edge.EdgeData = transition;
                AddElement(edge);
            }
        }

        public void RelabelStepNode(CharacterComboGraphAsset.StepNode step)
        {
            if (stepNodes.TryGetValue(step.Id, out ComboStepNode node))
            {
                node.UpdateLabel();
            }
        }

        ComboStepNode AddStepNode(CharacterComboGraphAsset.StepNode step)
        {
            ComboStepNode node = new(step);
            node.SetPosition(new Rect(step.Position, new Vector2(300, 180)));
            AddElement(node);
            stepNodes[step.Id] = node;
            return node;
        }

        void AddEntryNode(CharacterComboGraphAsset.EntryNode entry)
        {
            ComboEntryNode node = new(entry);
            node.SetPosition(new Rect(entry.Position, new Vector2(220, 80)));
            AddElement(node);
        }

        GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (change.elementsToRemove != null && graphAsset != null)
            {
                foreach (GraphElement element in change.elementsToRemove)
                {
                    switch (element)
                    {
                        case ComboStepNode stepNode:
                            graphAsset.RemoveStep(stepNode.StepNode.Id);
                            window.MarkDirty();
                            break;
                        case ComboEntryNode entryNode:
                            graphAsset.RemoveEntry(entryNode.EntryNode);
                            window.MarkDirty();
                            break;
                        case ComboEdge edge when edge.EdgeData != null:
                            graphAsset.RemoveTransition(edge.EdgeData);
                            window.MarkDirty();
                            break;
                        case Edge edge when edge.output?.node is ComboEntryNode entry && edge.input?.node is ComboStepNode:
                            entry.EntryNode.Entry.stepId = string.Empty;
                            window.MarkDirty();
                            break;
                    }
                }
            }

            if (change.movedElements != null && graphAsset != null)
            {
                foreach (GraphElement element in change.movedElements)
                {
                    switch (element)
                    {
                        case ComboStepNode step:
                            step.StepNode.Position = step.GetPosition().position;
                            window.MarkDirty();
                            break;
                        case ComboEntryNode entry:
                            entry.EntryNode.Position = entry.GetPosition().position;
                            window.MarkDirty();
                            break;
                    }
                }
            }

            if (change.edgesToCreate != null && graphAsset != null)
            {
                foreach (Edge edge in change.edgesToCreate)
                {
                    HandleEdgeCreated(edge);
                }
            }

            return change;
        }

        void HandleEdgeCreated(Edge edge)
        {
            if (edge.output is ComboPort output && edge.input?.node is ComboStepNode target)
            {
                if (output.owner is ComboEntryNode entryNode)
                {
                    entryNode.EntryNode.Entry.stepId = target.StepNode.Id;
                    entryNode.EntryNode.Entry.input = output.Input;
                    window.MarkDirty();
                    RefreshTransitions();
                }
                else if (output.owner is ComboStepNode stepNode)
                {
                    CharacterComboAction.ComboTransition transition = new()
                    {
                        input = output.Input,
                        nextStepId = target.StepNode.Id
                    };

                    CharacterComboGraphAsset.TransitionEdge data = graphAsset.Link(stepNode.StepNode.Id, target.StepNode.Id, transition);
                    ComboEdge comboEdge = edge as ComboEdge ?? output.ConnectTo<ComboEdge>(target.Input);
                    comboEdge.EdgeData = data;
                    window.MarkDirty();
                }
            }
        }
    }

    class ComboPort : Port
    {
        public CharacterComboAction.ComboInput Input { get; }

        protected ComboPort(Orientation portOrientation, Direction portDirection, Port.Capacity capacity, Type type, CharacterComboAction.ComboInput input)
            : base(portOrientation, portDirection, capacity, type)
        {
            Input = input;
            portName = input.ToString();
        }

        public static ComboPort Create(Direction direction, CharacterComboAction.ComboInput input)
        {
            return new ComboPort(Orientation.Horizontal, direction, Port.Capacity.Multi, typeof(float), input);
        }
    }

    class ComboStepNode : Node
    {
        readonly Dictionary<CharacterComboAction.ComboInput, ComboPort> outputs = new();
        public CharacterComboGraphAsset.StepNode StepNode { get; }
        public Port Input { get; }

        public ComboStepNode(CharacterComboGraphAsset.StepNode data)
        {
            StepNode = data;
            title = string.IsNullOrWhiteSpace(data.Id) ? "Step" : data.Id;
            Input = ComboPort.Create(Direction.Input, CharacterComboAction.ComboInput.SameAsBinding);
            Input.portName = "Prev";
            inputContainer.Add(Input);

            foreach (CharacterComboAction.ComboInput input in Enum.GetValues(typeof(CharacterComboAction.ComboInput)))
            {
                if (input == CharacterComboAction.ComboInput.SameAsBinding)
                {
                    continue;
                }
                ComboPort port = ComboPort.Create(Direction.Output, input);
                outputs[input] = port;
                outputContainer.Add(port);
            }

            RefreshExpandedState();
            RefreshPorts();
        }

        public ComboPort GetOutput(CharacterComboAction.ComboInput input)
        {
            outputs.TryGetValue(input, out ComboPort port);
            return port;
        }

        public void UpdateLabel()
        {
            title = string.IsNullOrWhiteSpace(StepNode.Id) ? "Step" : StepNode.Id;
        }
    }

    class ComboEntryNode : Node
    {
        public CharacterComboGraphAsset.EntryNode EntryNode { get; }
        public ComboPort Output { get; }

        public ComboEntryNode(CharacterComboGraphAsset.EntryNode data)
        {
            EntryNode = data;
            title = "Entry";
            Output = ComboPort.Create(Direction.Output, data.Entry.input);
            outputContainer.Add(Output);
            RefreshExpandedState();
            RefreshPorts();
        }
    }

    class ComboEdge : Edge
    {
        public CharacterComboGraphAsset.TransitionEdge EdgeData { get; set; }
    }
}
#endif
