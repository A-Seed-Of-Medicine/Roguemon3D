using System;
using _PinBoy.Scripts.CharacterMovement;
using AdvancedController;
using UnityEngine;
using UnityEngine.Events;

namespace _PinBoy.Scripts.Gameplay.Effects
{
    [Flags]
    public enum AllegianceType
    {
        Ally = 1 << 0,
        Enemy = 1 << 1,
        Neutral = 1 << 2,
    }
    
    [Serializable]
    public struct DamageInfo
    {
        public float amount;
        public IDamager source;
        public IDamageable target;
        public Vector3 direction;
        public Vector3 point;

        public DamageInfo(float amount, IDamager source, IDamageable target, Vector3 direction, Vector3 point)
        {
            this.amount = amount;
            this.source = source;
            this.target = target;
            this.direction = direction;
            this.point = point;
        }
    }

    public interface IDamageable
    {
        Transform transform { get; }
        public AllegianceType allegiance { get; }
        void ApplyDamage(DamageInfo damageInfo);
        Health health { get;  }
        StatusHandler statusHandler { get;  }
    }
    
    public interface IDamager
    {
        Transform transform { get; }
        AllegianceType allegiance { get; }
        void ApplyDamage(DamageInfo damageInfo);
    }

    public interface IMovable
    {
        void ApplyKnockback(Vector3 direction, float force, KnockbackSettings settings);
        void ApplyMovementModifier(MovementProfile profile, float duration);
        void RemoveMovementModifier(MovementProfile profile);
    }

    [Serializable]
    public struct KnockbackSettings
    {
        public ForceMode forceMode;
        public bool clearVelocityBeforeImpact;

        public KnockbackSettings(ForceMode forceMode, bool clearVelocityBeforeImpact)
        {
            this.forceMode = forceMode;
            this.clearVelocityBeforeImpact = clearVelocityBeforeImpact;
        }
    }
}
