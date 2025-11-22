using _PinBoy.Scripts.Player;
using UnityEngine;

namespace _PinBoy.Scripts.CharacterMovement
{
    public class SummonedCreature : AgentController
    {
        [SerializeField] PlayerController owner;

        public PlayerController Owner => owner;

        public void SetOwner(PlayerController controller)
        {
            owner = controller;
            allegiance = controller ? controller.allegiance : allegiance;
        }
    }
}
