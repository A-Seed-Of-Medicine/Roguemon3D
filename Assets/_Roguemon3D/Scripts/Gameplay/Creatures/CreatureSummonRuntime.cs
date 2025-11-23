using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Actions;
using _PinBoy.Scripts.Player;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Creatures
{
    /// <summary>
    /// Centralized runtime for preparing, swapping and applying creature hosts/summons so the logic
    /// can be driven from both input (legacy) and the AgentActionRuntime effect pipeline.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AgentController))]
    public class CreatureSummonRuntime : MonoBehaviour
    {
        [Header("Creature Configuration")]
        [SerializeField] PlayerController playerController;
        [SerializeField] CharacterComboAction comboAction;
        [SerializeField, Tooltip("The starting host creature for the player.")]
        CreatureSummon startingHostPrefab;
        [SerializeField, Tooltip("If set, this creature will be summoned at the start of the game.")]
        CreatureSummon initialSummonPrefab;

        [Header("Spawn")]
        [SerializeField] Transform summonParent;
        [SerializeField] Transform summonSpawnPoint;

        readonly Dictionary<CreatureHostData, CreatureSummon> runtimeInstances = new();

        CreatureSummon hostInstance;
        CreatureSummon summonInstance;
        bool initialized;

        public CreatureSummon HostInstance => hostInstance;
        public CreatureSummon SummonInstance => summonInstance;

        void Awake()
        {
            playerController ??= GetComponent<PlayerController>();
            comboAction ??= GetComponent<CharacterComboAction>();
            summonParent ??= transform;
            summonSpawnPoint ??= transform;
        }

        void Start()
        {
            InitializeRuntime();
        }

        public void ConfigureDefaults(CreatureSummon hostPrefab, CreatureSummon summonPrefab, Transform parent, Transform spawnPoint)
        {
            startingHostPrefab ??= hostPrefab;
            initialSummonPrefab ??= summonPrefab;
            summonParent ??= parent;
            summonSpawnPoint ??= spawnPoint;
        }

        public void InitializeRuntime()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;

            if (startingHostPrefab && hostInstance == null)
            {
                CreatureSummon createdHost = PrepareCreatureInstance(startingHostPrefab, false);
                ApplyHost(createdHost);
                DeactivateSummon(createdHost);
            }

            if (initialSummonPrefab && summonInstance == null)
            {
                CreatureSummon createdSummon = PrepareCreatureInstance(initialSummonPrefab, true);
                SetActiveSummon(createdSummon, true);
            }
        }

        public void Summon(CreatureHostData hostData, bool swapWithHost)
        {
            InitializeRuntime();

            CreatureSummon summon = GetOrCreateInstance(hostData, true);
            if (!summon)
            {
                return;
            }

            SetActiveSummon(summon, true);

            if (swapWithHost)
            {
                SwapHostAndSummon();
            }
        }

        public void RequestSwapWithCurrentHost()
        {
            InitializeRuntime();
            SwapHostAndSummon();
        }

        CreatureSummon GetOrCreateInstance(CreatureHostData hostData, bool activate)
        {
            if (hostData == null || hostData.summonPrefab == null)
            {
                return null;
            }

            if (runtimeInstances.TryGetValue(hostData, out CreatureSummon existing) && existing)
            {
                if (activate)
                {
                    ActivateSummon(existing);
                }
                return existing;
            }

            CreatureSummon creature = PrepareCreatureInstance(hostData.summonPrefab, activate);

            if (creature && creature.hostData != null)
            {
                runtimeInstances[creature.hostData] = creature;
            }

            return creature;
        }

        CreatureSummon PrepareCreatureInstance(CreatureSummon prefab, bool activate)
        {
            if (!prefab)
            {
                return null;
            }

            CreatureSummon creature = Instantiate(prefab, summonParent);

            if (playerController != null)
            {
                creature.SetOwner(playerController);
            }

            if (activate)
            {
                ActivateSummon(creature);
            }
            else
            {
                DeactivateSummon(creature);
            }

            return creature;
        }

        void ApplyHost(CreatureSummon hostSummon)
        {
            if (!hostSummon || hostSummon == hostInstance)
            {
                return;
            }

            hostInstance = hostSummon;

            if (playerController)
            {
                playerController.ApplyCreatureHost(hostInstance);
            }

            if (comboAction)
            {
                comboAction.SetComboDefinition(hostInstance.hostData ? hostInstance.hostData.comboDefinition : null);
            }

            hostSummon.SetOwner(playerController ?? GetComponent<PlayerController>());
        }

        void SetActiveSummon(CreatureSummon creatureSummon, bool activate)
        {
            if (summonInstance && summonInstance != creatureSummon)
            {
                DeactivateSummon(summonInstance);
            }

            summonInstance = creatureSummon;

            if (summonInstance == null)
            {
                return;
            }

            if (activate)
            {
                ActivateSummon(summonInstance);
            }
            else
            {
                DeactivateSummon(summonInstance);
            }
        }

        void SwapHostAndSummon()
        {
            if (summonInstance == null && hostInstance != null)
            {
                SetActiveSummon(hostInstance, true);
                return;
            }

            if (summonInstance == null)
            {
                return;
            }

            CreatureSummon previousHost = hostInstance;
            ApplyHost(summonInstance);
            SetActiveSummon(previousHost, false);
        }

        void ActivateSummon(CreatureSummon creatureSummon)
        {
            if (creatureSummon == null)
            {
                return;
            }

            Vector3 position = summonSpawnPoint ? summonSpawnPoint.position : transform.position;
            Quaternion rotation = summonSpawnPoint ? summonSpawnPoint.rotation : transform.rotation;
            creatureSummon.transform.SetPositionAndRotation(position, rotation);
            creatureSummon.gameObject.SetActive(true);
        }

        void DeactivateSummon(CreatureSummon creatureSummon)
        {
            if (creatureSummon == null)
            {
                return;
            }

            creatureSummon.gameObject.SetActive(false);
        }
    }
}
