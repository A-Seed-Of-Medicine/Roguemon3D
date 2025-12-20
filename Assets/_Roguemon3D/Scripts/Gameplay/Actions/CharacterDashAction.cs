using System;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Player;
using _Roguemon3D.Scripts.ThirdParty.ImprovedTimers;
using UnityEngine;
using UnityEngine.Serialization;
using HSM;
using AgentController = _PinBoy.Scripts.CharacterMovement.AgentController;

using _PinBoy.Scripts.Gameplay.Actions;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    /// <summary>
    /// Performs a directional dash by forcing the controller velocity along a
    /// configurable curve for a fixed duration. Works with the AgentStateDriver to
    /// lock movement while the dash resolves.
    /// </summary>
    [RequireComponent(typeof(AgentController))]
    [DisallowMultipleComponent]
    public sealed class CharacterDashAction : CharacterAction
    {
        [Header("Dash Settings")]
        [SerializeField, Tooltip("Distance covered during the dash."), Min(0f)]
        private float dashDistance = 5f;

        [field: SerializeField, Tooltip("How long the dash lasts in seconds."), Min(0f)]
        public float dashDuration { get; private set; } = 0.2f;
        [SerializeField, Tooltip("How long before the dash can consecutively execute"), Min(0f)]
        private float dashCooldown = 0.5f;
        
        [SerializeField, Tooltip("Curve controlling the dash speed over time. Evaluated 0-1 across the dash duration.")]
        private AnimationCurve speedCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        [SerializeField, Tooltip("Optional movement profile applied while dashing.")]
        private MovementProfile dashProfile;
        [Header("Behaviour")]
        [SerializeField, Tooltip("Zero the rigidbody velocity when the dash begins.")]
        private bool zeroVelocityOnStart = true;
        [SerializeField, Tooltip("Zero the rigidbody velocity once the dash ends.")]
        private bool zeroVelocityOnEnd;
        [SerializeField, Tooltip("If true movement input is overridden with the dash direction while active.")]
        private bool lockMovementInput = true;
        [SerializeField, Tooltip("Time window before the dash ends to allow buffering a chained dash.")]
        private float dashChainPreInputTolerance = 0.1f;

        [SerializeField, Tooltip("Time window after a dash completes to allow chaining without waiting for the cooldown.")]
        private float dashChainPostInputTolerance = 0.3f;
        
        [SerializeField, Tooltip("Time window after a dash completes to allow chaining without waiting for the cooldown.")]
        private float dashQueueDuration = 0.3f;

        private MyCountdownTimer dashChainPostTimer;
        private MyCountdownTimer dashQueueTimer;
        public bool isDashing => IsInPhase(ActionPhase.Active);
        Vector3 dashDirection;
        Func<Vector3, Vector3> dashRedirector;
        Func<Vector3, Vector3> previousRedirector;
        public Vector3 dashCache;
        float dashBaseSpeed;
        MyCountdownTimer dashCooldownTimer;
        bool queuedDash;
        private bool canDashAgain = true;
        float currentDashDuration;

        protected override void Awake()
        {
            base.Awake();

            dashCooldownTimer = new MyCountdownTimer(Mathf.Max(0f, dashCooldown));
            dashChainPostTimer = new MyCountdownTimer(dashChainPostInputTolerance);
            dashQueueTimer = new MyCountdownTimer(dashQueueDuration);
            dashCooldownTimer.OnTimerFinish += () => { canDashAgain = true; };
        }

        private void FixedUpdate()
        {
            if (!isDashing)
            {
                if (dashQueueTimer.IsRunning && CanStartDash())
                {
                    BeginDashInternal(null);
                }

                return;
            }

            float normalizedTime = 1f - (activeTimer?.Progress ?? 1f);
            ApplyDashVelocity(Mathf.Clamp01(normalizedTime));
        }

        public bool CanChain(Vector3 velocity, float tolerance)
        {
            if (!IsInDashChainWindow())
                return false;
            // Check if velocity direction is within tolerance of dashcache direction
            if (dashCache.sqrMagnitude <= 0.0001f || velocity.sqrMagnitude <= 0.0001f)
            {
                return false;
            }
            Vector3 planarCache = new Vector3(dashCache.x, 0f, dashCache.z);
            Vector3 planarVelocity = new Vector3(velocity.x, 0f, velocity.z);
            if (planarCache.sqrMagnitude <= 0.0001f || planarVelocity.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            float angle = Vector3.Angle(planarCache.normalized, planarVelocity.normalized);
            return angle <= tolerance;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            queuedDash = false;
            StopDash();
            dashChainPostTimer?.Cancel();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            queuedDash = false;
            StopDash();
            dashChainPostTimer?.Cancel();
        }

        protected override void OnActionPressed()
        {
            if (isDashing)
            {
                if (IsDashWithinPreInputWindowInternal())
                {
                    if (!queuedDash && canDashAgain)
                    {
                        queuedDash = true;
                        canDashAgain = false;
                    }
                }

                return;
            }

            if (!CanStartDash())
            {
                dashQueueTimer.Start(dashQueueDuration);
                return;
            }

            BeginDashInternal(null);
        }

        protected override void OnActionReleased()
        {
            // Dash is time based, releasing early does not cancel by default.
        }

        protected override void OnPhaseStarted(ActionPhase phase, float duration)
        {
            base.OnPhaseStarted(phase, duration);

            if (phase == ActionPhase.Active)
            {
                StartDashMovement(duration);
            }
        }

        protected override void OnPhaseEnded(ActionPhase phase)
        {
            base.OnPhaseEnded(phase);

            if (phase == ActionPhase.Active)
            {
                StopDash();
            }
        }

        protected override void OnPhasesCompleted()
        {
            base.OnPhasesCompleted();

            if (queuedDash)
            {
                queuedDash = false;
                BeginDashInternal(null);
            }
        }

        protected override void OnPhaseCancelled(ActionPhase phase)
        {
            base.OnPhaseCancelled(phase);
            StopDash();
        }

        bool CanStartDash()
        {
            if (isDashing)
            {
                return false;
            }

            if (Controller == null || Controller.IsMovementLocked || Controller.statusHandler.StunnedStatus.IsActive)
            {
                return false;
            }

            bool cooldownReady = dashCooldownTimer == null || !dashCooldownTimer.IsRunning;
            bool inChainPostWindow = IsDashWithinPostInputWindow();

            return cooldownReady || inChainPostWindow;
        }

        ActionPhaseDurations ResolvePhaseDurations()
        {
            ActionPhaseDurations durations = GetDefaultPhaseDurations();
            if (durations.Active <= 0f)
            {
                durations.Active = dashDuration;
            }

            return durations;
        }

        void StartDashMovement(float duration)
        {
            currentDashDuration = duration > 0f ? duration : dashDuration;

            if (zeroVelocityOnStart)
            {
                Vector3 current = body.linearVelocity;
                body.linearVelocity = new Vector3(0f, current.y, 0f);
            }

            if (dashProfile)
            {
                Controller.ApplyMovementModifier(dashProfile, -1f);
            }

            if (currentDashDuration > 0f)
            {
                Controller.LockMovement(currentDashDuration, zeroVelocityOnStart);
            }

            dashBaseSpeed = dashDistance > 0f && currentDashDuration > 0f
                ? dashDistance / Mathf.Max(0.0001f, currentDashDuration)
                : 0f;

            Vector3 initialVelocity = body.linearVelocity;
            float planarMagnitude = new Vector3(initialVelocity.x, 0f, initialVelocity.z).magnitude;
            dashCache = planarMagnitude * dashDirection;

            ApplyDashVelocity(0f);
        }

        void BeginDashInternal(Vector3? directionOverride)
        {
            if (isDashing || Controller == null || body == null)
                return;

            queuedDash = false;
            canDashAgain = true;

            Vector3 resolvedDirection = directionOverride ?? ResolveDashDirection();
            if (resolvedDirection.sqrMagnitude <= 0.0001f)
            {
                resolvedDirection = dashDirection.sqrMagnitude > 0.0001f ? dashDirection : Vector3.forward;
            }

            dashDirection = resolvedDirection.normalized;

            dashChainPostTimer?.Cancel();
            dashCooldownTimer?.Cancel();
            dashQueueTimer?.Cancel();

            ActionPhaseDurations durations = ResolvePhaseDurations();
            currentDashDuration = durations.Active > 0f ? durations.Active : dashDuration;

            actionStarted?.Invoke();
            StartActionPhases(durations, GetDefaultPhaseAnimations());
        }

        void ApplyDashVelocity(float normalizedTime)
        {
            float baseSpeed = dashBaseSpeed;
            if (currentDashDuration <= 0f || dashDistance <= 0f)
            {
                baseSpeed = 0f;
            }

            float curveValue = speedCurve != null ? Mathf.Max(0f, speedCurve.Evaluate(Mathf.Clamp01(normalizedTime))) : 1f;
            float speed = baseSpeed * curveValue;

            Vector3 velocity = dashDirection * speed;
            Vector3 current = body.linearVelocity;
            body.linearVelocity = new Vector3(velocity.x, current.y, velocity.z);
        }

        void StopDash()
        {
            if (Controller == null || body == null)
            {
                return;
            }

            ApplyDashVelocity(1f);

            if (lockMovementInput)
            {
                Controller.UnlockMovement();
            }

            if (dashProfile != null)
            {
                Controller.RemoveMovementModifier(dashProfile);
            }

            Vector3 current = body.linearVelocity;
            Vector3 planar = zeroVelocityOnEnd ? Vector3.zero : dashCache;
            body.linearVelocity = new Vector3(planar.x, current.y, planar.z);

            if (dashChainPostInputTolerance > 0f)
            {
                dashChainPostTimer.Start(dashChainPostInputTolerance);
            }
            else
            {
                dashChainPostTimer.Cancel();
            }

            if (dashCooldown > 0f)
            {
                dashCooldownTimer.Start(dashCooldown);
            }
            else
            {
                dashCooldownTimer.Cancel();
            }

            dashQueueTimer?.Cancel();
        }

        bool IsInDashChainWindow()
        {
            if (isDashing)
            {
                bool withinPreWindow = IsDashWithinPreInputWindowInternal();
                canDashAgain = withinPreWindow && !queuedDash;
                return withinPreWindow;
            }

            return IsDashWithinPostInputWindow();
        }

        private bool IsDashWithinPreInputWindowInternal()
        {
            if (!isDashing || activeTimer is not { IsRunning: true })
            {
                return false;
            }

            if (dashChainPreInputTolerance <= 0f)
            {
                return false;
            }

            return activeTimer.CurrentTime <= dashChainPreInputTolerance;
        }

        public bool IsDashWithinPreInputWindow()
        {
            return canDashAgain && IsDashWithinPreInputWindowInternal();
        }

        public float DashChainPreInputTolerance => Mathf.Max(0f, dashChainPreInputTolerance);

        bool IsDashWithinPostInputWindow()
        {
            return dashChainPostTimer is { IsRunning: true };
        }

        Vector3 ResolveDashDirection()
        {
            if (InputReader != null)
            {
                Vector2 move = InputReader.Direction;
                if (move.sqrMagnitude > 0.0001f)
                {
                    Vector3 moveDir = new Vector3(move.x, 0f, move.y);
                    return moveDir.normalized;
                }
            }

            if (Controller != null)
            {
                Vector3 aim = Controller.AimDirection;
                if (aim.sqrMagnitude > 0.0001f)
                {
                    return aim.normalized;
                }
            }

            return Vector3.forward;
        }

        protected override ActionState CreateActionExecuteState(AgentRoot root)
        {
            if (Controller == null || root == null)
            {
                return null;
            }

            DashState controller = new DashState(Controller, root.Machine, root, this, root.Grounded);
            return controller;
        }
    }
    
    
    sealed class DashState : ActionState
    {
        readonly CharacterDashAction dashAction;

        public DashState(AgentController controller, StateMachine machine, AgentRoot root, CharacterDashAction dashAction, AgentState parent)
            : base(controller, machine, root, dashAction, parent)
        {
            this.dashAction = dashAction;
        }

        protected override State GetTransition()
        {
            if (IsStunned)
            {
                return AgentRoot.Stunned;
            }

            ActionState interrupt = CheckForRequestedActionInterrupt();
            if (interrupt != null)
            {
                return interrupt;
            }

            bool dashRunning = dashAction.isDashing;
            if (!dashRunning)
            {
                return GetLocomotionState();
            }

            return null;
        }
    }
}
