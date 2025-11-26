using System;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Projectiles;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Effects
{
    /// <summary>
    /// Spawns a configured projectile using the SpawnProjectile component on the source agent.
    /// </summary>
    [Serializable]
    public sealed class SpawnProjectileEffect : Effect
    {
        [SerializeField] private string projectilePresetId = "default";

        public override void Apply(EffectContext context)
        {
            if (context?.Source is not AgentController controller)
            {
                return;
            }

            SpawnProjectile spawner = controller.GetComponent<SpawnProjectile>() ??
                                       controller.GetComponentInChildren<SpawnProjectile>();
            if (!spawner)
            {
                Debug.LogWarning($"{nameof(SpawnProjectileEffect)} requires a {nameof(SpawnProjectile)} component on the sourc" +
                                 "e.", controller);
                return;
            }

            spawner.SpawnFromContext(context, projectilePresetId);
        }
    }
}
