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
        [SerializeField, Tooltip("The starting host creature for the player.")]
        CreatureSummon startingHostPrefab;
        [SerializeField, Tooltip("If set, this creature will be summoned at the start of the game.")]
        CreatureSummon initialSummonPrefab;

        [Header("Spawn")]
        [SerializeField] Transform summonParent;
        [SerializeField] Transform summonSpawnPoint;

        public CreatureSummon _hostInstance
        {
            get => playerController.CreatureHost;
            private set => playerController.ApplyCreatureHost(value);
        }

        public CreatureSummon _summonInstance { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            playerController ??= GetComponent<PlayerController>();
            comboAction ??= GetComponent<CharacterComboAction>();
        }

        protected override void Start()
        {
            base.Start();
            if (startingHostPrefab && !_hostInstance)
                InitializeHost(startingHostPrefab);
            if (initialSummonPrefab && !_summonInstance)
                InitializeSummon(initialSummonPrefab);
        }

        protected override void DefaultActionTrigger(bool pressed)
        {
            if (!pressed)
            {
                return;
            }
            
            RequestSummon(_hostInstance);
        }

        void InitializeHost(CreatureSummon hostPrefab)
        {
            _hostInstance = hostPrefab;
            _hostInstance = hostPrefab != null ? PrepareCreatureInstance(hostPrefab, false) : null;
            ApplyHost(_hostInstance);
        }

        void InitializeSummon(CreatureSummon summonPrefab)
        {
            _summonInstance = PrepareCreatureInstance(initialSummonPrefab, true);
            RequestSummon(initialSummonPrefab);
        }

        void ApplyHost(CreatureSummon hostSummon)
        {
            if (!hostSummon || hostSummon == _hostInstance)
                return;
            
            if (_summonInstance == hostSummon)
            {
                DeactivateSummon(_summonInstance);
                _summonInstance = null;
            }
            
            _hostInstance = hostSummon;

            if (playerController)
                playerController.ApplyCreatureHost(_hostInstance);
            if (comboAction)
                comboAction.SetComboDefinition(_hostInstance.hostData ? _hostInstance.hostData.ComboDefinition : null);
            
            hostSummon.SetOwner(playerController ?? Controller as PlayerController);
            hostSummon.gameObject.SetActive(false);
        }

        void RequestSummon(CreatureSummon summon)
        {
            if (!summon)
                return;

            if (summon == _summonInstance)
                return;

            if (summon == _hostInstance)
                SwapHostAndSummon();
        }

        void SwapHostAndSummon()
        {
            if (!_hostInstance && _summonInstance)
            {
                ApplyHost(_summonInstance);
                return;
            }
            
            if (!_summonInstance)
                return;
            
            CreatureSummon previousHost = _hostInstance;
            ApplyHost(_summonInstance);
            RequestSummon(previousHost);
        }

        CreatureSummon PrepareCreatureInstance(CreatureSummon prefab, bool activate)
        {
            if (!prefab || !prefab.hostData)
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

        void ActivateSummon(CreatureSummon creatureSummon)
        {
            if (creatureSummon == null)
            {
                return;
            }

            Vector3 position = summonSpawnPoint ? summonSpawnPoint.position : Controller.transform.position;
            Quaternion rotation = summonSpawnPoint ? summonSpawnPoint.rotation : Controller.transform.rotation;
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
