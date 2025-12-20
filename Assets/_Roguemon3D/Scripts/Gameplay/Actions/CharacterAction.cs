using System;
using System.Collections.Generic;
using System.Threading;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Effects;
using AdvancedController;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using HSM;
using UtilityAI;
using _Roguemon3D.Scripts.ThirdParty.ImprovedTimers;

namespace _PinBoy.Scripts.Gameplay.Actions
{
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

        public enum ActionPhase
        {
            None = -1,
            Windup = 0,
            Active = 1,
            Recovery = 2
        }

        [Serializable]
        protected struct PhaseAnimationSettings
        {
            public bool usePhaseAnimations;
            public AgentAnimationRequest defaultAnimation;
            public AgentAnimationRequest windupAnimation;
            public AgentAnimationRequest activeAnimation;
            public AgentAnimationRequest recoveryAnimation;
            public float animationCrossFade;
            public float animationSpeedMultiplier;
            public bool scaleAnimationSpeedToDuration;
            public bool scaleWindupAnimationToDuration;
            public bool scaleActiveAnimationToDuration;
            public bool scaleRecoveryAnimationToDuration;
            public bool overrideAnimationSpeed;
            public float windupDuration;
            public float activeDuration;
            public float recoveryDuration;
            public float totalDuration;

            public float GetPhaseDuration(ActionPhase phase, ActionPhaseDurations fallback)
            {
                return phase switch
                {
                    ActionPhase.Windup => ResolveDuration(windupDuration, fallback.Windup),
                    ActionPhase.Active => ResolveDuration(activeDuration, fallback.Active),
                    ActionPhase.Recovery => ResolveDuration(recoveryDuration, fallback.Recovery),
                    _ => 0f
                };
            }

            public float GetTotalDuration(ActionPhaseDurations fallback)
            {
                float duration = totalDuration;
                return duration > 0f ? duration : fallback.TotalDuration;
            }

            static float ResolveDuration(float overrideDuration, float fallback)
            {
                return overrideDuration > 0f ? overrideDuration : Mathf.Max(0f, fallback);
            }
        }

        [Serializable]
        protected struct ActionPhaseDurations
        {
            public float Windup;
            public float Active;
            public float Recovery;

            public ActionPhaseDurations(float windup, float active, float recovery)
            {
                Windup = Mathf.Max(0f, windup);
                Active = Mathf.Max(0f, active);
                Recovery = Mathf.Max(0f, recovery);
            }

            public float TotalDuration => Mathf.Max(0f, Windup + Active + Recovery);
        }

        [Serializable]
        protected struct PhaseFxSettings
        {
            public PhaseFX[] WindupFx;
            public PhaseFX[] ActiveFx;
            public PhaseFX[] RecoveryFx;

            public PhaseFX[] GetWindup(PhaseFX[] fallback)
            {
                return WindupFx ?? fallback ?? Array.Empty<PhaseFX>();
            }

            public PhaseFX[] GetActive(PhaseFX[] fallback)
            {
                return ActiveFx ?? fallback ?? Array.Empty<PhaseFX>();
            }

            public PhaseFX[] GetRecovery(PhaseFX[] fallback)
            {
                return RecoveryFx ?? fallback ?? Array.Empty<PhaseFX>();
            }
        }

        [Header("Action")]
        public PressBinding binding;
        public UnityAction<bool> actionTrigger;
        [SerializeField] protected AgentActionDefinition actionDefinition;
        [SerializeField, Min(0f)] protected float actionMagnitude = 1f;
        [SerializeField] protected bool skipIfActionInProgress = true;
        [Header("Animation")]
        [SerializeField] private AgentAnimationRequest defaultAnimationRequest;

        [Header("Phase Defaults")]
        [SerializeField] protected ActionPhaseDurations defaultPhaseDurations = new ActionPhaseDurations(0f, 0f, 0f);
        [SerializeField] protected PhaseAnimationSettings defaultPhaseAnimations = new PhaseAnimationSettings
        {
            animationCrossFade = 0.1f,
            animationSpeedMultiplier = 1f,
            defaultAnimation = AgentAnimationRequest.None
        };

        [Header("Phase FX")]
        [SerializeReference] [SerializeField] PhaseFX[] windupFx = Array.Empty<PhaseFX>();
        [SerializeReference] [SerializeField] PhaseFX[] activeFx = Array.Empty<PhaseFX>();
        [SerializeReference] [SerializeField] PhaseFX[] recoveryFx = Array.Empty<PhaseFX>();

        [field: SerializeField, HideInInspector]
        public AgentController Controller { get; private set; }
        protected InputReader InputReader => Controller != null ? Controller.inputReader : null;
        protected Vector3 LastAimWorldPosition => lastAimWorldPosition;
        protected virtual bool UsesAimInput => false;
        protected bool IsAnyPhaseRunning => currentPhase != ActionPhase.None;
        protected bool IsInPhase(ActionPhase phase) => currentPhase == phase;

        PendingExecution pendingExecution;
        CancellationTokenSource aiAimCancellation;
        bool aimPressed;
        Vector3 lastAimWorldPosition;
        public UnityAction actionStarted;
        public UnityAction actionComplete;
        public Rigidbody body => Controller?.rb;
        ActionState _actionState;
        private AgentAnimationRequest runtimeAnimationRequest;
        private event Action<AgentAnimationRequest> animationRequestChanged;
        internal event Action<ActionPhase, float> phaseStarted;
        internal event Action<ActionPhase> phaseEnded;
        protected MyFixedTimer windupTimer;
        protected MyFixedTimer activeTimer;
        protected MyFixedTimer recoveryTimer;
        ActionPhase currentPhase = ActionPhase.None;
        ActionPhaseDurations currentPhaseDurations;
        PhaseAnimationSettings currentPhaseAnimations;
        bool autoCompleteWindupWithoutDuration = true;
        CharacterAction.ActionPhase _activeActionPhase = CharacterAction.ActionPhase.None;
        readonly Dictionary<ActionPhase, List<IPhaseFxInstance>> runningPhaseFx = new();
        PhaseFxSettings currentPhaseFx;

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

            windupTimer = new MyFixedTimer(0f);
            windupTimer.OnTimerFinish += HandleWindupTimerFinished;

            activeTimer = new MyFixedTimer(0f);
            activeTimer.OnTimerFinish += HandleActiveTimerFinished;

            recoveryTimer = new MyFixedTimer(0f);
            recoveryTimer.OnTimerFinish += HandleRecoveryTimerFinished;
        }

        protected virtual void Start()
        {
        }

        protected virtual void OnEnable()
        {
            SubscribeInput();
        }

        protected virtual void OnDisable()
        {
            UnsubscribeInput();
            CancelAiAimRoutine();
            aimPressed = false;
            CancelActionPhases();
        }

        protected virtual void OnDestroy()
        {
            CancelAiAimRoutine();
            aimPressed = false;

            if (windupTimer != null)
            {
                windupTimer.OnTimerFinish -= HandleWindupTimerFinished;
            }

            if (activeTimer != null)
            {
                activeTimer.OnTimerFinish -= HandleActiveTimerFinished;
            }

            if (recoveryTimer != null)
            {
                recoveryTimer.OnTimerFinish -= HandleRecoveryTimerFinished;
            }
        }

        internal void ConfigureActionState(AgentRoot root)
        {
            if (_actionState != null)
            {
                return;
            }

            Controller ??= GetComponent<AgentController>();
            if (Controller == null || root == null)
            {
                return;
            }

            _actionState = CreateActionExecuteState(root);
        }

        protected virtual ActionState CreateActionExecuteState(AgentRoot root)
        {
            return null;
        }

        internal ActionState ActionState => _actionState;

        internal AgentAnimationRequest GetAnimationRequest()
        {
            return runtimeAnimationRequest;
        }

        internal void RegisterAnimationListener(Action<AgentAnimationRequest> listener)
        {
            animationRequestChanged += listener;
        }

        internal void UnregisterAnimationListener(Action<AgentAnimationRequest> listener)
        {
            animationRequestChanged -= listener;
        }

        internal void ResetAnimationRequest()
        {
            UpdateAnimationRequest(defaultAnimationRequest);
        }

        internal void ApplyAnimationRequest(AgentAnimationRequest request)
        {
            UpdateAnimationRequest(request);
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

        protected PhaseAnimationSettings GetDefaultPhaseAnimations()
        {
            PhaseAnimationSettings settings = defaultPhaseAnimations;
            settings.animationCrossFade = Mathf.Max(0f, settings.animationCrossFade);
            settings.animationSpeedMultiplier = settings.animationSpeedMultiplier <= 0f
                ? 1f
                : settings.animationSpeedMultiplier;

            if (!settings.defaultAnimation.IsValid)
            {
                settings.defaultAnimation = defaultAnimationRequest;
            }

            return settings;
        }

        protected PhaseFxSettings GetDefaultPhaseFxSettings()
        {
            return new PhaseFxSettings
            {
                WindupFx = windupFx,
                ActiveFx = activeFx,
                RecoveryFx = recoveryFx
            };
        }

        protected void StartActionPhases(bool completeWindupWhenDurationMissing = true)
        {
            StartActionPhases(defaultPhaseDurations, GetDefaultPhaseAnimations(), completeWindupWhenDurationMissing);
        }

        protected void StartActionPhases(ActionPhaseDurations durations, PhaseAnimationSettings animations,
            bool completeWindupWhenDurationMissing = true, PhaseFxSettings? fxSettings = null)
        {
            currentPhaseDurations = durations;
            currentPhaseAnimations = animations;
            autoCompleteWindupWithoutDuration = completeWindupWhenDurationMissing;
            currentPhaseFx = ResolvePhaseFxSettings(fxSettings);

            CancelActionPhases(false);
            ResetAnimationRequest();
            BeginWindupPhase();
        }

        internal CharacterAction.ActionPhase ActiveActionPhase => _activeActionPhase;

        internal bool IsActionInProgress => IsAnyPhaseRunning;

        protected void CancelActionPhases(bool notify = true)
        {
            windupTimer?.Cancel();
            activeTimer?.Cancel();
            recoveryTimer?.Cancel();

            if (notify && currentPhase != ActionPhase.None)
            {
                OnPhaseCancelled(currentPhase);
            }

            if (_activeActionPhase != CharacterAction.ActionPhase.None)
            {
                phaseEnded?.Invoke(_activeActionPhase);
            }

            currentPhase = ActionPhase.None;
            SetActiveExecutionPhase(CharacterAction.ActionPhase.None);
            StopAllPhaseFx();
        }

        void BeginWindupPhase()
        {
            currentPhase = ActionPhase.Windup;
            ApplyPhaseAnimation(ActionPhase.Windup);
            NotifyPhaseStarted(ActionPhase.Windup, currentPhaseDurations.Windup);

            if (currentPhaseDurations.Windup > 0f)
            {
                windupTimer.Start(currentPhaseDurations.Windup);
            }
            else if (autoCompleteWindupWithoutDuration)
            {
                HandleWindupTimerFinished();
            }
        }

        void BeginActivePhase()
        {
            currentPhase = ActionPhase.Active;
            ApplyPhaseAnimation(ActionPhase.Active);
            NotifyPhaseStarted(ActionPhase.Active, currentPhaseDurations.Active);

            if (currentPhaseDurations.Active > 0f)
            {
                activeTimer.Start(currentPhaseDurations.Active);
            }
            else
            {
                HandleActiveTimerFinished();
            }
        }

        void BeginRecoveryPhase()
        {
            currentPhase = ActionPhase.Recovery;
            ApplyPhaseAnimation(ActionPhase.Recovery);
            NotifyPhaseStarted(ActionPhase.Recovery, currentPhaseDurations.Recovery);

            if (currentPhaseDurations.Recovery > 0f)
            {
                recoveryTimer.Start(currentPhaseDurations.Recovery);
            }
            else
            {
                HandleRecoveryTimerFinished();
            }
        }

        void HandleWindupTimerFinished()
        {
            if (currentPhase != ActionPhase.Windup)
            {
                return;
            }

            TryCompleteWindupPhase();
        }

        protected void TryCompleteWindupPhase()
        {
            if (!CanCompleteWindupPhase())
            {
                return;
            }

            CompleteWindupPhase();
        }

        protected void CompleteWindupPhase()
        {
            if (currentPhase != ActionPhase.Windup)
            {
                return;
            }

            windupTimer.Cancel();
            NotifyPhaseEnded(ActionPhase.Windup);
            BeginActivePhase();
        }

        void HandleActiveTimerFinished()
        {
            if (currentPhase != ActionPhase.Active)
            {
                return;
            }

            activeTimer.Cancel();
            NotifyPhaseEnded(ActionPhase.Active);
            BeginRecoveryPhase();
        }

        void HandleRecoveryTimerFinished()
        {
            if (currentPhase != ActionPhase.Recovery)
            {
                return;
            }

            recoveryTimer.Cancel();
            NotifyPhaseEnded(ActionPhase.Recovery);
            FinishPhaseSequence();
        }

        void FinishPhaseSequence()
        {
            currentPhase = ActionPhase.None;
            SetActiveExecutionPhase(CharacterAction.ActionPhase.None);
            StopAllPhaseFx();
            OnPhasesCompleted();
        }

        void NotifyPhaseStarted(ActionPhase phase, float duration)
        {
            CharacterAction.ActionPhase mapped = MapPhase(phase);
            SetActiveExecutionPhase(mapped);
            ApplyPhaseFx(phase, duration);
            phaseStarted?.Invoke(mapped, duration);
            OnPhaseStarted(phase, duration);
        }

        void NotifyPhaseEnded(ActionPhase phase)
        {
            OnPhaseEnded(phase);
            StopPhaseFx(phase);
            CharacterAction.ActionPhase mapped = MapPhase(phase);
            phaseEnded?.Invoke(mapped);
            if (mapped == _activeActionPhase)
            {
                SetActiveExecutionPhase(CharacterAction.ActionPhase.None);
            }
        }

        void SetActiveExecutionPhase(CharacterAction.ActionPhase phase)
        {
            _activeActionPhase = phase;
        }

        static CharacterAction.ActionPhase MapPhase(ActionPhase phase)
        {
            return phase switch
            {
                ActionPhase.Windup => CharacterAction.ActionPhase.Windup,
                ActionPhase.Active => CharacterAction.ActionPhase.Active,
                ActionPhase.Recovery => CharacterAction.ActionPhase.Recovery,
                _ => CharacterAction.ActionPhase.None
            };
        }

        void ApplyPhaseFx(ActionPhase phase, float duration)
        {
            PhaseFX[] fxArray = GetPhaseFxForPhase(phase);
            if (fxArray == null || fxArray.Length == 0)
            {
                return;
            }

            List<IPhaseFxInstance> activeInstances = GetOrCreateFxList(phase);
            foreach (PhaseFX fx in fxArray)
            {
                if (fx == null || Controller == null)
                {
                    continue;
                }

                IPhaseFxInstance instance = fx.Play(Controller, duration);
                if (instance != null)
                {
                    activeInstances.Add(instance);
                }
            }
        }

        void StopPhaseFx(ActionPhase phase)
        {
            if (!runningPhaseFx.TryGetValue(phase, out List<IPhaseFxInstance> instances))
            {
                return;
            }

            foreach (IPhaseFxInstance instance in instances)
            {
                instance?.Cancel();
            }

            instances.Clear();
        }

        void StopAllPhaseFx()
        {
            foreach (ActionPhase phase in runningPhaseFx.Keys)
            {
                StopPhaseFx(phase);
            }
        }

        PhaseFX[] GetPhaseFxForPhase(ActionPhase phase)
        {
            return phase switch
            {
                ActionPhase.Windup => currentPhaseFx.WindupFx,
                ActionPhase.Active => currentPhaseFx.ActiveFx,
                ActionPhase.Recovery => currentPhaseFx.RecoveryFx,
                _ => Array.Empty<PhaseFX>()
            };
        }

        PhaseFxSettings ResolvePhaseFxSettings(PhaseFxSettings? overrides)
        {
            PhaseFxSettings defaults = GetDefaultPhaseFxSettings();
            PhaseFxSettings resolved = overrides ?? defaults;

            return new PhaseFxSettings
            {
                WindupFx = resolved.GetWindup(defaults.WindupFx),
                ActiveFx = resolved.GetActive(defaults.ActiveFx),
                RecoveryFx = resolved.GetRecovery(defaults.RecoveryFx)
            };
        }

        List<IPhaseFxInstance> GetOrCreateFxList(ActionPhase phase)
        {
            if (!runningPhaseFx.TryGetValue(phase, out List<IPhaseFxInstance> instances))
            {
                instances = new List<IPhaseFxInstance>();
                runningPhaseFx[phase] = instances;
            }

            return instances;
        }

        void ApplyPhaseAnimation(ActionPhase phase)
        {
            if (Controller == null)
            {
                return;
            }

            if (!TryGetAnimationRequestForPhase(currentPhaseAnimations, phase, out AgentAnimationRequest request,
                    out float targetDuration, out bool scaleToDuration))
            {
                return;
            }

            AgentAnimationRequest animationRequest = PrepareAnimationRequest(currentPhaseAnimations, request, targetDuration,
                scaleToDuration);
            SetAnimationRequest(animationRequest);
        }

        bool TryGetAnimationRequestForPhase(PhaseAnimationSettings settings, ActionPhase phase,
            out AgentAnimationRequest request, out float targetDuration, out bool scaleToDuration)
        {
            request = AgentAnimationRequest.None;
            targetDuration = settings.GetTotalDuration(currentPhaseDurations);
            scaleToDuration = settings.scaleAnimationSpeedToDuration;

            if (!settings.usePhaseAnimations)
            {
                request = settings.defaultAnimation.IsValid ? settings.defaultAnimation : defaultAnimationRequest;
                return request.IsValid;
            }

            targetDuration = settings.GetPhaseDuration(phase, currentPhaseDurations);
            request = phase switch
            {
                ActionPhase.Windup => settings.windupAnimation,
                ActionPhase.Active => settings.activeAnimation,
                ActionPhase.Recovery => settings.recoveryAnimation,
                _ => AgentAnimationRequest.None
            };

            scaleToDuration = phase switch
            {
                ActionPhase.Windup => settings.scaleWindupAnimationToDuration,
                ActionPhase.Active => settings.scaleActiveAnimationToDuration,
                ActionPhase.Recovery => settings.scaleRecoveryAnimationToDuration,
                _ => false
            };

            if (!scaleToDuration)
            {
                scaleToDuration = settings.scaleAnimationSpeedToDuration;
            }

            if (!request.IsValid)
            {
                if (phase != ActionPhase.Windup || !settings.defaultAnimation.IsValid)
                {
                    return false;
                }

                request = settings.defaultAnimation;
                targetDuration = settings.GetTotalDuration(currentPhaseDurations);
                scaleToDuration = settings.scaleAnimationSpeedToDuration;
                return true;
            }

            return request.IsValid;
        }

        AgentAnimationRequest PrepareAnimationRequest(PhaseAnimationSettings settings, AgentAnimationRequest request,
            float targetDuration, bool scaleToDuration)
        {
            AgentAnimationRequest animationRequest = request;

            if (Controller != null && (scaleToDuration || settings.overrideAnimationSpeed ||
                                       !Mathf.Approximately(settings.animationSpeedMultiplier, 0f)))
            {
                AnimationClip resolvedClip = Controller.AnimationController.GetClip(animationRequest);
                float speed = settings.animationSpeedMultiplier > 0f ? settings.animationSpeedMultiplier : 1f;

                if (scaleToDuration)
                {
                    float clipLength = resolvedClip ? resolvedClip.length : 0f;
                    if (clipLength > 0f)
                    {
                        float duration = Mathf.Max(0.0001f, targetDuration);
                        speed *= clipLength / duration;
                    }
                }

                bool shouldOverride = settings.overrideAnimationSpeed || scaleToDuration || !Mathf.Approximately(speed, 1f);
                float playbackSpeed = shouldOverride ? Mathf.Max(0.0001f, speed) : 1f;
                animationRequest.playbackSpeed = playbackSpeed;
                animationRequest.overrideSpeed = shouldOverride;
            }

            animationRequest.crossFade = settings.animationCrossFade;
            return animationRequest;
        }

        protected virtual void OnPhaseStarted(ActionPhase phase, float duration)
        {
        }

        protected virtual void OnPhaseEnded(ActionPhase phase)
        {
        }

        protected virtual void OnPhaseCancelled(ActionPhase phase)
        {
        }

        protected virtual void OnPhasesCompleted()
        {
            actionComplete?.Invoke();
        }

        protected virtual bool CanCompleteWindupPhase()
        {
            return true;
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

        struct PendingExecution
        {
            public bool HasValue;
            public Context Context;
            public Transform Target;
            public float MagnitudeMultiplier;
        }
    }
}
