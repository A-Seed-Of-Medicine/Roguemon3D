using System;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Player;
using UnityEngine;
using _PinBoy.Scripts.Utils;
using HSM;

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
        [SerializeField, Tooltip("Time window before the dash ends during which a new dash input will be buffered."), Min(0f)]
        private float preInputChainTolerance = 0.1f;

        [SerializeField, Tooltip("Time window after the dash ends during which a new dash input continues the chain."), Min(0f)]
        private float postInputChainTolerance = 0.3f;

        private CountdownTimer dashChainTimer;
        public bool isDashing;
        Vector3 dashDirection;
        Func<Vector3, Vector3> dashRedirector;
        Func<Vector3, Vector3> previousRedirector;
        public Vector3 dashCache;
        Vector3 cachedPreDashVelocity;
        float dashElapsed;
        float dashBaseSpeed;
        CountdownTimer dashTimer;
        bool queuedDashInput;
        Vector3 queuedDashDirection;

        protected override void Awake()
        {
            base.Awake();
            dashTimer = new CountdownTimer(0f);
            dashTimer.OnTimerFinish += HandleDashTimerFinished;

            dashChainTimer = new CountdownTimer(0f);
        }

        protected void FixedUpdate()
        {
            if (!isDashing)
            {
                return;
            }

            dashElapsed += Time.fixedDeltaTime;

            float normalizedTime = dashDuration > 0f
                ? Mathf.Clamp01(dashElapsed / Mathf.Max(0.0001f, dashDuration))
                : 1f;

            ApplyDashVelocity(normalizedTime);
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
            queuedDashInput = false;
            StopDash();
            dashTimer?.Cancel();
            dashChainTimer?.Cancel();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            queuedDashInput = false;
            StopDash();
            if (dashTimer != null)
            {
                dashTimer.OnTimerFinish -= HandleDashTimerFinished;
            }
            dashChainTimer?.Cancel();
        }

        protected override void OnActionPressed()
        {
            Vector3 desiredDirection = ResolveDashDirection();

            if (isDashing)
            {
                if (IsWithinPreInputWindow())
                {
                    QueueDash(desiredDirection);
                }

                return;
            }

            BeginDash(desiredDirection);
        }

        protected override void OnActionReleased()
        {
            // Dash is time based, releasing early does not cancel by default.
        }

        void BeginDash(Vector3 desiredDirection)
        {
            if (isDashing || Controller == null || body == null)
                return;

            dashDirection = desiredDirection.sqrMagnitude <= 0.0001f ? Vector3.forward : desiredDirection.normalized;

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
            
            isDashing = true;
            dashChainTimer?.Cancel();
            actionStarted?.Invoke();
            dashElapsed = 0f;

            queuedDashInput = false;
            dashTimer.Cancel();
            dashBaseSpeed = dashDistance > 0f && duration > 0f
                ? dashDistance / Mathf.Max(0.0001f, duration)
                : 0f;
            dashCache = dashDirection * dashBaseSpeed;

            if (duration <= 0f)
            {
                ApplyDashVelocity(1f);
                HandleDashTimerFinished();
                return;
            }
            
            Vector3 initialVelocity = body.linearVelocity;
            cachedPreDashVelocity = new Vector3(initialVelocity.x, 0f, initialVelocity.z);
            dashTimer.Start(duration);
            ApplyDashVelocity(0f);
        }

        void HandleDashTimerFinished()
        {
            if (!isDashing)
            {
                dashTimer?.Cancel();
                return;
            }

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
                dashTimer?.Cancel();
                return;
            }

            if (lockMovementInput)
            {
                Controller.UnlockMovement();
            }

            isDashing = false;
            dashTimer?.Cancel();

            if (dashProfile != null)
            {
                Controller.RemoveMovementModifier(dashProfile);
            }

            Vector3 current = body.linearVelocity;
            Vector3 planar = zeroVelocityOnEnd ? Vector3.zero : cachedPreDashVelocity;
            body.linearVelocity = new Vector3(planar.x, current.y, planar.z);

            bool hasQueuedDash = TryConsumeQueuedDash(out Vector3 queuedDirection);

            if (postInputChainTolerance > 0f)
            {
                dashChainTimer.Start(postInputChainTolerance);
            }
            else
            {
                dashChainTimer.Cancel();
            }

            actionComplete?.Invoke();

            if (hasQueuedDash)
            {
                BeginDash(queuedDirection);
            }
        }

        bool IsInDashChainWindow()
        {
            if (isDashing)
            {
                return IsWithinPreInputWindow();
            }

            return dashChainTimer is { IsRunning: true };
        }

        public bool IsWithinPreInputWindow()
        {
            if (!isDashing || dashTimer == null || !dashTimer.IsRunning)
            {
                return false;
            }

            if (preInputChainTolerance <= 0f)
            {
                return false;
            }

            return dashTimer.CurrentTime <= preInputChainTolerance;
        }

        public float PreInputChainTolerance => Mathf.Max(0f, preInputChainTolerance);
        public float PostInputChainTolerance => Mathf.Max(0f, postInputChainTolerance);

        void QueueDash(Vector3 desiredDirection)
        {
            Vector3 direction = desiredDirection.sqrMagnitude > 0.0001f ? desiredDirection : dashDirection;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                queuedDashInput = false;
                return;
            }

            queuedDashInput = true;
            queuedDashDirection = direction;
        }

        bool TryConsumeQueuedDash(out Vector3 direction)
        {
            if (queuedDashInput)
            {
                queuedDashInput = false;
                direction = queuedDashDirection;
                return true;
            }

            direction = Vector3.zero;
            return false;
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
