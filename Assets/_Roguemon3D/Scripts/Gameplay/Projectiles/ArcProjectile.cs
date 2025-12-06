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

        [Header("Target Effect")]
        [SerializeField] private ParticleSystem targetEffectPrefab;
        [SerializeField] private float targetEffectLifetimeMultiplier = 1f;
        [SerializeField] private bool alignEffectToSurface = true;
        [SerializeField, Min(0f)] private float surfaceRaycastDistance = 2f;
        [SerializeField] private LayerMask surfaceMask = ~0;

        Vector3 targetPosition;
        float flightDuration;
        float flightElapsed;
        ParticleSystem activeTargetEffect;

        protected override bool ShouldApplyAcceleration => false;
        protected override bool ShouldMaintainVelocity => true;

        public override void Launch(LaunchData data)
        {
            targetPosition = ResolveTargetPosition(data);
            data.Direction = ResolveLaunchDirection(data, targetPosition);

            flightDuration = CalculateFlightDuration(data, targetPosition);
            flightElapsed = 0f;

            base.Launch(data);
            SpawnTargetEffect();
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

        void SpawnTargetEffect()
        {
            if (!targetEffectPrefab)
            {
                return;
            }

            Vector3 spawnPosition = targetPosition;
            Quaternion spawnRotation = Quaternion.identity;

            if (alignEffectToSurface)
            {
                if (Physics.Raycast(targetPosition + Vector3.up * surfaceRaycastDistance, Vector3.down, out RaycastHit hit,
                        surfaceRaycastDistance * 2f, surfaceMask))
                {
                    spawnPosition = hit.point;
                    spawnRotation = Quaternion.LookRotation(hit.normal, Vector3.up);
                }
            }

            activeTargetEffect = Instantiate(targetEffectPrefab, spawnPosition, spawnRotation);
            float desiredLifetime = flightDuration * targetEffectLifetimeMultiplier;
            ParticleSystem.MainModule main = activeTargetEffect.main;
            main.startLifetime = desiredLifetime;
        }
    }
}
