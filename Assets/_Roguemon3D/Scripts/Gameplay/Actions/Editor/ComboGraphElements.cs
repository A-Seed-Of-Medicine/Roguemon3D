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
        readonly SerializedProperty transitionsProperty;
        readonly System.Collections.Generic.List<Port> transitionPorts = new();

        public string StepId
        {
            get
            {
                string id = stepProperty.FindPropertyRelative("id").stringValue;
                return string.IsNullOrWhiteSpace(id) ? stepProperty.propertyPath : id;
            }
        }
        public SerializedProperty SerializedStep => stepProperty;
        public SerializedProperty TransitionsProperty => transitionsProperty;
        public Port InputPort { get; }
        public System.Collections.Generic.IReadOnlyList<Port> TransitionPorts => transitionPorts;

        readonly System.Action<ComboStepNode> onSelected;

        public ComboStepNode(SerializedProperty stepProperty, System.Action<ComboStepNode> onSelected)
        {
            this.stepProperty = stepProperty;
            positionProperty = stepProperty.FindPropertyRelative("graphPosition");
            transitionsProperty = stepProperty.FindPropertyRelative("transitions");
            this.onSelected = onSelected;

            title = string.IsNullOrWhiteSpace(StepId) ? "Step" : StepId;

            capabilities |= Capabilities.Movable | Capabilities.Selectable | Capabilities.Ascendable;
            capabilities &= ~Capabilities.Deletable;
            capabilities &= ~Capabilities.Collapsible;
            titleButtonContainer?.Clear();

            InputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(string));
            InputPort.portName = "Previous";
            inputContainer.Add(InputPort);

            RebuildTransitionPorts(null);

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

            Rect rect = GetPosition();
            Vector2 position = rect.position;
            positionProperty.vector2Value = position;
            positionProperty.serializedObject.ApplyModifiedProperties();
        }

        public void RebuildTransitionPorts(System.Func<SerializedProperty, string> labelFormatter)
        {
            foreach (Port port in transitionPorts)
            {
                outputContainer.Remove(port);
            }
            transitionPorts.Clear();

            if (transitionsProperty == null)
            {
                RefreshExpandedState();
                RefreshPorts();
                return;
            }

            for (int i = 0; i < transitionsProperty.arraySize; i++)
            {
                SerializedProperty transition = transitionsProperty.GetArrayElementAtIndex(i);
                Port port = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(string));
                port.portName = labelFormatter != null ? labelFormatter(transition) : transition.displayName;
                port.userData = i;
                outputContainer.Add(port);
                transitionPorts.Add(port);
            }

            RefreshExpandedState();
            RefreshPorts();
        }

        public SerializedProperty GetTransitionPropertyForPort(Port port)
        {
            if (transitionsProperty == null)
            {
                return null;
            }

            int index = transitionPorts.IndexOf(port);
            if (index < 0 || index >= transitionsProperty.arraySize)
            {
                return null;
            }

            return transitionsProperty.GetArrayElementAtIndex(index);
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
            OutputPort.userData = entryProperty;
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

            Rect rect = GetPosition();
            Vector2 position = rect.position;
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
