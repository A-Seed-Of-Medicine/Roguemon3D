using System;
using UnityEngine;
using UnityEngine.Events;

namespace _PinBoy.Scripts.Gameplay.Effects
{
    public class Health : MonoBehaviour
    {
        [Serializable]
        public class DamageEvent : UnityEvent<DamageInfo> { }
        
        [SerializeField, Min(1f)] private float maxHealth = 100f;
        [SerializeField] private DamageEvent onDamaged = new();
        [SerializeField] private UnityEvent onDeath = new();

        private float currentHealth;
        private bool isDead;

        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;
        public bool IsDead => isDead;

        private void Awake()
        {
            currentHealth = Mathf.Max(1f, maxHealth);
            isDead = false;
        }

        public AllegianceType allegiance { get; }

        public void ApplyDamage(DamageInfo damageInfo)
        {
            if (isDead)
            {
                return;
            }

            currentHealth = Mathf.Max(0f, currentHealth - Mathf.Max(0f, damageInfo.amount));
            onDamaged.Invoke(damageInfo);

            if (currentHealth <= 0f)
            {
                isDead = true;
                onDeath.Invoke();
            }
        }
        
        public void Heal(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            currentHealth = Mathf.Clamp(currentHealth + amount, 0f, maxHealth);
            if (currentHealth > 0f)
            {
                isDead = false;
            }
        }
    }
}
