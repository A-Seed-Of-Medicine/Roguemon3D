using System;
using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Effects;
using AdvancedController;
using Cysharp.Threading.Tasks;
using UnityEngine;
using HSM;
using ImprovedTimers;
using _Roguemon3D.Scripts.Utils;

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
            [Tooltip("If true this entry is only taken on a long press using the configured thresholds.")]
            public bool longPress;
            [Tooltip("Minimum time (seconds) a button must be held to trigger this long press entry.")]
            [Min(0f)] public float longPressMinThreshold = 0.35f;
            [Tooltip("Maximum time (seconds) to consider when normalizing the press duration for a long press entry.")]
            [Min(0f)] public float longPressMaxThreshold = 1f;
            [HideInInspector]
            public Vector2 graphPosition;
        }

        [Serializable]
        public class ComboTransition
        {
            public ComboInput input = ComboInput.SameAsBinding;
            [Tooltip("Step to play when this transition is taken.")]
            [SerializeReference] public ComboStep nextStep;
            [Tooltip("If true the input will be stored until the transition window opens.")]
            public bool queueUntilWindow = true;
            [Tooltip("Additional delay applied once the transition window opens before the next step starts.")]
            [Min(0f)] public float transitionDelay;
            [Tooltip("If set, the transition will automatically trigger once the button has been held for at least this duratio" +
                "n. Hold time includes the uninterrupted press time from the previous step.")]
            [Min(0f)] public float minimumHoldTime;
            [Header("Long Press")]
            [Tooltip("If true this transition is triggered by a long press instead of a tap.")]
            public bool longPress;
            [Tooltip("Minimum time (seconds) a button must be held before this transition can trigger.")]
            [Min(0f)] public float longPressMinThreshold = 0.35f;
            [Tooltip("Maximum time (seconds) to consider when normalizing the press duration for this transition.")]
            [Min(0f)] public float longPressMaxThreshold = 1f;
        }

        [Serializable]
        public class ComboStep
        {
            [Serializable]
            public class MovementLockSettings
            {
                public bool windup = true;
                public bool active = true;
                public bool recovery = true;
            }

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

            [Header("Charge")]
            public bool chargeWindup;
            [Tooltip("Minimum time the attack must be charged before it can release after the input is lifted.")]
            [Min(0f)] public float minimumChargeTime;
            [Tooltip("Maximum time the attack can be charged before automatically releasing. Set to 0 for unlimited.")]
            [Min(0f)] public float maximumChargeTime;

            [Header("Movement")]
            public MovementLockSettings movementLocks = new();
            public MovementLockSettings chargeMovementLocks = new();
            public bool lockAim = true;
            public bool zeroVelocityOnStart = true;
            [Tooltip("Impulse applied along the aim direction if the step doesn't connect with a target.")]
            public float missNudgeImpulse;
            [Min(0f)] public float missNudgeDelay;
            public bool applyNudgeWhenHit;

            [Header("Hit Detection")]
            public HitDetector hitDetectorPrefab;
            public bool parentHitDetectorToPivot = true;
            public Vector3 hitDetectorPositionOffset;
            public Vector3 hitDetectorRotationOffset;
            [Tooltip("Fallback direction if aim input is not available.")]
            public Vector3 fallbackDirection = Vector3.forward;

            [HideInInspector]
            public Vector2 graphPosition;

            [Header("Branches")]
            public ComboTransition[] transitions = Array.Empty<ComboTransition>();

            [Header("VFX")]
            public ParticleSystem vfx;

            [Header("Hit Stop")]
            [Min(0f)] public float hitStopOnExecute;
            [Min(0f)] public float hitStopOnHit;
            public bool multiplyHitStopPerHit = true;

            [Header("Animation")]
            public bool usePhaseAnimations;
            public AgentAnimationRequest animation;
            public AgentAnimationRequest windupAnimation;
            public AgentAnimationRequest activeAnimation;
            public AgentAnimationRequest recoveryAnimation;
            [Min(0f)] public float animationCrossFade = 0.1f;
            public float animationSpeedMultiplier = 1f;
            public bool scaleAnimationSpeedToStepDuration;
            public bool scaleWindupAnimationToStepDuration;
            public bool scaleActiveAnimationToStepDuration;
            public bool scaleRecoveryAnimationToStepDuration;
            public bool overrideAnimationSpeed;

            [NonSerialized] public float pressDuration;
            [NonSerialized] public float pressDurationNormalized;
            [NonSerialized] public bool longPressTriggered;

            public float TotalDuration => Mathf.Max(0.0001f, windup + active + recovery);
            public float TransitionOpenTime => Mathf.Clamp01(transitionWindowOpen) * TotalDuration;
            public float TransitionCloseTime => Mathf.Clamp01(transitionWindowClose) * TotalDuration;
        }

        [Header("Combo Definition")]
        [SerializeField] CharacterComboDefinition comboDefinition;

        [Header("Runtime Behaviour")]
        public MovementProfile overrideMovementProfile;

        readonly Dictionary<string, ComboStep> stepLookup = new();
        readonly HashSet<IDamageable> stepHitTargets = new();
        readonly Dictionary<ComboInput, UnityEngine.Events.UnityAction<bool>> inputHandlers = new();
        readonly Dictionary<ComboInput, PressState> inputPressStates = new();
        bool statusEventsRegistered;
        HitDetector activeHitDetector;

        const float DefaultQueuedInputLifetime = 0.35f;

        ComboEntry[] EntrySteps => comboDefinition ? comboDefinition.EntrySteps : Array.Empty<ComboEntry>();
        ComboStep[] Steps => comboDefinition ? comboDefinition.Steps : Array.Empty<ComboStep>();
        float QueuedInputLifetime => comboDefinition ? comboDefinition.QueuedInputLifetime : DefaultQueuedInputLifetime;

        public CharacterComboDefinition ComboDefinition => comboDefinition;

        public void SetComboDefinition(CharacterComboDefinition definition)
        {
            if (comboDefinition == definition)
            {
                return;
            }

            comboDefinition = definition;
            ResetComboState();
            BuildLookups();
        }

        ComboStep currentStep;
        ComboStep pendingStep;
        ComboInput pendingStepInput = ComboInput.Primary;
        float pendingStepDelay;
        float pendingStepExpireTime;
        bool pendingDelayActive;
        bool pendingStepIsLongPress;
        float pendingStepHoldDuration;
        float pendingStepHoldNormalized;

        ComboInput currentStepInput = ComboInput.Primary;
        float chargePressStartTime;
        bool chargeWindupActive;
        bool chargeReleaseRequested;
        bool zeroVelocityApplied;
        float stepStartTime;

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

        protected override bool UsesAimInput => comboDefinition ? comboDefinition.RequiresAimInput : true;

        class PressState
        {
            public bool Pressed;
            public float PressStartTime;
            public bool Triggered;
            public bool LongTriggered;
            public ComboTransition ShortTransition;
            public ComboTransition LongTransition;
            public ComboEntry ShortEntry;
            public ComboEntry LongEntry;

            public void ResetForPress(float time)
            {
                Pressed = true;
                PressStartTime = time;
                Triggered = false;
                LongTriggered = false;
                ShortTransition = null;
                LongTransition = null;
                ShortEntry = null;
                LongEntry = null;
            }

            public void Reset()
            {
                Pressed = false;
                Triggered = false;
                LongTriggered = false;
                ShortTransition = null;
                LongTransition = null;
                ShortEntry = null;
                LongEntry = null;
                PressStartTime = 0f;
            }

            public float GetHoldDuration(float now)
            {
                return Mathf.Max(0f, Pressed ? now - PressStartTime : 0f);
            }
        }

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
            EvaluateLongPressStates();
            EvaluateChargeWindup();

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
            ProcessComboInput(mapped, pressed);

            if (!pressed)
            {
                EvaluateReleaseTransitions();
            }
        }

        void ProcessComboInput(ComboInput input, bool pressed)
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

            if (pressed)
            {
                HandleInputPressed(input);
            }
            else
            {
                HandleInputReleased(input);
            }
        }

        void HandleInputPressed(ComboInput input)
        {
            PressState state = GetOrCreatePressState(input);
            state.ResetForPress(Time.time);

            if (TryHandleEntryPress(input, state))
            {
                return;
            }

            TryHandleTransitionPress(input, state);
        }

        void HandleInputReleased(ComboInput input)
        {
            if (!inputPressStates.TryGetValue(input, out PressState state) || !state.Pressed)
            {
                return;
            }

            float duration = Mathf.Max(0f, Time.time - state.PressStartTime);

            if (!state.LongTriggered && !state.Triggered)
            {
                if (state.LongTransition != null && HasLongPressElapsed(duration, state.LongTransition.longPressMinThreshold))
                {
                    float normalized = CalculateHoldNormalized(duration, state.LongTransition.longPressMinThreshold,
                        state.LongTransition.longPressMaxThreshold);
                    TriggerTransition(state.LongTransition, true, duration, normalized, input);
                    state.LongTriggered = true;
                    state.Triggered = true;
                }
                else if (state.LongEntry != null && HasLongPressElapsed(duration, state.LongEntry.longPressMinThreshold))
                {
                    float normalized = CalculateHoldNormalized(duration, state.LongEntry.longPressMinThreshold,
                        state.LongEntry.longPressMaxThreshold);
                    TriggerEntry(state.LongEntry, true, duration, normalized, input);
                    state.LongTriggered = true;
                    state.Triggered = true;
                }
            }

            if (!state.LongTriggered && !state.Triggered)
            {
                float normalized = CalculateShortPressNormalized(duration, state);

                if (state.ShortTransition != null)
                {
                    TriggerTransition(state.ShortTransition, false, duration, normalized, input);
                    state.Triggered = true;
                }
                else if (state.ShortEntry != null)
                {
                    TriggerEntry(state.ShortEntry, false, duration, normalized, input);
                    state.Triggered = true;
                }
            }

            if (currentStep != null && currentStep.chargeWindup && input == currentStepInput)
            {
                chargeReleaseRequested = true;
            }

            state.Reset();
        }

        bool TryHandleEntryPress(ComboInput input, PressState state)
        {
            bool canStartEntry = !comboActive || (!IsCurrentStepRunning && currentStep == null);
            if (!canStartEntry)
            {
                return false;
            }

            GetEntryOptions(input, out ComboEntry shortEntry, out ComboEntry longEntry);

            state.ShortEntry = shortEntry;
            state.LongEntry = longEntry;

            if (shortEntry == null && longEntry == null && EntrySteps.Length == 0 && Steps.Length > 0)
            {
                StartStep(Steps[0], false, false, 0f, 0f, input);
                state.Triggered = true;
                return true;
            }

            if (longEntry != null)
            {
                return true;
            }

            if (shortEntry != null)
            {
                TriggerEntry(shortEntry, false, 0f, 0f, input);
                state.Triggered = true;
                return true;
            }

            return false;
        }

        void TryHandleTransitionPress(ComboInput input, PressState state)
        {
            if (currentStep == null)
            {
                return;
            }

            FindTransitionOptions(currentStep, input, out ComboTransition shortTransition, out ComboTransition longTransition);

            state.ShortTransition = shortTransition;
            state.LongTransition = longTransition;
        }

        float CalculateShortPressNormalized(float duration, PressState state)
        {
            if (state.LongTransition != null)
            {
                return CalculateHoldNormalized(duration, state.LongTransition.longPressMinThreshold,
                    state.LongTransition.longPressMaxThreshold);
            }

            if (state.LongEntry != null)
            {
                return CalculateHoldNormalized(duration, state.LongEntry.longPressMinThreshold,
                    state.LongEntry.longPressMaxThreshold);
            }

            return 0f;
        }

        void TriggerTransition(ComboTransition transition, bool isLongPress, float holdDuration, float holdNormalized,
            ComboInput input)
        {
            if (transition == null)
            {
                return;
            }

            QueueOrExecuteTransition(transition, holdDuration, holdNormalized, isLongPress, input);
        }

        void TriggerEntry(ComboEntry entry, bool isLongPress, float holdDuration, float holdNormalized, ComboInput input)
        {
            if (entry == null)
            {
                return;
            }

            ComboStep step = ResolveStep(entry.stepId);
            if (step == null)
            {
                return;
            }

            StartStep(step, false, isLongPress, holdDuration, holdNormalized, input);
        }

        void GetEntryOptions(ComboInput input, out ComboEntry shortEntry, out ComboEntry longEntry)
        {
            shortEntry = ResolveEntry(input, false);
            longEntry = ResolveEntry(input, true);
        }

        ComboEntry ResolveEntry(ComboInput input, bool requireLongPress)
        {
            ComboInput bindingInput = MapBindingToInput(binding);
            foreach (ComboEntry entry in EntrySteps)
            {
                ComboInput mapped = entry.input == ComboInput.SameAsBinding ? bindingInput : entry.input;
                if (mapped == input && entry.longPress == requireLongPress)
                {
                    return entry;
                }
            }

            return null;
        }

        float CalculateHoldNormalized(float duration, float minThreshold, float maxThreshold)
        {
            float min = Mathf.Max(0f, minThreshold);
            float max = Mathf.Max(min, maxThreshold);

            if (max <= 0f)
            {
                return 1f;
            }

            float clamped = Mathf.Clamp(duration, min, max);
            return Mathf.InverseLerp(min, max, clamped);
        }

        bool HasLongPressElapsed(float duration, float minThreshold)
        {
            return duration >= Mathf.Max(0f, minThreshold);
        }

        PressState GetOrCreatePressState(ComboInput input)
        {
            if (!inputPressStates.TryGetValue(input, out PressState state))
            {
                state = new PressState();
                inputPressStates[input] = state;
            }

            return state;
        }

        float ResolvePressStartTime(ComboInput input)
        {
            if (inputPressStates.TryGetValue(input, out PressState state) && state.PressStartTime > 0f)
            {
                return state.PressStartTime;
            }

            return Time.time;
        }

        void RefreshPressedTransitionOptions()
        {
            if (currentStep == null)
            {
                return;
            }

            foreach (var pair in inputPressStates)
            {
                if (!pair.Value.Pressed)
                {
                    continue;
                }

                FindTransitionOptions(currentStep, pair.Key, out ComboTransition shortTransition, out ComboTransition longTransition);
                pair.Value.ShortTransition = shortTransition;
                pair.Value.LongTransition = longTransition;
            }
        }

        void EvaluateLongPressStates()
        {
            float now = Time.time;

            foreach (var pair in inputPressStates)
            {
                ComboInput input = pair.Key;
                PressState state = pair.Value;

                if (!state.Pressed || state.LongTriggered || state.Triggered)
                {
                    continue;
                }

                float duration = state.GetHoldDuration(now);

                if (state.LongEntry != null && HasLongPressElapsed(duration, state.LongEntry.longPressMinThreshold))
                {
                    float normalized = CalculateHoldNormalized(duration, state.LongEntry.longPressMinThreshold,
                        state.LongEntry.longPressMaxThreshold);
                    TriggerEntry(state.LongEntry, true, duration, normalized, input);
                    state.LongTriggered = true;
                    state.Triggered = true;
                    continue;
                }

                if (TryTriggerMinimumHoldTransition(state.LongTransition, duration, input, state))
                {
                    continue;
                }

                TryTriggerMinimumHoldTransition(state.ShortTransition, duration, input, state);
            }
        }

        bool TryTriggerMinimumHoldTransition(ComboTransition transition, float duration, ComboInput input, PressState state)
        {
            if (transition == null)
            {
                return false;
            }

            if (transition.minimumHoldTime <= 0f || duration < transition.minimumHoldTime)
            {
                return false;
            }

            float normalized = CalculateHoldNormalized(duration, transition.longPressMinThreshold,
                transition.longPressMaxThreshold);
            bool isLong = transition.longPress || transition.minimumHoldTime >= transition.longPressMinThreshold;
            TriggerTransition(transition, isLong, duration, normalized, input);
            state.Triggered = true;
            state.LongTriggered |= isLong;
            return true;
        }

        void EvaluateChargeWindup()
        {
            if (!chargeWindupActive || currentStep == null || !currentStep.chargeWindup)
            {
                return;
            }

            MaintainLockDuringCharge(currentStep);

            if (inActivePhase || inRecoveryPhase)
            {
                chargeWindupActive = false;
                return;
            }

            float elapsedSincePress = Mathf.Max(0f, Time.time - chargePressStartTime);
            float minCharge = Mathf.Max(currentStep.windup, currentStep.minimumChargeTime);
            float maxCharge = currentStep.maximumChargeTime > 0f
                ? Mathf.Max(minCharge, currentStep.maximumChargeTime)
                : float.PositiveInfinity;

            if (elapsedSincePress >= maxCharge)
            {
                CompleteChargeWindup();
                return;
            }

            if (chargeReleaseRequested && elapsedSincePress >= minCharge)
            {
                CompleteChargeWindup();
            }
        }

        void CompleteChargeWindup()
        {
            chargeWindupActive = false;
            chargeReleaseRequested = false;
            HandleWindupTimerFinished();
        }

        ComboStep.MovementLockSettings ResolveMovementLocks(ComboStep step)
        {
            if (step == null)
            {
                return new ComboStep.MovementLockSettings();
            }

            return step.chargeWindup ? step.chargeMovementLocks : step.movementLocks;
        }

        float ResolveChargeWindupTarget(ComboStep step)
        {
            if (step == null)
            {
                return 0f;
            }

            if (!step.chargeWindup)
            {
                return Mathf.Max(0f, step.windup);
            }

            float minCharge = Mathf.Max(step.windup, step.minimumChargeTime);
            float maxCharge = step.maximumChargeTime > 0f ? Mathf.Max(minCharge, step.maximumChargeTime) : float.PositiveInfinity;

            return maxCharge;
        }

        float CalculateMovementLockDuration(ComboStep step, ComboStep.MovementLockSettings locks, float windupDuration)
        {
            float duration = 0f;

            if (locks.windup)
            {
                if (float.IsInfinity(windupDuration))
                {
                    return float.PositiveInfinity;
                }

                duration += windupDuration;
            }

            if (locks.active)
            {
                duration += Mathf.Max(0f, step.active);
            }

            if (locks.recovery)
            {
                duration += Mathf.Max(0f, step.recovery);
            }

            return duration;
        }

        void MaintainLockDuringCharge(ComboStep step)
        {
            if (step == null || Controller == null)
            {
                return;
            }

            ComboStep.MovementLockSettings locks = ResolveMovementLocks(step);
            float buffer = 0.25f;

            if (locks.windup)
            {
                Controller.LockMovement(buffer, false);
            }

            if (step.lockAim && locks.windup)
            {
                Controller.LockAim(buffer);
            }
        }

        void ApplyMovementAndAimLocks(ComboStep step)
        {
            if (step == null || Controller == null)
            {
                return;
            }

            ComboStep.MovementLockSettings locks = ResolveMovementLocks(step);
            float windupDuration = ResolveChargeWindupTarget(step);
            float lockDuration = CalculateMovementLockDuration(step, locks, windupDuration);

            if (locks.windup || locks.active || locks.recovery)
            {
                if (float.IsInfinity(lockDuration))
                {
                    MaintainLockDuringCharge(step);
                }
                else
                {
                    Controller.LockMovement(lockDuration, step.zeroVelocityOnStart, true);
                }
            }

            if (step.lockAim)
            {
                if (float.IsInfinity(lockDuration))
                {
                    Controller.LockAim(0.25f);
                }
                else
                {
                    Controller.LockAim(lockDuration);
                }
            }
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

            return Mathf.Max(0f, Time.time - stepStartTime);
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

            activeHitDetector?.Deactivate();
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

        void StartStep(ComboStep step, bool isContinuation, bool isLongPress, float holdDuration, float holdNormalized,
            ComboInput input)
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
            currentStepInput = input;
            ApplyStepPressMetadata(step, isLongPress, holdDuration, holdNormalized);
            stepStartTime = Time.time;
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
            chargePressStartTime = ResolvePressStartTime(input);
            chargeReleaseRequested = false;
            chargeWindupActive = false;
            zeroVelocityApplied = false;
            RefreshPressedTransitionOptions();

            inActivePhase = false;
            inRecoveryPhase = false;

            windupTimer.Cancel();
            activeTimer.Cancel();
            recoveryTimer.Cancel();
            stepTimer.Cancel();
            transitionDelayTimer.Cancel();

            float windupDuration = ResolveChargeWindupTarget(step);
            float totalDuration = Mathf.Max(0f, windupDuration + step.active + step.recovery);
            if (!float.IsInfinity(totalDuration) && totalDuration > 0f)
            {
                stepTimer.Start(totalDuration);
            }

            actionStarted?.Invoke();

            comboActive = true;
            SpawnHitDetectorForStep(step);
            ApplyStepHitStopOnExecute(step);

            if (step.zeroVelocityOnStart && body && !zeroVelocityApplied)
            {
                zeroVelocityApplied = true;
                Vector3 currentVelocity = body.linearVelocity;
                body.linearVelocity = new Vector3(0f, currentVelocity.y, 0f);
            }

            ApplyMovementAndAimLocks(step);

            if (cachedStepDirection.sqrMagnitude > 0.0001f)
            {
                Controller.ForceFacing(cachedStepDirection);
            }
            
            Controller.ApplyMovementModifier(overrideMovementProfile, -1f);

            StartWindupPhase(step);
        }

        void ApplyStepPressMetadata(ComboStep step, bool isLongPress, float holdDuration, float holdNormalized)
        {
            if (step == null)
            {
                return;
            }

            step.longPressTriggered = isLongPress;
            step.pressDuration = Mathf.Max(0f, holdDuration);
            step.pressDurationNormalized = Mathf.Clamp01(holdNormalized);
        }

        void ApplyStepAnimation(ComboStep step, HitDetector.ExecutionPhase phase)
        {
            if (!TryGetAnimationRequestForPhase(step, phase, out AgentAnimationRequest animation, out float targetDuration,
                    out bool scaleToDuration))
            {
                return;
            }

            AgentAnimationRequest request = PrepareAnimationRequest(step, animation, targetDuration, scaleToDuration);
            ResetAnimationRequest();
            SetAnimationRequest(request);
        }

        bool TryGetAnimationRequestForPhase(ComboStep step, HitDetector.ExecutionPhase phase, out AgentAnimationRequest request,
            out float targetDuration, out bool scaleToDuration)
        {
            request = AgentAnimationRequest.None;
            targetDuration = 0f;
            scaleToDuration = false;

            if (step == null)
            {
                return false;
            }

            if (!step.usePhaseAnimations)
            {
                if (phase != HitDetector.ExecutionPhase.Windup || !step.animation.IsValid)
                {
                    return false;
                }

                request = step.animation;
                targetDuration = step.TotalDuration;
                scaleToDuration = step.scaleAnimationSpeedToStepDuration;
                return true;
            }

            targetDuration = ResolvePhaseDuration(step, phase);
            request = phase switch
            {
                HitDetector.ExecutionPhase.Windup => step.windupAnimation,
                HitDetector.ExecutionPhase.Active => step.activeAnimation,
                HitDetector.ExecutionPhase.Recovery => step.recoveryAnimation,
                _ => AgentAnimationRequest.None
            };

            scaleToDuration = phase switch
            {
                HitDetector.ExecutionPhase.Windup => step.scaleWindupAnimationToStepDuration,
                HitDetector.ExecutionPhase.Active => step.scaleActiveAnimationToStepDuration,
                HitDetector.ExecutionPhase.Recovery => step.scaleRecoveryAnimationToStepDuration,
                _ => false
            };

            if (!scaleToDuration)
            {
                scaleToDuration = step.scaleAnimationSpeedToStepDuration;
            }

            return request.IsValid;
        }

        static float ResolvePhaseDuration(ComboStep step, HitDetector.ExecutionPhase phase)
        {
            return phase switch
            {
                HitDetector.ExecutionPhase.Windup => Mathf.Max(0f, step.windup),
                HitDetector.ExecutionPhase.Active => Mathf.Max(0f, step.active),
                HitDetector.ExecutionPhase.Recovery => Mathf.Max(0f, step.recovery),
                _ => Mathf.Max(0f, step.TotalDuration)
            };
        }

        AgentAnimationRequest PrepareAnimationRequest(ComboStep step, AgentAnimationRequest request, float targetDuration,
            bool scaleToDuration)
        {
            AgentAnimationRequest animationRequest = request;

            if (scaleToDuration || step.overrideAnimationSpeed)
            {
                AnimationClip resolvedClip = Controller.AnimationController.GetClip(animationRequest);
                float speed = step.animationSpeedMultiplier > 0f ? step.animationSpeedMultiplier : 1f;

                if (scaleToDuration)
                {
                    float clipLength = resolvedClip ? resolvedClip.length : 0f;
                    if (clipLength > 0f)
                    {
                        float duration = Mathf.Max(0.0001f, targetDuration);
                        speed *= clipLength / duration;
                    }
                }

                bool shouldOverride = step.overrideAnimationSpeed || scaleToDuration || !Mathf.Approximately(speed, 1f);
                float playbackSpeed = shouldOverride ? Mathf.Max(0.0001f, speed) : 1f;
                animationRequest.playbackSpeed = playbackSpeed;
                animationRequest.overrideSpeed = shouldOverride;
            }

            animationRequest.crossFade = step.animationCrossFade;
            return animationRequest;
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
            ResetAnimationRequest();
            ApplyStepAnimation(step, HitDetector.ExecutionPhase.Windup);

            activeHitDetector?.HandlePhaseStart(HitDetector.ExecutionPhase.Windup, step);

            if (step.chargeWindup)
            {
                chargeWindupActive = true;
                MaintainLockDuringCharge(step);
                EvaluateChargeWindup();
                return;
            }

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

            activeHitDetector?.HandlePhaseEnd(HitDetector.ExecutionPhase.Windup);
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

            activeHitDetector?.HandlePhaseStart(HitDetector.ExecutionPhase.Active, currentStep);
            ApplyStepAnimation(currentStep, HitDetector.ExecutionPhase.Active);

            if (currentStep.vfx)
            {
                currentStep.vfx.Clear();
                currentStep.vfx.Play();
            }

            activeHitDetector?.Activate(currentStep, currentStep.active);

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

            activeHitDetector?.HandlePhaseEnd(HitDetector.ExecutionPhase.Active);
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

            activeHitDetector?.HandlePhaseStart(HitDetector.ExecutionPhase.Recovery, currentStep);
            ApplyStepAnimation(currentStep, HitDetector.ExecutionPhase.Recovery);

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

            activeHitDetector?.HandlePhaseEnd(HitDetector.ExecutionPhase.Recovery);
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
            if (activeHitDetector == null || step == null)
            {
                return;
            }

            activeHitDetector.EvaluateHits(stepHitTargets, step.allowRepeatedHits, damageable =>
            {
                ExecuteStepAction(step, damageable);
                stepRegisteredHit = true;
            });
        }

        void SpawnHitDetectorForStep(ComboStep step)
        {
            ReleaseActiveHitDetector();

            if (!step?.hitDetectorPrefab || !Controller)
                return;

            Transform parent = step.parentHitDetectorToPivot && Controller.aimPivotObject
                ? Controller.aimPivotObject.transform
                : null;

            Vector3 worldPosition = Controller.transform.TransformPoint(step.hitDetectorPositionOffset);
            Quaternion worldRotation = Controller.transform.rotation * Quaternion.Euler(step.hitDetectorRotationOffset);

            if (PoolManager.Instance != null)
            {
                if (parent)
                    activeHitDetector = PoolManager.Instance.Spawn(step.hitDetectorPrefab, step.hitDetectorPositionOffset, step.hitDetectorRotationOffset, parent);
                else
                    activeHitDetector = PoolManager.Instance.Spawn(step.hitDetectorPrefab, worldPosition, worldRotation.eulerAngles);
            }
            else
            {
                activeHitDetector = Instantiate(step.hitDetectorPrefab, worldPosition, worldRotation, parent);
            }

            if (activeHitDetector == null)
            {
                return;
            }
            
            activeHitDetector.Initialize(Controller);
        }

        void ReleaseActiveHitDetector()
        {
            if (activeHitDetector == null)
            {
                return;
            }

            activeHitDetector.Deactivate();

            if (PoolManager.Instance)
            {
                PoolManager.Instance.Despawn(activeHitDetector);
            }
            else
            {
                Destroy(activeHitDetector.gameObject);
            }

            activeHitDetector = null;
        }

        void ExecuteStepAction(ComboStep step, IDamageable target)
        {
            AgentActionDefinition runtimeAction = step.action ? step.action : actionDefinition;
            if (!runtimeAction || !Controller)
            {
                return;
            }

            float magnitude = Mathf.Max(0f, actionMagnitude) * Mathf.Max(0f, step.magnitudeMultiplier);
            AgentActionRuntime runtime = new AgentActionRuntime(Controller, this, target, magnitude);
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
            if (!Controller || !body)
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

        void QueueOrExecuteTransition(ComboTransition transition, float holdDuration, float holdNormalized, bool isLongPress,
            ComboInput input)
        {
            ComboStep step = transition.nextStep;
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
            pendingStepExpireTime = Time.time + QueuedInputLifetime;
            pendingDelayActive = false;
            pendingStepReady = false;
            pendingStepIsLongPress = isLongPress;
            pendingStepHoldDuration = Mathf.Max(0f, holdDuration);
            pendingStepHoldNormalized = Mathf.Clamp01(holdNormalized);
            pendingStepInput = input;
            currentStepInput = input;
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
                bool isLongPress = pendingStepIsLongPress;
                float holdDuration = pendingStepHoldDuration;
                float holdNormalized = pendingStepHoldNormalized;
                ComboInput input = pendingStepInput;
                ResetPendingTransition();
                StartStep(step, true, isLongPress, holdDuration, holdNormalized, input);
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

            FindTransitionOptions(currentStep, ComboInput.Release, out ComboTransition releaseTransition, out _);
            if (releaseTransition != null)
            {
                QueueOrExecuteTransition(releaseTransition, 0f, 0f, false, currentStepInput);
            }
        }

        void FindTransitionOptions(ComboStep step, ComboInput input, out ComboTransition shortPress, out ComboTransition longPress)
        {
            shortPress = null;
            longPress = null;

            if (step == null || step.transitions == null)
            {
                return;
            }

            foreach (ComboTransition transition in step.transitions)
            {
                ComboInput mapped = transition.input == ComboInput.SameAsBinding
                    ? MapBindingToInput(binding)
                    : transition.input;

                if (mapped == input)
                {
                    if (transition.longPress)
                    {
                        longPress ??= transition;
                    }
                    else
                    {
                        shortPress ??= transition;
                    }
                }
            }
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
            if (clearStepReference && currentStep != null)
            {
                ApplyStepPressMetadata(currentStep, false, 0f, 0f);
            }

            if (clearStepReference)
            {
                currentStep = null;
            }
            ReleaseActiveHitDetector();
            currentStepElapsed = 0f;
            stepWasActive = false;
            stepRegisteredHit = false;
            stepHitTargets.Clear();
            nudgePending = false;
            nudgeTimer = 0f;
            inActivePhase = false;
            inRecoveryPhase = false;
            stepStartTime = Time.time;
            chargeWindupActive = false;
            chargeReleaseRequested = false;
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
            pendingStepIsLongPress = false;
            pendingStepHoldDuration = 0f;
            pendingStepHoldNormalized = 0f;
            pendingStepInput = ComboInput.Primary;
            transitionDelayTimer.Cancel();
        }

        void ResetComboState()
        {
            Controller?.RemoveMovementModifier(overrideMovementProfile);
            ResetPendingTransition();
            ResetStepState();
            comboResetTimer.Cancel();
            pendingComboResetDelay = 0f;
            inputPressStates.Clear();
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
                    ProcessComboInput(input, pressed);
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

            foreach (ComboEntry entry in EntrySteps)
            {
                result.Add(entry.input);
            }

            foreach (ComboStep step in Steps)
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

            if (Steps == null)
            {
                return;
            }

            foreach (ComboStep step in Steps)
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
