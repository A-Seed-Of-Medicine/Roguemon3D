using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Actions;
using _PinBoy.Scripts.Gameplay.Effects;
using UnityEngine;

public class HitComboDetector : HitDetector
{
    public enum ExecutionPhase
    {
        None = -1,
        Windup = 0,
        Active = 1,
        Recovery = 2
    }
    
    [System.Serializable]
    public class PhaseParticleEffect
    {
        public ParticleSystem particleEffect;
        public ExecutionPhase startPhase = ExecutionPhase.Active;
        public ExecutionPhase endPhase = ExecutionPhase.None;

        float? baseSimulationSpeed;

        public void HandlePhaseStart(ExecutionPhase phase, CharacterComboAction.ComboStep step)
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

        public void HandlePhaseEnd(ExecutionPhase phase)
        {
            if (particleEffect == null)
            {
                return;
            }

            if (endPhase == ExecutionPhase.None || phase != endPhase)
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

            if (endPhase == ExecutionPhase.None || step == null)
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

        static float CalculatePhaseDuration(CharacterComboAction.ComboStep step, ExecutionPhase start, ExecutionPhase end)
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
                ExecutionPhase phase = (ExecutionPhase)i;
                duration += GetPhaseDuration(step, phase);
            }

            return Mathf.Max(0.0001f, duration);
        }

        static int PhaseIndex(ExecutionPhase phase)
        {
            return phase switch
            {
                ExecutionPhase.Windup => 0,
                ExecutionPhase.Active => 1,
                ExecutionPhase.Recovery => 2,
                _ => -1
            };
        }

        static float GetPhaseDuration(CharacterComboAction.ComboStep step, ExecutionPhase phase)
        {
            return phase switch
            {
                ExecutionPhase.Windup => step.windup,
                ExecutionPhase.Active => step.active,
                ExecutionPhase.Recovery => step.recovery,
                _ => 0f
            };
        }
    }

    [Header("Phase Effects")]
    [SerializeField] ExecutionPhase windupDeactivePhase = ExecutionPhase.Recovery;
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

    public void HandlePhaseStart(ExecutionPhase phase, CharacterComboAction.ComboStep step)
    {
        if (phase == ExecutionPhase.Windup)
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

    public void HandlePhaseEnd(ExecutionPhase phase)
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
