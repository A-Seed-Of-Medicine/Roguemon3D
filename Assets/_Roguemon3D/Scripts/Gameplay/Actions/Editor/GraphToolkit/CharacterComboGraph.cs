using System;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor;

namespace _PinBoy.Scripts.Gameplay.Actions.Editor.GraphToolkit
{
    /// <summary>
    /// Graph Toolkit driven representation of a character combo graph.
    /// </summary>
    [Serializable]
    [Graph(AssetExtension)]
    internal class CharacterComboGraph : Graph
    {
        internal const string AssetExtension = "ccg";

        public bool requiresAimInput = true;
        public float queuedInputLifetime = 0.35f;

        [MenuItem("Assets/Create/Graph Toolkit/Character Combo Graph")]
        static void CreateAssetFile()
        {
            GraphDatabase.PromptInProjectBrowserToCreateNewAsset<CharacterComboGraph>("Character Combo Graph");
        }

        public override void OnGraphChanged(GraphLogger infos)
        {
            base.OnGraphChanged(infos);

            ValidateEntries(infos);
            ValidateSteps(infos);
        }

        void ValidateEntries(GraphLogger infos)
        {
            var entries = GetNodes().OfType<ComboEntryNode>().ToList();
            if (entries.Count == 0)
            {
                infos.LogWarning("Add at least one entry node to start the combo graph.", this);
            }

            foreach (var entry in entries.Where(e => string.IsNullOrWhiteSpace(e.Entry.stepId)))
            {
                infos.LogWarning("Entry nodes should target a combo step.", entry);
            }
        }

        void ValidateSteps(GraphLogger infos)
        {
            foreach (var step in GetNodes().OfType<ComboStepNode>())
            {
                if (string.IsNullOrWhiteSpace(step.Step.id))
                {
                    infos.LogWarning("Combo steps should define an id.", step);
                }
            }
        }
    }
}
