using System;
using _PinBoy.Scripts.Gameplay.Actions;
using Unity.GraphToolkit.Editor;

namespace _PinBoy.Scripts.Gameplay.Actions.Editor.GraphToolkit
{
    /// <summary>
    /// Base node for combo graph nodes.
    /// </summary>
    [Serializable]
    internal abstract class ComboGraphNode : Node
    {
        internal const string ExecutionPortName = "Execution";

        protected void AddExecutionPorts(IPortDefinitionContext context)
        {
            context.AddInputPort(ExecutionPortName)
                .WithDisplayName(string.Empty)
                .WithConnectorUI(PortConnectorUI.Arrowhead)
                .Build();

            context.AddOutputPort(ExecutionPortName)
                .WithDisplayName(string.Empty)
                .WithConnectorUI(PortConnectorUI.Arrowhead)
                .Build();
        }
    }

    /// <summary>
    /// Entry node for initiating a combo.
    /// </summary>
    [Serializable]
    internal class ComboEntryNode : ComboGraphNode
    {
        public ComboEntry Entry = new();

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort(ExecutionPortName)
                .WithDisplayName(Entry.input.ToString())
                .WithConnectorUI(PortConnectorUI.Arrowhead)
                .Build();
        }
    }

    /// <summary>
    /// Node describing an individual combo step.
    /// </summary>
    [Serializable]
    internal class ComboStepNode : ComboGraphNode
    {
        public ComboStep Step = new();

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort(ExecutionPortName)
                .WithDisplayName(string.Empty)
                .WithConnectorUI(PortConnectorUI.Arrowhead)
                .Build();

            if (Step.transitions == null || Step.transitions.Length == 0)
            {
                Step.transitions = new[] { new ComboTransition() };
            }

            for (int i = 0; i < Step.transitions.Length; i++)
            {
                ComboTransition transition = Step.transitions[i] ?? new ComboTransition();
                string portName = $"{ExecutionPortName}_{i}";
                context.AddOutputPort(portName)
                    .WithDisplayName(transition.input.ToString())
                    .WithConnectorUI(PortConnectorUI.Arrowhead)
                    .Build();
            }
        }
    }
}
