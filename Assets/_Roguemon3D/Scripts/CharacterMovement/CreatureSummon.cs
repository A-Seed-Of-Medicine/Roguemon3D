using _PinBoy.Scripts.Gameplay.Creatures;
using _PinBoy.Scripts.Gameplay.Effects;
using _PinBoy.Scripts.Player;
using UnityEngine;

namespace _PinBoy.Scripts.CharacterMovement
{
    public class CreatureSummon : AgentController
    {
        public PlayerController owner;
        public CreatureHostData hostData;
        public override AllegianceType allegiance => owner ? owner.allegiance : base.allegiance;

        public void SetOwner(PlayerController controller)
        {
            owner = controller;
            allegiance = controller ? controller.allegiance : allegiance;
        }
    }
}
