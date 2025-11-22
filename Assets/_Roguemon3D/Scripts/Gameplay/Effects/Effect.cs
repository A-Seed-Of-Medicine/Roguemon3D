using System;
using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using UnityEngine;
using UtilityAI;

namespace _PinBoy.Scripts.Gameplay.Effects
{
    [Serializable]
    public abstract class Effect
    {
        public abstract void Apply(EffectContext context);
    }

    public sealed class EffectContext
    {
        public EffectContext(Context aiContext, IDamager source, IDamageable target, Vector3 sourcePosition,
            Vector3 targetPosition, Vector3 direction, float magnitude)
        {
            AIContext = aiContext;
            Source = source;
            Target = target;
            SourcePosition = sourcePosition;
            TargetPosition = targetPosition;
            Direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
            Magnitude = magnitude;
        }

        public Context AIContext { get; }
        public IDamager Source { get; }
        public IDamageable Target { get; }
        public Vector3 SourcePosition { get; }
        public Vector3 TargetPosition { get; }
        public Vector3 Direction { get; }
        public float Magnitude { get; }
        public float Distance => Vector3.Distance(SourcePosition, TargetPosition);
    }
}
