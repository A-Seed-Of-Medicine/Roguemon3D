using System;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Effects
{
    [Serializable]
    public sealed class KnockbackEffect : Effect
    {
        [SerializeField] private float force = 5f;
        [SerializeField] private bool scaleWithMagnitude = true;
        [SerializeField] private bool clearVelocity = true;
        [SerializeField] private ForceMode forceMode = ForceMode.Impulse;
        [SerializeField] private bool useContextDirection = true;
        [SerializeField] private Vector3 customDirection = Vector3.forward;

        public override void Apply(EffectContext context)
        {
            if (context == null)
            {
                return;
            }

            if (context.Target is not IMovable knockbackable)
            {
                return;
            }

            Vector3 direction = useContextDirection ? context.Direction : customDirection;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.forward;
            }

            float finalForce = force * (scaleWithMagnitude ? Mathf.Max(0f, context.Magnitude) : 1f);
            var settings = new KnockbackSettings(forceMode, clearVelocity);
            knockbackable.ApplyKnockback(direction.normalized, finalForce, settings);
        }
    }
}
