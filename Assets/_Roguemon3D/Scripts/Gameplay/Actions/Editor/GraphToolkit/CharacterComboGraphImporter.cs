using System;
using System.Collections.Generic;
using System.Linq;
using _PinBoy.Scripts.Gameplay.Actions;
using Unity.GraphToolkit.Editor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Actions.Editor.GraphToolkit
{
    /// <summary>
    /// Scripted importer that translates a combo graph asset into a runtime CharacterComboDefinition.
    /// </summary>
    [ScriptedImporter(1, CharacterComboGraph.AssetExtension)]
    internal class CharacterComboGraphImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            CharacterComboGraph graph = GraphDatabase.LoadGraphForImporter<CharacterComboGraph>(ctx.assetPath);
            if (graph == null)
            {
                Debug.LogError($"Failed to load CharacterComboGraph from {ctx.assetPath}");
                return;
            }

            CharacterComboDefinition definition = ScriptableObject.CreateInstance<CharacterComboDefinition>();
            BuildRuntimeDefinition(graph, definition);

            ctx.AddObjectToAsset("RuntimeCombo", definition);
            ctx.SetMainObject(definition);
        }

        static void BuildRuntimeDefinition(CharacterComboGraph graph, CharacterComboDefinition runtime)
        {
            List<ComboEntry> entries = new();
            Dictionary<ComboEntryNode, ComboEntry> entryLookup = new();
            List<ComboStep> steps = new();
            Dictionary<ComboStepNode, ComboStep> stepLookup = new();

            foreach (ComboEntryNode entryNode in graph.GetNodes().OfType<ComboEntryNode>())
            {
                ComboEntry clone = Clone(entryNode.Entry);
                entryLookup[entryNode] = clone;
                entries.Add(clone);
            }

            foreach (ComboStepNode stepNode in graph.GetNodes().OfType<ComboStepNode>())
            {
                ComboStep cloned = Clone(stepNode.Step);
                stepLookup[stepNode] = cloned;
                steps.Add(cloned);
            }

            foreach (ComboStepNode stepNode in stepLookup.Keys)
            {
                ComboStep runtimeStep = stepLookup[stepNode];
                for (int i = 0; i < runtimeStep.transitions.Length; i++)
                {
                    string portName = $"{ComboGraphNode.ExecutionPortName}_{i}";
                    IPort port = stepNode.GetOutputPortByName(portName);
                    IPort connected = port?.firstConnectedPort;
                    ComboStepNode targetNode = connected?.GetNode() as ComboStepNode;
                    if (targetNode != null && stepLookup.TryGetValue(targetNode, out ComboStep targetStep))
                    {
                        runtimeStep.transitions[i].nextStepId = targetStep.id;
                    }
                }
            }

            foreach (var pair in entryLookup)
            {
                ComboEntry runtimeEntry = pair.Value;
                IPort connected = pair.Key.GetOutputPortByName(ComboGraphNode.ExecutionPortName)?.firstConnectedPort;
                ComboStepNode targetNode = connected?.GetNode() as ComboStepNode;
                if (targetNode != null && stepLookup.TryGetValue(targetNode, out ComboStep targetStep))
                {
                    runtimeEntry.stepId = targetStep.id;
                }
            }

            runtime.SetGraphFields(graph.requiresAimInput, graph.queuedInputLifetime, entries.ToArray(), steps.ToArray());
        }

        static T Clone<T>(T source)
        {
            if (source == null)
            {
                return default;
            }

            return JsonUtility.FromJson<T>(JsonUtility.ToJson(source));
        }
    }
}
