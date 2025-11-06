using System;
using System.Collections.Generic;
using System.Threading;
using _PinBoy.Scripts.Gameplay.Actions;
using _PinBoy.Scripts.Gameplay.Effects;
using AdvancedController;
using Cysharp.Threading.Tasks;
using UnityEngine;
using _PinBoy.Scripts.Utils;
using HSM;

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
        public bool grounded { get; private set; } = true;
        
        [field: SerializeField]
        public AllegianceType allegiance { get; set; }
        [field: SerializeField]
        public Health health { get; private set; }
        public StatusHandler statusHandler { get; private set; }

        [Header("Animation (optional)")]
        [SerializeField] private Animator animator;
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
        
        
        public bool IsMoving => currentVelocity.sqrMagnitude > 0.0001f;
        

        protected MovementParams baseParams;
        protected MovementParams effective;

        public Rigidbody rb { get; private set; }
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
        protected CountdownTimer movementLockTimer;
        public bool IsMovementLocked => movementLockTimer.IsRunning;

        readonly Dictionary<MovementProfile, CancellationTokenSource> movementOverrideTokens = new Dictionary<MovementProfile, CancellationTokenSource>();
        CancellationTokenSource activeActionToken;
        bool isActionRunning;

        bool inputSubscribed;
        

        public Func<Vector3, Vector3> InputRedirector { get; set; }

        public bool IsPerformingAction => isActionRunning;
        public float AnimatorSpeed
        {
            get => animator ? animator.speed : 1f;
            set
            {
                if (animator)
                {
                    animator.speed = value;
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
            
            movementLockTimer = new CountdownTimer(0f);
            inputReader ??= new InputReader();
            inputReader.controller = this;
            rb = GetComponent<Rigidbody>();
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;

            animationController.Initialize(animator);
            
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

            Vector3 animFacing = snapFacingTo8 ? SnapTo8(facingDirection) : facingDirection.normalized;
            if (animator)
            {
                animator.SetFloat("MoveX", currentVelocity.x);
                animator.SetFloat("MoveZ", currentVelocity.z);
                animator.SetFloat("Speed", currentVelocity.magnitude);
                animator.SetInteger("FacingIndex", FacingIndex(animFacing));
            }

            machine?.Tick(Time.deltaTime);
        }

        protected virtual void FixedUpdate()
        {
            if (baseParams == null)
            {
                return;
            }
            
            effective = baseParams.WithOverrides();
            //Debug.Log($"Current params: speed={effective.moveSpeed}, accel={effective.acceleration}, decel={effective.deceleration}, turnAccel={effective.turnAcceleration}, inputDecel={effective.inputDeceleration}, maxSpeedMult={effective.maxSpeedMult}");
            Vector3 bodyVelocity = rb.linearVelocity;
            float verticalVelocity = bodyVelocity.y;
            Vector3 planarVelocity = new Vector3(bodyVelocity.x, 0f, bodyVelocity.z);

            Vector3 desired = IsMovementLocked ? Vector3.zero : new Vector3(moveInput.x, 0f, moveInput.y);
            if (InputRedirector != null)
                desired = InputRedirector(desired);

            if (normalizeDiagonals && desired.sqrMagnitude > 0.0001f)
                desired = desired.normalized;

            float targetSpeed = effective.moveSpeed;
            Vector3 targetVelocity = desired * (targetSpeed * effective.maxSpeedMult);

            if (moveInput == Vector2.zero || targetVelocity == Vector3.zero)
            {
                planarVelocity = Vector3.Lerp(planarVelocity, targetVelocity, effective.deceleration * Time.fixedDeltaTime);
            }
            else
            {
                Vector3 desiredDirection = desired.sqrMagnitude > 0.0001f ? desired.normalized : Vector3.zero;
                Vector3 currentDirection = planarVelocity.sqrMagnitude > 0.0001f ? planarVelocity.normalized : Vector3.zero;
                float pressedDeceleration = Mathf.Abs(effective.inputDeceleration);

                float ax = ResolveAxisRate(planarVelocity.x, targetVelocity.x, effective.acceleration,
                    effective.turnAcceleration, pressedDeceleration, currentDirection, desiredDirection);
                float az = ResolveAxisRate(planarVelocity.z, targetVelocity.z, effective.acceleration,
                    effective.turnAcceleration, pressedDeceleration, currentDirection, desiredDirection);

                planarVelocity.x = MoveTowards(planarVelocity.x, targetVelocity.x, Mathf.Abs(ax) * Time.fixedDeltaTime);
                planarVelocity.z = MoveTowards(planarVelocity.z, targetVelocity.z, Mathf.Abs(az) * Time.fixedDeltaTime);
            }

            currentVelocity = new Vector3(planarVelocity.x, verticalVelocity, planarVelocity.z);
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
                    aimPivotObject.transform.rotation = Quaternion.LookRotation(planarDirection, Vector3.up);
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
            inputSubscribed = true;
        }

        protected virtual void UnsubscribeFromInput()
        {
            if (inputReader == null || !inputSubscribed)
            {
                return;
            }
            
            inputReader.Aim -= HandleAimInput;
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

        public void PlayActionAnimation(AnimationClip clip, float crossFade)
        {
            if (!animator || !clip)
            {
                return;
            }

            if (crossFade > 0f)
            {
                animator.CrossFadeInFixedTime(clip.name, crossFade);
            }
            else
            {
                animator.Play(clip.name);
            }
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
            throw new NotImplementedException();
        }

        public void DealDamage(DamageInfo damageInfo)
        {
            throw new NotImplementedException();
        }
    }
}
