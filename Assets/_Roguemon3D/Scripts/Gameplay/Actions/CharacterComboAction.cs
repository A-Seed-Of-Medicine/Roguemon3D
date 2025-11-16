using System;
using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Effects;
using AdvancedController;
using Cysharp.Threading.Tasks;
using UnityEngine;
using HSM;
using ImprovedTimers;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    /// <summary>
    /// General purpose combo attack action that supports branching follow ups, per-step
    /// action definitions and collider driven hit detection. Designed to work with both
    /// melee and ranged steps in the same combo graph while reusing the CharacterAction
    /// execution pipeline.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AgentController))]
    public class CharacterComboAction : CharacterAction
    {
        public enum ComboInput
        {
            SameAsBinding,
            Primary,
            Secondary,
            AimPrimary,
            AimSecondary,
            Dash,
            Interact,
            Sprint,
            Release
        }

        [Serializable]
        public class ComboEntry
        {
            public ComboInput input = ComboInput.SameAsBinding;
            [Tooltip("Identifier of the combo step that should be triggered by this input when no combo is active.")]
            public string stepId;
        }

        [Serializable]
        public class ComboTransition
        {
            public ComboInput input = ComboInput.SameAsBinding;
            [Tooltip("Identifier of the combo step to play when this transition is taken.")]
            public string nextStepId;
            [Tooltip("If true the input will be stored until the transition window opens.")]
            public bool queueUntilWindow = true;
            [Tooltip("Additional delay applied once the transition window opens before the next step starts.")]
            [Min(0f)] public float transitionDelay;
        }

        [Serializable]
        public class ComboStep
        {
            public string id = "attack";
            [Tooltip("Optional action definition executed for this step. If omitted the actionDefinition on the component is used.")]
            public AgentActionDefinition action;
            [Tooltip("Multiplier applied to the base action magnitude for this step.")]
            public float magnitudeMultiplier = 1f;
            [Tooltip("Execute the configured action even if no targets were detected.")]
            public bool triggerWhenNoTarget;
            [Tooltip("If false a target will only be affected once per active window.")]
            public bool allowRepeatedHits;
            [Tooltip("If true the step will continue even when the agent is stunned.")]
            public bool stunImmune;

            [Header("Timing")]
            [Min(0f)] public float windup = 0.05f;
            [Tooltip("Duration of the active window when hits can be registered.")]
            [Min(0f)] public float active = 0.15f;
            [Tooltip("Recovery time after the active window.")]
            [Min(0f)] public float recovery = 0.25f;
            [Tooltip("Maximum time after recovery before the combo automatically resets if no input is given.")]
            [Min(0f)] public float comboResetDelay = 1.2f;
            [Tooltip("Normalized time (0-1) when the transition window (which is used for queuing inputs) opens.")]
            [Range(0f, 1f)] public float transitionWindowOpen = 0.35f;
            [Tooltip("Normalized time (0-1) when the transition window (which is used for queuing inputs) closes.")]
            [Range(0f, 1f)] public float transitionWindowClose = 0.9f;

            [Header("Movement")]
            public bool lockMovement = true;
            public bool lockAim = true;
            public bool zeroVelocityOnStart = true;
            [Tooltip("Impulse applied along the aim direction if the step doesn't connect with a target.")]
            public float missNudgeImpulse;
            [Min(0f)] public float missNudgeDelay;
            public bool applyNudgeWhenHit;

            [Header("Hit Detection")]
            public Collider[] hitColliders = Array.Empty<Collider>();
            public LayerMask targetLayers = Physics.DefaultRaycastLayers;
            public bool includeTriggerColliders = true;
            public List<AllegianceType> allegianceMask = new();
            [Tooltip("Fallback direction if aim input is not available.")]
            public Vector3 fallbackDirection = Vector3.forward;

            [Header("Branches")]
            public ComboTransition[] transitions = Array.Empty<ComboTransition>();

            [Header("VFX")]
            public ParticleSystem vfx;

            [Header("Hit Stop")]
            [Min(0f)] public float hitStopOnExecute;
            [Min(0f)] public float hitStopOnHit;
            public bool multiplyHitStopPerHit = true;

            [Header("Animation")]
            public AgentAnimationRequest animation;
            [Min(0f)] public float animationCrossFade = 0.1f;
            public float animationSpeedMultiplier = 1f;
            public bool scaleAnimationSpeedToStepDuration;
            public bool overrideAnimationSpeed;

            public float TotalDuration => Mathf.Max(0.0001f, windup + active + recovery);
            public float TransitionOpenTime => Mathf.Clamp01(transitionWindowOpen) * TotalDuration;
            public float TransitionCloseTime => Mathf.Clamp01(transitionWindowClose) * TotalDuration;
        }

        [Header("Combo Graph")]
        [SerializeField] bool requiresAimInput = true;
        [SerializeField] ComboEntry[] entrySteps = Array.Empty<ComboEntry>();
        [SerializeField] ComboStep[] steps = Array.Empty<ComboStep>();

        [Header("Runtime Behaviour")]
        public MovementProfile overrideMovementProfile;
        [SerializeField, Tooltip("How long queued input remains valid before it expires.")]
        [Min(0f)] float queuedInputLifetime = 0.35f;

        readonly Dictionary<string, ComboStep> stepLookup = new();
        readonly Dictionary<ComboStep, StepOverlapSettings> overlapSettingsCache = new();
        readonly HashSet<IDamageable> stepHitTargets = new();
        readonly Dictionary<ComboInput, UnityEngine.Events.UnityAction<bool>> inputHandlers = new();
        readonly Collider[] colliderCache = new Collider[16];
        bool statusEventsRegistered;

        struct StepOverlapSettings
        {
            public LayerMask LayerMask;
            public QueryTriggerInteraction Query;
        }

        ComboStep currentStep;
        ComboStep pendingStep;
        float pendingStepDelay;
        float pendingStepExpireTime;
        bool pendingDelayActive;

        float currentStepElapsed;
        bool stepWasActive;
        bool stepRegisteredHit;
        bool comboActive;
        bool pendingStepReady;
        float nudgeTimer;
        bool nudgePending;
        Vector3 cachedStepDirection = Vector3.forward;
        float pendingComboResetDelay;
        bool stepHitStopAppliedOnHit;

        internal bool IsComboExecuting => comboActive || IsCurrentStepRunning || pendingDelayActive;

        internal bool IsCurrentStepRunning => currentStep != null &&
                                      ((windupTimer?.IsRunning ?? false) ||
                                       (activeTimer?.IsRunning ?? false) ||
                                       (recoveryTimer?.IsRunning ?? false) ||
                                       (stepTimer?.IsRunning ?? false));

        bool currentStepExpired => currentStep == null || !IsCurrentStepRunning;

        MyCountTimer windupTimer;
        MyCountTimer activeTimer;
        MyCountTimer recoveryTimer;
        MyCountTimer stepTimer;
        MyCountTimer transitionDelayTimer;
        MyCountTimer comboResetTimer;

        bool inActivePhase;
        bool inRecoveryPhase;

        protected override bool UsesAimInput => requiresAimInput;

        protected override void Awake()
        {
            actionTrigger = HandleBindingInput;
            base.Awake();
            windupTimer = new MyCountTimer(0f);
            windupTimer.OnTimerFinish += HandleWindupTimerFinished;

            activeTimer = new MyCountTimer(0f);
            activeTimer.OnTimerFinish += HandleActiveTimerFinished;

            recoveryTimer = new MyCountTimer(0f);
            recoveryTimer.OnTimerFinish += HandleRecoveryTimerFinished;

            stepTimer = new MyCountTimer(0f);

            transitionDelayTimer = new MyCountTimer(0f);
            transitionDelayTimer.OnTimerFinish += HandleTransitionDelayTimerFinished;

            comboResetTimer = new MyCountTimer(0f);
            comboResetTimer.OnTimerFinish += HandleComboResetTimerFinished;
            BuildLookups();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            SubscribeStatusEvents();
            if (Controller?.statusHandler?.StunnedStatus?.IsActive ?? false)
            {
                HandleStunStatusStarted(Controller.statusHandler.StunnedStatus);
            }
        }

        void OnValidate()
        {
            BuildLookups();
        }

        void FixedUpdate()
        {
            if (IsCurrentStepRunning)
            {
                AdvanceCurrentStep(Time.fixedDeltaTime);
            }

            HandlePendingTransition();
        }

        protected override void OnDisable()
        {
            UnsubscribeStatusEvents();
            base.OnDisable();
            ResetComboState();
        }

        protected override void OnDestroy()
        {
            UnsubscribeStatusEvents();
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

            if (transitionDelayTimer != null)
            {
                transitionDelayTimer.OnTimerFinish -= HandleTransitionDelayTimerFinished;
            }

            if (comboResetTimer != null)
            {
                comboResetTimer.OnTimerFinish -= HandleComboResetTimerFinished;
            }

            base.OnDestroy();
        }

        protected override void SubscribeToInput(InputReader reader)
        {
            base.SubscribeToInput(reader);
            SubscribeComboInputs(reader);
        }

        protected override void UnsubscribeFromInput(InputReader reader)
        {
            UnsubscribeComboInputs(reader);
            base.UnsubscribeFromInput(reader);
        }

        void HandleBindingInput(bool pressed)
        {
            ComboInput mapped = MapBindingToInput(binding);
            if (pressed)
            {
                ProcessComboInput(mapped);
            }
            else
            {
                ProcessComboInput(ComboInput.Release);
            }
        }

        void ProcessComboInput(ComboInput input)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (input == ComboInput.Release)
            {
                EvaluateReleaseTransitions();
                return;
            }
            
            if (!comboActive)
            {
                comboActive = TryBeginCombo(input);
                return;
            }
            
            if (!IsCurrentStepRunning && currentStep == null)
            {
                TryBeginCombo(input);
                return;
            }

            ComboTransition transition = FindTransition(currentStep, input);
            if (transition == null)
            {
                return;
            }

            QueueOrExecuteTransition(transition);
        }

        bool TryBeginCombo(ComboInput input)
        {
            ComboStep step = ResolveEntryStep(input);
            if (step == null)
            {
                return false;
            }

            StartStep(step, false);
            actionStarted?.Invoke();
            return true;
        }

        void AdvanceCurrentStep(float delta)
        {
            if (!IsCurrentStepRunning)
            {
                return;
            }

            currentStepElapsed = GetCurrentStepElapsed();

            bool isActiveWindow = inActivePhase;

            if (isActiveWindow)
            {
                EvaluateStepHits(currentStep);
            }

            if (stepWasActive && !isActiveWindow)
            {
                OnStepActiveWindowEnded();
            }

            stepWasActive = isActiveWindow;

            if (pendingStep != null)
            {
                HandlePendingTransition();
            }

            if (nudgePending)
            {
                nudgeTimer -= delta;
                if (nudgeTimer <= 0f)
                {
                    ApplyNudge(currentStep);
                    nudgePending = false;
                }
            }
        }

        float GetCurrentStepElapsed()
        {
            if (currentStep == null)
            {
                return 0f;
            }

            float totalDuration = Mathf.Max(0f, currentStep.TotalDuration);
            float remaining = Mathf.Clamp(stepTimer?.CurrentTime ?? 0f, 0f, totalDuration);
            return totalDuration - remaining;
        }

        void OnStepActiveWindowEnded()
        {
            if (!stepRegisteredHit && currentStep != null && currentStep.triggerWhenNoTarget)
            {
                ExecuteStepAction(currentStep, null);
            }

            if (currentStep == null)
            {
                return;
            }
        }

        void CompleteCurrentStep()
        {
            ComboStep finishedStep = currentStep;
            ResetStepState(false);
            pendingComboResetDelay = finishedStep != null ? Mathf.Max(0f, finishedStep.comboResetDelay) : 0f;

            HandlePendingTransition();

            if (!currentStepExpired)
            {
                return;
            }

            if (pendingStep != null)
            {
                return;
            }
            Controller.RemoveMovementModifier(overrideMovementProfile);
            StartComboResetCountdown();
        }

        void StartStep(ComboStep step, bool isContinuation)
        {
            if (step == null || Controller == null)
            {
                return;
            }

            if (!isContinuation && currentStep == null && skipIfActionInProgress && Controller.IsPerformingAction)
            {
                return;
            }

            if (Controller.statusHandler?.StunnedStatus?.IsActive ?? false)
            {
                if (!step.stunImmune)
                {
                    AbortComboDueToStun();
                    return;
                }
            }

            ResetPendingTransition();
            comboResetTimer.Cancel();
            currentStep = step;
            currentStepElapsed = 0f;
            stepWasActive = false;
            stepRegisteredHit = false;
            stepHitTargets.Clear();
            cachedStepDirection = ResolveStepDirection(step);
            pendingStepReady = false;
            nudgePending = false;
            nudgeTimer = 0f;
            pendingComboResetDelay = 0f;
            stepHitStopAppliedOnHit = false;

            inActivePhase = false;
            inRecoveryPhase = false;

            windupTimer.Cancel();
            activeTimer.Cancel();
            recoveryTimer.Cancel();
            stepTimer.Cancel();
            transitionDelayTimer.Cancel();

            float totalDuration = Mathf.Max(0f, currentStep.TotalDuration);
            if (totalDuration > 0f)
            {
                stepTimer.Start(totalDuration);
            }
            
            actionStarted?.Invoke();

            comboActive = true;
            ApplyStepAnimation(step);
            ApplyStepHitStopOnExecute(step);

            if (step.lockMovement)
            {
                float lockTime = step.windup + step.active;
                Controller.LockMovement(lockTime, step.zeroVelocityOnStart);
            }
            
            if (step.lockAim)
            {
                float lockTime = step.windup + step.active;
                Controller.LockAim(lockTime);
            }

            if (step.zeroVelocityOnStart && body != null)
            {
                Vector3 currentVelocity = body.linearVelocity;
                body.linearVelocity = new Vector3(0f, currentVelocity.y, 0f);
            }

            if (cachedStepDirection.sqrMagnitude > 0.0001f)
            {
                Controller.ForceFacing(cachedStepDirection);
            }
            
            Controller.ApplyMovementModifier(overrideMovementProfile, -1f);

            StartWindupPhase(step);
        }

        void ApplyStepAnimation(ComboStep step)
        {
            if (step == null)
            {
                ResetAnimationRequest();
                return;
            }
            
            
            if (step.animation.IsValid)
            {
                if (step.scaleAnimationSpeedToStepDuration)
                {
                    AnimationClip resolvedClip = Controller.AnimationController.GetClip(step.animation);
                    float speed = step.animationSpeedMultiplier > 0f ? step.animationSpeedMultiplier : 1f;
                    float clipLength = resolvedClip.length;
                    if (clipLength > 0f)
                    {
                        float duration = Mathf.Max(0.0001f, step.TotalDuration);
                        speed *= clipLength / duration;
                    }
                    bool shouldOverride = step.overrideAnimationSpeed || step.scaleAnimationSpeedToStepDuration || !Mathf.Approximately(speed, 1f);
                    float playbackSpeed = shouldOverride ? Mathf.Max(0.0001f, speed) : 1f;
                    step.animation.playbackSpeed = playbackSpeed;
                    step.animation.overrideSpeed = shouldOverride;
                    step.animation.crossFade = step.animationCrossFade;
                }
                ResetAnimationRequest();
                SetAnimationRequest(step.animation);
            }
            else
            {
                ResetAnimationRequest();
            }
        }

        void ApplyStepHitStopOnExecute(ComboStep step)
        {
            if (step == null || step.hitStopOnExecute <= 0f)
            {
                return;
            }

            CameraManager.Instance?.TryAddHitStopForAgent(Controller, step.hitStopOnExecute);
        }

        void StartWindupPhase(ComboStep step)
        {
            if (step == null)
            {
                return;
            }

            windupTimer.Cancel();

            if (step.windup > 0f)
            {
                windupTimer.Start(step.windup);
            }
            else
            {
                HandleWindupTimerFinished();
            }
        }

        void HandleWindupTimerFinished()
        {
            if (currentStep == null)
            {
                return;
            }

            StartActivePhase();
        }

        void StartActivePhase()
        {
            if (currentStep == null)
            {
                return;
            }

            inActivePhase = currentStep.active > 0f;
            activeTimer.Cancel();

            if (currentStep.vfx)
            {
                currentStep.vfx.Clear();
                currentStep.vfx.Play();
            }

            if ((currentStep.applyNudgeWhenHit || !stepRegisteredHit) && currentStep.missNudgeImpulse > 0f)
            {
                if (currentStep.missNudgeDelay > 0f)
                {
                    nudgePending = true;
                    nudgeTimer = currentStep.missNudgeDelay;
                }
                else
                {
                    ApplyNudge(currentStep);
                }
            }

            if (inActivePhase)
            {
                activeTimer.Start(currentStep.active);
                EvaluateStepHits(currentStep);
            }
            else
            {
                HandleActiveTimerFinished();
            }
        }

        void HandleActiveTimerFinished()
        {
            inActivePhase = false;

            if (currentStep == null)
            {
                return;
            }

            StartRecoveryPhase();
        }

        void StartRecoveryPhase()
        {
            if (currentStep == null)
            {
                return;
            }

            inRecoveryPhase = currentStep.recovery > 0f;
            recoveryTimer.Cancel();

            if (inRecoveryPhase)
            {
                recoveryTimer.Start(currentStep.recovery);
            }
            else
            {
                HandleRecoveryTimerFinished();
            }
        }

        void HandleRecoveryTimerFinished()
        {
            inRecoveryPhase = false;

            if (currentStep == null)
            {
                return;
            }

            CompleteCurrentStep();
        }

        void HandleTransitionDelayTimerFinished()
        {
            pendingDelayActive = false;
            pendingStepDelay = 0f;
            TryMarkPendingTransitionReady();
            HandlePendingTransition();
        }

        void HandleComboResetTimerFinished()
        {
            if (comboActive)
            {
                comboActive = false;
                actionComplete?.Invoke();
            }

            ResetPendingTransition();
            ResetStepState();
        }

        void StartComboResetCountdown()
        {
            comboResetTimer.Cancel();
            if (pendingComboResetDelay > 0f)
            {
                comboResetTimer.Start(pendingComboResetDelay);
            }
            else
            {
                HandleComboResetTimerFinished();
            }

            pendingComboResetDelay = 0f;
        }

        void EvaluateStepHits(ComboStep step)
        {
            if (step.hitColliders == null || step.hitColliders.Length == 0)
            {
                return;
            }

            StepOverlapSettings settings = GetOverlapSettings(step);
            foreach (Collider source in step.hitColliders)
            {
                if (!source)
                {
                    continue;
                }

                int hitCount = OverlapColliderNonAlloc(source, colliderCache, settings);
                for (int i = 0; i < hitCount; i++)
                {
                    Collider other = colliderCache[i];
                    colliderCache[i] = null;

                    if (!other || other == source)
                    {
                        continue;
                    }

                    IDamageable damageable = other.GetComponentInParent<IDamageable>();
                    if (damageable == null || (AgentController)damageable == Controller)
                    {
                        continue;
                    }

                    if (step.allegianceMask is { Count: > 0 } && !step.allegianceMask.Contains(damageable.allegiance))
                    {
                        continue;
                    }

                    if (!step.allowRepeatedHits && stepHitTargets.Contains(damageable))
                    {
                        continue;
                    }

                    stepHitTargets.Add(damageable);
                    ExecuteStepAction(step, damageable);
                    stepRegisteredHit = true;
                }
            }
        }

        void ExecuteStepAction(ComboStep step, IDamageable target)
        {
            AgentActionDefinition runtimeAction = step.action ? step.action : actionDefinition;
            if (!runtimeAction || Controller == null)
            {
                return;
            }

            float magnitude = Mathf.Max(0f, actionMagnitude) * Mathf.Max(0f, step.magnitudeMultiplier);
            var runtime = new AgentActionRuntime(Controller, this, target, magnitude);
            Controller.ExecuteAction(runtimeAction, runtime).Forget();
            ApplyStepHitStopOnHit(step);
        }

        void ApplyStepHitStopOnHit(ComboStep step)
        {
            if (step == null || step.hitStopOnHit <= 0f)
            {
                return;
            }

            if (!step.multiplyHitStopPerHit && stepHitStopAppliedOnHit)
            {
                return;
            }

            if (CameraManager.Instance != null && CameraManager.Instance.TryAddHitStopForAgent(Controller, step.hitStopOnHit))
            {
                if (!step.multiplyHitStopPerHit)
                {
                    stepHitStopAppliedOnHit = true;
                }
            }
        }

        void ApplyNudge(ComboStep step)
        {
            if (!Controller || body == null)
            {
                return;
            }

            if (step.missNudgeImpulse <= 0f)
            {
                return;
            }

            Vector3 direction = cachedStepDirection;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Controller.AimDirection;
            }

            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.forward;
            }

            Vector3 planarDirection = new Vector3(direction.x, 0f, direction.z);
            if (planarDirection.sqrMagnitude <= 0.0001f)
            {
                planarDirection = Vector3.forward;
            }

            Vector3 planarVelocity = planarDirection.normalized * step.missNudgeImpulse;
            Vector3 current = body.linearVelocity;
            body.linearVelocity = new Vector3(planarVelocity.x, current.y, planarVelocity.z);
        }

        void QueueOrExecuteTransition(ComboTransition transition)
        {
            ComboStep step = ResolveStep(transition.nextStepId);
            if (step == null)
            {
                return;
            }

            if (!transition.queueUntilWindow && !CanTransitionFromCurrentStep())
            {
                return;
            }

            pendingStep = step;
            pendingStepDelay = Mathf.Max(0f, transition.transitionDelay);
            pendingStepExpireTime = Time.time + queuedInputLifetime;
            pendingDelayActive = false;
            pendingStepReady = false;
            transitionDelayTimer.Cancel();

            if (!transition.queueUntilWindow && CanTransitionFromCurrentStep() && pendingStepDelay <= 0f)
            {
                pendingStepReady = true;
                return;
            }

            TryMarkPendingTransitionReady();
        }

        void HandlePendingTransition()
        {
            if (pendingStep == null)
            {
                return;
            }

            if (Time.time > pendingStepExpireTime)
            {
                ResetPendingTransition();
                if (currentStepExpired)
                {
                    StartComboResetCountdown();
                }
                return;
            }

            TryMarkPendingTransitionReady();

            if (pendingStepReady && currentStepExpired)
            {
                ComboStep step = pendingStep;
                ResetPendingTransition();
                StartStep(step, true);
            }
        }

        void TryMarkPendingTransitionReady()
        {
            if (pendingStep == null)
            {
                return;
            }

            if (!CanTransitionFromCurrentStep())
            {
                return;
            }

            if (pendingStepDelay > 0f)
            {
                if (!pendingDelayActive)
                {
                    pendingDelayActive = true;
                    transitionDelayTimer.Start(pendingStepDelay);
                }

                return;
            }

            pendingStepReady = true;
        }

        bool CanTransitionFromCurrentStep()
        {
            if (currentStepExpired)
            {
                return true;
            }

            float totalDuration = Mathf.Max(0f, currentStep.TotalDuration);
            if (totalDuration <= 0f)
            {
                return true;
            }

            float elapsed = GetCurrentStepElapsed();
            float open = Mathf.Clamp(currentStep.TransitionOpenTime, 0f, totalDuration);
            float close = Mathf.Clamp(currentStep.TransitionCloseTime, 0f, totalDuration);

            if (close < open)
            {
                close = open;
            }

            if (elapsed >= open && elapsed <= close)
            {
                return true;
            }

            if (elapsed >= totalDuration)
            {
                return true;
            }

            return false;
        }

        void EvaluateReleaseTransitions()
        {
            if (currentStep == null)
            {
                ResetComboState();
                return;
            }

            ComboTransition releaseTransition = FindTransition(currentStep, ComboInput.Release);
            if (releaseTransition != null)
            {
                QueueOrExecuteTransition(releaseTransition);
            }
        }

        ComboTransition FindTransition(ComboStep step, ComboInput input)
        {
            if (step == null || step.transitions == null)
            {
                return null;
            }

            foreach (ComboTransition transition in step.transitions)
            {
                ComboInput mapped = transition.input == ComboInput.SameAsBinding
                    ? MapBindingToInput(binding)
                    : transition.input;

                if (mapped == input)
                {
                    return transition;
                }
            }

            return null;
        }

        ComboStep ResolveEntryStep(ComboInput input)
        {
            ComboInput bindingInput = MapBindingToInput(binding);
            foreach (ComboEntry entry in entrySteps)
            {
                ComboInput mapped = entry.input == ComboInput.SameAsBinding ? bindingInput : entry.input;
                if (mapped == input)
                {
                    ComboStep step = ResolveStep(entry.stepId);
                    if (step != null)
                    {
                        return step;
                    }
                }
            }

            if (entrySteps.Length == 0)
            {
                return steps.Length > 0 ? steps[0] : null;
            }

            return null;
        }

        ComboStep ResolveStep(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            stepLookup.TryGetValue(id, out ComboStep step);
            return step;
        }

        void ResetStepState(bool clearStepReference = true)
        {
            if (clearStepReference)
            {
                currentStep = null;
            }
            currentStepElapsed = 0f;
            stepWasActive = false;
            stepRegisteredHit = false;
            stepHitTargets.Clear();
            nudgePending = false;
            nudgeTimer = 0f;
            inActivePhase = false;
            inRecoveryPhase = false;
            windupTimer.Cancel();
            activeTimer.Cancel();
            recoveryTimer.Cancel();
            stepTimer.Cancel();
            pendingComboResetDelay = 0f;
        }

        void ResetPendingTransition()
        {
            pendingStep = null;
            pendingStepDelay = 0f;
            pendingStepExpireTime = 0f;
            pendingDelayActive = false;
            pendingStepReady = false;
            transitionDelayTimer.Cancel();
        }

        void ResetComboState()
        {
            Controller?.RemoveMovementModifier(overrideMovementProfile);
            ResetPendingTransition();
            ResetStepState();
            comboResetTimer.Cancel();
            pendingComboResetDelay = 0f;
            if (comboActive)
            {
                comboActive = false;
                actionComplete?.Invoke();
            }
        }

        void SubscribeStatusEvents()
        {
            if (statusEventsRegistered)
            {
                return;
            }

            if (Controller?.statusHandler?.StunnedStatus == null)
            {
                return;
            }

            Controller.statusHandler.StunnedStatus.OnStart += HandleStunStatusStarted;
            Controller.statusHandler.StunnedStatus.OnEnd += HandleStunStatusEnded;
            statusEventsRegistered = true;
        }

        void UnsubscribeStatusEvents()
        {
            if (!statusEventsRegistered)
            {
                return;
            }

            if (Controller?.statusHandler?.StunnedStatus != null)
            {
                Controller.statusHandler.StunnedStatus.OnStart -= HandleStunStatusStarted;
                Controller.statusHandler.StunnedStatus.OnEnd -= HandleStunStatusEnded;
            }

            statusEventsRegistered = false;
        }

        void HandleStunStatusStarted(IStatusEffect _)
        {
            if (currentStep != null && !currentStep.stunImmune)
            {
                AbortComboDueToStun();
                return;
            }

            if (pendingStep != null && !pendingStep.stunImmune)
            {
                ResetPendingTransition();
            }

            if (comboActive && currentStep == null)
            {
                ResetComboState();
            }
        }

        void HandleStunStatusEnded(IStatusEffect _)
        {
            if (Controller?.statusHandler?.StunnedStatus?.IsActive ?? false)
            {
                return;
            }

            if (!comboActive)
            {
                return;
            }

            if (currentStep != null && currentStep.stunImmune)
            {
                return;
            }

            if (currentStep == null)
            {
                ResetComboState();
            }
        }

        void AbortComboDueToStun()
        {
            ResetComboState();
        }

        Vector3 ResolveStepDirection(ComboStep step)
        {
            Vector3 direction = Vector3.zero;
            if (InputReader != null)
            {
                Vector2 aimInput = InputReader.aimDirection;
                if (aimInput.sqrMagnitude > 0.0001f)
                {
                    direction = new Vector3(aimInput.x, 0f, aimInput.y);
                }
            }

            if (direction.sqrMagnitude <= 0.0001f && Controller != null)
            {
                direction = Controller.AimDirection;
            }

            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = step.fallbackDirection;
            }

            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.forward;
            }

            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }

        StepOverlapSettings GetOverlapSettings(ComboStep step)
        {
            if (overlapSettingsCache.TryGetValue(step, out StepOverlapSettings cached))
            {
                return cached;
            }

            StepOverlapSettings settings = new StepOverlapSettings
            {
                LayerMask = step.targetLayers,
                Query = step.includeTriggerColliders ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore
            };
            overlapSettingsCache[step] = settings;
            return settings;
        }

        static int OverlapColliderNonAlloc(Collider source, Collider[] results, StepOverlapSettings settings)
        {
            return OverlapColliderNonAlloc(source, results, settings.LayerMask, settings.Query);
        }

        static int OverlapColliderNonAlloc(Collider source, Collider[] results, LayerMask layerMask, QueryTriggerInteraction query)
        {
            if (!source)
            {
                return 0;
            }

            switch (source)
            {
                case BoxCollider box:
                    return OverlapBoxCollider(box, results, layerMask, query);
                case SphereCollider sphere:
                    return OverlapSphereCollider(sphere, results, layerMask, query);
                case CapsuleCollider capsule:
                    return OverlapCapsuleCollider(capsule, results, layerMask, query);
                default:
                    Bounds bounds = source.bounds;
                    return Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, results, Quaternion.identity, layerMask, query);
            }
        }

        static int OverlapBoxCollider(BoxCollider collider, Collider[] results, LayerMask layerMask, QueryTriggerInteraction query)
        {
            Vector3 center = collider.transform.TransformPoint(collider.center);
            Vector3 lossyScale = collider.transform.lossyScale;
            Vector3 halfExtents = Vector3.Scale(collider.size * 0.5f, new Vector3(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y), Mathf.Abs(lossyScale.z)));
            Quaternion orientation = collider.transform.rotation;
            return Physics.OverlapBoxNonAlloc(center, halfExtents, results, orientation, layerMask, query);
        }

        static int OverlapSphereCollider(SphereCollider collider, Collider[] results, LayerMask layerMask, QueryTriggerInteraction query)
        {
            Vector3 center = collider.transform.TransformPoint(collider.center);
            float radius = collider.radius * MaxAbsComponent(collider.transform.lossyScale);
            return Physics.OverlapSphereNonAlloc(center, radius, results, layerMask, query);
        }

        static int OverlapCapsuleCollider(CapsuleCollider collider, Collider[] results, LayerMask layerMask, QueryTriggerInteraction query)
        {
            GetCapsulePoints(collider, out Vector3 point0, out Vector3 point1, out float radius);
            return Physics.OverlapCapsuleNonAlloc(point0, point1, radius, results, layerMask, query);
        }

        static void GetCapsulePoints(CapsuleCollider collider, out Vector3 point0, out Vector3 point1, out float radius)
        {
            Transform transform = collider.transform;
            Vector3 center = transform.TransformPoint(collider.center);
            Vector3 lossyScale = transform.lossyScale;

            switch (collider.direction)
            {
                case 0:
                {
                    radius = collider.radius * Mathf.Max(Mathf.Abs(lossyScale.y), Mathf.Abs(lossyScale.z));
                    float height = Mathf.Max(radius * 2f, collider.height * Mathf.Abs(lossyScale.x));
                    Vector3 axis = transform.right;
                    float offset = Mathf.Max(0f, height * 0.5f - radius);
                    point0 = center + axis * offset;
                    point1 = center - axis * offset;
                    break;
                }
                case 1:
                {
                    radius = collider.radius * Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.z));
                    float height = Mathf.Max(radius * 2f, collider.height * Mathf.Abs(lossyScale.y));
                    Vector3 axis = transform.up;
                    float offset = Mathf.Max(0f, height * 0.5f - radius);
                    point0 = center + axis * offset;
                    point1 = center - axis * offset;
                    break;
                }
                case 2:
                default:
                {
                    radius = collider.radius * Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y));
                    float height = Mathf.Max(radius * 2f, collider.height * Mathf.Abs(lossyScale.z));
                    Vector3 axis = transform.forward;
                    float offset = Mathf.Max(0f, height * 0.5f - radius);
                    point0 = center + axis * offset;
                    point1 = center - axis * offset;
                    break;
                }
            }
        }

        static float MaxAbsComponent(Vector3 vector)
        {
            return Mathf.Max(Mathf.Abs(vector.x), Mathf.Abs(vector.y), Mathf.Abs(vector.z));
        }

        void SubscribeComboInputs(InputReader reader)
        {
            HashSet<ComboInput> required = GatherComboInputs();
            required.Remove(MapBindingToInput(binding));
            required.Remove(ComboInput.SameAsBinding);
            required.Remove(ComboInput.Release);

            foreach (ComboInput input in required)
            {
                if (inputHandlers.ContainsKey(input))
                {
                    continue;
                }

                void Handler(bool pressed)
                {
                    if (pressed)
                    {
                        ProcessComboInput(input);
                    }
                }

                UnityEngine.Events.UnityAction<bool> action = Handler;
                inputHandlers[input] = action;

                switch (input)
                {
                    case ComboInput.Primary:
                        reader.PrimaryAction += action;
                        break;
                    case ComboInput.Secondary:
                        reader.SecondaryAction += action;
                        break;
                    case ComboInput.AimPrimary:
                        reader.AimPrimary += action;
                        break;
                    case ComboInput.AimSecondary:
                        reader.AimSecondary += action;
                        break;
                    case ComboInput.Dash:
                        reader.Dash += action;
                        break;
                    case ComboInput.Interact:
                        reader.Interact += action;
                        break;
                    case ComboInput.Sprint:
                        reader.Sprint += action;
                        break;
                }
            }
        }

        void UnsubscribeComboInputs(InputReader reader)
        {
            foreach (var pair in inputHandlers)
            {
                switch (pair.Key)
                {
                    case ComboInput.Primary:
                        reader.PrimaryAction -= pair.Value;
                        break;
                    case ComboInput.Secondary:
                        reader.SecondaryAction -= pair.Value;
                        break;
                    case ComboInput.AimPrimary:
                        reader.AimPrimary -= pair.Value;
                        break;
                    case ComboInput.AimSecondary:
                        reader.AimSecondary -= pair.Value;
                        break;
                    case ComboInput.Dash:
                        reader.Dash -= pair.Value;
                        break;
                    case ComboInput.Interact:
                        reader.Interact -= pair.Value;
                        break;
                    case ComboInput.Sprint:
                        reader.Sprint -= pair.Value;
                        break;
                }
            }

            inputHandlers.Clear();
        }

        HashSet<ComboInput> GatherComboInputs()
        {
            HashSet<ComboInput> result = new();
            result.Add(ComboInput.SameAsBinding);

            foreach (ComboEntry entry in entrySteps)
            {
                result.Add(entry.input);
            }

            foreach (ComboStep step in steps)
            {
                if (step.transitions == null)
                {
                    continue;
                }

                foreach (ComboTransition transition in step.transitions)
                {
                    result.Add(transition.input);
                }
            }

            return result;
        }

        ComboInput MapBindingToInput(PressBinding pressBinding)
        {
            return pressBinding switch
            {
                PressBinding.PrimaryAction => ComboInput.Primary,
                PressBinding.SecondaryAction => ComboInput.Secondary,
                PressBinding.AimPrimary => ComboInput.AimPrimary,
                PressBinding.AimSecondary => ComboInput.AimSecondary,
                PressBinding.Dash => ComboInput.Dash,
                PressBinding.Interact => ComboInput.Interact,
                PressBinding.Sprint => ComboInput.Sprint,
                _ => ComboInput.Primary
            };
        }

        protected override ActionState CreateActionExecuteState(AgentRoot root)
        {
            if (Controller == null || root == null)
                return null;

            ComboState state = new ComboState(Controller, root.Machine, root, this, root.Grounded);
            root.Grounded.ComboExecuting = state;
            return state;
        }

        void BuildLookups()
        {
            stepLookup.Clear();
            overlapSettingsCache.Clear();

            if (steps == null)
            {
                return;
            }

            foreach (ComboStep step in steps)
            {
                if (step == null || string.IsNullOrEmpty(step.id))
                {
                    continue;
                }

                if (!stepLookup.ContainsKey(step.id))
                {
                    stepLookup.Add(step.id, step);
                }
                else
                {
                    stepLookup[step.id] = step;
                }
            }
        }
    }
}
