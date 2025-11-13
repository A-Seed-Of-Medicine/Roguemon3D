using System;
using System.Collections.Generic;
using System.Threading;
using _PinBoy.Scripts.Gameplay.Actions;
using _PinBoy.Scripts.Gameplay.Effects;
using _PinBoy.Scripts.Animation;
using AdvancedController;
using Cysharp.Threading.Tasks;
using UnityEngine;
using HSM;
using ImprovedTimers;

namespace _PinBoy.Scripts.CharacterMovement
{
    [RequireComponent(typeof(Rigidbody))]
    public class AgentController : MonoBehaviour, IMovable, IDamager, IDamageable
    {
        [Header("Input")] 
        public virtual InputReader inputReader { get; private set; }

        [Header("Movement")]
        [SerializeField] private MovementProfile baseProfile;
        [SerializeField] private bool normalizeDiagonals = true;
        [SerializeField] private bool snapFacingTo8 = true;
        public bool grounded { get; private set; }
        public Vector3 GroundNormal => groundNormal;

        [Header("Grounding")]
        [SerializeField] private LayerMask groundLayers = Physics.DefaultRaycastLayers;
        [SerializeField, Tooltip("Radius used for sphere checks when evaluating the ground below the agent.")]
        private float groundCheckRadius = 0.3f;
        [SerializeField, Tooltip("Additional distance below the collider bounds to search for the ground.")]
        private float groundCheckDistance = 0.3f;
        [SerializeField, Tooltip("Vertical offset added above the collider when performing ground checks.")]
        private float groundCheckOffset = 0.02f;
        [SerializeField, Range(0f, 89f)]
        private float maxGroundSlopeAngle = 60f;
        [SerializeField]
        private Collider groundCollider;

        [Header("Step Handling")]
        [SerializeField, Tooltip("Enables automatic stepping up small height differences when moving across uneven terrain.")]
        private bool enableStepHandling = true;
        [SerializeField, Tooltip("Maximum height difference in meters that the agent can automatically step up.")]
        private float maxStepHeight = 0.4f;
        [SerializeField, Tooltip("Vertical offset above the feet used for the lower step detection cast.")]
        private float stepCheckVerticalOffset = 0.05f;
        [SerializeField, Tooltip("Forward distance in meters used when probing for a potential step.")]
        private float stepCheckDistance = 0.4f;
        [SerializeField, Tooltip("Radius used for the forward step detection casts. Leave at 0 to reuse the ground check radius.")]
        private float stepCheckRadius = 0f;
        [SerializeField, Tooltip("Speed in meters per second used to interpolate towards the new height when stepping up. Set to 0 for an instant snap.")]
        private float stepSnapSpeed = 10f;

        [Header("Jumping")]
        [SerializeField, Tooltip("Downward acceleration applied while airborne. Negative values accelerate towards the ground.")]
        private float gravity = -30f;
        [SerializeField, Tooltip("Small downward force applied to keep the agent snapped to the ground.")]
        private float groundedGravity = -2f;
        [SerializeField, Tooltip("Desired jump height in meters for the initial jump.")]
        private float jumpHeight = 2f;
        [SerializeField, Tooltip("Number of additional jumps allowed while airborne.")]
        private int extraAirJumps = 0;
        [SerializeField, Tooltip("Time in seconds after leaving the ground during which a jump can still be triggered.")]
        private float coyoteTime = 0.15f;
        [SerializeField, Tooltip("Time window in seconds during which a buffered jump request remains valid.")]
        private float jumpBufferTime = 0.15f;
        [SerializeField, Tooltip("Multiplier applied to gravity while the agent is descending.")]
        private float fallGravityMultiplier = 2f;
        [SerializeField, Tooltip("Multiplier applied to gravity when the jump button is released early.")]
        private float jumpReleaseGravityMultiplier = 2f;
        [SerializeField, Tooltip("Maximum downward speed reached while falling.")]
        private float terminalVelocity = -60f;
        
        [field: SerializeField]
        public AllegianceType allegiance { get; set; }
        [field: SerializeField]
        public Health health { get; private set; }
        public StatusHandler statusHandler { get; private set; }

        [Header("Animation (optional)")]
        [SerializeField] private SpriteAnimator spriteAnimator;
        [Header("State Animations")]
        [SerializeField] private AgentAnimationRequest idleAnimation;
        [SerializeField] private AgentAnimationRequest movingAnimation;
        [SerializeField] private AgentAnimationRequest airborneAnimation;
        [SerializeField] private AgentAnimationRequest stunnedAnimation;

        [Header("Look/Aim (optional)")]
        [SerializeField] private MovementProfile aimProfile;
        [SerializeField] protected bool faceAimDirection;
        [SerializeField] protected Vector3 aimPivot;
        [SerializeField] protected float aimOffset;
        public GameObject aimPivotObject;
        
        
        public bool IsMoving => new Vector3(currentVelocity.x, 0f, currentVelocity.z).sqrMagnitude > 0.0001f;


        protected MovementParams baseParams;
        protected MovementParams effective;

        public Rigidbody rb { get; private set; }
        protected float verticalSpeed;
        Vector3 groundNormal = Vector3.up;
        readonly RaycastHit[] groundHitsBuffer = new RaycastHit[8];
        float lastGroundedTime = float.NegativeInfinity;
        float jumpRequestTime = float.NegativeInfinity;
        int jumpPhase;
        bool jumpButtonHeld;
        StateMachine machine;
        AgentRoot agentRoot;
        bool actionStatesInitialized;
        readonly AgentAnimationController animationController = new AgentAnimationController();
        ActionState pendingActionState;

        public StateMachine Machine => machine;
        public AgentRoot AgentRoot => agentRoot;
        public State ActiveLeafState => machine?.Root?.Leaf();
        public string ActiveStatePath => machine?.StatePath();

        protected Vector2 moveInput  {
            get
            {
                if (IsMovementLocked)
                    return Vector2.zero;

                if (inputReader?.moveInput.sqrMagnitude > 0.0001f && snapFacingTo8)
                    return SnapTo8(inputReader?.moveInput ?? Vector2.zero);
                return inputReader?.moveInput ?? Vector2.zero;
            }
        }
        protected Vector3 currentVelocity;
        protected Vector3 facingDirection = Vector3.forward;
        protected MyCountTimer movementLockTimer;
        public bool IsMovementLocked => movementLockTimer.IsRunning;

        readonly Dictionary<MovementProfile, CancellationTokenSource> movementOverrideTokens = new Dictionary<MovementProfile, CancellationTokenSource>();
        CancellationTokenSource activeActionToken;
        bool isActionRunning;

        bool inputSubscribed;
        

        public Func<Vector3, Vector3> InputRedirector { get; set; }

        public bool IsPerformingAction => isActionRunning;
        public float AnimatorSpeed
        {
            get => spriteAnimator ? spriteAnimator.SpeedMultiplier : 1f;
            set
            {
                if (spriteAnimator)
                {
                    spriteAnimator.SetSpeed(Mathf.Max(0f, value));
                }
            }
        }

        internal void RequestActionState(ActionState state)
        {
            if (state == null)
            {
                return;
            }

            pendingActionState = state;
        }

        internal bool TryConsumeActionState(State expectedParent, out ActionState state)
        {
            if (pendingActionState != null && (expectedParent == null || pendingActionState.Parent == expectedParent))
            {
                state = pendingActionState;
                pendingActionState = null;
                return true;
            }

            state = null;
            return false;
        }

        internal void CancelPendingActionState(ActionState state)
        {
            if (pendingActionState == state)
            {
                pendingActionState = null;
            }
        }

        public AgentAnimationController AnimationController => animationController;
        internal AgentAnimationRequest IdleAnimation => idleAnimation;
        internal AgentAnimationRequest MovingAnimation => movingAnimation;
        internal AgentAnimationRequest AirborneAnimation => airborneAnimation;
        internal AgentAnimationRequest StunnedAnimation => stunnedAnimation;

        protected virtual void Awake()
        {
            statusHandler = new StatusHandler(this);
            agentRoot = new AgentRoot(null, this);
            machine = new StateMachineBuilder(agentRoot).Build();
            
            movementLockTimer = new MyCountTimer(0f);
            inputReader ??= new InputReader();
            inputReader.controller = this;
            rb = GetComponent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.useGravity = false;

            if (!groundCollider)
            {
                groundCollider = GetComponent<Collider>() ?? GetComponentInChildren<Collider>();
            }

            EvaluateGroundImmediate();

            if (!spriteAnimator)
            {
                spriteAnimator = GetComponent<SpriteAnimator>() ?? GetComponentInChildren<SpriteAnimator>();
            }

            animationController.Initialize(spriteAnimator);
            
            if (baseProfile != null)
            {
                baseParams = new MovementParams(baseProfile);
            }
            else
            {
                Debug.LogError($"{nameof(AgentController)} on {name} requires a base movement profile.", this);
            }
        }

        protected virtual void Start()
        {
            if (baseProfile != null)
            {
                baseParams = new MovementParams(baseProfile);
            }
            InitializeActionExecuteStates();
            SubscribeToInput();
        }

        protected virtual void OnEnable()
        {
            inputReader.EnableCharacterActions(true);
        }

        protected virtual void OnDisable()
        {
            UnsubscribeFromInput();
            CancelActiveActionTask();
            ClearMovementOverrideTasks();
        }

        protected virtual void OnDestroy()
        {
            UnsubscribeFromInput();
            CancelActiveActionTask();
            ClearMovementOverrideTasks();
        }

        protected virtual void Update()
        {
            if (faceAimDirection && inputReader.isAiming)
            {
                Vector3 aim = AimDirection;
                if (aim.sqrMagnitude > 0.0001f)
                {
                    facingDirection = aim.normalized;
                }
            }
            else if (moveInput.sqrMagnitude > 0.0001f)
            {
                Vector3 moveDir = new Vector3(moveInput.x, 0f, moveInput.y);
                facingDirection = moveDir.sqrMagnitude > 0.0001f ? moveDir.normalized : facingDirection;
            }

            if (inputReader.isAiming)
            {
                SetAimIndicator(AimDirection);
            }
            else
            {
                SetAimIndicator(facingDirection);
            }

            animationController.UpdateDirection(moveInput, facingDirection);

            machine?.Tick(Time.deltaTime);
        }

        protected virtual void FixedUpdate()
        {
            if (baseParams == null)
            {
                return;
            }

            effective = baseParams.WithOverrides();
            Vector3 bodyVelocity = rb.linearVelocity;
            Vector3 planarVelocity = new Vector3(bodyVelocity.x, 0f, bodyVelocity.z);
            float vertical = bodyVelocity.y;

            UpdateGroundedState(ref vertical);

            Vector3 desiredInput = IsMovementLocked ? Vector3.zero : new Vector3(moveInput.x, 0f, moveInput.y);
            if (InputRedirector != null)
            {
                desiredInput = InputRedirector(desiredInput);
            }
            
            float inputMagnitude = desiredInput.magnitude;
            Vector3 desiredDirection = inputMagnitude > 0.0001f ? desiredInput / inputMagnitude : Vector3.zero;
            if (normalizeDiagonals && inputMagnitude > 0.0001f)
            {
                desiredDirection = desiredDirection.normalized;
                inputMagnitude = 1f;
            }

            if (grounded && desiredDirection.sqrMagnitude > 0.0001f)
            {
                Vector3 projected = Vector3.ProjectOnPlane(desiredDirection, groundNormal);
                if (projected.sqrMagnitude > 0.0001f)
                {
                    desiredDirection = projected.normalized;
                }
            }

            Vector3 desired = desiredDirection * inputMagnitude;

            float targetSpeed = effective.moveSpeed;
            Vector3 targetVelocity = new Vector3(desired.x, 0f, desired.z) * (targetSpeed * effective.maxSpeedMult);

            if (inputMagnitude <= 0.0001f || targetVelocity == Vector3.zero)
            {
                planarVelocity = Vector3.Lerp(planarVelocity, targetVelocity, effective.deceleration * Time.fixedDeltaTime);
            }
            else
            {
                Vector3 desiredPlanarDirection = targetVelocity.sqrMagnitude > 0.0001f ? targetVelocity.normalized : Vector3.zero;
                Vector3 currentPlanarDirection = planarVelocity.sqrMagnitude > 0.0001f ? planarVelocity.normalized : Vector3.zero;
                float pressedDeceleration = Mathf.Abs(effective.inputDeceleration);

                float ax = ResolveAxisRate(planarVelocity.x, targetVelocity.x, effective.acceleration,
                    effective.turnAcceleration, pressedDeceleration, currentPlanarDirection, desiredPlanarDirection);
                float az = ResolveAxisRate(planarVelocity.z, targetVelocity.z, effective.acceleration,
                    effective.turnAcceleration, pressedDeceleration, currentPlanarDirection, desiredPlanarDirection);
                planarVelocity.x = MoveTowards(planarVelocity.x, targetVelocity.x, Mathf.Abs(ax) * Time.fixedDeltaTime);
                planarVelocity.z = MoveTowards(planarVelocity.z, targetVelocity.z, Mathf.Abs(az) * Time.fixedDeltaTime);
            }
            
            TryResolveStep(ref planarVelocity, ref vertical, desiredDirection, Time.fixedDeltaTime);

            bool jumpPerformed = TryHandleJump(ref vertical);
            ApplyGravity(ref vertical, Time.fixedDeltaTime, jumpPerformed);

            verticalSpeed = vertical;
            currentVelocity = new Vector3(planarVelocity.x, vertical, planarVelocity.z);
            rb.linearVelocity = currentVelocity;
        }

        public Vector3 AimOrigin => transform.position + aimPivot;

        public void SetAimPosition(Vector3 worldPosition)
        {
            Vector3 direction = worldPosition - AimOrigin;
            SetAimIndicator(direction);
        }

        public Vector3 GetAimPosition(Vector3 direction)
        {
            Vector3 aimDir = direction;
            if (aimDir.sqrMagnitude <= 0.0001f)
            {
                aimDir = facingDirection.sqrMagnitude > 0.0001f ? facingDirection : Vector3.forward;
            }

            return AimOrigin + aimDir.normalized * aimOffset;
        }

        public Vector3 SetAimIndicator(Vector3 direction)
        {
            Vector3 aimDir = direction;
            if (aimDir.sqrMagnitude <= 0.0001f)
            {
                aimDir = facingDirection.sqrMagnitude > 0.0001f ? facingDirection : Vector3.forward;
            }

            Vector3 position = AimOrigin + aimDir.normalized * aimOffset;
            if (aimPivotObject != null)
            {
                aimPivotObject.transform.position = position;
                Vector3 planarDirection = new Vector3(aimDir.x, 0f, aimDir.z);
                if (planarDirection.sqrMagnitude > 0.0001f)
                {
                    aimPivotObject.transform.rotation = Quaternion.LookRotation(-planarDirection, Vector3.up);
                }
            }

            return position;
        }

        protected virtual void SubscribeToInput()
        {
            if (inputReader == null || inputSubscribed)
            {
                return;
            }
            
            inputReader.Aim += HandleAimInput;
            inputReader.Jump += HandleJumpInput;
            inputSubscribed = true;
        }

        protected virtual void UnsubscribeFromInput()
        {
            if (inputReader == null || !inputSubscribed)
            {
                return;
            }
            
            inputReader.Aim -= HandleAimInput;
            inputReader.Jump -= HandleJumpInput;
            inputSubscribed = false;
        }

        protected virtual void HandleAimInput(bool pressed, Vector2 direction)
        {
            Vector3 worldDirection = new Vector3(direction.x, 0f, direction.y);
            if (!pressed)
            {
                RemoveMovementModifier(aimProfile);
            }
            else
            {
                if (faceAimDirection && worldDirection.sqrMagnitude > 0.0001f)
                {
                    facingDirection = worldDirection.normalized;
                }
                ApplyMovementModifier(aimProfile, -1f);
                SetAimIndicator(worldDirection);
            }
        }

        protected virtual void HandleJumpInput(bool pressed)
        {
            jumpButtonHeld = pressed;
            if (pressed)
            {
                QueueJump();
            }
        }

        public void QueueJump()
        {
            jumpRequestTime = Time.time;
        }

        public void SetJumpHeld(bool held)
        {
            jumpButtonHeld = held;
            if (held)
            {
                QueueJump();
            }
        }

        public virtual void OnLocomotionStateEntered() { }
        public virtual void OnMovementLockedStateEntered() { }
        public virtual void OnMovementLockedStateExited() { }

        private void AddMovementOverride(MovementProfile profile)
        {
            if (baseParams == null || !profile)
            {
                return;
            }

            baseParams.AddOverride(profile);
        }

        private void RemoveMovementOverride(MovementProfile profile)
        {
            if (baseParams == null || !profile)
            {
                return;
            }

            baseParams.RemoveOverride(profile);
        }

        void ClearMovementOverrideTasks()
        {
            if (movementOverrideTokens.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<MovementProfile, CancellationTokenSource> kvp in movementOverrideTokens)
            {
                CancellationTokenSource source = kvp.Value;
                if (source != null)
                {
                    source.Cancel();
                    source.Dispose();
                }
            }

            movementOverrideTokens.Clear();
        }

        public void ApplyMovementModifier(MovementProfile profile, float duration)
        {
            if (!profile)
            {
                return;
            }

            AddMovementOverride(profile);
            if (duration > 0f)
            {
                CancelMovementOverrideTask(profile);
                var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
                movementOverrideTokens[profile] = linkedToken;
                RemoveMovementOverrideAfterDelayAsync(profile, duration, linkedToken.Token).Forget();
            }
            else
            {
                CancelMovementOverrideTask(profile);
            }
        }

        public void RemoveMovementModifier(MovementProfile profile)
        {
            if (!profile)
            {
                return;
            }

            CancelMovementOverrideTask(profile);
            RemoveMovementOverride(profile);
        }

        void CancelMovementOverrideTask(MovementProfile profile)
        {
            if (movementOverrideTokens.TryGetValue(profile, out CancellationTokenSource source) && source != null)
            {
                source.Cancel();
                source.Dispose();
                movementOverrideTokens.Remove(profile);
            }
        }

        async UniTask RemoveMovementOverrideAfterDelayAsync(MovementProfile profile, float duration, CancellationToken token)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(duration), cancellationToken: token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            RemoveMovementOverride(profile);
            movementOverrideTokens.Remove(profile);
        }

        public void LockMovement(float duration, bool zeroVelocity)
        {
            movementLockTimer.Start(duration);
            if (zeroVelocity)
            {
                currentVelocity = Vector3.zero;
                if (rb)
                {
                    rb.linearVelocity = Vector3.zero;
                }
                verticalSpeed = 0f;
            }
        }
        
        public void UnlockMovement()
        {
            if (IsMovementLocked)
                movementLockTimer.Stop();
        }

        public virtual Vector3 AimDirection
        {
            get
            {
                if (inputReader.isAiming)
                {
                    Vector2 aim = inputReader.aimDirection;
                    if (aim.sqrMagnitude > 0.0001f)
                        return new Vector3(aim.x, 0f, aim.y).normalized;
                }

                if (facingDirection.sqrMagnitude > 0.0001f)
                    return facingDirection.normalized;

                return Vector3.forward;
            }
        }

        public void ForceFacing(Vector3 direction)
        {
            if (direction.sqrMagnitude > 0.0001f)
            {
                facingDirection = direction.normalized;
            }
        }

        void InitializeActionExecuteStates()
        {
            if (actionStatesInitialized || agentRoot == null)
            {
                return;
            }

            actionStatesInitialized = true;
            CharacterAction[] actions = GetComponents<CharacterAction>();
            foreach (CharacterAction action in actions)
            {
                action?.ConfigureActionState(agentRoot);
            }
        }

        public void PlayActionAnimation(AnimationClip clip)
        {
            if (!spriteAnimator || clip == null)
            {
                return;
            }

            spriteAnimator.SetClip(clip, 0f, true);
            spriteAnimator.Play();
        }

        public UniTask ExecuteAction(AgentActionDefinition action, AgentActionRuntime runtime)
        {
            if (!action || runtime == null)
            {
                return UniTask.CompletedTask;
            }

            CancelActiveActionTask();

            var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            activeActionToken = linkedToken;
            isActionRunning = true;
            return RunActionAsync(action, runtime, linkedToken);
        }

        void CancelActiveActionTask()
        {
            if (activeActionToken != null)
            {
                activeActionToken.Cancel();
                activeActionToken.Dispose();
                activeActionToken = null;
            }

            isActionRunning = false;
        }

        async UniTask RunActionAsync(AgentActionDefinition action, AgentActionRuntime runtime, CancellationTokenSource linkedToken)
        {
            try
            {
                await action.ExecuteAsync(runtime, linkedToken.Token);
            }
            catch (OperationCanceledException)
            {
                // Swallow cancellation
            }
            finally
            {
                if (activeActionToken == linkedToken)
                {
                    activeActionToken.Dispose();
                    activeActionToken = null;
                    isActionRunning = false;
                }
                else
                {
                    linkedToken.Dispose();
                }
            }
        }

        void EvaluateGroundImmediate()
        {
            float vertical = 0f;
            UpdateGroundedState(ref vertical);
            verticalSpeed = vertical;
        }

        void UpdateGroundedState(ref float verticalVelocity)
        {
            bool wasGrounded = grounded;
            if (CheckGround(out RaycastHit hit))
            {
                grounded = true;
                groundNormal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.up;
                lastGroundedTime = Time.time;
                if (!wasGrounded)
                {
                    jumpPhase = 0;
                }

                if (verticalVelocity < 0f)
                {
                    verticalVelocity = Mathf.Max(verticalVelocity, groundedGravity);
                }
            }
            else
            {
                if (wasGrounded)
                {
                    lastGroundedTime = Time.time;
                }

                grounded = false;
                groundNormal = Vector3.up;
            }
        }

        bool TryHandleJump(ref float verticalVelocity)
        {
            if (Time.time > jumpRequestTime + Mathf.Max(0f, jumpBufferTime))
            {
                jumpRequestTime = float.NegativeInfinity;
                return false;
            }

            bool wasGroundedRecently = Time.time <= lastGroundedTime + Mathf.Max(0f, coyoteTime);
            int maxJumps = Mathf.Max(1, extraAirJumps + 1);
            bool canGroundJump = grounded || (wasGroundedRecently && jumpPhase == 0);

            if (!canGroundJump && jumpPhase >= maxJumps)
            {
                return false;
            }

            float jumpSpeed = CalculateJumpSpeed(jumpHeight);
            if (verticalVelocity < 0f)
            {
                verticalVelocity = 0f;
            }

            verticalVelocity = Mathf.Max(verticalVelocity, jumpSpeed);
            grounded = false;
            groundNormal = Vector3.up;
            jumpPhase = canGroundJump ? 1 : jumpPhase + 1;
            jumpRequestTime = float.NegativeInfinity;
            return true;
        }

        void ApplyGravity(ref float verticalVelocity, float deltaTime, bool jumpJustPerformed)
        {
            if (grounded)
            {
                verticalVelocity = Mathf.Max(verticalVelocity, groundedGravity);
                return;
            }

            bool isHoldingJump = jumpButtonHeld || jumpJustPerformed;

            float multiplier = verticalVelocity > 0f
                ? (isHoldingJump ? 1f : Mathf.Max(1f, jumpReleaseGravityMultiplier))
                : Mathf.Max(1f, fallGravityMultiplier);

            verticalVelocity += gravity * multiplier * deltaTime;

            if (terminalVelocity < 0f)
            {
                verticalVelocity = Mathf.Max(verticalVelocity, terminalVelocity);
            }
            else
            {
                verticalVelocity = Mathf.Min(verticalVelocity, terminalVelocity);
            }
        }

        float CalculateJumpSpeed(float height)
        {
            float g = Mathf.Abs(gravity);
            return Mathf.Sqrt(2f * g * Mathf.Max(0f, height));
        }

        void TryResolveStep(ref Vector3 planarVelocity, ref float verticalVelocity, Vector3 desiredDirection, float deltaTime)
        {
            if (!enableStepHandling || !grounded)
            {
                return;
            }

            Vector3 planarDirection = planarVelocity.sqrMagnitude > 0.0001f
                ? new Vector3(planarVelocity.x, 0f, planarVelocity.z)
                : new Vector3(desiredDirection.x, 0f, desiredDirection.z);

            if (planarDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            planarDirection = planarDirection.normalized;

            float lowerOffset = Mathf.Max(0.001f, stepCheckVerticalOffset);
            float allowableStepHeight = Mathf.Max(0f, maxStepHeight);
            if (allowableStepHeight <= 0f)
            {
                return;
            }

            float checkDistance = Mathf.Max(0.01f, stepCheckDistance);
            float radius = stepCheckRadius > 0f ? stepCheckRadius : Mathf.Max(0.01f, GetGroundCheckRadius());

            Bounds bounds;
            if (groundCollider)
            {
                bounds = groundCollider.bounds;
            }
            else
            {
                bounds = new Bounds(rb.position, Vector3.zero);
            }

            Vector3 basePosition = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            Vector3 lowOrigin = basePosition + Vector3.up * lowerOffset;
            Vector3 highOrigin = basePosition + Vector3.up * (lowerOffset + allowableStepHeight);

            if (!Physics.SphereCast(lowOrigin, radius, planarDirection, out RaycastHit lowerHit, checkDistance, groundLayers, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            if (IsSelfCollider(lowerHit.collider))
            {
                return;
            }

            if (Physics.SphereCast(highOrigin, radius, planarDirection, out RaycastHit upperHit, checkDistance, groundLayers, QueryTriggerInteraction.Ignore))
            {
                if (!IsSelfCollider(upperHit.collider))
                {
                    return;
                }
            }

            float forwardDistance = Mathf.Min(checkDistance, lowerHit.distance + Mathf.Max(radius, 0.05f));
            Vector3 stepOrigin = highOrigin + planarDirection * forwardDistance;
            float downwardDistance = allowableStepHeight + lowerOffset + 0.1f;

            if (!Physics.Raycast(stepOrigin, Vector3.down, out RaycastHit stepHit, downwardDistance, groundLayers, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            if (IsSelfCollider(stepHit.collider))
            {
                return;
            }

            if (Vector3.Angle(stepHit.normal, Vector3.up) > maxGroundSlopeAngle)
            {
                return;
            }

            float heightDifference = stepHit.point.y - basePosition.y;
            if (heightDifference <= 0f || heightDifference > allowableStepHeight + 0.01f)
            {
                return;
            }

            float snapSpeed = Mathf.Max(0f, stepSnapSpeed);
            float stepDelta = snapSpeed <= 0f ? heightDifference : Mathf.Min(heightDifference, snapSpeed * deltaTime);
            if (stepDelta <= 0f)
            {
                return;
            }

            Vector3 newPosition = rb.position + Vector3.up * stepDelta;
            rb.position = newPosition;

            verticalVelocity = Mathf.Max(verticalVelocity, 0f);
            grounded = true;
            groundNormal = stepHit.normal.sqrMagnitude > 0.0001f ? stepHit.normal.normalized : Vector3.up;
            lastGroundedTime = Time.time;
            jumpPhase = 0;
        }

        bool CheckGround(out RaycastHit bestHit)
        {
            float radius = Mathf.Max(0.01f, GetGroundCheckRadius());
            Vector3 origin = GetGroundCheckOrigin(radius, out float castDistance);
            int hitCount = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, groundHitsBuffer, castDistance,
                groundLayers, QueryTriggerInteraction.Ignore);

            bestHit = default;
            float closest = float.PositiveInfinity;
            bool found = false;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = groundHitsBuffer[i];
                Collider candidate = hit.collider;
                if (!candidate)
                {
                    continue;
                }

                if (candidate == groundCollider || candidate.transform.IsChildOf(transform))
                {
                    continue;
                }

                float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);
                if (slopeAngle > maxGroundSlopeAngle)
                {
                    continue;
                }

                if (hit.distance < closest)
                {
                    closest = hit.distance;
                    bestHit = hit;
                    found = true;
                }
            }

            return found;
        }

        bool IsSelfCollider(Collider candidate)
        {
            if (!candidate)
            {
                return false;
            }

            if (candidate == groundCollider)
            {
                return true;
            }

            return candidate.transform.IsChildOf(transform);
        }

        float GetGroundCheckRadius()
        {
            if (groundCheckRadius > 0f)
            {
                return groundCheckRadius;
            }

            if (!groundCollider)
            {
                return 0.3f;
            }

            Bounds bounds = groundCollider.bounds;
            return Mathf.Max(0.05f, Mathf.Min(bounds.extents.x, bounds.extents.z));
        }

        Vector3 GetGroundCheckOrigin(float radius, out float distance)
        {
            float offset = Mathf.Max(groundCheckOffset, 0.01f);
            float extraDistance = Mathf.Max(groundCheckDistance, 0.01f);

            if (groundCollider)
            {
                Bounds bounds = groundCollider.bounds;
                float verticalExtent = Mathf.Max(bounds.extents.y, radius);
                distance = verticalExtent + extraDistance + offset;
                return bounds.center + Vector3.up * (verticalExtent + offset);
            }

            distance = radius + extraDistance + offset;
            return transform.position + Vector3.up * (radius + offset);
        }

        public void ApplyKnockback(Vector3 direction, float force, KnockbackSettings settings)
        {
            if (!rb)
            {
                return;
            }

            Vector3 normalized = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;

            if (settings.clearVelocityBeforeImpact)
            {
                Vector3 current = rb.linearVelocity;
                rb.linearVelocity = new Vector3(0f, current.y, 0f);
            }

            rb.AddForce(normalized * force, settings.forceMode);
        }

        static float ResolveAxisRate(float currentVelocity, float targetVelocity, float acceleration, float turnAcceleration,
            float pressedDeceleration, Vector3 currentDirection, Vector3 desiredDirection)
        {
            float delta = targetVelocity - currentVelocity;
            if (Mathf.Abs(delta) <= 0.0001f)
            {
                return 0f;
            }

            bool hasTargetVelocity = Mathf.Abs(targetVelocity) > 0.0001f;
            bool reversing = hasTargetVelocity && Mathf.Abs(currentVelocity) > 0.0001f &&
                             !Mathf.Approximately(Mathf.Sign(targetVelocity), Mathf.Sign(currentVelocity));
            bool overSpeeding = hasTargetVelocity && Mathf.Abs(targetVelocity) < Mathf.Abs(currentVelocity) &&
                                Mathf.Abs(currentVelocity) > 0.0001f;
            
            if (!hasTargetVelocity)
            {
                return pressedDeceleration;
            }

            if (reversing)
            {
                float alignment = 0f;
                if (currentDirection.sqrMagnitude > 0.0001f && desiredDirection.sqrMagnitude > 0.0001f)
                {
                    alignment = Mathf.Abs(Vector3.Dot(currentDirection.normalized, desiredDirection.normalized));
                }

                return acceleration + turnAcceleration * alignment;
            }

            if (overSpeeding)
            {
                return pressedDeceleration;
            }

            return acceleration;
        }

        static float MoveTowards(float current, float target, float maxDelta)
        {
            return Mathf.Abs(target - current) <= maxDelta
                ? target
                : current + Mathf.Sign(target - current) * maxDelta;
        }

        static Vector3 SnapTo8(Vector3 v)
        {
            if (v.sqrMagnitude < 0.0001f) return Vector3.zero;
            Vector2 planar = new Vector2(v.x, v.z);
            if (planar.sqrMagnitude < 0.0001f) return Vector3.zero;
            float angle = Mathf.Atan2(planar.y, planar.x);
            float sector = Mathf.Round(angle / (Mathf.PI / 4f));
            float snapped = sector * (Mathf.PI / 4f);
            Vector2 snapped2D = new Vector2(Mathf.Cos(snapped), Mathf.Sin(snapped));
            return new Vector3(snapped2D.x, 0f, snapped2D.y).normalized;
        }

        static int FacingIndex(Vector3 dir)
        {
            if (dir.sqrMagnitude < 0.0001f) return 0;
            Vector2 planar = new Vector2(dir.x, dir.z);
            if (planar.sqrMagnitude < 0.0001f) return 0;
            float angleDeg = Mathf.Repeat(Mathf.Atan2(planar.y, planar.x) * Mathf.Rad2Deg + 90f + 22.5f, 360f);
            return Mathf.FloorToInt(angleDeg / 45f) % 8;
        }
        
        public void ApplyDamage(DamageInfo damageInfo)
        {
            health?.ApplyDamage(damageInfo);
        }
        
        protected virtual void OnDrawGizmosSelected()
        {
            // Draw gizmos for ground check
            float radius = Mathf.Max(0.01f, GetGroundCheckRadius());
            Vector3 origin = GetGroundCheckOrigin(radius, out float distance);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(origin - Vector3.up * distance, radius);
            
            //Draw step check gizmos
            if (enableStepHandling)
            {
                Bounds bounds;
                if (groundCollider)
                {
                    bounds = groundCollider.bounds;
                }
                else
                {
                    bounds = new Bounds(transform.position, Vector3.zero);
                }

                Vector3 basePosition = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                Vector3 lowOrigin = basePosition + Vector3.up * Mathf.Max(0.001f, stepCheckVerticalOffset);
                Vector3 highOrigin = basePosition + Vector3.up * (Mathf.Max(0.001f, stepCheckVerticalOffset) + Mathf.Max(0f, maxStepHeight));
                float checkDistance = Mathf.Max(0.01f, stepCheckDistance);
                float stepRadius = stepCheckRadius > 0f ? stepCheckRadius : Mathf.Max(0.01f, GetGroundCheckRadius());

                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(lowOrigin + transform.forward * checkDistance, stepRadius);
                Gizmos.DrawWireSphere(highOrigin + transform.forward * checkDistance, stepRadius);
            }
        }
    }
}
