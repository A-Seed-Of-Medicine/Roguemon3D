using System;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Creatures;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Effects
{
    [Serializable]
    public sealed class SummonEffect : Effect
    {
        public override void Apply(EffectContext context)
        {
            Debug.Log($"Applying {context.GetType().Name}");
            if (context?.Source is not AgentController controller)
                return;

            if (!controller?.SummonData)
                return;

            controller.SummonData.SwapHostAndSummon();
        }
    }
}
