using System;
using System.Collections.Generic;
using System.Threading;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Effects;
using AdvancedController;
using UnityEngine;
using UnityEngine.Events;
using HSM;
using UtilityAI;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    public enum ExecutionPhase
    {
        None = -1,
        Windup,
        Active,
        Recovery
    }
    
    [RequireComponent(typeof(AgentController))]
    public abstract class CharacterAction : MonoBehaviour
    {
        public enum PressBinding
        {
            PrimaryAction,
            SecondaryAction,
            Interact,
            AimPrimary,
            AimSecondary,
            Dash,
            Jump,
            Sprint
        }

        [Serializable]
        public struct PhaseExecution
        {
            [Min(0f)] public float Duration;
            [Tooltip("Actions that are allowed to interrupt during this phase.")]
            public CharacterAction[] ActionInterrupts;
            
            public CharacterAction[] Interrupts => ActionInterrupts ?? Array.Empty<CharacterAction>();
        }

        [Header("Action")]
        public PressBinding binding;
        public UnityAction<bool> actionTrigger;
        [SerializeField] protected AgentActionDefinition actionDefinition;
        [SerializeField, Min(0f)] protected float actionMagnitude = 1f;
        [SerializeField] protected bool skipIfActionInProgress = true;
        [Header("Animation")]
        [SerializeField] private AgentAnimationRequest defaultAnimationRequest;
        [SerializeField] private bool scaleDefaultAnimationToPhaseDuration;
        [Header("Phase Animations")]
        [SerializeField] private bool usePhaseAnimationRequests = true;
        [SerializeField] private AgentAnimationRequest windupAnimationRequest;
        [SerializeField] private bool scaleWindupAnimationToPhaseDuration = true;
        [SerializeField] private AgentAnimationRequest activeAnimationRequest;
        [SerializeField] private bool scaleActiveAnimationToPhaseDuration = true;
        [SerializeField] private AgentAnimationRequest recoveryAnimationRequest;
        [SerializeField] private bool scaleRecoveryAnimationToPhaseDuration = true;

        [Header("Phase Execution")]
        [SerializeField] protected PhaseExecution windupPhaseExecution;
        [SerializeField] protected PhaseExecution activePhaseExecution;
        [SerializeField] protected PhaseExecution recoveryPhaseExecution;

        [field: SerializeField, HideInInspector]
        public AgentController Controller { get; private set; }
        protected InputReader InputReader => Controller != null ? Controller.inputReader : null;
        protected Vector3 LastAimWorldPosition => lastAimWorldPosition;
        protected virtual bool UsesAimInput => false;

        PendingExecution pendingExecution;
        CancellationTokenSource aiAimCancellation;
        bool aimPressed;
        Vector3 lastAimWorldPosition;
        public UnityAction actionStarted;
        public UnityAction actionComplete;
        public Rigidbody body => Controller?.rb;
        ActionState _actionState;
        private AgentAnimationRequest runtimeAnimationRequest;
        protected ExecutionPhase CurrentPhase { get; private set; } = ExecutionPhase.None;
        protected float CurrentPhaseDuration { get; private set; }
        private event Action<AgentAnimationRequest> animationRequestChanged;
        private event Action<ExecutionPhase> phaseStarted;
        private event Action<ExecutionPhase> phaseCompleted;
        PhaseSequence phaseSequence;

        public virtual void OnValidate()
        {
            if (!Controller)
                Controller = GetComponent<AgentController>();
        }

        protected virtual void Awake()
        {
            if (!Controller)
            {
                Debug.LogError($"{GetType().Name} requires an {nameof(AgentController)} on the same GameObject.", this);
            }

            actionTrigger ??= DefaultActionTrigger;
            runtimeAnimationRequest = NormalizeAnimationRequest(defaultAnimationRequest);
            defaultAnimationRequest = runtimeAnimationRequest;
            windupAnimationRequest = NormalizeAnimationRequest(windupAnimationRequest);
            activeAnimationRequest = NormalizeAnimationRequest(activeAnimationRequest);
            recoveryAnimationRequest = NormalizeAnimationRequest(recoveryAnimationRequest);

            phaseSequence = new PhaseSequence(this);
        }

        protected virtual void Start()
        {
        }

        protected virtual void OnEnable()
        {
            SubscribeInput();
        }

        protected virtual void FixedUpdate()
        {
            phaseSequence?.Tick(Time.fixedDeltaTime);
        }

        protected virtual void OnDisable()
        {
            UnsubscribeInput();
            CancelAiAimRoutine();
            aimPressed = false;
            CancelPhaseSequence();
        }

        protected virtual void OnDestroy()
        {
            CancelAiAimRoutine();
            aimPressed = false;
            CancelPhaseSequence();
        }

        internal void ConfigureActionState(AgentRoot root)
        {
            Controller ??= GetComponent<AgentController>();
            if (Controller == null || root == null)
            {
                return;
            }

            if (_actionState == null)
            {
                _actionState = CreateActionState(root);
            }

            RegisterActionStateWithParent(_actionState);
        }

        protected abstract ActionState CreateActionState(AgentRoot root);

        protected virtual AgentState GetDefaultActionParent(AgentRoot root)
        {
            return root != null ? root.Grounded : null;
        }

        void RegisterActionStateWithParent(ActionState state)
        {
            if (state?.Parent == null)
            {
                return;
            }

            state.Parent.RegisterDynamicChild(state);
        }

        internal ActionState ActionState => _actionState;

        internal AgentAnimationRequest GetAnimationRequest()
        {
            return runtimeAnimationRequest;
        }

        internal ExecutionPhase ActivePhase => CurrentPhase;

        internal bool IsPhaseSequenceActive => phaseSequence is { IsRunning: true };

        internal PhaseExecution GetPhaseExecution(ExecutionPhase phase)
        {
            return phase switch
            {
                ExecutionPhase.Windup => windupPhaseExecution,
                ExecutionPhase.Active => activePhaseExecution,
                ExecutionPhase.Recovery => recoveryPhaseExecution,
                _ => default
            };
        }

        protected void BeginPhaseSequence(bool includeWindup = true, bool includeActive = true,
            bool includeRecovery = true, float windupDurationOverride = 0f, float activeDurationOverride = 0f,
            float recoveryDurationOverride = 0f, bool invokeCompleteOnFinish = true)
        {
            CancelPhaseSequence();

            List<PhaseRequest> requests = new List<PhaseRequest>();

            if (includeWindup)
            {
                requests.Add(BuildPhaseRequest(ExecutionPhase.Windup, windupDurationOverride));
            }

            if (includeActive)
            {
                requests.Add(BuildPhaseRequest(ExecutionPhase.Active, activeDurationOverride));
            }

            if (includeRecovery)
            {
                requests.Add(BuildPhaseRequest(ExecutionPhase.Recovery, recoveryDurationOverride));
            }

            phaseSequence?.Begin(requests, invokeCompleteOnFinish);
        }

        PhaseRequest BuildPhaseRequest(ExecutionPhase phase, float phaseDuration)
        {
            if (!TryGetPhaseAnimation(phase, out AgentAnimationRequest request, out bool scaleToDuration))
            {
                ResetAnimationRequest();
                return CreatePhaseRequest(phase, AgentAnimationRequest.None, phaseDuration, false);
            }

            return CreatePhaseRequest(phase, request, phaseDuration, scaleToDuration);
        }

        protected AgentAnimationRequest PreparePhaseAnimation(AgentAnimationRequest request, float targetDuration,
            bool scaleToDuration)
        {
            AgentAnimationRequest animationRequest = request;
            if (!scaleToDuration || Controller == null)
            {
                return animationRequest;
            }

            AnimationClip resolvedClip = Controller.AnimationController.GetClip(animationRequest);
            if (!resolvedClip)
            {
                return animationRequest;
            }

            float duration = Mathf.Max(0.0001f, targetDuration);
            float clipLength = resolvedClip.length;
            if (clipLength <= 0f)
            {
                return animationRequest;
            }

            animationRequest.overrideSpeed = true;
            float speed = animationRequest.playbackSpeed > 0f ? animationRequest.playbackSpeed : 1f;
            animationRequest.playbackSpeed = speed * (clipLength / duration);
            return animationRequest;
        }

        internal void RegisterAnimationListener(Action<AgentAnimationRequest> listener)
        {
            animationRequestChanged += listener;
        }

        internal void UnregisterAnimationListener(Action<AgentAnimationRequest> listener)
        {
            animationRequestChanged -= listener;
        }

        internal void RegisterPhaseListeners(Action<ExecutionPhase> onPhaseStarted, Action<ExecutionPhase> onPhaseCompleted)
        {
            if (onPhaseStarted != null)
            {
                phaseStarted += onPhaseStarted;
            }

            if (onPhaseCompleted != null)
            {
                phaseCompleted += onPhaseCompleted;
            }
        }

        internal void UnregisterPhaseListeners(Action<ExecutionPhase> onPhaseStarted, Action<ExecutionPhase> onPhaseCompleted)
        {
            if (onPhaseStarted != null)
            {
                phaseStarted -= onPhaseStarted;
            }

            if (onPhaseCompleted != null)
            {
                phaseCompleted -= onPhaseCompleted;
            }
        }

        internal void ResetAnimationRequest()
        {
            BroadcastPhaseStarted(ExecutionPhase.None);
            UpdateAnimationRequest(defaultAnimationRequest);
        }

        internal void ApplyAnimationRequest(AgentAnimationRequest request)
        {
            UpdateAnimationRequest(request);
        }

        protected void ApplyPhaseAnimation(ExecutionPhase phase, float phaseDuration = 0f)
        {
            PhaseRequest request = BuildPhaseRequest(phase, phaseDuration);
            if (request.HasAnimation)
            {
                ApplyAnimationRequest(request.AnimationRequest);
            }
        }

        protected void ApplyPhaseAnimation(ExecutionPhase phase, AgentAnimationRequest request, float phaseDuration, bool scaleToDuration)
        {
            PhaseRequest phaseRequest = CreatePhaseRequest(phase, request, phaseDuration, scaleToDuration);
            if (phaseRequest.HasAnimation)
            {
                ApplyAnimationRequest(phaseRequest.AnimationRequest);
            }
        }

        protected virtual bool TryGetPhaseAnimation(ExecutionPhase phase, out AgentAnimationRequest request, out bool scaleToDuration)
        {
            request = AgentAnimationRequest.None;
            scaleToDuration = false;

            AgentAnimationRequest candidate = defaultAnimationRequest;
            bool candidateScale = scaleDefaultAnimationToPhaseDuration;

            if (usePhaseAnimationRequests)
            {
                candidate = phase switch
                {
                    ExecutionPhase.Windup => windupAnimationRequest,
                    ExecutionPhase.Active => activeAnimationRequest,
                    ExecutionPhase.Recovery => recoveryAnimationRequest,
                    _ => defaultAnimationRequest
                };

                candidateScale = phase switch
                {
                    ExecutionPhase.Windup => scaleWindupAnimationToPhaseDuration,
                    ExecutionPhase.Active => scaleActiveAnimationToPhaseDuration,
                    ExecutionPhase.Recovery => scaleRecoveryAnimationToPhaseDuration,
                    _ => scaleDefaultAnimationToPhaseDuration
                };

                if (!candidate.IsValid)
                {
                    candidate = defaultAnimationRequest;
                    candidateScale = scaleDefaultAnimationToPhaseDuration;
                }
            }

            if (!candidate.IsValid)
            {
                return false;
            }

            request = candidate;
            scaleToDuration = candidateScale;
            return true;
        }

        float ResolvePhaseDuration(ExecutionPhase phase, float overrideDuration)
        {
            float provided = Mathf.Max(0f, overrideDuration);
            if (provided > 0f)
            {
                return provided;
            }

            PhaseExecution execution = GetPhaseExecution(phase);
            return execution.Duration > 0f ? execution.Duration : 0f;
        }

        PhaseRequest CreatePhaseRequest(ExecutionPhase phase, AgentAnimationRequest request, float phaseDuration,
            bool scaleToDuration)
        {
            float resolvedDuration = ResolvePhaseDuration(phase, phaseDuration);
            AgentAnimationRequest sanitized = NormalizeAnimationRequest(request);
            bool hasAnimation = sanitized.IsValid;
            AgentAnimationRequest prepared = hasAnimation
                ? PreparePhaseAnimation(sanitized, resolvedDuration, scaleToDuration && resolvedDuration > 0f)
                : sanitized;

            return new PhaseRequest
            {
                Phase = phase,
                Duration = resolvedDuration,
                AnimationRequest = prepared,
                HasAnimation = hasAnimation && prepared.IsValid
            };
        }

        void BroadcastPhaseStarted(ExecutionPhase phase)
        {
            CurrentPhase = phase;
            phaseStarted?.Invoke(phase);
            OnPhaseStarted(phase);
        }

        void BroadcastPhaseCompleted(ExecutionPhase phase)
        {
            phaseCompleted?.Invoke(phase);
            OnPhaseCompleted(phase);
        }

        protected virtual void OnPhaseStarted(ExecutionPhase phase)
        {
        }

        protected virtual void OnPhaseCompleted(ExecutionPhase phase)
        {
        }

        protected virtual void OnPhaseTick(ExecutionPhase phase, float elapsed, float deltaTime)
        {
        }

        protected void CancelPhaseSequence()
        {
            phaseSequence?.Cancel();
        }

        protected void CompleteCurrentPhase()
        {
            phaseSequence?.AdvancePhase();
        }

        void ResetPhaseSequence(bool invokeComplete)
        {
            CurrentPhaseDuration = 0f;
            BroadcastPhaseStarted(ExecutionPhase.None);
            BroadcastPhaseCompleted(ExecutionPhase.None);

            if (invokeComplete)
            {
                actionComplete?.Invoke();
            }
        }

        protected void SetAnimationRequest(AgentAnimationRequest request)
        {
            UpdateAnimationRequest(request);
        }

        AgentAnimationRequest NormalizeAnimationRequest(AgentAnimationRequest request)
        {
            return request.Sanitized();
        }

        void UpdateAnimationRequest(AgentAnimationRequest request)
        {
            AgentAnimationRequest normalized = NormalizeAnimationRequest(request);
            if (runtimeAnimationRequest.Equals(normalized))
            {
                runtimeAnimationRequest = normalized;
                return;
            }

            runtimeAnimationRequest = normalized;
            animationRequestChanged?.Invoke(runtimeAnimationRequest);
        }

        sealed class PhaseSequence
        {
            readonly CharacterAction owner;
            readonly Queue<PhaseRequest> phaseQueue = new();
            PhaseRequest currentPhase;
            bool invokeCompleteOnFinish;
            bool active;
            float remainingPhaseTime;

            internal bool IsRunning => active || phaseQueue.Count > 0;

            internal PhaseSequence(CharacterAction owner)
            {
                this.owner = owner;
            }

            internal void Begin(IEnumerable<PhaseRequest> phases, bool invokeComplete)
            {
                phaseQueue.Clear();

                if (phases != null)
                {
                    foreach (PhaseRequest request in phases)
                    {
                        if (request.Phase == ExecutionPhase.None)
                        {
                            continue;
                        }

                        phaseQueue.Enqueue(request);
                    }
                }

                invokeCompleteOnFinish = invokeComplete;
                active = phaseQueue.Count > 0;

                if (active)
                {
                    StartNextPhase();
                }
                else
                {
                    owner.ResetPhaseSequence(invokeCompleteOnFinish);
                }
            }

            internal void Cancel()
            {
                bool hadActiveSequence = active || phaseQueue.Count > 0;
                phaseQueue.Clear();
                active = false;
                remainingPhaseTime = 0f;

                if (hadActiveSequence)
                {
                    owner.ResetPhaseSequence(false);
                }
            }

            internal void AdvancePhase()
            {
                if (!active)
                {
                    return;
                }

                remainingPhaseTime = 0f;
                CompletePhase();
            }

            internal void Tick(float deltaTime)
            {
                if (!active || remainingPhaseTime <= 0f)
                {
                    return;
                }

                float clampedDelta = Mathf.Max(0f, deltaTime);
                remainingPhaseTime = Mathf.Max(0f, remainingPhaseTime - clampedDelta);

                float elapsed = Mathf.Max(0f, currentPhase.Duration - remainingPhaseTime);
                owner.OnPhaseTick(currentPhase.Phase, elapsed, clampedDelta);

                if (remainingPhaseTime <= 0f)
                {
                    CompletePhase();
                }
            }

            void StartNextPhase()
            {
                if (phaseQueue.Count == 0)
                {
                    CompleteSequence();
                    return;
                }

                currentPhase = phaseQueue.Dequeue();
                owner.CurrentPhaseDuration = currentPhase.Duration;
                owner.BroadcastPhaseStarted(currentPhase.Phase);

                if (currentPhase.HasAnimation)
                {
                    owner.ApplyAnimationRequest(currentPhase.AnimationRequest);
                }

                remainingPhaseTime = Mathf.Max(0f, currentPhase.Duration);

                if (remainingPhaseTime <= 0f)
                {
                    CompletePhase();
                }
            }

            void CompletePhase()
            {
                remainingPhaseTime = 0f;
                owner.BroadcastPhaseCompleted(currentPhase.Phase);
                StartNextPhase();
            }

            void CompleteSequence()
            {
                active = false;
                remainingPhaseTime = 0f;
                owner.ResetPhaseSequence(invokeCompleteOnFinish);
            }
        }

        readonly struct PhaseRequest
        {
            public ExecutionPhase Phase { get; init; }
            public float Duration { get; init; }
            public AgentAnimationRequest AnimationRequest { get; init; }
            public bool HasAnimation { get; init; }
        }

        void SubscribeInput()
        {
            InputReader reader = InputReader;
            if (reader == null)
            {
                return;
            }

            if (actionTrigger == null)
            {
                actionTrigger = DefaultActionTrigger;
            }

            SubscribeToInput(reader);
        }

        void UnsubscribeInput()
        {
            InputReader reader = InputReader;
            if (reader == null)
            {
                return;
            }

            UnsubscribeFromInput(reader);
        }

        protected virtual void SubscribeToInput(InputReader reader)
        {
            switch (binding)
            {
                case PressBinding.AimPrimary:
                    reader.AimPrimary += actionTrigger;
                    break;
                case PressBinding.AimSecondary:
                    reader.AimSecondary += actionTrigger;
                    break;
                case PressBinding.Interact:
                    reader.Interact += actionTrigger;
                    break;
                case PressBinding.Dash:
                    reader.Dash += actionTrigger;
                    break;
                case PressBinding.Jump:
                    reader.Jump += actionTrigger;
                    break;
                case PressBinding.Sprint:
                    reader.Sprint += actionTrigger;
                    break;
                case PressBinding.PrimaryAction:
                    reader.PrimaryAction += actionTrigger;
                    break;
                case PressBinding.SecondaryAction:
                    reader.SecondaryAction += actionTrigger;
                    break;
            }

            if (UsesAimInput)
            {
                reader.Aim += HandleAimInput;
            }
        }

        protected virtual void UnsubscribeFromInput(InputReader reader)
        {
            switch (binding)
            {
                case PressBinding.AimPrimary:
                    reader.AimPrimary -= actionTrigger;
                    break;
                case PressBinding.AimSecondary:
                    reader.AimSecondary -= actionTrigger;
                    break;
                case PressBinding.Interact:
                    reader.Interact -= actionTrigger;
                    break;
                case PressBinding.Dash:
                    reader.Dash -= actionTrigger;
                    break;
                case PressBinding.Jump:
                    reader.Jump -= actionTrigger;
                    break;
                case PressBinding.Sprint:
                    reader.Sprint -= actionTrigger;
                    break;
                case PressBinding.PrimaryAction:
                    reader.PrimaryAction -= actionTrigger;
                    break;
                case PressBinding.SecondaryAction:
                    reader.SecondaryAction -= actionTrigger;
                    break;
            }

            if (UsesAimInput)
            {
                reader.Aim -= HandleAimInput;
            }
        }

        protected virtual void Reset()
        {
            actionMagnitude = Mathf.Max(0f, actionMagnitude);
            skipIfActionInProgress = true;
        }

        protected void RecordInputDirection(Vector3 direction)
        {
            if (direction.sqrMagnitude > 0.0001f && InputReader != null)
            {
                Vector2 planar = new Vector2(direction.x, direction.z);
                if (planar.sqrMagnitude > 0.0001f)
                {
                    InputReader.InvokeMove(planar.normalized);
                }
            }
        }

        protected void PreparePendingExecution(Context context, Transform target, float magnitudeMultiplier = 1f)
        {
            pendingExecution = new PendingExecution
            {
                HasValue = true,
                Context = context,
                Target = target,
                MagnitudeMultiplier = Mathf.Max(0f, magnitudeMultiplier)
            };
        }

        protected void ClearPendingExecution()
        {
            pendingExecution = default;
        }

        protected bool ExecuteConfiguredAction(IDamageable targetOverride = null, float magnitudeMultiplier = 1f)
        {
            if (!actionDefinition || !Controller)
            {
                ClearPendingExecution();
                return false;
            }

            if (skipIfActionInProgress && Controller.IsPerformingAction)
            {
                ClearPendingExecution();
                return false;
            }

            IDamageable runtimeTarget = targetOverride;
            Context runtimeContext = pendingExecution.Context;

            float runtimeMagnitude = Mathf.Max(0f, actionMagnitude);
            if (pendingExecution.HasValue && pendingExecution.MagnitudeMultiplier > 0f)
            {
                runtimeMagnitude *= pendingExecution.MagnitudeMultiplier;
            }

            float extraMultiplier = Mathf.Max(0f, magnitudeMultiplier);
            if (extraMultiplier > 0f)
            {
                runtimeMagnitude *= extraMultiplier;
            }

            if (runtimeMagnitude <= 0f)
            {
                runtimeMagnitude = actionMagnitude > 0f ? actionMagnitude : 1f;
            }

            var runtime = new AgentActionRuntime(Controller, this, runtimeTarget, runtimeMagnitude);
            Controller.ExecuteAction(actionDefinition, runtime).Forget();

            ClearPendingExecution();
            return true;
        }

        public virtual void TriggerFromAI(Context context, Transform target, Vector3 direction, float magnitudeMultiplier = 1f,
            float aimDelay = 0f, bool followTarget = false)
        {
            PreparePendingExecution(context, target, magnitudeMultiplier);

            if (UsesAimInput && InputReader != null)
            {
                Vector3 aimPosition = ResolveAimWorldPosition(direction, target);
                lastAimWorldPosition = aimPosition;
                Vector3 aimVector = aimPosition - GetAimOrigin();
                if (aimVector.sqrMagnitude <= 0.0001f && Controller)
                {
                    aimVector = Controller.AimDirection;
                }

                RecordInputDirection(aimVector);
                StartAiAimRoutine(aimPosition, target, aimDelay, followTarget).Forget();
            }
            else
            {
                Vector3 aimVector = direction;
                if (aimVector.sqrMagnitude <= 0.0001f && Controller)
                {
                    aimVector = Controller.AimDirection;
                }

                RecordInputDirection(aimVector);
                if (aimVector.sqrMagnitude > 0.0001f)
                {
                    lastAimWorldPosition = GetAimOrigin() + aimVector;
                }

                InvokeActionTrigger(true);
            }
        }

        public virtual void ReleaseFromAI(Context context, Transform target)
        {
            if (UsesAimInput && InputReader != null)
            {
                Vector2 aimDirection = InputReader.aimDirection;
                Vector3 aimVector = new Vector3(aimDirection.x, 0f, aimDirection.y);
                if (aimVector.sqrMagnitude <= 0.0001f)
                {
                    Vector3 aimPosition = ResolveAimWorldPosition(Vector3.zero, target);
                    aimVector = aimPosition - GetAimOrigin();
                }

                RecordInputDirection(aimVector);
                lastAimWorldPosition = GetAimOrigin() + aimVector;
                InputReader.CancelAim();
            }

            CancelAiAimRoutine();
            InvokeActionTrigger(false);
        }

        protected Vector3 ResolveAimWorldPosition(Vector3 directionOrPosition, Transform target)
        {
            if (target)
            {
                return target.position;
            }

            Vector3 origin = GetAimOrigin();

            if (directionOrPosition.sqrMagnitude > 0.0001f)
            {
                return origin + directionOrPosition;
            }

            if (Controller)
            {
                Vector3 controllerAim = Controller.AimDirection;
                if (controllerAim.sqrMagnitude > 0.0001f)
                {
                    return origin + controllerAim;
                }
            }

            return origin + Vector3.forward;
        }

        protected Vector3 ResolveAimLocalPosition(Vector3 directionOrPosition, Transform target)
        {
            if (target)
            {
                return Controller.transform.InverseTransformPoint(target.position);
            }

            if (directionOrPosition.sqrMagnitude > 0.0001f)
            {
                return directionOrPosition;
            }

            if (Controller)
            {
                Vector3 controllerAim = Controller.AimDirection;
                if (controllerAim.sqrMagnitude > 0.0001f)
                {
                    return controllerAim;
                }
            }

            return Vector3.forward;
        }

        protected Vector3 GetAimOrigin()
        {
            if (Controller)
            {
                return Controller.AimOrigin;
            }

            return transform.position;
        }

        async UniTaskVoid StartAiAimRoutine(Vector3 initialAimPosition, Transform target, float aimDelay, bool followTarget)
        {
            CancelAiAimRoutine();

            if (InputReader == null)
            {
                InvokeActionTrigger(true);
                return;
            }

            aiAimCancellation = new CancellationTokenSource();
            CancellationToken token = aiAimCancellation.Token;

            try
            {
                float remaining = Mathf.Max(0f, aimDelay);
                float elapsed = 0f;

                Vector3 initialVector = initialAimPosition - GetAimOrigin();
                lastAimWorldPosition = initialAimPosition;
                Vector2 initialPlanar = new Vector2(initialVector.x, initialVector.z);
                InputReader.InvokeAim(initialPlanar);

                while (!token.IsCancellationRequested && elapsed < remaining)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    elapsed += Time.deltaTime;

                    Vector3 nextPosition = initialAimPosition;
                    if (followTarget && target)
                    {
                        nextPosition = target.position;
                    }

                    Vector3 aimVector = nextPosition - GetAimOrigin();
                    if (aimVector.sqrMagnitude <= 0.0001f && Controller)
                    {
                        aimVector = Controller.AimDirection;
                    }

                    RecordInputDirection(aimVector);
                    lastAimWorldPosition = nextPosition;
                    Vector2 planar = new Vector2(aimVector.x, aimVector.z);
                    InputReader.InvokeAim(planar);
                }

                if (!token.IsCancellationRequested)
                {
                    InvokeActionTrigger(true);
                }
            }
            catch (OperationCanceledException)
            {
                // Swallow cancellation triggered by ReleaseFromAI.
            }
            finally
            {
                if (aiAimCancellation != null)
                {
                    aiAimCancellation.Dispose();
                    aiAimCancellation = null;
                }
            }
        }

        void CancelAiAimRoutine()
        {
            if (aiAimCancellation != null)
            {
                aiAimCancellation.Cancel();
                aiAimCancellation.Dispose();
                aiAimCancellation = null;
            }
            aimPressed = false;
        }

        void HandleAimInput(bool pressed, Vector2 direction)
        {
            Vector3 worldOffset = new Vector3(direction.x, 0f, direction.y);
            Vector3 worldPosition = GetAimOrigin() + worldOffset;
            lastAimWorldPosition = worldPosition;

            if (pressed)
            {
                if (!aimPressed)
                {
                    aimPressed = true;
                    OnAimStarted(worldPosition);
                }

                OnAimUpdated(worldPosition);
            }
            else
            {
                if (aimPressed)
                {
                    OnAimReleased(worldPosition);
                }

                aimPressed = false;
            }
        }

        protected virtual void OnAimStarted(Vector3 worldPosition)
        {
        }

        protected virtual void OnAimUpdated(Vector3 worldPosition)
        {
        }

        protected virtual void OnAimReleased(Vector3 worldPosition)
        {
        }

        protected void InvokeActionTrigger(bool pressed)
        {
            actionTrigger?.Invoke(pressed);
        }

        protected virtual void OnActionPressed()
        {
            ExecuteConfiguredAction();
        }

        protected virtual void OnActionReleased()
        {
        }

        protected virtual void DefaultActionTrigger(bool pressed)
        {
            if (pressed)
            {
                OnActionPressed();
            }
            else
            {
                OnActionReleased();
            }
        }

        struct PhaseRequest
        {
            public ExecutionPhase Phase;
            public AgentAnimationRequest AnimationRequest;
            public float Duration;
            public bool HasAnimation;
        }

        struct PendingExecution
        {
            public bool HasValue;
            public Context Context;
            public Transform Target;
            public float MagnitudeMultiplier;
        }
    }
}
