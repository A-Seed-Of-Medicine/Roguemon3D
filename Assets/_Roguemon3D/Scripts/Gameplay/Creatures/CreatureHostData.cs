using _PinBoy.Scripts.Gameplay.Actions;
using _PinBoy.Scripts.CharacterMovement;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Creatures
{
    [CreateAssetMenu(menuName = "Gameplay/Creatures/Creature Host Data", fileName = "CreatureHostData")]
    public class CreatureHostData : ScriptableObject
    {
        [Header("Gameplay")]
        [field: SerializeField] public CharacterComboDefinition comboDefinition { get; private set; }
        [field: SerializeField] public CreatureSummon summonPrefab { get; private set; }
        
        
    }
}
