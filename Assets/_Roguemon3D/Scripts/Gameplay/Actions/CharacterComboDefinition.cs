using System;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    [CreateAssetMenu(menuName = "Gameplay/Character Combo Definition", fileName = "CharacterComboDefinition")]
    public class CharacterComboDefinition : ScriptableObject
    {
        [Header("Combo Graph")]
        [SerializeField, Tooltip("GraphToolkit driven graph asset used to author the combo tree.")]
        CharacterComboGraphAsset comboGraph;
        [SerializeField] bool requiresAimInput = true;
        [SerializeField, Tooltip("How long queued input remains valid before it expires.")]
        [Min(0f)] float queuedInputLifetime = 0.35f;
        [SerializeField] CharacterComboAction.ComboEntry[] entrySteps = Array.Empty<CharacterComboAction.ComboEntry>();
        [SerializeField] CharacterComboAction.ComboStep[] steps = Array.Empty<CharacterComboAction.ComboStep>();

        public CharacterComboGraphAsset ComboGraph => comboGraph;
        public bool RequiresAimInput => requiresAimInput;
        public float QueuedInputLifetime => Mathf.Max(0f, queuedInputLifetime);
        public CharacterComboAction.ComboEntry[] EntrySteps => entrySteps;
        public CharacterComboAction.ComboStep[] Steps => steps;

        void OnValidate()
        {
            if (comboGraph != null)
            {
                comboGraph.ApplyToDefinition(this);
            }
        }

#if UNITY_EDITOR
        internal void EditorSetGraph(CharacterComboGraphAsset graph)
        {
            comboGraph = graph;
        }

        internal void EditorSetRequiresAimInput(bool value)
        {
            requiresAimInput = value;
        }

        internal void EditorSetQueuedInputLifetime(float value)
        {
            queuedInputLifetime = Mathf.Max(0f, value);
        }

        internal void EditorSetEntries(CharacterComboAction.ComboEntry[] value)
        {
            entrySteps = value ?? Array.Empty<CharacterComboAction.ComboEntry>();
        }

        internal void EditorSetSteps(CharacterComboAction.ComboStep[] value)
        {
            steps = value ?? Array.Empty<CharacterComboAction.ComboStep>();
        }
#endif
    }
}
