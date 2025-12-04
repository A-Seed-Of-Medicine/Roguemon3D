using System;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    [CreateAssetMenu(menuName = "Gameplay/Character Combo Definition", fileName = "CharacterComboDefinition")]
    public class CharacterComboDefinition : ScriptableObject
    {
        [Header("Combo Graph")]
        [SerializeField] bool requiresAimInput = true;
        [SerializeField, Tooltip("How long queued input remains valid before it expires.")]
        [Min(0f)] float queuedInputLifetime = 0.35f;
        [SerializeField] ComboEntry[] entrySteps = Array.Empty<ComboEntry>();
        [SerializeField] ComboStep[] steps = Array.Empty<ComboStep>();

        public bool RequiresAimInput => requiresAimInput;
        public float QueuedInputLifetime => Mathf.Max(0f, queuedInputLifetime);
        public ComboEntry[] EntrySteps => entrySteps;
        public ComboStep[] Steps => steps;

        public void SetGraphFields(bool requireAim, float queuedLifetime, ComboEntry[] entries, ComboStep[] runtimeSteps)
        {
            requiresAimInput = requireAim;
            queuedInputLifetime = Mathf.Max(0f, queuedLifetime);
            entrySteps = entries ?? Array.Empty<ComboEntry>();
            steps = runtimeSteps ?? Array.Empty<ComboStep>();
        }
    }
}
