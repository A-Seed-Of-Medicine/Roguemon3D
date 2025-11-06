using System;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Effects
{
    [Serializable]
    public sealed class DamageEffect : Effect
    {
        [SerializeField] private float amount = 10f;
        [SerializeField] private bool scaleWithMagnitude = true;

        public override void Apply(EffectContext context)
        {
            if (context == null)
            {
                return;
            }

            float finalAmount = amount * (scaleWithMagnitude ? Mathf.Max(0f, context.Magnitude) : 1f);
            context.Target.ApplyDamage(new DamageInfo(finalAmount, context.Source, context.Target, context.Direction, context.TargetPosition));
        }
    }
}
