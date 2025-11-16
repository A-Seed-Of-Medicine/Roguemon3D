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
        [SerializeField] CharacterComboAction.ComboEntry[] entrySteps = Array.Empty<CharacterComboAction.ComboEntry>();
        [SerializeField] CharacterComboAction.ComboStep[] steps = Array.Empty<CharacterComboAction.ComboStep>();

        public bool RequiresAimInput => requiresAimInput;
        public float QueuedInputLifetime => Mathf.Max(0f, queuedInputLifetime);
        public CharacterComboAction.ComboEntry[] EntrySteps => entrySteps;
        public CharacterComboAction.ComboStep[] Steps => steps;
    }
}
