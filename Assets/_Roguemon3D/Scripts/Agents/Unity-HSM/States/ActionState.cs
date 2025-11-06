using _PinBoy.Scripts.CharacterMovement;
using HSM;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    public abstract class ActionState : AgentState
    {
        protected readonly CharacterAction action;
        bool isActive;

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
                HandleAnimationRequestChanged(action.GetAnimationRequest());
            }
        }

        protected override void OnExit()
        {
            if (action != null)
            {
                action.UnregisterAnimationListener(HandleAnimationRequestChanged);
                action.ResetAnimationRequest();
            }

            isActive = false;
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

            if (!comboAction.IsComboExecuting)
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

            if (!dashAction.isDashing)
            {
                return controller && controller.grounded ? AgentRoot.Grounded : AgentRoot.Airborne;
            }

            return null;
        }
    }
}
