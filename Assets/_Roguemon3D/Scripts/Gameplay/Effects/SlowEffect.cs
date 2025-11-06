using System;
using _PinBoy.Scripts.CharacterMovement;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Effects
{
    [Serializable]
    public sealed class SlowEffect : Effect
    {
        [SerializeField] private MovementProfile slowProfile;
        [SerializeField, Min(0f)] private float duration = 1f;
        [SerializeField] private bool removeOnExpire = true;

        public override void Apply(EffectContext context)
        {
            if (context == null || slowProfile == null)
            {
                return;
            }

            if (context.Target is not IMovable movable)
                return;

            float appliedDuration = removeOnExpire ? duration : 0f;
            movable.ApplyMovementModifier(slowProfile, appliedDuration);
        }
    }
}
