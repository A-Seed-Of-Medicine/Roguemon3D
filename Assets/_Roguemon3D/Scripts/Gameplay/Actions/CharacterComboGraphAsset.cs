using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    /// <summary>
    /// Serializable data model backing the GraphToolkit editor for combo construction. Acts as a
    /// bridge between the visual graph and the runtime friendly arrays stored on
    /// <see cref="CharacterComboDefinition"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Gameplay/Character Combo Graph", fileName = "CharacterComboGraph")]
    public class CharacterComboGraphAsset : ScriptableObject
    {
        [Serializable]
        public class StepNode
        {
            [SerializeField] string id = "step";
            [SerializeField] Vector2 position;
            [SerializeField] CharacterComboAction.ComboStep step = new();

            public string Id
            {
                get => id;
                set => id = value;
            }

            public Vector2 Position
            {
                get => position;
                set => position = value;
            }

            public CharacterComboAction.ComboStep Step => step;
        }

        [Serializable]
        public class EntryNode
        {
            [SerializeField] CharacterComboAction.ComboEntry entry = new();
            [SerializeField] Vector2 position;

            public CharacterComboAction.ComboEntry Entry => entry;
            public Vector2 Position
            {
                get => position;
                set => position = value;
            }
        }

        [Serializable]
        public class TransitionEdge
        {
            [SerializeField] string guid = Guid.NewGuid().ToString();
            [SerializeField] string fromStepId = string.Empty;
            [SerializeField] string toStepId = string.Empty;
            [SerializeField] CharacterComboAction.ComboTransition transition = new();

            public string Guid
            {
                get => guid;
                set => guid = value;
            }

            public string FromStepId
            {
                get => fromStepId;
                set => fromStepId = value;
            }

            public string ToStepId
            {
                get => toStepId;
                set => toStepId = value;
            }

            public CharacterComboAction.ComboTransition Transition => transition;
        }

        [SerializeField] bool requiresAimInput = true;
        [SerializeField, Min(0f)] float queuedInputLifetime = 0.35f;
        [SerializeField] List<StepNode> steps = new();
        [SerializeField] List<EntryNode> entries = new();
        [SerializeField] List<TransitionEdge> transitions = new();

        public bool RequiresAimInput
        {
            get => requiresAimInput;
            set => requiresAimInput = value;
        }

        public float QueuedInputLifetime
        {
            get => Mathf.Max(0f, queuedInputLifetime);
            set => queuedInputLifetime = Mathf.Max(0f, value);
        }

        public IReadOnlyList<StepNode> Steps => steps;
        public IReadOnlyList<EntryNode> Entries => entries;
        public IReadOnlyList<TransitionEdge> Transitions => transitions;

        public StepNode GetOrCreateStep(string id)
        {
            StepNode existing = steps.FirstOrDefault(s => s.Id == id);
            if (existing != null)
            {
                return existing;
            }

            StepNode node = new() { Id = id };
            steps.Add(node);
            return node;
        }

        public StepNode CreateStep(string baseId = "step")
        {
            StepNode node = new()
            {
                Id = GenerateUniqueStepId(baseId)
            };
            steps.Add(node);
            return node;
        }

        public void RemoveStep(string id)
        {
            steps.RemoveAll(s => s.Id == id);
            transitions.RemoveAll(t => t.FromStepId == id || t.ToStepId == id);
        }

        public void RenameStep(string previousId, string newId)
        {
            if (string.IsNullOrWhiteSpace(previousId) || string.IsNullOrWhiteSpace(newId))
            {
                return;
            }

            foreach (TransitionEdge edge in transitions)
            {
                if (edge.FromStepId == previousId)
                {
                    edge.FromStepId = newId;
                }

                if (edge.ToStepId == previousId)
                {
                    edge.ToStepId = newId;
                }
            }

            foreach (EntryNode entry in entries)
            {
                if (entry.Entry.stepId == previousId)
                {
                    entry.Entry.stepId = newId;
                }
            }
        }

        public EntryNode AddEntry()
        {
            EntryNode node = new();
            entries.Add(node);
            return node;
        }

        public void RemoveEntry(EntryNode node)
        {
            entries.Remove(node);
        }

        public TransitionEdge Link(string fromId, string toId, CharacterComboAction.ComboTransition transition)
        {
            TransitionEdge edge = new()
            {
                Guid = System.Guid.NewGuid().ToString(),
                FromStepId = fromId,
                ToStepId = toId
            };
            transition.nextStepId = toId;
            edge.Transition.input = transition.input;
            edge.Transition.queueUntilWindow = transition.queueUntilWindow;
            edge.Transition.transitionDelay = transition.transitionDelay;
            transitions.Add(edge);
            return edge;
        }

        string GenerateUniqueStepId(string baseId)
        {
            string sanitized = string.IsNullOrWhiteSpace(baseId) ? "step" : baseId.Trim().Replace(' ', '_');
            HashSet<string> existing = new();
            foreach (StepNode node in steps)
            {
                if (!string.IsNullOrWhiteSpace(node.Id))
                {
                    existing.Add(node.Id);
                }
            }

            string candidate = sanitized;
            int suffix = 1;
            while (existing.Contains(candidate))
            {
                candidate = $"{sanitized}_{suffix++}";
            }

            return candidate;
        }

        public void RemoveTransition(TransitionEdge edge)
        {
            transitions.Remove(edge);
        }

        public void ApplyToDefinition(CharacterComboDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            definition.EditorSetRequiresAimInput(requiresAimInput);
            definition.EditorSetQueuedInputLifetime(queuedInputLifetime);

            List<CharacterComboAction.ComboStep> orderedSteps = steps
                .Select(s => s.Step)
                .Where(s => !string.IsNullOrWhiteSpace(s.id))
                .ToList();

            foreach (CharacterComboAction.ComboStep step in orderedSteps)
            {
                step.transitions = transitions
                    .Where(t => t.FromStepId == step.id)
                    .Select(t =>
                    {
                        t.Transition.nextStepId = t.ToStepId;
                        return t.Transition;
                    })
                    .ToArray();
            }

            definition.EditorSetSteps(orderedSteps.ToArray());
            definition.EditorSetEntries(entries.Select(e => e.Entry).ToArray());
        }
    }
}
