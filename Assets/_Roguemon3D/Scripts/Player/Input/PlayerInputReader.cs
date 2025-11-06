using AdvancedController;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace _PinBoy.Scripts.Player.Input
{
    [CreateAssetMenu(menuName = "PinBoy/Input/Player Input Reader", fileName = "PlayerInputReader")]
    public class PlayerInputReader : ScriptableObject, InputSystem_Actions.IPlayerActions
    {
        public float deadzone = 0.1f;
        public InputReader inputReader = new InputReader();
        public Camera mainCamera;
        InputSystem_Actions actions;
        InputSystem_Actions.PlayerActions playerActions;
        private bool callbacksRegistered;
        public InputDevice lastUsedDevice { get; private set; }
        
        public bool IsMouseKeyboardActive => lastUsedDevice is Mouse or Keyboard;

        void OnEnable()
        {
            EnsureActions();
            RegisterCallbacks();
            EnableCharacterActions(true);
        }

        protected void OnDisable()
        {
            inputReader.OnDisable();
            if (actions != null)
            {
                if (callbacksRegistered)
                {
                    playerActions.RemoveCallbacks(this);
                    callbacksRegistered = false;
                }

                playerActions.Disable();
                //actions.Dispose();
               // actions = null;
            }
        }

        public void EnableCharacterActions(bool enabled)
        {
            if (enabled)
            {
                EnsureActions();
                playerActions.Enable();
            }
            else
            {
                if (actions != null)
                {
                    if (callbacksRegistered)
                    {
                        playerActions.RemoveCallbacks(this);
                        callbacksRegistered = false;
                    }

                    playerActions.Disable();
                    actions.Dispose();
                    actions = null;
                }
            }
        }

        void EnsureActions()
        {
            if (actions != null)
            {
                return;
            }

            actions = new InputSystem_Actions();
            playerActions = actions.Player;
        }

        void RegisterCallbacks()
        {
            if (callbacksRegistered || actions == null)
            {
                return;
            }

            playerActions.AddCallbacks(this);
            InputSystem.onActionChange += OnInputActionChange;
            callbacksRegistered = true;
        }
        
        public void OnInputActionChange(object obj, InputActionChange change)
        {
            if (obj is not InputAction action) return;
            if (action.activeControl?.device != null)
                lastUsedDevice = action.activeControl.device;
        }

        public void OnMove(InputAction.CallbackContext context)
        {
            Vector2 move = context.ReadValue<Vector2>();
            if (move.magnitude < deadzone)
            {
                move = Vector2.zero;
            }
            if (inputReader.isAiming)
            {
                inputReader.moveInput = move;
                inputReader.InvokeAim(move);
            }
            else
            {
                inputReader.SetAimInput(move);
                inputReader.InvokeMove(move);
            }
        }

        public void OnLook(InputAction.CallbackContext context)
        {
            return;
            if (!inputReader?.controller)
                return;
            if (context.control is { device: Mouse or Keyboard })
                return;

            Vector2 inputDirection = context.ReadValue<Vector2>();
            Vector3 worldAimPosition = inputReader.controller.GetAimPosition(new Vector3(inputDirection.x, 0f, inputDirection.y));
            Vector3 aimDirection = worldAimPosition - inputReader.controller.AimOrigin;
            Vector2 planarAim = new Vector2(aimDirection.x, aimDirection.z);

            if (context.started || context.performed)
            {
                inputReader.InvokeAim(planarAim);
            }
            else if (context.canceled)
            {
                inputReader.InvokeAim(planarAim);
            }
        }

        public void OnPrimaryAction(InputAction.CallbackContext context)
        {
            if (context.performed)
            {
                InvokePrimaryAction(true);
            }
            else if (context.canceled)
            {
                InvokePrimaryAction(false);
            }
        }

        public void OnSecondaryAction(InputAction.CallbackContext context)
        {
            if (context.performed)
            {
                InvokeSecondaryAction(true);
            }
            else if (context.canceled)
            {
                InvokeSecondaryAction(false);
            }
        }

        public void OnPrimaryAimAction(InputAction.CallbackContext context)
        {
            if (context.performed)
            {
                InvokePrimaryAim(true);
            }
            else if (context.canceled)
            {
                InvokePrimaryAim(false);
            }
        }

        public void OnSecondaryAimAction(InputAction.CallbackContext context)
        {
            if (context.performed)
            {
                InvokeSecondaryAim(true);
            }
            else if (context.canceled)
            {
                InvokeSecondaryAim(false);
            }
        }


        public void OnInteract(InputAction.CallbackContext context)
        {
            if (context.performed)
            {
                InvokeInteract(true);
            }
            else if (context.canceled)
            {
                InvokeInteract(false);
            }
        }

        public void OnDash(InputAction.CallbackContext context)
        {
            if (context.performed)
            {
                InvokeDash(true);
            }
            else if (context.canceled)
            {
                InvokeDash(false);
            }
        }

        public void OnSprint(InputAction.CallbackContext context)
        {
            float value = context.ReadValue<float>();
            if (!context.performed && !context.canceled)
                return;
            bool pressed = value > 0.5f || context.performed;
            if (context.canceled)
            {
                pressed = false;
            }

            InvokeSprint(pressed);
        }
        
        public void InvokePrimaryAction(bool pressed) => inputReader.InvokePrimary(pressed);

        public void InvokeSecondaryAction(bool pressed) => inputReader.InvokeSecondary(pressed);
        public void InvokeAim() => inputReader.InvokeAim();
        
        public void InvokeAim(Vector2 direction) => inputReader.InvokeAim(direction);
        public void InvokePrimaryAim(bool pressed) => inputReader.InvokePrimaryAim(pressed);
        public void InvokeSecondaryAim(bool pressed) => inputReader.InvokeSecondaryAim(pressed);
        public void InvokeDash(bool pressed) => inputReader.InvokeDash(pressed);

        public void InvokeInteract(bool pressed) => inputReader.InvokeInteract(pressed);
        
        public void InvokeSprint(bool pressed)
        {
            if (pressed)
            {
                inputReader.InvokeSprint(pressed);
                InvokeAim();
            }
            else
            {
                inputReader.moveInput = inputReader.aimDirection;
                inputReader.CancelAim();
                inputReader.InvokeSprint(pressed);
            }
        }


        public Vector3 GetWorldAimPosition()
        {
            if (!inputReader?.controller)
            {
                return Vector3.zero;
            }

            Vector3 origin = inputReader.controller.AimOrigin;

            if (!mainCamera)
            {
                Vector2 aimDir = inputReader.aimDirection;
                return origin + new Vector3(aimDir.x, 0f, aimDir.y);
            }

            if (Mouse.current != null)
            {
                Vector3 screenPosition = Mouse.current.position.ReadValue();
                Ray ray = mainCamera.ScreenPointToRay(screenPosition);
                Plane aimPlane = new Plane(Vector3.up, origin);
                if (aimPlane.Raycast(ray, out float enter))
                {
                    return ray.GetPoint(enter);
                }
            }

            Vector2 fallback = inputReader.aimDirection;
            return origin + new Vector3(fallback.x, 0f, fallback.y);
        }
    }
}
