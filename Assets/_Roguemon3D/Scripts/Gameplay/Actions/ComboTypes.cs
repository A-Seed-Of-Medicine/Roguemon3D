using System;
using _PinBoy.Scripts.CharacterMovement;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    /// <summary>
    /// Inputs that can drive combo steps and transitions.
    /// </summary>
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

    /// <summary>
    /// Entry points for starting a combo graph.
    /// </summary>
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

    /// <summary>
    /// Transition definition between combo steps.
    /// </summary>
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
        [Header("Long Press")]
        [Tooltip("If true this transition is triggered by a long press instead of a tap.")]
        public bool longPress;
        [Tooltip("Minimum time (seconds) a button must be held before this transition can trigger.")]
        [Min(0f)] public float longPressMinThreshold = 0.35f;
        [Tooltip("Maximum time (seconds) to consider when normalizing the press duration for this transition.")]
        [Min(0f)] public float longPressMaxThreshold = 1f;
    }

    /// <summary>
    /// Definition of a single combo step.
    /// </summary>
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
        public bool lockMovementInRecovery = true;
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
        public GameObject vfx;

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
}
