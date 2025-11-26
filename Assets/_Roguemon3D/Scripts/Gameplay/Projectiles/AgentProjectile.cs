using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Effects;
using UnityEngine;
using UnityEngine.Events;

namespace _PinBoy.Scripts.Gameplay.Projectiles
{
    /// <summary>
    /// Projectile variant that communicates with the agent damage pipeline by emitting
    /// IDamageable/IDamager-aware callbacks on impact.
    /// </summary>
    public class AgentProjectile : Projectile
    {
        [Header("Damage")]
        [SerializeField] private float baseDamage = 10f;
        [SerializeField] private bool scaleDamageWithMagnitude = true;
        [SerializeField] private bool ignoreSameAllegiance = true;
        [SerializeField] private List<AllegianceType> allegianceMask = new();
        [SerializeField] private bool notifyDamagerOnHit = true;
        [SerializeField] private UnityEvent<DamageInfo> onDamageApplied;

        protected override bool IsValidTarget(Collider other)
        {
            if (!base.IsValidTarget(other))
            {
                return false;
            }

            IDamageable damageable = other.GetComponentInParent<IDamageable>();
            if (damageable == null)
            {
                return false;
            }

            if (ignoreSameAllegiance && LaunchDamager is IDamageable damagerAsDamageable &&
                damageable.allegiance == damagerAsDamageable.allegiance)
            {
                return false;
            }

            if (allegianceMask is { Count: > 0 } && !allegianceMask.Contains(damageable.allegiance))
            {
                return false;
            }

            return true;
        }

        protected override void HandleHit(Collider other, Vector3? hitPoint = null)
        {
            IDamageable damageable = other.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                float magnitudeMultiplier = scaleDamageWithMagnitude ? Mathf.Max(LaunchMagnitude, 0f) : 1f;
                float finalDamage = baseDamage * (magnitudeMultiplier <= 0f ? 1f : magnitudeMultiplier);
                Vector3 point = hitPoint ?? other.bounds.ClosestPoint(transform.position);
                Vector3 direction = LaunchDirection.sqrMagnitude > 0.0001f ? LaunchDirection : transform.forward;
                DamageInfo info = new DamageInfo(finalDamage, LaunchDamager, damageable, direction, point);
                damageable.ApplyDamage(info);

                if (notifyDamagerOnHit && LaunchDamager is AgentController controller)
                {
                    controller.NotifyDamageDealt(info);
                }

                onDamageApplied?.Invoke(info);
            }

            base.HandleHit(other, hitPoint);
        }
    }
}
