using System;
using UnityEngine;
using UnityEngine.Events;

namespace _PinBoy.Scripts.Gameplay.Effects
{
    [Serializable]
    public class Health
    {
        public Health (float maxHealth)
        {
            max = Mathf.Max(1f, maxHealth);
            current = max;
            isDead = false;
        }
        
        [Serializable]
        public class DamageEvent : UnityEvent<DamageInfo> { }
        
        [SerializeField, Min(1f)] private float max = 10f;
        [SerializeField, Min(1f)] private float current;
        [SerializeField] private bool isDead;
        public UnityEvent<Health> OnHealthChanged;
        private IDamageable damageable;

        public float Current => current;
        public float Max => max;
        public bool IsDead => isDead;
        public float Ratio => current / max;

        public void Init()
        {
            current = Mathf.Max(1f, max);
            isDead = false;
            OnHealthChanged?.Invoke(this);
        }

        public AllegianceType allegiance { get; }

        public void ApplyDamage(DamageInfo damageInfo)
        {
            if (isDead)
            {
                return;
            }

            current = Mathf.Max(0f, current - Mathf.Max(0f, damageInfo.amount));

            if (current <= 0f)
            {
                isDead = true;
            }
            OnHealthChanged?.Invoke(this);
        }
        
        public void Heal(float amount)
        {
            if (amount <= 0f)
                return;

            current = Mathf.Clamp(current + amount, 0f, max);
            if (current > 0f)
            {
                isDead = false;
            }
            OnHealthChanged?.Invoke(this);
        }

        public void SetMaxHealth(float newMaxHealth,  bool adjustCurrentProportionally = false)
        {
            if (adjustCurrentProportionally)
            {
                float healthRatio = current / max;
                max = Mathf.Max(1f, newMaxHealth);
                current = max * healthRatio;
                OnHealthChanged?.Invoke(this);
            }
            else
            {
                max = Mathf.Max(1f, newMaxHealth);
                current = Mathf.Min(current, max);
            }
            OnHealthChanged?.Invoke(this);
        }
    }
}
