using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using _PinBoy.Scripts.Gameplay.Actions;

namespace _PinBoy.Scripts.Gameplay.Actions.Editor
{
    /// <summary>
    /// GraphToolkit driven view that visualizes CharacterComboDefinition data as
    /// a navigable node graph.
    /// </summary>
    class CharacterComboGraphView : GraphView
    {
        const float DefaultNodeWidth = 260f;
        const float DefaultNodeHeight = 140f;
        const float HorizontalSpacing = 320f;
        const float VerticalSpacing = 200f;

        GridBackground grid;
        SerializedObject serializedDefinition;
        SerializedProperty stepsProperty;
        SerializedProperty entryStepsProperty;

        readonly Dictionary<string, ComboStepNode> stepNodes = new();
        readonly List<EntryNode> entryNodes = new();

        public event Action<SerializedProperty> StepSelected;
        public event Action<SerializedProperty> EntrySelected;
        public event Action NothingSelected;

        public CharacterComboGraphView()
        {
            style.flexGrow = 1f;
            SetupZoom(0.1f, 2f);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            graphViewChanged = OnGraphViewChanged;

            RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.target == this || evt.target == contentViewContainer || evt.target is GridBackground)
                {
                    NothingSelected?.Invoke();
                }
            });
        }

        public void SetDefinition(SerializedObject serializedObject)
        {
            serializedDefinition = serializedObject;
            stepsProperty = serializedDefinition?.FindProperty("steps");
            entryStepsProperty = serializedDefinition?.FindProperty("entrySteps");
            RefreshGraph();
        }

        public void RefreshGraph()
        {
            foreach (GraphElement element in graphElements.ToList())
            {
                //if (element != grid)
                RemoveElement(element);
            }
            stepNodes.Clear();
            entryNodes.Clear();

            if (serializedDefinition == null)
            {
                return;
            }

            serializedDefinition.UpdateIfRequiredOrScript();

            if (stepsProperty != null)
            {
                for (int i = 0; i < stepsProperty.arraySize; i++)
                {
                    SerializedProperty stepProperty = stepsProperty.GetArrayElementAtIndex(i);
                    ComboStepNode node = CreateStepNode(stepProperty, i);
                    AddElement(node);
                    stepNodes[node.StepId] = node;
                }
            }

            if (entryStepsProperty != null)
            {
                for (int i = 0; i < entryStepsProperty.arraySize; i++)
                {
                    SerializedProperty entry = entryStepsProperty.GetArrayElementAtIndex(i);
                    EntryNode node = CreateEntryNode(entry, i);
                    AddElement(node);
                    entryNodes.Add(node);
                }
            }

            BuildConnections();
            NothingSelected?.Invoke();
        }

        ComboStepNode CreateStepNode(SerializedProperty stepProperty, int index)
        {
            Vector2 position = stepProperty.FindPropertyRelative("graphPosition")?.vector2Value ?? Vector2.zero;
            if (position == Vector2.zero)
            {
                int row = index / 4;
                int column = index % 4;
                position = new Vector2(column * HorizontalSpacing + 400f, row * VerticalSpacing + 80f);
                stepProperty.FindPropertyRelative("graphPosition").vector2Value = position;
            }

            ComboStepNode node = new(stepProperty, HandleStepSelected)
            {
                userData = stepProperty
            };
            node.RebuildTransitionPorts(transition => FormatTransitionLabel(transition,
                transition.FindPropertyRelative("nextStepId")?.stringValue));
            node.SetPosition(new Rect(position, new Vector2(DefaultNodeWidth, DefaultNodeHeight)));
            return node;
        }

        EntryNode CreateEntryNode(SerializedProperty entryProperty, int index)
        {
            Vector2 position = entryProperty.FindPropertyRelative("graphPosition")?.vector2Value ?? Vector2.zero;
            if (position == Vector2.zero)
            {
                position = new Vector2(60f, index * VerticalSpacing + 120f);
                entryProperty.FindPropertyRelative("graphPosition").vector2Value = position;
            }

            EntryNode node = new(entryProperty, HandleEntrySelected)
            {
                userData = entryProperty
            };
            node.SetPosition(new Rect(position, new Vector2(220f, 80f)));
            return node;
        }

        void BuildConnections()
        {
            foreach (ComboStepNode source in stepNodes.Values)
            {
                SerializedProperty transitions = source.TransitionsProperty;
                if (transitions == null)
                {
                    continue;
                }

                source.RebuildTransitionPorts(transition => FormatTransitionLabel(transition,
                    transition.FindPropertyRelative("nextStepId")?.stringValue));

                for (int i = 0; i < transitions.arraySize; i++)
                {
                    if (i >= source.TransitionPorts.Count)
                    {
                        continue;
                    }

                    Port outputPort = source.TransitionPorts[i];
                    SerializedProperty transition = transitions.GetArrayElementAtIndex(i);
                    string targetId = transition.FindPropertyRelative("nextStepId").stringValue;
                    if (string.IsNullOrWhiteSpace(targetId) || !stepNodes.TryGetValue(targetId, out ComboStepNode target))
                    {
                        continue;
                    }

                    ComboTransitionEdge edge = new(FormatTransitionLabel(transition, targetId))
                    {
                        userData = transition
                    };
                    edge.output = outputPort;
                    edge.input = target.InputPort;
                    AddElement(edge);
                }
            }

            foreach (EntryNode entryNode in entryNodes)
            {
                string targetId = entryNode.StepId;
                if (!string.IsNullOrWhiteSpace(targetId) && stepNodes.TryGetValue(targetId, out ComboStepNode stepNode))
                {
                    Edge edge = entryNode.OutputPort.ConnectTo(stepNode.InputPort);
                    edge.userData = entryNode.SerializedEntry;
                    edge.capabilities &= ~Capabilities.Deletable;
                    AddElement(edge);
                }
            }
        }

        static string FormatTransitionLabel(SerializedProperty transition, string targetId = null)
        {
            string input = ((CharacterComboAction.ComboInput)transition.FindPropertyRelative("input").enumValueIndex).ToString();
            float delay = transition.FindPropertyRelative("transitionDelay").floatValue;
            bool queued = transition.FindPropertyRelative("queueUntilWindow").boolValue;

            string baseLabel = queued || delay > 0f
                ? $"{input} {(queued ? "(queued)" : string.Empty)}{(delay > 0f ? $" +{delay:0.00}s" : string.Empty)}"
                : input;

            if (string.IsNullOrWhiteSpace(targetId))
            {
                return baseLabel;
            }

            return $"{baseLabel} -> {targetId}";
        }

        GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (change.edgesToCreate != null)
            {
                foreach (Edge edge in change.edgesToCreate)
                {
                    HandleEdgeConnection(edge);
                }
            }

            if (change.elementsToRemove != null)
            {
                foreach (GraphElement element in change.elementsToRemove)
                {
                    if (element is Edge edge)
                    {
                        HandleEdgeRemoval(edge);
                    }
                }
            }

            if (change.movedElements != null)
            {
                foreach (GraphElement element in change.movedElements)
                {
                    switch (element)
                    {
                        case ComboStepNode stepNode:
                            stepNode.UpdateStoredPosition();
                            break;
                        case EntryNode entryNode:
                            entryNode.UpdateStoredPosition();
                            break;
                    }
                }
            }

            serializedDefinition?.ApplyModifiedProperties();
            return change;
        }

        void HandleEdgeConnection(Edge edge)
        {
            if (edge == null)
            {
                return;
            }

            switch (edge.output?.node)
            {
                case ComboStepNode source when edge.input?.node is ComboStepNode target:
                    SerializedProperty transition = source.GetTransitionPropertyForPort(edge.output);
                    if (transition != null)
                    {
                        transition.FindPropertyRelative("nextStepId").stringValue = target.StepId;
                        edge.userData = transition;
                        edge.output.portName = FormatTransitionLabel(transition, target.StepId);
                        transition.serializedObject.ApplyModifiedProperties();
                    }
                    break;
                case EntryNode entry when edge.input?.node is ComboStepNode targetStep:
                    SerializedProperty entryProp = entry.SerializedEntry;
                    entryProp.FindPropertyRelative("stepId").stringValue = targetStep.StepId;
                    edge.userData = entryProp;
                    entryProp.serializedObject.ApplyModifiedProperties();
                    break;
            }
        }

        void HandleEdgeRemoval(Edge edge)
        {
            if (edge?.userData is SerializedProperty property)
            {
                if (property.propertyPath.Contains("transitions"))
                {
                    property.FindPropertyRelative("nextStepId").stringValue = string.Empty;
                    edge.output.portName = FormatTransitionLabel(property);
                }
                else if (property.propertyPath.Contains("entrySteps"))
                {
                    property.FindPropertyRelative("stepId").stringValue = string.Empty;
                }

                property.serializedObject.ApplyModifiedProperties();
            }
        }

        void HandleStepSelected(ComboStepNode node)
        {
            StepSelected?.Invoke(node.SerializedStep);
        }

        void HandleEntrySelected(EntryNode node)
        {
            EntrySelected?.Invoke(node.SerializedEntry);
        }
    }
}
