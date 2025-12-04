using System;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using _PinBoy.Scripts.Gameplay.Actions;

namespace _PinBoy.Scripts.Gameplay.Actions.Editor
{
    class ComboStepNode : Node
    {
        readonly SerializedProperty stepProperty;
        readonly SerializedProperty positionProperty;
        readonly Label transitionsLabel;

        public string StepId => stepProperty.FindPropertyRelative("id").stringValue;
        public SerializedProperty SerializedStep => stepProperty;
        public SerializedProperty TransitionsProperty => stepProperty.FindPropertyRelative("transitions");
        public Port InputPort { get; }
        public Port OutputPort { get; }

        readonly System.Action<ComboStepNode> onSelected;

        public ComboStepNode(SerializedProperty stepProperty, System.Action<ComboStepNode> onSelected)
        {
            this.stepProperty = stepProperty;
            positionProperty = stepProperty.FindPropertyRelative("graphPosition");
            this.onSelected = onSelected;

            title = string.IsNullOrWhiteSpace(StepId) ? "Step" : StepId;

            capabilities |= Capabilities.Movable | Capabilities.Selectable | Capabilities.Ascendable;
            capabilities &= ~Capabilities.Deletable;
            capabilities &= ~Capabilities.Collapsible;

            InputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(string));
            InputPort.portName = "Previous";
            inputContainer.Add(InputPort);

            OutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(string));
            OutputPort.portName = "Transitions";
            outputContainer.Add(OutputPort);

            transitionsLabel = new Label("No transitions")
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    whiteSpace = WhiteSpace.Normal,
                    unityTextAlign = TextAnchor.UpperLeft,
                    marginTop = 4,
                    marginBottom = 4
                }
            };
            extensionContainer.Add(transitionsLabel);

            RefreshExpandedState();
            RefreshPorts();
            RefreshTransitionSummary();
        }

        public override void OnSelected()
        {
            base.OnSelected();
            onSelected?.Invoke(this);
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            UpdateStoredPosition();
        }

        public void UpdateStoredPosition()
        {
            if (positionProperty == null)
            {
                return;
            }

            Vector2 position = GetPosition().position;
            positionProperty.vector2Value = position;
            positionProperty.serializedObject.ApplyModifiedProperties();
        }

        public void RefreshTransitionSummary(Func<SerializedProperty, string> labelFormatter = null)
        {
            if (transitionsLabel == null)
            {
                return;
            }

            if (TransitionsProperty == null)
            {
                transitionsLabel.text = "No transitions";
                return;
            }

            List<string> summaries = new();
            for (int i = 0; i < TransitionsProperty.arraySize; i++)
            {
                SerializedProperty transition = TransitionsProperty.GetArrayElementAtIndex(i);
                string target = transition.FindPropertyRelative("nextStepId").stringValue;
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                string label = labelFormatter != null ? labelFormatter.Invoke(transition) : transition.FindPropertyRelative("input").enumDisplayNames[transition.FindPropertyRelative("input").enumValueIndex];
                summaries.Add($"{label} → {target}");
            }

            transitionsLabel.text = summaries.Count == 0 ? "No transitions" : string.Join("\n", summaries);
        }
    }

    class EntryNode : Node
    {
        readonly SerializedProperty entryProperty;
        readonly SerializedProperty positionProperty;

        public string StepId => entryProperty.FindPropertyRelative("stepId").stringValue;
        public SerializedProperty SerializedEntry => entryProperty;
        public Port OutputPort { get; }

        readonly System.Action<EntryNode> onSelected;

        public EntryNode(SerializedProperty entryProperty, System.Action<EntryNode> onSelected)
        {
            this.entryProperty = entryProperty;
            positionProperty = entryProperty.FindPropertyRelative("graphPosition");
            this.onSelected = onSelected;

            CharacterComboAction.ComboInput input = (CharacterComboAction.ComboInput)entryProperty.FindPropertyRelative("input").enumValueIndex;
            title = $"Entry: {input}";
            capabilities |= Capabilities.Movable | Capabilities.Selectable;
            capabilities &= ~Capabilities.Deletable;

            OutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(string));
            OutputPort.portName = "Start";
            outputContainer.Add(OutputPort);

            RefreshExpandedState();
            RefreshPorts();
        }

        public override void OnSelected()
        {
            base.OnSelected();
            onSelected?.Invoke(this);
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            UpdateStoredPosition();
        }

        public void UpdateStoredPosition()
        {
            if (positionProperty == null)
            {
                return;
            }

            Vector2 position = GetPosition().position;
            positionProperty.vector2Value = position;
            positionProperty.serializedObject.ApplyModifiedProperties();
        }
    }

    class ComboTransitionEdge : Edge
    {
        readonly Label label;

        public ComboTransitionEdge(string transitionLabel)
        {
            label = new Label(transitionLabel)
            {
                pickingMode = PickingMode.Ignore
            };
            label.AddToClassList("combo-transition-label");
            Add(label);

            PlaceLabel();
        }

        public override void OnPortChanged(bool isInputPort)
        {
            base.OnPortChanged(isInputPort);
            PlaceLabel();
        }

        void PlaceLabel()
        {
            if (label == null)
            {
                return;
            }

            Vector3 from = output != null ? (Vector3)output.worldBound.center : Vector3.zero;
            Vector3 to = input != null ? (Vector3)input.worldBound.center : Vector3.zero;
            Vector3 mid = (from + to) * 0.5f;
            label.transform.position = mid;
        }
    }
}
