using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Creatures;
using _PinBoy.Scripts.Player;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    public class CreatureSummonAction : CharacterAction
    {
        [Header("Creature Configuration")]
        [SerializeField] PlayerController playerController;
        [SerializeField] CharacterComboAction comboAction;
        [SerializeField] CreatureHostData startingHost;
        [SerializeField] CreatureHostData initialSummon;
        [SerializeField] CreatureHostData summonTarget;

        [Header("Spawn")]
        [SerializeField] Transform summonParent;
        [SerializeField] Transform summonSpawnPoint;

        readonly Dictionary<CreatureHostData, SummonedCreature> creaturePool = new();

        CreatureHostData currentHost;
        CreatureHostData currentSummoned;
        SummonedCreature summonedInstance;
        SummonedCreature cachedHostInstance;

        protected override void Awake()
        {
            base.Awake();
            playerController ??= GetComponent<PlayerController>();
            comboAction ??= GetComponent<CharacterComboAction>();
        }

        protected override void Start()
        {
            base.Start();
            InitializeHost(startingHost);
            if (initialSummon)
            {
                RequestSummon(initialSummon);
            }
        }

        protected override void DefaultActionTrigger(bool pressed)
        {
            if (!pressed)
            {
                return;
            }

            CreatureHostData target = summonTarget ? summonTarget : currentHost;
            RequestSummon(target);
        }

        void InitializeHost(CreatureHostData hostData)
        {
            currentHost = hostData;
            cachedHostInstance = hostData != null ? PrepareCreatureInstance(hostData, false) : null;
            ApplyHost(currentHost, cachedHostInstance);
        }

        void ApplyHost(CreatureHostData hostData, SummonedCreature hostSummon)
        {
            currentHost = hostData;
            cachedHostInstance = hostSummon;

            if (playerController != null)
            {
                playerController.ApplyHostData(hostData);
            }
            else if (comboAction != null)
            {
                comboAction.SetComboDefinition(hostData ? hostData.ComboDefinition : null);
            }

            if (hostSummon != null)
            {
                hostSummon.SetOwner(playerController ?? Controller as PlayerController);
                hostSummon.gameObject.SetActive(false);
            }
        }

        void RequestSummon(CreatureHostData data)
        {
            if (data == null)
            {
                return;
            }

            if (data == currentHost)
            {
                SwapHostAndSummon();
                return;
            }

            SummonedCreature active = PrepareCreatureInstance(data, true);
            if (summonedInstance != null && summonedInstance != active)
            {
                DeactivateSummon(summonedInstance);
            }

            summonedInstance = active;
            currentSummoned = data;
        }

        void SwapHostAndSummon()
        {
            if (currentHost == null)
            {
                return;
            }

            if (currentSummoned == null)
            {
                SummonedCreature hostAsSummon = PrepareCreatureInstance(currentHost, true);
                summonedInstance = hostAsSummon;
                currentSummoned = currentHost;
                return;
            }

            CreatureHostData previousHost = currentHost;
            CreatureHostData nextHostData = currentSummoned;

            SummonedCreature hostSummon = PrepareCreatureInstance(previousHost, true);
            SummonedCreature nextHostInstance = PrepareCreatureInstance(nextHostData, false);

            if (summonedInstance != null && summonedInstance != nextHostInstance && summonedInstance != hostSummon)
            {
                DeactivateSummon(summonedInstance);
            }

            summonedInstance = hostSummon;
            currentSummoned = previousHost;

            ApplyHost(nextHostData, nextHostInstance);
        }

        SummonedCreature PrepareCreatureInstance(CreatureHostData data, bool activate)
        {
            if (data == null || data.SummonPrefab == null)
            {
                return null;
            }

            if (!creaturePool.TryGetValue(data, out SummonedCreature creature) || creature == null)
            {
                Transform parent = summonParent ? summonParent : null;
                Vector3 spawnPosition = summonSpawnPoint ? summonSpawnPoint.position : Controller.transform.position;
                Quaternion spawnRotation = summonSpawnPoint ? summonSpawnPoint.rotation : Controller.transform.rotation;

                creature = Instantiate(data.SummonPrefab, spawnPosition, spawnRotation, parent);
                creaturePool[data] = creature;
            }

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

        void ActivateSummon(SummonedCreature creature)
        {
            if (creature == null)
            {
                return;
            }

            Vector3 position = summonSpawnPoint ? summonSpawnPoint.position : Controller.transform.position;
            Quaternion rotation = summonSpawnPoint ? summonSpawnPoint.rotation : Controller.transform.rotation;
            creature.transform.SetPositionAndRotation(position, rotation);
            creature.gameObject.SetActive(true);
        }

        void DeactivateSummon(SummonedCreature creature)
        {
            if (creature == null)
            {
                return;
            }

            creature.gameObject.SetActive(false);
        }
    }
}
