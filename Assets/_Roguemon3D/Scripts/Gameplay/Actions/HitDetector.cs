using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Actions;
using _PinBoy.Scripts.Gameplay.Effects;
using UnityEngine;

public class HitDetector : MonoBehaviour
{
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
            //Debug.Log($"Starting particle effect for phase {phase} with step {step} and base speed {baseSimulationSpeed}");
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

    public enum ExecutionPhase
    {
        None = -1,
        Windup,
        Active,
        Recovery
    }

    [Header("Detection")]
    [SerializeField] Collider[] triggerColliders = System.Array.Empty<Collider>();
    [SerializeField] LayerMask targetLayers = Physics.DefaultRaycastLayers;
    [SerializeField] bool includeTriggerColliders = true;
    [SerializeField] List<AllegianceType> allegianceMask = new();
    
    public ProceduralMeshGenerator windupIndicator;
    public bool scaleWindupDuration = true;
    public ExecutionPhase windupDeactivePhase = ExecutionPhase.Recovery;
    private static readonly int TimerStart = Shader.PropertyToID("_TimerStart");
    private static readonly int TimerDuration = Shader.PropertyToID("_TimerDuration");

    [Header("Animation")]
    [SerializeField] AnimationClip activeAnimation;

    [Header("Phase Effects")]
    [SerializeField] PhaseParticleEffect[] phaseParticleEffects = System.Array.Empty<PhaseParticleEffect>();

    readonly Collider[] colliderCache = new Collider[16];

    AgentController owner;
    CharacterComboAction.ComboStep activeStep;

    public void Initialize(AgentController agentController)
    {
        owner = agentController;
    }

    public void Activate(CharacterComboAction.ComboStep step, float activeDuration)
    {
        activeStep = step;
        PlayActiveAnimation(activeDuration);
    }

    public void Deactivate()
    {
        activeStep = null;
        ResetPhaseParticleEffects();
    }

    public void HandlePhaseStart(ExecutionPhase phase, CharacterComboAction.ComboStep step)
    {
        if (phase == ExecutionPhase.Windup)
            HandleWindupIndicator(step);
        
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
    
    public void HandleWindupIndicator(CharacterComboAction.ComboStep step)
    {
        if (!windupIndicator)
            return;
        if (scaleWindupDuration)
        {
            float duration = scaleWindupDuration ? Mathf.Max(0.0001f, step.windup) : 1f;
            ParticleSystem.MainModule main = windupIndicator.particleSystem.main;
            main.startLifetime = duration;
        }
        windupIndicator.particleSystem.time = 0f;
        windupIndicator.particleSystem.Play();
    }

    public void EvaluateHits(HashSet<IDamageable> hitTargets, bool allowRepeatedHits, System.Action<IDamageable> onHit)
    {
        if (activeStep == null)
        {
            return;
        }
        
        Collider[] colliders = triggerColliders;
        if (windupIndicator && windupIndicator.colliders != null)
        {
            colliders = new Collider[triggerColliders.Length + windupIndicator.colliders.Count];
            triggerColliders.CopyTo(colliders, 0);
            windupIndicator.colliders.CopyTo(colliders, triggerColliders.Length);
            Debug.Log(windupIndicator.colliders.Count);
        }
        
        if (colliders == null || colliders.Length == 0)
        {
            return;
        }

        StepOverlapSettings settings = GetOverlapSettings();

        foreach (Collider source in colliders)
        {
            if (!source)
            {
                continue;
            }
            Debug.Log(source.gameObject.name);

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
                if (damageable == null || (AgentController)damageable == owner)
                {
                    continue;
                }

                if (allegianceMask is { Count: > 0 } && !allegianceMask.Contains(damageable.allegiance))
                {
                    continue;
                }

                if (!allowRepeatedHits && hitTargets.Contains(damageable))
                {
                    continue;
                }

                hitTargets.Add(damageable);
                onHit?.Invoke(damageable);
            }
        }
    }

    void PlayActiveAnimation(float activeDuration)
    {
        if (activeAnimation == null)
        {
            return;
        }

        Animation animation = GetComponent<Animation>();
        if (animation == null)
        {
            animation = gameObject.AddComponent<Animation>();
        }

        string clipName = activeAnimation.name;
        if (animation.GetClip(clipName) == null)
        {
            animation.AddClip(activeAnimation, clipName);
        }

        if (activeDuration > 0.0001f && activeAnimation.length > 0.0001f)
        {
            animation[clipName].speed = activeAnimation.length / activeDuration;
        }
        else
        {
            animation[clipName].speed = 1f;
        }

        animation[clipName].time = 0f;
        animation.Play(clipName);
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

    StepOverlapSettings GetOverlapSettings()
    {
        return new StepOverlapSettings
        {
            LayerMask = targetLayers,
            Query = includeTriggerColliders ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore
        };
    }

    struct StepOverlapSettings
    {
        public LayerMask LayerMask;
        public QueryTriggerInteraction Query;
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

        return source switch
        {
            BoxCollider box => OverlapBoxCollider(box, results, layerMask, query),
            SphereCollider sphere => OverlapSphereCollider(sphere, results, layerMask, query),
            CapsuleCollider capsule => OverlapCapsuleCollider(capsule, results, layerMask, query),
            _ =>
                Physics.OverlapBoxNonAlloc(source.bounds.center, source.bounds.extents, results, Quaternion.identity,
                    layerMask, query)
        };
    }

    static int OverlapBoxCollider(BoxCollider collider, Collider[] results, LayerMask layerMask, QueryTriggerInteraction query)
    {
        Vector3 center = collider.transform.TransformPoint(collider.center);
        Vector3 lossyScale = collider.transform.lossyScale;
        Vector3 halfExtents = Vector3.Scale(collider.size * 0.5f,
            new Vector3(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y), Mathf.Abs(lossyScale.z)));
        Quaternion orientation = collider.transform.rotation;
        return Physics.OverlapBoxNonAlloc(center, halfExtents, results, orientation, layerMask, query);
    }

    static int OverlapSphereCollider(SphereCollider collider, Collider[] results, LayerMask layerMask,
        QueryTriggerInteraction query)
    {
        Vector3 center = collider.transform.TransformPoint(collider.center);
        float radius = collider.radius * MaxAbsComponent(collider.transform.lossyScale);
        return Physics.OverlapSphereNonAlloc(center, radius, results, layerMask, query);
    }

    static int OverlapCapsuleCollider(CapsuleCollider collider, Collider[] results, LayerMask layerMask,
        QueryTriggerInteraction query)
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
}
