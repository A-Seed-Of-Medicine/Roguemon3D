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
        public event UnityAction<bool> Jump = delegate { };
        public event UnityAction<bool> PrimaryAction = delegate { };
        public event UnityAction<bool> SecondaryAction = delegate { };
        public event UnityAction<bool, Vector2> Aim = delegate { };
        public event UnityAction<bool> AimPrimary = delegate { };
        public event UnityAction<bool> AimSecondary = delegate { };
        public event UnityAction<bool> Interact = delegate { };
        public event UnityAction<bool> Sprint = delegate { };
        public AgentController controller;

        public Vector2 moveInput { get; private set; }
        private Vector2 aimInput;
        public Vector2 aimDirection => isAiming ? aimInput.normalized : moveInput.normalized;
        private bool actionsEnabled = false;
        private bool stunned;
        public bool isAiming {get; private set;}

        public bool ControlsEnabled => actionsEnabled && !stunned;

        public virtual Vector2 Direction => moveInput;

        public virtual void EnableCharacterActions(bool enabled)
        {
            actionsEnabled = enabled;
            UpdateControlState();
        }

        public void OnDisable()
        {
            EnableCharacterActions(false);
        }

        public void InvokeMove(Vector2 value)
        {
            if (!ControlsEnabled)
            {
                moveInput = Vector2.zero;
                Move.Invoke(moveInput);
                return;
            }

            moveInput = value;
            Move.Invoke(value);
        }

        public void InvokePrimary(bool pressed)
        {
            if (!ControlsEnabled)
            {
                return;
            }

            PrimaryAction.Invoke(pressed);
        }

        public void InvokeSecondary(bool pressed)
        {
            if (!ControlsEnabled)
            {
                return;
            }

            SecondaryAction.Invoke(pressed);
        }

        public void InvokeAim()
        {
            if (!ControlsEnabled)
            {
                return;
            }

            isAiming = true;
            Aim.Invoke(true, aimInput);
        }

        public void InvokeAim(Vector2 direction)
        {
            if (!ControlsEnabled)
            {
                return;
            }

            isAiming = true;
            aimInput = direction;
            Aim.Invoke(true, direction);
        }

        public void CancelAim()
        {
            isAiming = false;
            Aim.Invoke(false, aimDirection);
        }

        public void SetMoveInput(Vector2 direction)
        {
            moveInput = direction;
        }

        public void SetAimInput(Vector2 direction)
        {
            aimInput = direction;
        }

        public void InvokePrimaryAim(bool pressed)
        {
            if (!ControlsEnabled)
            {
                return;
            }

            AimPrimary.Invoke(pressed);
        }

        public void InvokeSecondaryAim(bool pressed)
        {
            if (!ControlsEnabled)
            {
                return;
            }

            AimSecondary.Invoke(pressed);
        }

        public void InvokeInteract(bool pressed)
        {
            if (!ControlsEnabled)
            {
                return;
            }

            Interact.Invoke(pressed);
        }

        public void InvokeDash(bool pressed)
        {
            if (!ControlsEnabled)
            {
                return;
            }

            Dash.Invoke(pressed);
        }

        public void InvokeJump(bool pressed)
        {
            if (!ControlsEnabled)
            {
                return;
            }

            Jump.Invoke(pressed);
        }

        public void InvokeSprint(bool pressed)
        {
            if (!ControlsEnabled)
            {
                return;
            }

            Sprint.Invoke(pressed);
        }

        public void SetStunned(bool isStunned)
        {
            if (stunned == isStunned)
            {
                return;
            }

            stunned = isStunned;
            UpdateControlState();
        }

        void UpdateControlState()
        {
            bool enable = ControlsEnabled;
            if (!enable)
            {
                moveInput = Vector2.zero;
                Move.Invoke(moveInput);
                if (isAiming)
                {
                    isAiming = false;
                    Aim.Invoke(false, Vector2.zero);
                }
            }

            EnableControls?.Invoke(enable);
        }
    }
}
