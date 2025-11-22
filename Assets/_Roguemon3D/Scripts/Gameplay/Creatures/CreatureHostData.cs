using _PinBoy.Scripts.Gameplay.Actions;
using _PinBoy.Scripts.CharacterMovement;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Creatures
{
    [CreateAssetMenu(menuName = "Gameplay/Creatures/Creature Host Data", fileName = "CreatureHostData")]
    public class CreatureHostData : ScriptableObject
    {
        [Header("Presentation")]
        [SerializeField] string displayName;

        [Header("Gameplay")]
        [SerializeField] CharacterComboDefinition comboDefinition;
        [SerializeField] SummonedCreature summonPrefab;

        public string DisplayName => displayName;
        public CharacterComboDefinition ComboDefinition => comboDefinition;
        public SummonedCreature SummonPrefab => summonPrefab;
    }
}
