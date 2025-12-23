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

        public string StepId => stepProperty.FindPropertyRelative("id").stringValue;
        public SerializedProperty SerializedStep => stepProperty;
        public SerializedProperty TransitionsProperty => stepProperty.FindPropertyRelative("transitions");
        public Port InputPort { get; }
        public Port OutputPort { get; }
        readonly System.Action<ComboStepNode> onSelected;
        readonly System.Action<ComboStepNode> onDuplicate;
        readonly System.Action<ComboStepNode> onDelete;
        readonly System.Action<ComboStepNode, string, string> onRenamed;

        public ComboStepNode(
            SerializedProperty stepProperty,
            System.Action<ComboStepNode> onSelected,
            System.Action<ComboStepNode> onDuplicate,
            System.Action<ComboStepNode> onDelete,
            System.Action<ComboStepNode, string, string> onRenamed)
        {
            this.stepProperty = stepProperty;
            positionProperty = stepProperty.FindPropertyRelative("graphPosition");
            this.onSelected = onSelected;
            this.onDuplicate = onDuplicate;
            this.onDelete = onDelete;
            this.onRenamed = onRenamed;

            title = string.IsNullOrWhiteSpace(StepId) ? "Step" : StepId;

            capabilities |= Capabilities.Movable | Capabilities.Selectable | Capabilities.Ascendable | Capabilities.Deletable;


            InputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(string));
            InputPort.portName = "Previous";
            inputContainer.Add(InputPort);

            OutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(string));
            OutputPort.portName = "Transitions";
            outputContainer.Add(OutputPort);

            RefreshExpandedState();
            RefreshPorts();
        }
        
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.ClearItems();
            evt.menu.AppendAction(
                "Duplicate Step",
                _ => onDuplicate?.Invoke(this));

            evt.menu.AppendAction(
                "Delete Step",
                _ => onDelete?.Invoke(this));
        }

        public override void OnSelected()
        {
            base.OnSelected();
            onSelected?.Invoke(this);
        }

        public void UpdateStoredPosition()
        {
            if (positionProperty == null)
            {
                return;
            }

            Vector2 position = new Vector2(layout.xMin, layout.yMin);
            positionProperty.vector2Value = position;
            positionProperty.serializedObject.ApplyModifiedProperties();
        }
    }

    class EntryNode : Node
    {
        readonly SerializedProperty entryProperty;
        readonly SerializedProperty positionProperty;
        SerializedProperty nextStepProp   => entryProperty.FindPropertyRelative("nextStep");
        public SerializedProperty stepIndexProp  => nextStepProp.FindPropertyRelative("stepIndex");
        public SerializedProperty SerializedEntry => entryProperty;
        public Port OutputPort { get; }

        readonly System.Action<EntryNode> onSelected;
        readonly System.Action<EntryNode> onDelete;

        public EntryNode(SerializedProperty entryProperty, System.Action<EntryNode> onSelected, System.Action<EntryNode> onDelete)
        {
            this.entryProperty = entryProperty;
            positionProperty = entryProperty.FindPropertyRelative("graphPosition");
            this.onSelected = onSelected;
            this.onDelete = onDelete;

            CharacterComboAction.ComboInput input = (CharacterComboAction.ComboInput)entryProperty.FindPropertyRelative("input").enumValueIndex;
            title = $"Entry: {input}";
            capabilities |= Capabilities.Movable | Capabilities.Selectable;

            OutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(string));
            OutputPort.portName = "Start";
            outputContainer.Add(OutputPort);

            RefreshExpandedState();
            RefreshPorts();
        }
        
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.ClearItems();
            evt.menu.AppendAction(
                "Delete Step",
                _ => onDelete?.Invoke(this));
        }

        public override void OnSelected()
        {
            base.OnSelected();
            onSelected?.Invoke(this);
        }

        public void UpdateStoredPosition()
        {
            if (positionProperty == null)
            {
                return;
            }

            Vector2 position = new Vector2(layout.xMin, layout.yMin);
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

            Vector3 from = output != null ? output.worldBound.center : Vector3.zero;
            Vector3 to = input != null ? input.worldBound.center : Vector3.zero;
            Vector3 mid = (from + to) * 0.5f;
            label.style.translate = new Translate(mid.x, mid.y, 0f);
        }
    }
}
