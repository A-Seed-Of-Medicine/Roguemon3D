using System;
using System.Threading;
using _PinBoy.Scripts.CharacterMovement;
using Cysharp.Threading.Tasks;
using HSM;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    /// <summary>
    /// Executes a scripted jump arc that temporarily suspends normal controller
    /// physics and input while the character is moved along configurable curves.
    /// Works for both player-controlled and AI-driven characters.
    /// </summary>
    [RequireComponent(typeof(AgentController))]
    [DisallowMultipleComponent]
    public sealed class JumpAction : CharacterAction
    {
        [Header("Arc Shape")]
        [field: SerializeField, Tooltip("Time in seconds to complete the jump arc."), Min(0f)]
        private float arcDuration
        {
            get => activePhaseExecution.Duration;
            set => activePhaseExecution.Duration = Mathf.Max(0f, value);
        }
        [SerializeField, Tooltip("Planar distance travelled during the jump."), Min(0f)]
        private float arcDistance = 4f;
        [SerializeField, Tooltip("Maximum height reached above the starting point."), Min(0f)]
        private float arcHeight = 2f;
        [SerializeField, Tooltip("Controls how quickly planar distance is covered (0-1 over the arc duration).")]
        private AnimationCurve distanceCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField, Tooltip("Controls the vertical displacement (0-1 over the arc duration).")]
        private AnimationCurve heightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField, Tooltip("Local offset applied at the start of the arc.")]
        private Vector3 localStartOffset;
        [SerializeField, Tooltip("Local offset applied at the end of the arc.")]
        private Vector3 localEndOffset;

        [Header("Direction")]
        [SerializeField, Tooltip("Prefer aim direction when available; otherwise fall back to move/facing direction.")]
        private bool preferAimDirection = true;
        [SerializeField, Tooltip("If true the jump direction is forced to the facing direction when no input is available.")]
        private bool defaultToFacingDirection = true;

        [Header("Controller Suspension")]
        [SerializeField, Tooltip("Lock AgentController movement/physics while the arc plays.")]
        private bool suspendControllerWhileJumping = true;
        [SerializeField, Tooltip("Disable input callbacks while the jump arc runs.")]
        private bool disableInputDuringJump = true;
        [SerializeField, Tooltip("Zero out existing velocity before the arc begins.")]
        private bool resetVelocityOnStart = true;
        [SerializeField, Tooltip("Reapply the stored planar velocity after the arc completes.")]
        private bool restoreVelocityOnComplete = true;

        public float maxDirectionOffset = 45f;
        public float directionalOffset = 0;

        CancellationTokenSource jumpCancellation;
        bool isJumping;
        internal bool IsJumping => isJumping;

        protected override void OnDisable()
        {
            base.OnDisable();
            CancelJump();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            CancelJump();
        }

        protected override void OnActionPressed()
        {
            if (isJumping || Controller == null)
            {
                return;
            }
            Debug.Log("Jump Action Pressed");
            BeginPhaseSequence(true, true, true, windupPhaseExecution.Duration, arcDuration,
                recoveryPhaseExecution.Duration);
        }

        async UniTaskVoid BeginJumpArc(float duration)
        {
            isJumping = true;
            jumpCancellation = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            CancellationToken token = jumpCancellation.Token;

            Vector3 jumpDirection = ResolveJumpDirection();
            if (jumpDirection.sqrMagnitude <= 0.0001f)
            {
                jumpDirection = Vector3.forward;
            }
            
            // Apply any fixed directional offset first
            if (directionalOffset > 0f)
                jumpDirection = Quaternion.Euler(0f, directionalOffset, 0f) * jumpDirection;
            
            // Randomize the direction within the allowed  on the horizontal plane
            if (maxDirectionOffset > 0f)
                jumpDirection = Quaternion.Euler(0f, UnityEngine.Random.Range(-maxDirectionOffset, maxDirectionOffset), 0f) * jumpDirection;

            Vector3 startPosition = transform.position;
            Vector3 startOffset = transform.TransformVector(localStartOffset);
            Vector3 endOffset = transform.TransformVector(localEndOffset);
            Vector3 planarOffset = jumpDirection.normalized * Mathf.Max(0f, arcDistance);

            SuspensionState suspension = SuspendController(duration);
            actionStarted?.Invoke();
            ExecuteConfiguredAction();

            try
            {
                float resolvedDuration = Mathf.Max(0f, duration);
                if (resolvedDuration <= 0f)
                {
                    ApplyJumpPosition(startPosition, startOffset, endOffset, planarOffset, 1f);
                    return;
                }

                float elapsed = 0f;
                while (elapsed < resolvedDuration)
                {
                    token.ThrowIfCancellationRequested();
                    float normalizedTime = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, resolvedDuration));
                    ApplyJumpPosition(startPosition, startOffset, endOffset, planarOffset, normalizedTime);
                    await UniTask.Yield(PlayerLoopTiming.FixedUpdate, token);
                    elapsed += Time.fixedDeltaTime;
                }

                ApplyJumpPosition(startPosition, startOffset, endOffset, planarOffset, 1f);
            }
            catch (OperationCanceledException)
            {
                // Swallow cancellation so cleanup still runs.
            }
            finally
            {
                RestoreController(suspension);
                isJumping = false;
                jumpCancellation?.Dispose();
                jumpCancellation = null;
            }
        }

        void ApplyJumpPosition(Vector3 origin, Vector3 startOffset, Vector3 endOffset, Vector3 planarOffset, float normalizedTime)
        {
            float distanceFactor = distanceCurve != null ? distanceCurve.Evaluate(Mathf.Clamp01(normalizedTime)) : normalizedTime;
            float heightFactor = heightCurve != null ? heightCurve.Evaluate(Mathf.Clamp01(normalizedTime)) : normalizedTime;

            Vector3 offsetLerp = Vector3.Lerp(startOffset, endOffset, distanceFactor);
            Vector3 planar = planarOffset * Mathf.Max(0f, distanceFactor);
            float height = Mathf.Max(0f, arcHeight) * Mathf.Max(0f, heightFactor);

            Vector3 target = origin + offsetLerp + planar + Vector3.up * height;

            if (body != null)
            {
                body.MovePosition(target);
            }
            else
            {
                transform.position = target;
            }
        }

        Vector3 ResolveJumpDirection()
        {
            if (preferAimDirection && Controller != null)
            {
                Vector3 aim = Controller.AimDirection;
                if (aim.sqrMagnitude > 0.0001f)
                {
                    return aim.normalized;
                }
            }

            if (InputReader != null)
            {
                Vector2 move = InputReader.Direction;
                if (move.sqrMagnitude > 0.0001f)
                {
                    return new Vector3(move.x, 0f, move.y).normalized;
                }
            }

            if (defaultToFacingDirection && Controller != null)
            {
                Vector3 facing = Controller.AimDirection;
                if (facing.sqrMagnitude > 0.0001f)
                {
                    return facing.normalized;
                }
            }

            return Vector3.forward;
        }

        protected override ActionState CreateActionState(AgentRoot root)
        {
            if (Controller == null || root == null)
            {
                return null;
            }

            AgentState parent = GetDefaultActionParent(root);
            return new JumpState(Controller, root.Machine, root, this, parent);
        }

        protected override void OnPhaseStarted(ExecutionPhase phase)
        {
            base.OnPhaseStarted(phase);

            if (phase == ExecutionPhase.Active)
            {
                BeginJumpArc(CurrentPhaseDuration > 0f ? CurrentPhaseDuration : arcDuration).Forget();
            }
        }

        protected override void OnPhaseCompleted(ExecutionPhase phase)
        {
            base.OnPhaseCompleted(phase);

            if (phase == ExecutionPhase.Active)
            {
                CancelJump();
            }
        }

        SuspensionState SuspendController(float activeDuration)
        {
            SuspensionState state = new SuspensionState
            {
                InputEnabled = InputReader?.ControlsEnabled ?? false,
                BodyKinematic = body != null && body.isKinematic,
                StoredVelocity = body != null ? body.linearVelocity : Vector3.zero,
                MovementLocked = Controller != null && Controller.IsMovementLocked
            };

            if (disableInputDuringJump && InputReader != null && state.InputEnabled)
            {
                InputReader.EnableCharacterActions(false);
            }

            if (suspendControllerWhileJumping && Controller != null && !Controller.IsMovementLocked)
            {
                Controller.LockMovement(Mathf.Max(activeDuration, 0f), true);
            }

            if (body != null)
            {
                if (resetVelocityOnStart)
                {
                    body.linearVelocity = Vector3.zero;
                }

                body.isKinematic = true;
            }

            return state;
        }

        void RestoreController(SuspensionState state)
        {
            if (body != null)
            {
                if (restoreVelocityOnComplete)
                {
                    body.linearVelocity = new Vector3(state.StoredVelocity.x, 0f, state.StoredVelocity.z);
                }

                body.isKinematic = state.BodyKinematic;
            }

            if (suspendControllerWhileJumping && Controller != null && !state.MovementLocked)
            {
                Controller.UnlockMovement();
            }

            if (disableInputDuringJump && InputReader != null && state.InputEnabled)
            {
                InputReader.EnableCharacterActions(true);
            }
        }

        void CancelJump()
        {
            if (jumpCancellation != null)
            {
                jumpCancellation.Cancel();
                jumpCancellation.Dispose();
                jumpCancellation = null;
            }

            isJumping = false;
        }

        struct SuspensionState
        {
            public bool InputEnabled;
            public bool BodyKinematic;
            public bool MovementLocked;
            public Vector3 StoredVelocity;
        }
    }

    sealed class JumpState : ActionState
    {
        readonly JumpAction jumpAction;

        public JumpState(AgentController controller, StateMachine machine, AgentRoot root, JumpAction jumpAction, AgentState parent)
            : base(controller, machine, root, jumpAction, parent)
        {
            this.jumpAction = jumpAction;
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

            bool jumpRunning = jumpAction.IsPhaseSequenceActive || jumpAction.IsJumping;
            if (!jumpRunning)
            {
                return GetLocomotionState();
            }

            return null;
        }
    }
}
