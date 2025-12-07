using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Actions;
using _PinBoy.Scripts.Gameplay.Creatures;
using _PinBoy.Scripts.Gameplay.Effects;
using _PinBoy.Scripts.Player.Input;
using AdvancedController;
using UnityEngine;

namespace _PinBoy.Scripts.Player
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PlayerController : AgentController
    {
        public PlayerInputReader PlayerInput;
        
        public override AllegianceType allegiance => AllegianceType.Ally;

        public override InputReader inputReader => PlayerInput.inputReader;
        

        protected override void Awake()
        {
            PlayerInput.mainCamera = Camera.main;
            base.Awake();
        }

        protected override void Update()
        {
            if (PlayerInput.inputReader.isAiming && PlayerInput.IsMouseKeyboardActive && inputReader?.controller)
            {
                Vector3 worldAimPosition = PlayerInput.GetWorldAimPosition();
                Vector3 aimDirection = worldAimPosition - inputReader.controller.AimOrigin;
                Vector2 planarAim = new(aimDirection.x, aimDirection.z);
                inputReader.InvokeAim(planarAim);
            }
            base.Update();
        }
    }
}
