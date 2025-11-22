using System;
using _PinBoy.Scripts.CharacterMovement;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Effects
{
    [Serializable]
    public sealed class SummonCreatureEffect : Effect
    {
        [SerializeField] private SummonCreature summonPrefab;
        [SerializeField] private Vector3 spawnOffset = Vector3.zero;
        [SerializeField] private bool orientOffsetToDirection = true;

        public override void Apply(EffectContext context)
        {
            if (context == null || !summonPrefab)
            {
                return;
            }

            Vector3 offset = orientOffsetToDirection && context.Direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(context.Direction, Vector3.up) * spawnOffset
                : spawnOffset;

            Vector3 spawnPosition = context.SourcePosition + offset;
            Quaternion spawnRotation = context.Direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(context.Direction, Vector3.up)
                : Quaternion.identity;

            SummonCreature instance = UnityEngine.Object.Instantiate(summonPrefab, spawnPosition, spawnRotation);

            AgentController owner = context.Source as AgentController;
            if (context.Direction.sqrMagnitude > 0.0001f)
            {
                instance.ForceFacing(context.Direction);
            }

            instance.Summon(owner, context.Target, context.Magnitude);
        }
    }
}
