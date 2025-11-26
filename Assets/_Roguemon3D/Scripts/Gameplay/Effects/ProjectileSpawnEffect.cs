using System;
using _PinBoy.Scripts.Gameplay.Actions;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Effects
{
    [Serializable]
    public class ProjectileSpawnEffect : Effect
    {
        [SerializeField] private string projectileConfigurationId = "default";
        [SerializeField] private bool useContextDirection = true;

        public override void Apply(EffectContext context)
        {
            Debug.Log("Applying projectile spawn effect.");
            if (context == null || context.Source == null)
            {
                return;
            }

            if (context.Source is not Component sourceComponent)
            {
                return;
            }

            ProjectileCharacterAimAction aimAction = sourceComponent.GetComponent<ProjectileCharacterAimAction>();
            if (!aimAction)
            {
                aimAction = sourceComponent.GetComponentInChildren<ProjectileCharacterAimAction>();
            }

            if (!aimAction)
            {
                return;
            }

            Vector3? directionOverride = useContextDirection ? context.Direction : (Vector3?)null;
            aimAction.TryFireConfiguredProjectile(projectileConfigurationId, directionOverride, context.Source,
                context.Magnitude);
        }
    }
}

