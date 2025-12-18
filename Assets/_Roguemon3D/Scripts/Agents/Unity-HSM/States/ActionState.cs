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
        public ExecutionPhase ActivePhase { get; private set; } = ExecutionPhase.None;

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
                action.RegisterPhaseListeners(HandlePhaseStarted, HandlePhaseCompleted);
                HandleAnimationRequestChanged(action.GetAnimationRequest());
                ActivePhase = action.ActivePhase;
            }
        }

        protected override void OnExit()
        {
            if (action != null)
            {
                action.UnregisterAnimationListener(HandleAnimationRequestChanged);
                action.ResetAnimationRequest();
                action.UnregisterPhaseListeners(HandlePhaseStarted, HandlePhaseCompleted);
            }

            isActive = false;
            ActivePhase = ExecutionPhase.None;
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

            CharacterAction.PhaseExecution execution = action.GetPhaseExecution(ActivePhase);
            CharacterAction[] interrupts = execution.Interrupts;
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
            }

            return null;
        }

        void HandlePhaseStarted(ExecutionPhase phase)
        {
            ActivePhase = phase;
        }

        void HandlePhaseCompleted(ExecutionPhase phase)
        {
            ActivePhase = phase;
        }
    }
    
    public sealed class ComboState : ActionState
    {
        CharacterComboAction comboAction => action as CharacterComboAction;
        public ComboState(AgentController controller, StateMachine machine, AgentRoot root, CharacterComboAction comboAction, AgentState parent)
            : base(controller, machine, root, comboAction, parent)
        {
            
        }

        protected override State GetTransition()
        {
            if (controller?.statusHandler?.StunnedStatus?.IsActive ?? false)
            {
                return AgentRoot.Stunned;
            }

            ActionState interrupt = CheckForRequestedActionInterrupt();
            if (interrupt != null)
            {
                return interrupt;
            }

            if (!comboAction.IsCurrentStepRunning)
            {
                if (!controller)
                    return null;
                if (controller.grounded)
                {
                    if (controller.IsMoving) 
                        return AgentRoot.Grounded.Moving;
                    return AgentRoot.Grounded.Idle;
                }
                return AgentRoot.Airborne;
            }

            return null;
        }
    }
    
    public sealed class DashState : ActionState
    {
        readonly CharacterDashAction dashAction;

        public DashState(AgentController controller, StateMachine machine, AgentRoot root, CharacterDashAction dashAction, AgentState parent)
            : base(controller, machine, root, dashAction, parent)
        {
            this.dashAction = dashAction;
        }

        protected override State GetTransition()
        {
            if (controller?.statusHandler?.StunnedStatus?.IsActive ?? false)
                return AgentRoot.Stunned;

            ActionState interrupt = CheckForRequestedActionInterrupt();
            if (interrupt != null)
            {
                return interrupt;
            }
            
            if (!dashAction.isDashing)
            {
                if (!controller)
                    return null;
                if (controller.grounded)
                {
                    if (controller.IsMoving) 
                        return AgentRoot.Grounded.Moving;
                    return AgentRoot.Grounded.Idle;
                }
                return AgentRoot.Airborne;
            }

            return null;
        }
    }
}
