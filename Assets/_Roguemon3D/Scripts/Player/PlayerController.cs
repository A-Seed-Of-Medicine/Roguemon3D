using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Actions;
using _PinBoy.Scripts.Gameplay.Creatures;
using _PinBoy.Scripts.Player.Input;
using AdvancedController;
using UnityEngine;

namespace _PinBoy.Scripts.Player
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PlayerController : AgentController
    {
        public PlayerInputReader PlayerInput;

        [Header("Creature Host")]
        [SerializeField] CreatureHostData startingHost;
        [SerializeField] CharacterComboAction comboAction;

        public override InputReader inputReader => PlayerInput.inputReader;

        public CreatureHostData CurrentHost { get; private set; }

        public CharacterComboAction ComboAction => comboAction;

        protected override void Awake()
        {
            PlayerInput.mainCamera = Camera.main;
            comboAction ??= GetComponent<CharacterComboAction>();
            base.Awake();
        }

        protected override void Start()
        {
            base.Start();
            ApplyHostData(startingHost);
        }

        protected override void Update()
        {
            if (PlayerInput.inputReader.isAiming && PlayerInput.IsMouseKeyboardActive && inputReader?.controller)
            {
                Vector3 worldAimPosition = PlayerInput.GetWorldAimPosition();
                Vector3 aimDirection = worldAimPosition - inputReader.controller.AimOrigin;
                Vector2 planarAim = new(aimDirection.x, aimDirection.z);
                inputReader.InvokeAim(planarAim);
            }
            base.Update();
        }

        public void ApplyHostData(CreatureHostData hostData)
        {
            CurrentHost = hostData;
            if (comboAction != null)
            {
                comboAction.SetComboDefinition(hostData ? hostData.ComboDefinition : null);
            }
        }
    }
}
