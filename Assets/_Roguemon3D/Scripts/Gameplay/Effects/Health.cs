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
            this.max = Mathf.Max(1f, maxHealth);
            current = this.max;
            isDead = false;
        }
        
        [Serializable]
        public class DamageEvent : UnityEvent<DamageInfo> { }
        
        [SerializeField, Min(1f)] private float max = 100f;
        [SerializeField, Min(1f)] private float current;
        [SerializeField] private bool isDead;
        private IDamageable damageable;

        public float Current => current;
        public float Max => max;
        public bool IsDead => isDead;

        private void Awake()
        {
            current = Mathf.Max(1f, max);
            isDead = false;
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
        }
        
        public void Heal(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            current = Mathf.Clamp(current + amount, 0f, max);
            if (current > 0f)
            {
                isDead = false;
            }
        }
    }
}
