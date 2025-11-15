using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Actions;
using UnityEngine;

namespace HSM {
    public class AgentRoot : State {
        public readonly Grounded Grounded;
        public readonly Airborne Airborne;
        public readonly Stunned Stunned;
        readonly AgentController controller;

        public AgentRoot(StateMachine m, AgentController controller) : base(m) {
            this.controller = controller;
            Grounded = new Grounded(controller, m, this);
            Airborne = new Airborne(controller, m, this);
            Stunned = new Stunned(controller, m, this);
        }

        protected override State GetInitialState()
        {
            if (controller?.statusHandler?.StunnedStatus?.IsActive ?? false)
                return Stunned;
            

            return controller != null && controller.grounded ? Grounded : Airborne;
        }

        protected override State GetTransition()
        {
            if (controller?.statusHandler?.StunnedStatus?.IsActive ?? false)
                return Stunned;

            if (controller != null && !controller.grounded)
                return Airborne;

            return null;
        }
    }

    public abstract class AgentState: State
    {
        public readonly AgentController controller;
        public AgentRoot AgentRoot => (AgentRoot)Machine.Root;

        protected AgentState(AgentController controller, StateMachine machine, State parent = null) : base(machine, parent)
        {
            this.controller = controller;
        }

        protected override void OnEnter()
        {
            base.OnEnter();
            ApplyAnimation();
        }

        protected override void OnExit()
        {
            controller?.AnimationController.Unregister(this);
            base.OnExit();
        }

        protected virtual AgentAnimationRequest GetAnimationRequest()
        {
            return AgentAnimationRequest.None;
        }

        protected ActionState CheckForRequestedActionState()
        {
            if (controller == null)
            {
                return null;
            }

            return controller.TryConsumeActionState(Parent, out ActionState state) ? state : null;
        }

        void ApplyAnimation()
        {
            if (controller == null)
            {
                return;
            }

            AgentAnimationRequest request = GetAnimationRequest();
            if (request.IsValid)
            {
                controller.AnimationController.Register(this, request);
            }
            else
            {
                controller.AnimationController.Unregister(this);
            }
        }
    }

    public class Airborne : AgentState {
        public Airborne(AgentController controller, StateMachine m, State parent) : base(controller, m, parent) {

        }

        protected override State GetTransition()
        {
            if (controller?.statusHandler?.StunnedStatus?.IsActive ?? false)
                return ((AgentRoot)Parent).Stunned;

            return controller != null && controller.grounded ? ((AgentRoot)Parent).Grounded : null;
        }

        protected override AgentAnimationRequest GetAnimationRequest()
        {
            return controller ? controller.AirborneAnimation : AgentAnimationRequest.None;
        }
    }
    
    public class Grounded : AgentState {
        public readonly Idle Idle;
        public readonly Moving Moving;
        public ComboState ComboExecuting;
        public DashState DashExecuting;

        public Grounded(AgentController controller, StateMachine m, State parent) : base(controller, m, parent) {
            Idle = new Idle(controller, m, this);
            Moving = new Moving(controller, m, this);
        }
        
        protected override State GetInitialState() => Idle;

        protected override State GetTransition() {
            if (controller?.statusHandler?.StunnedStatus?.IsActive ?? false)
                return ((AgentRoot)Parent).Stunned;

            return controller != null && controller.grounded ? null : ((AgentRoot)Parent).Airborne;
        }
    }
    
    public class Idle : AgentState {

        public Idle(AgentController controller, StateMachine m, State parent) : base(controller, m, parent) {

        }

        protected override State GetTransition() {
            if (controller?.statusHandler?.StunnedStatus?.IsActive ?? false)
                return ((AgentRoot)Parent.Parent).Stunned;

            ActionState requested = CheckForRequestedActionState();
            if (requested != null)
            {
                return requested;
            }

            return controller != null && controller.IsMoving ? ((Grounded)Parent).Moving : null;
        }

        protected override AgentAnimationRequest GetAnimationRequest()
        {
            return controller ? controller.IdleAnimation : AgentAnimationRequest.None;
        }
    }

    public class Moving : AgentState {
        public Moving(AgentController controller, StateMachine machine, State parent = null) : base(controller, machine, parent)
        {
        }

        protected override State GetTransition() {
            if (controller?.statusHandler?.StunnedStatus?.IsActive ?? false)
                return ((AgentRoot)Parent.Parent).Stunned;

            if (controller != null && !controller.grounded) return ((AgentRoot)Parent).Airborne;

            ActionState requested = CheckForRequestedActionState();
            if (requested != null)
            {
                return requested;
            }

            return controller != null && !controller.IsMoving ? ((Grounded)Parent).Idle : null;
        }

        protected override AgentAnimationRequest GetAnimationRequest()
        {
            return controller ? controller.MovingAnimation : AgentAnimationRequest.None;
        }
    }

    public class Stunned : AgentState
    {
        public Stunned(AgentController controller, StateMachine machine, State parent = null) : base(controller, machine, parent)
        {
            
        }

        protected override State GetTransition()
        {
            if (controller?.statusHandler?.StunnedStatus?.IsActive ?? false)
                return null;

            AgentRoot root = (AgentRoot)Parent;
            if (controller != null && !controller.grounded)
                return root.Airborne;

            return root.Grounded;
        }

        protected override AgentAnimationRequest GetAnimationRequest()
        {
            return controller ? controller.StunnedAnimation : AgentAnimationRequest.None;
        }

        protected override void OnEnter()
        {
            controller?.AnimationController.Clear();
            base.OnEnter();
        }
    }
}