using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using _PinBoy.Scripts.Gameplay.Actions;
using Codice.CM.Client.Differences;

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

            ComboStepNode node = new(
                stepProperty,
                HandleStepSelected,
                HandleStepDuplicate,
                HandleStepDelete,
                HandleStepRenamed)
            {
                userData = stepProperty
            };

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

            EntryNode node = new(entryProperty, HandleEntrySelected, HandleEntryDelete)
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

                for (int i = 0; i < transitions.arraySize; i++)
                {
                    SerializedProperty transition = transitions.GetArrayElementAtIndex(i);
                    int targetIndex = transition.FindPropertyRelative("nextStep")?.FindPropertyRelative("stepIndex")?.intValue ?? -1;
                    string targetId = ResolveStepId(targetIndex);
                    if (string.IsNullOrWhiteSpace(targetId) || !stepNodes.TryGetValue(targetId, out ComboStepNode target))
                    {
                        continue;
                    }

                    ComboTransitionEdge edge = new(FormatTransitionLabel(transition))
                    {
                        userData = transition
                    };
                    edge.output = source.OutputPort;
                    edge.input = target.InputPort;
                    AddElement(edge);
                }
            }

            foreach (EntryNode entryNode in entryNodes)
            {
                int targetId = entryNode.stepIndexProp.intValue;
                if (targetId >= 0 && stepNodes.Values.ElementAtOrDefault(targetId) is { } stepNode)
                {
                    Edge edge = entryNode.OutputPort.ConnectTo(stepNode.InputPort);
                    edge.capabilities &= ~Capabilities.Deletable;
                    AddElement(edge);
                }
            }
        }

        string ResolveStepId(int stepIndex)
        {
            if (stepsProperty == null || stepIndex < 0 || stepIndex >= stepsProperty.arraySize)
            {
                return null;
            }

            SerializedProperty stepProperty = stepsProperty.GetArrayElementAtIndex(stepIndex);
            return stepProperty.FindPropertyRelative("id")?.stringValue;
        }

        static string FormatTransitionLabel(SerializedProperty transition)
        {
            string input = ((CharacterComboAction.ComboInput)transition.FindPropertyRelative("input").enumValueIndex).ToString();
            float delay = transition.FindPropertyRelative("transitionDelay").floatValue;
            bool queued = transition.FindPropertyRelative("queueUntilWindow").boolValue;

            return queued || delay > 0f ? $"{input} {(queued ? "(queued)" : string.Empty)}{(delay > 0f ? $" +{delay:0.00}s" : string.Empty)}" : input;
        }

        GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            // New edges (ports being connected)
            if (change.edgesToCreate != null && change.edgesToCreate.Count > 0)
            {
                foreach (Edge edge in change.edgesToCreate)
                {
                    HandleEdgeConnected(edge);
                }

                // We fully rebuild edges ourselves, so prevent GraphView from creating raw edges
                change.edgesToCreate.Clear();
            }

            // Deleted elements (ports being disconnected, nodes deleted, etc.)
            if (change.elementsToRemove != null)
            {
                foreach (GraphElement element in change.elementsToRemove)
                {
                    switch (element)
                    {
                        case ComboTransitionEdge transitionEdge:
                            HandleEdgeDisconnected(transitionEdge);
                            break;
                        case ComboStepNode stepNode:
                            HandleStepDelete(stepNode);
                            break;
                        case EntryNode entryNode:
                            HandleEntryDelete(entryNode);
                            break;
                    }
                }
            }

            // Keep your existing move-handling logic
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



        void HandleStepSelected(ComboStepNode node)
        {
            StepSelected?.Invoke(node.SerializedStep);
        }

        void HandleEntrySelected(EntryNode node)
        {
            EntrySelected?.Invoke(node.SerializedEntry);
        }
        
        int FindStepIndex(SerializedProperty stepProperty)
        {
            if (stepsProperty == null) return -1;

            for (int i = 0; i < stepsProperty.arraySize; i++)
            {
                SerializedProperty candidate = stepsProperty.GetArrayElementAtIndex(i);
                if (candidate.propertyPath == stepProperty.propertyPath)
                {
                    return i;
                }
            }

            return -1;
        }

        string GetUniqueStepId(string baseId)
        {
            if (string.IsNullOrEmpty(baseId))
                baseId = "Step";

            HashSet<string> used = new();
            if (stepsProperty != null)
            {
                for (int i = 0; i < stepsProperty.arraySize; i++)
                {
                    var p = stepsProperty.GetArrayElementAtIndex(i).FindPropertyRelative("id");
                    used.Add(p.stringValue);
                }
            }

            string candidate = baseId;
            int counter = 1;
            while (used.Contains(candidate))
            {
                candidate = $"{baseId}_{counter++}";
            }

            return candidate;
        }
        
        void HandleStepDuplicate(ComboStepNode node)
        {
            if (serializedDefinition == null || stepsProperty == null)
                return;

            serializedDefinition.Update();

            SerializedProperty sourceStep = node.SerializedStep;

            // Add new element to the steps array
            int newIndex = stepsProperty.arraySize;
            stepsProperty.InsertArrayElementAtIndex(newIndex);
            SerializedProperty newStep = stepsProperty.GetArrayElementAtIndex(newIndex);

            // Copy all fields from source
            //newStep.CopyFromSerializedProperty(sourceStep); TODO: Duplicate step method

            // Give it a unique id
            SerializedProperty idProp = newStep.FindPropertyRelative("id");
            idProp.stringValue = GetUniqueStepId(idProp.stringValue);

            // Offset position so it doesn't overlap the original
            SerializedProperty posProp = newStep.FindPropertyRelative("graphPosition");
            Vector2 newPos = node.GetPosition().position + new Vector2(40f, 40f);
            posProp.vector2Value = newPos;

            serializedDefinition.ApplyModifiedProperties();

            // Rebuild graph so the new node appears and edges are rebuilt
            RefreshGraph();
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compatible = new List<Port>();

            ports.ForEach(port =>
            {
                // Same port: never connect
                if (port == startPort)
                    return;

                // Do not connect ports on the same node
                if (port.node == startPort.node)
                    return;

                // Only opposite directions can connect
                if (port.direction == startPort.direction)
                    return;

                // Port types must match (you are using typeof(string) everywhere)
                if (port.portType != startPort.portType)
                    return;

                // Optional: restrict to only the combos you actually want.
                // Entry output -> Step input, Step output -> Step input, etc.
                bool startIsEntry = startPort.node is EntryNode;
                bool startIsStep  = startPort.node is ComboStepNode;
                bool endIsEntry   = port.node     is EntryNode;
                bool endIsStep    = port.node     is ComboStepNode;

                // Disallow Entry <-> Entry and Step input <-> Entry input, etc.
                if (!(startIsEntry && endIsStep) &&
                    !(startIsStep  && endIsStep))
                {
                    return;
                }

                compatible.Add(port);
            });

            return compatible;
        }


        void HandleStepDelete(ComboStepNode node)
        {
            if (serializedDefinition == null || stepsProperty == null)
                return;

            serializedDefinition.Update();

            SerializedProperty stepProp = node.SerializedStep;
            int removedIndex = FindStepIndex(stepProp);
            if (removedIndex < 0)
                return;

            // 1. Remove transitions that point to this step and fix indices above it
            for (int i = 0; i < stepsProperty.arraySize; i++)
            {
                SerializedProperty step = stepsProperty.GetArrayElementAtIndex(i);
                SerializedProperty transitions = step.FindPropertyRelative("transitions");
                if (transitions == null) continue;

                for (int t = transitions.arraySize - 1; t >= 0; t--)
                {
                    SerializedProperty transition = transitions.GetArrayElementAtIndex(t);
                    SerializedProperty nextStep = transition.FindPropertyRelative("nextStep");
                    if (nextStep == null) continue;

                    SerializedProperty stepIndexProp = nextStep.FindPropertyRelative("stepIndex");
                    int idx = stepIndexProp != null ? stepIndexProp.intValue : -1;
                    if (idx == removedIndex)
                    {
                        // Remove this transition entirely
                        transitions.DeleteArrayElementAtIndex(t);
                    }
                    else if (idx > removedIndex)
                    {
                        // Shift indices down because we are removing a step before it
                        stepIndexProp.intValue = idx - 1;
                    }
                }
            }
            
            // 2. Remove entry steps pointing at this stepId
            if (entryStepsProperty != null)
            {
                for (int i = entryStepsProperty.arraySize - 1; i >= 0; i--)
                {
                    SerializedProperty entry = entryStepsProperty.GetArrayElementAtIndex(i);
                    SerializedProperty nextStepProp = entry.FindPropertyRelative("nextStep");
                    SerializedProperty stepIndexProp = nextStepProp.FindPropertyRelative("stepIndex");
                    if (stepIndexProp.intValue == removedIndex)
                        stepIndexProp.intValue = -1;
                }
            }

            // 3. Remove the step itself
            stepsProperty.DeleteArrayElementAtIndex(removedIndex);

            serializedDefinition.ApplyModifiedProperties();
            RefreshGraph();
        }
        
         void HandleEntryDelete(EntryNode node)
        {
            if (serializedDefinition == null || stepsProperty == null)
                return;

            serializedDefinition.Update();

            SerializedProperty entryProp = node.SerializedEntry;
            // 2. Remove entry steps
            for (int i = entryStepsProperty.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty entry = entryStepsProperty.GetArrayElementAtIndex(i);
                if (entry.propertyPath == entryProp.propertyPath)
                {
                    entryStepsProperty.DeleteArrayElementAtIndex(i);
                    break;
                }
            }

            serializedDefinition.ApplyModifiedProperties();
            RefreshGraph();
        }

        void HandleStepRenamed(ComboStepNode node, string oldId, string newId)
        {
            if (entryStepsProperty == null || string.IsNullOrEmpty(oldId))
                return;

            serializedDefinition.Update();

            for (int i = 0; i < entryStepsProperty.arraySize; i++)
            {
                SerializedProperty entry = entryStepsProperty.GetArrayElementAtIndex(i);
                SerializedProperty stepIdProp = entry.FindPropertyRelative("stepIndexProp");
                if (stepIdProp.stringValue == oldId)
                {
                    stepIdProp.stringValue = newId;
                }
            }

            serializedDefinition.ApplyModifiedProperties();

            // Rebuild connections to reflect changed IDs
            RefreshGraph();
        }

        void DeleteTransition(ComboTransitionEdge edge)
        {
            if (edge.userData is not SerializedProperty transitionProperty)
                return;

            SerializedProperty transitionsArray = transitionProperty.serializedObject.FindProperty(transitionProperty.propertyPath.Substring(0, transitionProperty.propertyPath.LastIndexOf(".Array")));
            if (transitionsArray == null)
                return;

            // Find index
            for (int i = transitionsArray.arraySize - 1; i >= 0; i--)
            {
                if (transitionsArray.GetArrayElementAtIndex(i).propertyPath == transitionProperty.propertyPath)
                {
                    transitionsArray.DeleteArrayElementAtIndex(i);
                    transitionProperty.serializedObject.ApplyModifiedProperties();
                    break;
                }
            }
        }
        
        void HandleEdgeConnected(Edge edge)
        {
            if (serializedDefinition == null || stepsProperty == null)
                return;

            ComboStepNode targetNode = edge.input?.node as ComboStepNode;
            int targetIndex = FindStepIndex(targetNode.SerializedStep);
            if (edge.output?.node is ComboStepNode stepNode)
            {
                SerializedProperty transitions = stepNode.TransitionsProperty;
                SerializedProperty sourceProperty = stepNode.SerializedStep;

                

                serializedDefinition.Update();

                if (transitions == null)
                    return;

                int sourceIndex = FindStepIndex(sourceProperty);
                if (sourceIndex < 0 || targetIndex < 0)
                    return;

                // Optional: avoid duplicate transitions to the same step
                for (int i = 0; i < transitions.arraySize; i++)
                {
                    SerializedProperty t = transitions.GetArrayElementAtIndex(i);
                    int existingTarget = t.FindPropertyRelative("nextStep")
                        ?.FindPropertyRelative("stepIndex")
                        ?.intValue ?? -1;
                    if (existingTarget == targetIndex)
                    {
                        return;
                    }
                }

                int newIndex = transitions.arraySize;
                transitions.InsertArrayElementAtIndex(newIndex);
                SerializedProperty newTransition = transitions.GetArrayElementAtIndex(newIndex);

                // Set the next step index
                SerializedProperty nextStep = newTransition.FindPropertyRelative("nextStep");
                if (nextStep != null)
                {
                    SerializedProperty stepIndexProp = nextStep.FindPropertyRelative("stepIndex");
                    if (stepIndexProp != null)
                    {
                        stepIndexProp.intValue = targetIndex;
                    }
                }
            }
            else if (edge.output?.node is EntryNode entryNode)
            {
                entryNode.stepIndexProp.intValue = targetIndex;
            }

            serializedDefinition.ApplyModifiedProperties();

            RefreshGraph();
        }
        
        void HandleEdgeDisconnected(ComboTransitionEdge edge)
        {
            if (serializedDefinition == null || stepsProperty == null)
                return;

            if (edge.userData is not SerializedProperty transitionProp)
                return;

            serializedDefinition.Update();

            // Find and delete that transition from whichever step owns it
            for (int i = 0; i < stepsProperty.arraySize; i++)
            {
                SerializedProperty step = stepsProperty.GetArrayElementAtIndex(i);
                SerializedProperty transitions = step.FindPropertyRelative("transitions");
                if (transitions == null)
                    continue;

                for (int t = transitions.arraySize - 1; t >= 0; t--)
                {
                    SerializedProperty candidate = transitions.GetArrayElementAtIndex(t);
                    if (candidate.propertyPath == transitionProp.propertyPath)
                    {
                        transitions.DeleteArrayElementAtIndex(t);
                        serializedDefinition.ApplyModifiedProperties();
                        RefreshGraph();
                        return;
                    }
                }
            }
        }
    }
}
