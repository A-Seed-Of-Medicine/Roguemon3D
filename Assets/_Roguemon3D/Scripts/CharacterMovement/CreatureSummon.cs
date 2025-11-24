using _PinBoy.Scripts.Gameplay.Creatures;
using _PinBoy.Scripts.Gameplay.Effects;
using UnityEngine;

namespace _PinBoy.Scripts.CharacterMovement
{
    public class CreatureSummon : AgentController
    {
        [field: SerializeField] public AgentController owner { get; private set; }
        [field: SerializeField] public CreatureHostData hostData { get; private set; }
        public override AllegianceType allegiance => owner ? owner.allegiance : base.allegiance;

        public void SetOwner(AgentController controller)
        {
            owner = controller;
        }
    }
}
