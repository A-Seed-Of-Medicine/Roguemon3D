using System;
using _PinBoy.Scripts.CharacterMovement;
using UnityEngine;
using UnityEngine.Events;

namespace AdvancedController
{
    [Serializable]
    public class InputReader
    {
        public event UnityAction<Vector2> Move = delegate { };
        public event UnityAction<bool> EnableControls = delegate { };
        public event UnityAction<bool> Dash = delegate { };
        public event UnityAction<bool> PrimaryAction = delegate { };
        public event UnityAction<bool> SecondaryAction = delegate { };
        public event UnityAction<bool, Vector2> Aim = delegate { };
        public event UnityAction<bool> AimPrimary = delegate { };
        public event UnityAction<bool> AimSecondary = delegate { };
        public event UnityAction<bool> Interact = delegate { };
        public event UnityAction<bool> Sprint = delegate { };
        public AgentController controller;

        public Vector2 moveInput { get; set; }
        private Vector2 aimInput;
        public Vector2 aimDirection => isAiming ? aimInput.normalized : moveInput.normalized;
        private bool actionsEnabled = false;
        public bool isAiming {get; private set;}

        public virtual Vector2 Direction => moveInput;

        public virtual void EnableCharacterActions(bool enabled)
        {
            actionsEnabled = enabled;
            if (!enabled)
            {
                moveInput = Vector2.zero;
            }
            EnableControls?.Invoke(enabled);
        }

        public void OnDisable()
        {
            EnableCharacterActions(false);
        }

        public void InvokeMove(Vector2 value)
        {
            moveInput = value;
            Move.Invoke(value);
        }
        
        public void InvokePrimary(bool pressed) => PrimaryAction.Invoke(pressed);
        public void InvokeSecondary(bool pressed) => SecondaryAction.Invoke(pressed);
        
        public void InvokeAim()
        {
            isAiming = true;
            Aim.Invoke(true, aimInput);
        }
        
        public void InvokeAim(Vector2 direction)
        {
            isAiming = true;
            aimInput = direction;
            Aim.Invoke(true, direction);
        }

        public void CancelAim()
        {
            isAiming = false;
            Aim.Invoke(false, aimDirection);
        }
        
        public void SetAimInput(Vector2 direction)
        {
            aimInput = direction;
        }

        public void InvokePrimaryAim(bool pressed) => AimPrimary.Invoke(pressed);
        public void InvokeSecondaryAim(bool pressed) => AimSecondary.Invoke(pressed);
        public void InvokeInteract(bool pressed) => Interact.Invoke(pressed);
        public void InvokeDash(bool pressed) => Dash.Invoke(pressed);
        public void InvokeSprint(bool pressed) => Sprint.Invoke(pressed);
    }
}
