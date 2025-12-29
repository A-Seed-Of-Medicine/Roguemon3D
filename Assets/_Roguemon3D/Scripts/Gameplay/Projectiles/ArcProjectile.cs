using System.Collections.Generic;
using System.Runtime.CompilerServices;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Effects;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Projectiles
{
    /// <summary>
    /// Projectile that travels along a controllable arc toward a target position.
    /// Compatible with the CharacterAimAction pipeline and ProjectileSpawnEffect.
    /// </summary>
    public class ArcProjectile : Projectile
    {
        [Header("Targeting")]
        [SerializeField, Min(0f)] private float defaultDistance = 8f;
        [SerializeField, Min(0f)] private float minDistance;
        [SerializeField, Min(0f)] private float maxDistance = 30f;

        [Header("Arc")]
        [SerializeField] private AnimationCurve heightCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 1f),
            new Keyframe(0.5f, 1f, 0f, 0f),
            new Keyframe(1f, 0f, -1f, 0f));
        [SerializeField] private float arcHeight = 2f;
        [SerializeField] private bool useSpeedForDuration = true;
        [SerializeField, Min(0.05f)] private float baseFlightDuration = 0.75f;
        [SerializeField, Min(0.05f)] private float minFlightDuration = 0.1f;
        [SerializeField, Min(0f)] private float maxFlightDuration = 5f;
        [SerializeField] private bool expireAtEndOfArc = true;

        [Header("Target Detection")]
        [SerializeField] private HitDetector targetDetectorPrefab;
        [SerializeField] private bool activateDetectorOnLaunch = true;
        [SerializeField, Min(0f)] private float detectorLifetimeMultiplier = 1f;

        Vector3 targetPosition;
        float flightDuration;
        float flightElapsed;
        HitDetector activeTargetDetector;

        protected override bool ShouldApplyAcceleration => false;
        protected override bool ShouldMaintainVelocity => true;

        public override void Launch(LaunchData data)
        {
            targetPosition = ResolveTargetPosition(data);
            data.Direction = ResolveLaunchDirection(data, targetPosition);

            flightDuration = CalculateFlightDuration(data, targetPosition);
            flightElapsed = 0f;

            base.Launch(data);
            SpawnTargetDetector(data);
        }

        protected override void FixedUpdate()
        {
            if (Launched)
            {
                flightElapsed += Time.fixedDeltaTime;
            }

            base.FixedUpdate();

            if (expireAtEndOfArc && Launched && flightElapsed >= flightDuration)
            {
                Expire();
            }
        }

        protected override void ApplyVelocity()
        {
            if (!Launched)
            {
                return;
            }

            float duration = Mathf.Max(0.0001f, flightDuration);
            float progress = Mathf.Clamp01(flightElapsed / duration);
            Vector3 targetPositionWithBaseHeight = Vector3.Lerp(LaunchPosition, targetPosition, progress);
            float heightOffset = arcHeight * (heightCurve != null ? heightCurve.Evaluate(progress) : 0f);
            Vector3 nextPosition = targetPositionWithBaseHeight + Vector3.up * heightOffset;

            Vector3 velocity = (nextPosition - Body.position) / Time.fixedDeltaTime;
            Body.linearVelocity = velocity;
            Body.MovePosition(nextPosition);
        }

        protected override void HandleHit(Collider other, Vector3? hitPoint = null)
        {
            //ApplyTargetDetectorHits();
            base.HandleHit(other, hitPoint);
        }

        protected override void Expire()
        {
            ApplyTargetDetectorHits();
            ApplyImpactEffects(transform.position);
            base.Expire();
        }

        Vector3 ResolveLaunchDirection(LaunchData data, Vector3 resolvedTarget)
        {
            Vector3 planarDirection = resolvedTarget - data.Origin;
            planarDirection.y = 0f;
            if (planarDirection.sqrMagnitude <= 0.0001f)
            {
                planarDirection = data.Direction;
            }

            if (planarDirection.sqrMagnitude <= 0.0001f)
            {
                planarDirection = Vector3.forward;
            }

            return planarDirection.normalized;
        }

        Vector3 ResolveTargetPosition(LaunchData data)
        {
            Vector3 direction = data.Direction.sqrMagnitude > 0.0001f ? data.Direction.normalized : transform.forward;
            Vector3 resolvedTarget = data.TargetPosition ?? (data.Origin + direction * defaultDistance);

            Vector3 displacement = resolvedTarget - data.Origin;
            float distance = displacement.magnitude;
            if (distance <= 0.0001f)
            {
                displacement = direction * Mathf.Max(minDistance, defaultDistance);
                distance = displacement.magnitude;
            }

            if (minDistance > 0f && distance < minDistance)
            {
                displacement = displacement.normalized * minDistance;
            }

            if (maxDistance > 0f && distance > maxDistance)
            {
                displacement = displacement.normalized * maxDistance;
            }

            return data.Origin + displacement;
        }

        float CalculateFlightDuration(LaunchData data, Vector3 resolvedTarget)
        {
            if (!useSpeedForDuration)
            {
                return ClampFlightDuration(baseFlightDuration);
            }

            float speed = Mathf.Max(0.01f, BaseSpeed * (data.SpeedMultiplier <= 0f ? 1f : data.SpeedMultiplier));
            float distance = Vector3.Distance(data.Origin, resolvedTarget);
            float durationFromSpeed = distance / speed;
            return ClampFlightDuration(durationFromSpeed);
        }

        float ClampFlightDuration(float duration)
        {
            float clamped = Mathf.Max(minFlightDuration, duration);
            if (maxFlightDuration > 0f)
            {
                clamped = Mathf.Min(maxFlightDuration, clamped);
            }

            return clamped;
        }

        void SpawnTargetDetector(LaunchData data)
        {
            if (!targetDetectorPrefab)
            {
                return;
            }

            Quaternion spawnRotation = Quaternion.LookRotation(data.Direction, Vector3.up);
            activeTargetDetector = Instantiate(targetDetectorPrefab, targetPosition, spawnRotation);

            AgentController owner = data.Owner ? data.Owner.GetComponentInParent<AgentController>() : null;
            activeTargetDetector.Initialize(owner);

            if (activateDetectorOnLaunch)
            {
                activeTargetDetector.Activate(flightDuration * detectorLifetimeMultiplier);
            }
        }

        void ApplyTargetDetectorHits()
        {
            if (!activeTargetDetector)
            {
                return;
            }

            if (!activateDetectorOnLaunch)
            {
                activeTargetDetector.Activate(flightDuration * detectorLifetimeMultiplier);
            }

            HashSet<IDamageable> hits = new HashSet<IDamageable>();
            activeTargetDetector.EvaluateHits(hits, false, OnDetectorHit);

            Destroy(activeTargetDetector.gameObject);
            activeTargetDetector = null;
        }

        void OnDetectorHit(IDamageable damageable, Collider collider)
        {
            if (damageable == null)
            {
                return;
            }

            Collider targetCollider = collider;
            if (!targetCollider && damageable is Component component)
            {
                targetCollider = component.GetComponentInChildren<Collider>();
            }

            if (targetCollider)
            {
                onHit?.Invoke(targetCollider);
            }

            Vector3 hitPosition = targetCollider
                ? targetCollider.bounds.ClosestPoint(activeTargetDetector.transform.position)
                : activeTargetDetector.transform.position;

            ApplyHitEffects(damageable, hitPosition);
        }

        void UpdateDetectorPosition(Vector3? hitPoint)
        {
            if (!activeTargetDetector || !hitPoint.HasValue)
            {
                return;
            }

            activeTargetDetector.transform.position = hitPoint.Value;
        }
    }
}
