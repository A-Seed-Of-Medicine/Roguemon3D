using System;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Actions;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Effects
{
    [Serializable]
    public class ProjectileSpawnEffect : Effect
    {
        [SerializeField] private string projectileConfigurationId = "default";

        public override void Apply(EffectContext context)
        {
            if (context == null || context.Source == null)
            {
                return;
            }

            if (context.Source is not AgentController controller)
            {
                return;
            }
            
            
            if (!controller.aimData)
            {
                return;
            }

            controller.aimData.TryFireConfiguredProjectile(projectileConfigurationId, context.TargetPosition, context.Source,
                context.Magnitude);
        }
    }
}

