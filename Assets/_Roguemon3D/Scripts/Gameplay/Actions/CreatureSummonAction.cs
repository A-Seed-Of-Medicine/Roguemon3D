using _PinBoy.Scripts.Gameplay.Creatures;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    /// <summary>
    /// Legacy input wrapper that forwards to <see cref="CreatureSummonRuntime"/> so existing bindings
    /// continue to function while summoning is driven through the combo/effect pipeline.
    /// </summary>
    [RequireComponent(typeof(CreatureSummonRuntime))]
    public class CreatureSummonAction : CharacterAction
    {
        [Header("Creature Configuration")]
        [SerializeField, Tooltip("The starting host creature for the player.")]
        CreatureSummon startingHostPrefab;
        [SerializeField, Tooltip("If set, this creature will be summoned at the start of the game.")]
        CreatureSummon initialSummonPrefab;

        [Header("Spawn")]
        [SerializeField] Transform summonParent;
        [SerializeField] Transform summonSpawnPoint;

        CreatureSummonRuntime summonRuntime;

        protected override void Awake()
        {
            summonRuntime = GetComponent<CreatureSummonRuntime>();
            summonRuntime.ConfigureDefaults(startingHostPrefab, initialSummonPrefab, summonParent, summonSpawnPoint);
            base.Awake();
        }

        protected override void Start()
        {
            base.Start();
            summonRuntime.InitializeRuntime();
        }

        protected override void DefaultActionTrigger(bool pressed)
        {
            if (!pressed)
            {
                return;
            }

            summonRuntime.RequestSwapWithCurrentHost();
        }
    }
}
