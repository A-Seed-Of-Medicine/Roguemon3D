using System;
using _PinBoy.Scripts.CharacterMovement;
using HSM;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    [Serializable]
    public abstract class ActionState : AgentState
    {
        protected readonly CharacterAction action;
        bool isActive;

        public CharacterAction Action => action;

        public ActionState(AgentController controller, StateMachine machine, AgentRoot root, CharacterAction action, State parent = null) : base(controller,
            machine, parent)
        {
            this.action = action;
            if (this.action != null)
            {
                this.action.actionStarted += HandleActionStarted;
                this.action.actionComplete += HandleActionCompleted;
            }
        }

        protected override void OnEnter()
        {
            base.OnEnter();
            isActive = true;

            if (action != null)
            {
                action.RegisterAnimationListener(HandleAnimationRequestChanged);
                //action.RegisterPhaseListeners(HandlePhaseStarted, HandlePhaseCompleted);
                HandleAnimationRequestChanged(action.GetAnimationRequest());
                //ActivePhase = action.ActivePhase;
            }
        }

        protected override void OnExit()
        {
            if (action != null)
            {
                action.UnregisterAnimationListener(HandleAnimationRequestChanged);
                action.ResetAnimationRequest();
                //action.UnregisterPhaseListeners(HandlePhaseStarted, HandlePhaseCompleted);
            }

            isActive = false;
            //ActivePhase = ExecutionPhase.None;
            base.OnExit();
        }

        protected override AgentAnimationRequest GetAnimationRequest()
        {
            return action != null ? action.GetAnimationRequest() : base.GetAnimationRequest();
        }

        void HandleActionStarted()
        {
            controller?.RequestActionState(this);
        }

        void HandleActionCompleted()
        {
            if (!isActive)
            {
                action?.ResetAnimationRequest();
            }
            controller?.CancelPendingActionState(this);
        }

        void HandleAnimationRequestChanged(AgentAnimationRequest request)
        {
            if (!isActive || controller == null)
            {
                return;
            }

            if (request.IsValid)
            {
                controller.AnimationController.Update(this, request);
            }
            else
            {
                controller.AnimationController.Unregister(this);
            }
        }

        protected ActionState CheckForRequestedActionInterrupt()
        {
            if (controller == null || action == null)
            {
                return null;
            }

            if (!controller.TryPeekActionState(Parent, out ActionState requested))
            {
                return null;
            }
            
           /* CharacterAction[] interrupts = execution.Interrupts;
            if (interrupts == null || interrupts.Length == 0)
            {
                return null;
            }

            foreach (CharacterAction interrupt in interrupts)
            {
                if (interrupt == requested.Action)
                {
                    controller.TryConsumeActionState(Parent, out _);
                    return requested;
                }
            }*/

            return null;
        }
        
        protected bool IsStunned => controller?.statusHandler?.StunnedStatus?.IsActive ?? false;

        protected bool IsControllerPerformingAction => controller?.IsPerformingAction ?? false;

        protected State GetLocomotionState()
        {
            if (controller == null)
            {
                return null;
            }

            if (controller.grounded)
            {
                return controller.IsMoving ? AgentRoot.Grounded.Moving : AgentRoot.Grounded.Idle;
            }

            return AgentRoot.Airborne;
        }
    }
}
