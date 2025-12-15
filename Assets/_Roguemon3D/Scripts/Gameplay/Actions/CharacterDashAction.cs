using System;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Player;
using _Roguemon3D.Scripts.ThirdParty.ImprovedTimers;
using UnityEngine;
using UnityEngine.Serialization;
using HSM;
using ImprovedTimers;
using AgentController = _PinBoy.Scripts.CharacterMovement.AgentController;

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

        private MyCountTimer dashChainPostTimer;
        private MyCountTimer dashQueueTimer;
        public bool isDashing => dashTimer.IsRunning;
        Vector3 dashDirection;
        Func<Vector3, Vector3> dashRedirector;
        Func<Vector3, Vector3> previousRedirector;
        public Vector3 dashCache;
        float dashBaseSpeed;
        FixedCountdownTimer dashTimer;
        MyCountTimer dashCooldownTimer;
        bool queuedDash;
        private bool canDashAgain = true;

        protected override void Awake()
        {
            base.Awake();
            dashTimer = new FixedCountdownTimer(0f);
            dashTimer.OnTimerFinish += HandleDashTimerFinished;

            dashCooldownTimer = new MyCountTimer(Mathf.Max(0f, dashCooldown));
            dashChainPostTimer = new MyCountTimer(dashChainPostInputTolerance);
            dashQueueTimer = new MyCountTimer(dashQueueDuration);
            dashCooldownTimer.OnTimerFinish += () => { canDashAgain = true; };
        }

        private void FixedUpdate()
        {
            if (!isDashing)
                if (dashQueueTimer.IsRunning && CanStartDash())
                {
                    BeginDashInternal(null);
                    if (!isDashing)
                        return;
                }
                else 
                    return;

            ApplyDashVelocity(dashTimer.Progress);
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
            dashTimer?.Cancel();
            dashChainPostTimer?.Cancel();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            queuedDash = false;
            StopDash();
            if (dashTimer != null)
            {
                dashTimer.OnTimerFinish -= HandleDashTimerFinished;
            }
            dashChainPostTimer?.Cancel();
        }

        protected override void OnActionPressed()
        {
            if (isDashing)
            {
                if (IsDashWithinPreInputWindowInternal())
                {
                    Vector3 requestedDirection = ResolveDashDirection();
                    Vector3 normalizedDirection = requestedDirection.sqrMagnitude > 0.0001f
                        ? requestedDirection.normalized
                        : dashDirection.sqrMagnitude > 0.0001f ? dashDirection : Vector3.forward;

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
                Vector3 requestedDirection = ResolveDashDirection();
                Vector3 normalizedDirection = requestedDirection.sqrMagnitude > 0.0001f
                    ? requestedDirection.normalized
                    : dashDirection.sqrMagnitude > 0.0001f ? dashDirection : Vector3.forward;
                
                dashQueueTimer.Start(dashQueueDuration);
                return;
            }

            BeginDashInternal(null);
        }

        protected override void OnActionReleased()
        {
            // Dash is time based, releasing early does not cancel by default.
        }

        bool CanStartDash()
        {
            if (isDashing)
            {
                return false;
            }
            
            if (Controller.IsMovementLocked || Controller.statusHandler.StunnedStatus.IsActive)
            {
                return false;
            }

            bool cooldownReady = dashCooldownTimer == null || !dashCooldownTimer.IsRunning;
            bool inChainPostWindow = IsDashWithinPostInputWindow();

            return cooldownReady || inChainPostWindow;
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

            ApplyPhaseAnimation(ExecutionPhase.Windup, 0f);

            dashChainPostTimer?.Cancel();
            dashCooldownTimer?.Cancel();
            dashQueueTimer?.Cancel();

            if (zeroVelocityOnStart)
            {
                Vector3 current = body.linearVelocity;
                body.linearVelocity = new Vector3(0f, current.y, 0f);
            }

            if (dashProfile)
            {
                Controller.ApplyMovementModifier(dashProfile, -1f);
            }

            float duration = Mathf.Max(0f, dashDuration);
            if (duration > 0f)
            {
                Controller.LockMovement(duration, zeroVelocityOnStart);
            }

            ApplyPhaseAnimation(ExecutionPhase.Active, duration);
            actionStarted?.Invoke();
            dashBaseSpeed = dashDistance > 0f && duration > 0f
                ? dashDistance / Mathf.Max(0.0001f, duration)
                : 0f;
            
            
            Vector3 initialVelocity = body.linearVelocity;
            float planarMagnitude = new Vector3(initialVelocity.x, 0f, initialVelocity.z).magnitude;
            dashCache = planarMagnitude * dashDirection;
            if (dashTimer.IsRunning)
            {
                ApplyDashVelocity(1f);
                dashTimer.Finish();
            }
            dashTimer.Start(duration);
            queuedDash = false;
            ApplyDashVelocity(0f);
        }

        void HandleDashTimerFinished()
        {
            StopDash();
        }

        void ApplyDashVelocity(float normalizedTime)
        {
            float baseSpeed = dashBaseSpeed;
            if (dashDuration <= 0f || dashDistance <= 0f)
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
            if (!isDashing)
            {
                dashTimer.Cancel();
                return;
            }
            
            ApplyDashVelocity(1f);

            if (lockMovementInput)
            {
                Controller.UnlockMovement();
            }
            
            dashTimer.Cancel();

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

            ApplyPhaseAnimation(ExecutionPhase.Recovery, dashCooldown);
            actionComplete?.Invoke();

            if (queuedDash)
            {
                BeginDashInternal(null);
            }
            queuedDash = false;
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
            if (!isDashing || dashTimer is not { IsRunning: true })
            {
                return false;
            }

            if (dashChainPreInputTolerance <= 0f)
            {
                return false;
            }

            return dashTimer.CurrentTime <= dashChainPreInputTolerance;
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
            root.Grounded.DashExecuting = controller;
            return controller;
        }
    }
}
