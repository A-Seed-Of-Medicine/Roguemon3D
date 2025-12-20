using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Actions;
using _PinBoy.Scripts.Gameplay.Effects;
using UnityEngine;

public class HitComboDetector : HitDetector
{
    [System.Serializable]
    public class PhaseParticleEffect
    {
        public ParticleSystem particleEffect;
        public CharacterAction.ActionPhase startPhase = CharacterAction.ActionPhase.Active;
        public CharacterAction.ActionPhase endPhase = CharacterAction.ActionPhase.None;

        float? baseSimulationSpeed;

        public void HandlePhaseStart(CharacterAction.ActionPhase phase, CharacterComboAction.ComboStep step)
        {
            if (particleEffect == null)
            {
                return;
            }

            if (phase != startPhase)
            {
                return;
            }

            CacheBaseSimulationSpeed();
            ApplySimulationSpeed(step);
            particleEffect.Clear(true);
            particleEffect.Play();
        }

        public void HandlePhaseEnd(CharacterAction.ActionPhase phase)
        {
            if (particleEffect == null)
            {
                return;
            }

            if (endPhase == CharacterAction.ActionPhase.None || phase != endPhase)
            {
                return;
            }

            particleEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            RestoreBaseSimulationSpeed();
        }

        public void Reset()
        {
            if (particleEffect == null)
            {
                return;
            }

            particleEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            RestoreBaseSimulationSpeed();
        }

        void CacheBaseSimulationSpeed()
        {
            if (particleEffect == null || baseSimulationSpeed.HasValue)
            {
                return;
            }

            baseSimulationSpeed = particleEffect.main.simulationSpeed;
        }

        void ApplySimulationSpeed(CharacterComboAction.ComboStep step)
        {
            if (particleEffect == null)
            {
                return;
            }

            if (endPhase == CharacterAction.ActionPhase.None || step == null)
            {
                RestoreBaseSimulationSpeed();
                return;
            }

            float targetDuration = CalculatePhaseDuration(step, startPhase, endPhase);
            float particleDuration = Mathf.Max(0.0001f, particleEffect.main.duration);
            float speed = particleDuration / Mathf.Max(0.0001f, targetDuration);
            ParticleSystem.MainModule main = particleEffect.main;
            main.simulationSpeed = speed;
        }

        void RestoreBaseSimulationSpeed()
        {
            if (particleEffect == null || !baseSimulationSpeed.HasValue)
            {
                return;
            }

            ParticleSystem.MainModule main = particleEffect.main;
            main.simulationSpeed = baseSimulationSpeed.Value;
        }

        static float CalculatePhaseDuration(CharacterComboAction.ComboStep step, CharacterAction.ActionPhase start, CharacterAction.ActionPhase end)
        {
            int startIndex = PhaseIndex(start);
            int endIndex = PhaseIndex(end);

            if (startIndex < 0 || endIndex < 0)
            {
                return 0f;
            }

            if (startIndex > endIndex)
            {
                (startIndex, endIndex) = (endIndex, startIndex);
            }

            float duration = 0f;
            for (int i = startIndex; i <= endIndex; i++)
            {
                CharacterAction.ActionPhase phase = (CharacterAction.ActionPhase)i;
                duration += GetPhaseDuration(step, phase);
            }

            return Mathf.Max(0.0001f, duration);
        }

        static int PhaseIndex(CharacterAction.ActionPhase phase)
        {
            return phase switch
            {
                CharacterAction.ActionPhase.Windup => 0,
                CharacterAction.ActionPhase.Active => 1,
                CharacterAction.ActionPhase.Recovery => 2,
                _ => -1
            };
        }

        static float GetPhaseDuration(CharacterComboAction.ComboStep step, CharacterAction.ActionPhase phase)
        {
            return phase switch
            {
                CharacterAction.ActionPhase.Windup => step.windup,
                CharacterAction.ActionPhase.Active => step.active,
                CharacterAction.ActionPhase.Recovery => step.recovery,
                _ => 0f
            };
        }
    }

    [Header("Phase Effects")]
    [SerializeField] CharacterAction.ActionPhase windupDeactivePhase = CharacterAction.ActionPhase.Recovery;
    [SerializeField] PhaseParticleEffect[] phaseParticleEffects = System.Array.Empty<PhaseParticleEffect>();

    CharacterComboAction.ComboStep activeStep;

    public void Activate(CharacterComboAction.ComboStep step, float activeDuration)
    {
        activeStep = step;
        base.Activate(activeDuration);
    }

    public override void Deactivate()
    {
        base.Deactivate();
        activeStep = null;
        ResetPhaseParticleEffects();
    }

    public void HandlePhaseStart(CharacterAction.ActionPhase phase, CharacterComboAction.ComboStep step)
    {
        if (phase == CharacterAction.ActionPhase.Windup)
            HandleWindupIndicator(step.windup);

        if (phaseParticleEffects == null || phaseParticleEffects.Length == 0)
        {
            return;
        }

        foreach (PhaseParticleEffect effect in phaseParticleEffects)
        {
            effect?.HandlePhaseStart(phase, step);
        }
    }

    public void HandlePhaseEnd(CharacterAction.ActionPhase phase)
    {
        if (phaseParticleEffects == null || phaseParticleEffects.Length == 0)
        {
            return;
        }

        foreach (PhaseParticleEffect effect in phaseParticleEffects)
        {
            effect?.HandlePhaseEnd(phase);
        }
    }

    public override void EvaluateHits(HashSet<IDamageable> hitTargets, bool allowRepeatedHits,
        System.Action<IDamageable, Collider> onHit)
    {
        if (activeStep == null)
        {
            return;
        }

        base.EvaluateHits(hitTargets, allowRepeatedHits, onHit);
    }
    
    void ResetPhaseParticleEffects()
    {
        if (phaseParticleEffects == null || phaseParticleEffects.Length == 0)
        {
            return;
        }

        foreach (PhaseParticleEffect effect in phaseParticleEffects)
        {
            effect?.Reset();
        }
    }
}
