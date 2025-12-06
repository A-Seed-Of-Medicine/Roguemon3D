using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Projectiles
{
    /// <summary>
    /// Projectile that travels along an arc towards a resolved target position.
    /// Uses CharacterAimAction launch data to pick a destination, optionally clamped
    /// by distance, and spawns feedback on arrival.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ArcProjectile : Projectile
    {
        [Header("Arc Targeting")]
        [SerializeField, Min(0f)] private float defaultDistance = 8f;
        [SerializeField, Min(0f)] private float minDistance = 0.5f;
        [SerializeField, Min(0f)] private float maxDistance = 25f;

        [Header("Arc Shape")]
        [SerializeField, Min(0.01f)] private float baseFlightDuration = 1f;
        [SerializeField, Min(0f)] private float flightDurationPerUnit = 0.05f;
        [SerializeField] private AnimationCurve heightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 0f);
        [SerializeField, Min(0f)] private float arcHeight = 2.5f;
        [SerializeField, Min(0f)] private float distanceHeightMultiplier = 0.15f;
        [SerializeField] private bool alignRotationToArc = true;

        [Header("Target Feedback")]
        [SerializeField] private ParticleSystem targetImpactParticles;
        [SerializeField] private Vector3 targetImpactOffset;
        [SerializeField, Min(0f)] private float impactLifetimeMultiplier = 1f;

        Vector3 resolvedTarget;
        Vector3 planarOrigin;
        Vector3 planarTarget;
        float travelDuration;
        float travelTime;
        bool arrived;

        public override void Launch(LaunchData data)
        {
            base.Launch(data);
            ResolveTarget(data);
            travelTime = 0f;
            arrived = false;
        }

        protected override bool CustomFixedUpdate(float deltaTime)
        {
            if (arrived)
            {
                return true;
            }

            travelTime += deltaTime;
            float completion = travelDuration > 0.0001f ? Mathf.Clamp01(travelTime / travelDuration) : 1f;

            Vector3 planarPosition = Vector3.Lerp(planarOrigin, planarTarget, completion);
            float distance = Vector3.Distance(planarOrigin, planarTarget);
            float heightAmplitude = arcHeight + distance * distanceHeightMultiplier;
            float heightOffset = heightCurve != null ? heightCurve.Evaluate(completion) * heightAmplitude : 0f;
            float verticalPosition = Mathf.Lerp(LaunchPosition.y, resolvedTarget.y, completion) + heightOffset;

            Vector3 nextPosition = new Vector3(planarPosition.x, verticalPosition, planarPosition.z);
            Vector3 displacement = nextPosition - transform.position;
            Body.linearVelocity = displacement / Mathf.Max(deltaTime, 0.0001f);
            Body.MovePosition(nextPosition);

            if (alignRotationToArc && displacement.sqrMagnitude > 0.0001f)
            {
                Quaternion lookRotation = Quaternion.LookRotation(displacement.normalized, Vector3.up);
                Body.MoveRotation(lookRotation);
            }

            if (completion >= 1f)
            {
                arrived = true;
                HandleArrival();
            }

            return true;
        }

        void ResolveTarget(LaunchData data)
        {
            Vector3 origin = data.Origin;
            Vector3 rawTarget = data.TargetPosition ?? ResolveDefaultTarget(origin, data.Direction);
            Vector3 planarDelta = new Vector3(rawTarget.x - origin.x, 0f, rawTarget.z - origin.z);
            float planarDistance = planarDelta.magnitude;

            float desiredDistance = planarDistance;
            if (maxDistance > 0f)
            {
                desiredDistance = Mathf.Min(desiredDistance, maxDistance);
            }
            desiredDistance = Mathf.Max(desiredDistance, minDistance);

            if (desiredDistance <= 0.0001f)
            {
                desiredDistance = defaultDistance;
            }

            if (planarDelta.sqrMagnitude > 0.0001f)
            {
                planarDelta = planarDelta.normalized * desiredDistance;
            }
            else
            {
                planarDelta = ResolveDefaultDirection(data.Direction) * desiredDistance;
            }

            resolvedTarget = new Vector3(origin.x + planarDelta.x, rawTarget.y, origin.z + planarDelta.z);
            planarOrigin = new Vector3(origin.x, 0f, origin.z);
            planarTarget = new Vector3(resolvedTarget.x, 0f, resolvedTarget.z);
            travelDuration = Mathf.Max(0.01f, baseFlightDuration + desiredDistance * flightDurationPerUnit);
        }

        Vector3 ResolveDefaultTarget(Vector3 origin, Vector3 direction)
        {
            Vector3 fallbackDirection = ResolveDefaultDirection(direction);
            return origin + fallbackDirection * defaultDistance;
        }

        static Vector3 ResolveDefaultDirection(Vector3 direction)
        {
            if (direction.sqrMagnitude > 0.0001f)
            {
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    return direction.normalized;
                }
            }

            return Vector3.forward;
        }

        void HandleArrival()
        {
            SpawnImpactFeedback();
            Expire();
        }

        void SpawnImpactFeedback()
        {
            if (!targetImpactParticles)
            {
                return;
            }

            ParticleSystem impactInstance = Instantiate(targetImpactParticles, resolvedTarget + targetImpactOffset,
                Quaternion.identity);
            var main = impactInstance.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(travelDuration * impactLifetimeMultiplier);
            if (!impactInstance.isPlaying)
            {
                impactInstance.Play(true);
            }
        }
    }
}
