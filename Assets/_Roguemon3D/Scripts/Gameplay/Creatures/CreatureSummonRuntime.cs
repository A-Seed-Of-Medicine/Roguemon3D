using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Actions;
using _PinBoy.Scripts.Player;
using UnityEngine;
using AgentController = _PinBoy.Scripts.Player.AgentController;

namespace _PinBoy.Scripts.Gameplay.Creatures
{
    /// <summary>
    /// Centralized runtime for preparing, swapping and applying creature hosts/summons so the logic
    /// can be driven from both input (legacy) and the AgentActionRuntime effect pipeline.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterMovement.AgentController))]
    public class CreatureSummonRuntime : MonoBehaviour
    {
        [Header("Creature Configuration")]
        [SerializeField] AgentController agentController;
        [SerializeField] CharacterComboAction comboAction;
        [SerializeField, Tooltip("The starting host creature for the player.")]
        CreatureSummon startingHostPrefab;
        [SerializeField, Tooltip("If set, this creature will be summoned at the start of the game.")]
        CreatureSummon initialSummonPrefab;

        [Header("Spawn")]
        [SerializeField] Transform summonParent;
        [SerializeField] Transform summonSpawnPoint;

        CreatureSummon hostInstance;
        CreatureSummon summonInstance;
        bool initialized;

        public CreatureSummon HostInstance => hostInstance;
        public CreatureSummon SummonInstance => summonInstance;

        void Awake()
        {
            agentController ??= GetComponent<AgentController>();
            agentController.SummonData = this;
            comboAction ??= GetComponent<CharacterComboAction>();
            summonSpawnPoint ??= transform;
        }

        void Start()
        {
            InitializeRuntime();
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
            }

            if (initialSummonPrefab && summonInstance == null)
            {
                CreatureSummon createdSummon = PrepareCreatureInstance(initialSummonPrefab, true);
                SetActiveSummon(createdSummon);
            }
        }

        CreatureSummon PrepareCreatureInstance(CreatureSummon prefab, bool activate)
        {
            if (!prefab)
            {
                return null;
            }

            CreatureSummon creature = Instantiate(prefab, summonParent);

            if (agentController != null)
            {
                creature.SetOwner(agentController);
            }

            if (activate)
            {
                SetActiveSummon(creature);
            }

            return creature;
        }

        public void ApplyHost(CreatureSummon hostSummon)
        {
            if (!hostSummon || hostSummon == hostInstance)
            {
                return;
            }
            
            DeactivateSummon(hostSummon);

            hostInstance = hostSummon;

            if (comboAction)
                comboAction.SetComboDefinition(hostInstance.hostData ? hostInstance.hostData.comboDefinition : null);

            hostSummon.SetOwner(agentController ?? GetComponent<AgentController>());
        }

        void SetActiveSummon(CreatureSummon creatureSummon)
        {
            if (creatureSummon == summonInstance || !creatureSummon)
                return;
            
            if (summonInstance && summonInstance != creatureSummon)
            {
                if (hostInstance == creatureSummon)
                    SwapHostAndSummon();
                else
                    DeactivateSummon(summonInstance);
            }

            summonInstance = creatureSummon;

            if (!summonInstance)
                return;
            
            creatureSummon.transform.position = summonSpawnPoint ? summonSpawnPoint.position : transform.position;
            creatureSummon.gameObject.SetActive(true);
        }

        public void SwapHostAndSummon()
        {
            Debug.Log("SwapHostAndSummon");
            if (!summonInstance)
            {
                return;
            }

            CreatureSummon previousHost = hostInstance;
            ApplyHost(summonInstance);
            SetActiveSummon(previousHost);
        }

        void DeactivateSummon(CreatureSummon creatureSummon)
        {
            if (!creatureSummon)
                return;

            creatureSummon.gameObject.SetActive(false);
        }
    }
}
