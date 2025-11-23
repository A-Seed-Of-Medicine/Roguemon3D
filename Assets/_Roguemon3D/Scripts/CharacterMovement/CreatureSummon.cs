using _PinBoy.Scripts.Gameplay.Creatures;
using _PinBoy.Scripts.Gameplay.Effects;
using _PinBoy.Scripts.Player;
using UnityEngine;

namespace _PinBoy.Scripts.CharacterMovement
{
    public class CreatureSummon : AgentController
    {
        [field: SerializeField] public PlayerController owner { get; private set; }
        [field: SerializeField] public CreatureHostData hostData { get; private set; }
        public override AllegianceType allegiance => owner ? owner.allegiance : base.allegiance;

        public void SetOwner(PlayerController controller)
        {
            owner = controller;
        }
    }
}
