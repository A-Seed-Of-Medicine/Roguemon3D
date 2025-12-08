using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Effects;
using UnityEngine;

public class HitDetector : MonoBehaviour
{
    [Header("Detection")]
    [SerializeField] protected ProceduralMeshGenerator windupIndicator;
    [SerializeField] protected bool scaleWindupDuration = true;
    [SerializeField] protected Collider[] triggerColliders = System.Array.Empty<Collider>();
    [SerializeField] protected LayerMask targetLayers = Physics.DefaultRaycastLayers;
    [SerializeField] protected bool includeTriggerColliders = true;
    [SerializeField] protected List<AllegianceType> allegianceMask = new();

    [Header("Animation")]
    [SerializeField] protected AnimationClip activeAnimation;

    [Header("Ground Alignment")]
    [SerializeField] protected bool alignToGround = true;
    [SerializeField, Min(0f)] protected float groundRaycastDistance = 2f;
    [SerializeField] protected LayerMask groundLayerMask = 1 << 3; // Default to "Ground" layer
    [SerializeField, Min(0f)] protected float groundOffset = 0f;

    protected readonly Collider[] colliderCache = new Collider[16];

    AgentController owner;
    bool detectionActive;

    public virtual void Initialize(AgentController agentController)
    {
        owner = agentController;
        List<AllegianceType> contextMask = new();
        foreach (AllegianceType allegiance in allegianceMask)
        {
            switch (owner.allegiance)
            {
                case AllegianceType.Ally:
                    break;
                case AllegianceType.Enemy:
                    if (allegiance == AllegianceType.Ally)
                        contextMask.Add(AllegianceType.Enemy);
                    if (allegiance == AllegianceType.Enemy)
                        contextMask.Add(AllegianceType.Ally);
                    break;
                case AllegianceType.Neutral:
                    if (allegiance != AllegianceType.Ally)
                        contextMask.Add(AllegianceType.Neutral);
                    else if (!allegianceMask.Contains(AllegianceType.Ally))
                    {
                        contextMask.Add(AllegianceType.Ally);
                        contextMask.Add(AllegianceType.Enemy);
                    }

                    break;
            }
            
        }
        
        allegianceMask = contextMask;
        AlignToGround();
    }

    public virtual void Activate(float activeDuration)
    {
        detectionActive = true;
        HandleWindupIndicator(activeDuration);
        PlayActiveAnimation(activeDuration);
    }

    public virtual void Deactivate()
    {
        detectionActive = false;
    }
    
    public void HandleWindupIndicator(float windupDuration)
    {
        if (!windupIndicator)
            return;
        if (scaleWindupDuration)
        {
            float duration = scaleWindupDuration ? Mathf.Max(0.0001f, windupDuration) : 1f;
            ParticleSystem.MainModule main = windupIndicator.particleSystem.main;
            main.startLifetime = duration;
        }
        windupIndicator.particleSystem.time = 0f;
        windupIndicator.particleSystem.Play();
    }

    public void EvaluateHits(HashSet<IDamageable> hitTargets, bool allowRepeatedHits,
        System.Action<IDamageable> onHit)
    {
        EvaluateHits(hitTargets, allowRepeatedHits, (damageable, _) => onHit?.Invoke(damageable));
    }

    public virtual void EvaluateHits(HashSet<IDamageable> hitTargets, bool allowRepeatedHits,
        System.Action<IDamageable, Collider> onHit)
    {
        if (!detectionActive)
        {
            return;
        }

        Collider[] colliders = GetSourceColliders();

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
                onHit?.Invoke(damageable, other);
            }
        }
    }

    protected virtual Collider[] GetSourceColliders()
    {
        if (windupIndicator && windupIndicator.colliders != null)
        {
            Collider[] colliders = new Collider[triggerColliders.Length + windupIndicator.colliders.Count];
            triggerColliders.CopyTo(colliders, 0);
            windupIndicator.colliders.CopyTo(colliders, triggerColliders.Length);
            return colliders;
        }
        
        return triggerColliders;
    }

    protected void PlayActiveAnimation(float activeDuration)
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

    protected bool AlignToGround()
    {
        if (!alignToGround)
        {
            return false;
        }

        Vector3 origin = transform.position + Vector3.up * groundRaycastDistance;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundRaycastDistance * 2f, groundLayerMask,
                QueryTriggerInteraction.Ignore))
        {
            transform.position = hit.point + hit.normal * groundOffset;
            return true;
        }

        return false;
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
    
    protected bool IsValidAllegiance(IDamageable target)
    {
        if (allegianceMask == null || allegianceMask.Count == 0)
        {
            return true;
        }
        
        

        return allegianceMask.Contains(target.allegiance);
    }
}
