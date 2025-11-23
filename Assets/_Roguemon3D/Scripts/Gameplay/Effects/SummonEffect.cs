using System;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Creatures;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Effects
{
    [Serializable]
    public sealed class SummonEffect : Effect
    {
        [SerializeField] private CreatureHostData hostData;
        [SerializeField, Tooltip("When enabled, the summoned creature becomes the new host and the previous host is stored as the inactive summon.")]
        private bool swapWithCurrentHost = true;

        public override void Apply(EffectContext context)
        {
            if (context?.Source is not AgentController controller)
            {
                return;
            }

            CreatureSummonRuntime runtime = controller.GetComponent<CreatureSummonRuntime>();
            if (!runtime)
            {
                runtime = controller.gameObject.AddComponent<CreatureSummonRuntime>();
            }

            runtime.Summon(hostData, swapWithCurrentHost);
        }
    }
}
