using _PinBoy.Scripts.Gameplay.Actions;
using _PinBoy.Scripts.Gameplay.Effects;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace _PinBoy.Scripts.CharacterMovement
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SummonCreature : AgentController
    {
        [SerializeField] private AgentActionDefinition summonAction;
        [SerializeField, Min(0f)] private float summonActionMagnitude = 1f;

        [field: SerializeField]
        public AgentController Owner { get; private set; }

        public void Summon(AgentController owner, IDamageable target, float magnitudeMultiplier = 1f)
        {
            Owner = owner;
            ExecuteSummonAction(target, magnitudeMultiplier).Forget();
        }

        async UniTask ExecuteSummonAction(IDamageable target, float magnitudeMultiplier)
        {
            if (!summonAction)
            {
                return;
            }

            float runtimeMagnitude = Mathf.Max(0f, summonActionMagnitude);
            if (magnitudeMultiplier > 0f)
            {
                runtimeMagnitude *= magnitudeMultiplier;
            }

            var runtime = new AgentActionRuntime(this, null, target, runtimeMagnitude);
            await ExecuteAction(summonAction, runtime);
        }
    }
}
