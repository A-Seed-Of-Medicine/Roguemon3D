using System;
using AdvancedController;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Effects
{
    [Serializable]
    public sealed class ApplyStatusEffect : Effect
    {
        public enum Recipient
        {
            Target,
            Source
        }

        [SerializeField] private StatusType statusType = StatusType.Stunned;
        [SerializeField] private Recipient recipient = Recipient.Target;
        [SerializeField, Min(0f)] private float duration = 1f;
        [SerializeField, Min(0f)] private float minimumDuration = 0f;
        [SerializeField] private bool scaleByMagnitude;

        public override void Apply(EffectContext context)
        {
            if (context == null)
            {
                return;
            }

            var statusTarget = ResolveRecipient(context);
            if (statusTarget?.statusHandler == null)
            {
                return;
            }

            var status = statusTarget.statusHandler.GetStatus(statusType);
            if (status == null)
            {
                return;
            }

            float finalDuration = Mathf.Max(0f, duration);
            if (scaleByMagnitude)
            {
                finalDuration *= Mathf.Max(0f, context.Magnitude);
            }

            finalDuration = Mathf.Max(finalDuration, minimumDuration);
            if (finalDuration <= 0f)
            {
                return;
            }

            status.StartStatus(finalDuration);
        }

        private IDamageable ResolveRecipient(EffectContext context)
        {
            return recipient switch
            {
                Recipient.Source => context.Source as IDamageable ?? context.Target,
                _ => context.Target
            };
        }
    }
}
