using System;
using System.Threading;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Effects;
using AdvancedController;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using HSM;
using UtilityAI;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    [RequireComponent(typeof(AgentController))]
    public abstract class CharacterAction : MonoBehaviour
    {
        public enum PressBinding
        {
            PrimaryAction,
            SecondaryAction,
            Interact,
            AimPrimary,
            AimSecondary,
            Dash,
            Sprint
        }

        [Header("Action")]
        public PressBinding binding;
        public UnityAction<bool> actionTrigger;
        [SerializeField] protected AgentActionDefinition actionDefinition;
        [SerializeField, Min(0f)] protected float actionMagnitude = 1f;
        [SerializeField] protected bool skipIfActionInProgress = true;
        [Header("Animation")]
        [SerializeField] private AgentAnimationRequest defaultAnimationRequest;

        [field: SerializeField, HideInInspector]
        public AgentController Controller { get; private set; }
        protected InputReader InputReader => Controller != null ? Controller.inputReader : null;
        protected Vector3 LastAimWorldPosition => lastAimWorldPosition;
        protected virtual bool UsesAimInput => false;

        PendingExecution pendingExecution;
        CancellationTokenSource aiAimCancellation;
        bool aimPressed;
        Vector3 lastAimWorldPosition;
        public UnityAction actionStarted;
        public UnityAction actionComplete;
        public Rigidbody body => Controller?.rb;
        ActionState _actionState;
        private AgentAnimationRequest runtimeAnimationRequest;
        private event Action<AgentAnimationRequest> animationRequestChanged;

        public virtual void OnValidate()
        {
            if (!Controller)
                Controller = GetComponent<AgentController>();
        }

        protected virtual void Awake()
        {
            if (!Controller)
            {
                Debug.LogError($"{GetType().Name} requires an {nameof(AgentController)} on the same GameObject.", this);
            }

            actionTrigger ??= DefaultActionTrigger;
            runtimeAnimationRequest = NormalizeAnimationRequest(defaultAnimationRequest);
            defaultAnimationRequest = runtimeAnimationRequest;
        }

        protected virtual void Start()
        {
        }

        protected virtual void OnEnable()
        {
            SubscribeInput();
        }

        protected virtual void OnDisable()
        {
            UnsubscribeInput();
            CancelAiAimRoutine();
            aimPressed = false;
        }

        protected virtual void OnDestroy()
        {
            CancelAiAimRoutine();
            aimPressed = false;
        }

        internal void ConfigureActionState(AgentRoot root)
        {
            if (_actionState != null)
            {
                return;
            }

            Controller ??= GetComponent<AgentController>();
            if (Controller == null || root == null)
            {
                return;
            }

            _actionState = CreateActionExecuteState(root);
        }

        protected virtual ActionState CreateActionExecuteState(AgentRoot root)
        {
            return null;
        }

        internal ActionState ActionState => _actionState;

        internal AgentAnimationRequest GetAnimationRequest()
        {
            return runtimeAnimationRequest;
        }

        internal void RegisterAnimationListener(Action<AgentAnimationRequest> listener)
        {
            animationRequestChanged += listener;
        }

        internal void UnregisterAnimationListener(Action<AgentAnimationRequest> listener)
        {
            animationRequestChanged -= listener;
        }

        internal void ResetAnimationRequest()
        {
            UpdateAnimationRequest(defaultAnimationRequest);
        }

        internal void ApplyAnimationRequest(AgentAnimationRequest request)
        {
            UpdateAnimationRequest(request);
        }

        protected void SetAnimationRequest(AgentAnimationRequest request)
        {
            UpdateAnimationRequest(request);
        }

        AgentAnimationRequest NormalizeAnimationRequest(AgentAnimationRequest request)
        {
            return request.Sanitized();
        }

        void UpdateAnimationRequest(AgentAnimationRequest request)
        {
            AgentAnimationRequest normalized = NormalizeAnimationRequest(request);
            if (runtimeAnimationRequest.Equals(normalized))
            {
                runtimeAnimationRequest = normalized;
                return;
            }

            runtimeAnimationRequest = normalized;
            animationRequestChanged?.Invoke(runtimeAnimationRequest);
        }

        void SubscribeInput()
        {
            InputReader reader = InputReader;
            if (reader == null)
            {
                return;
            }

            if (actionTrigger == null)
            {
                actionTrigger = DefaultActionTrigger;
            }

            SubscribeToInput(reader);
        }

        void UnsubscribeInput()
        {
            InputReader reader = InputReader;
            if (reader == null)
            {
                return;
            }

            UnsubscribeFromInput(reader);
        }

        protected virtual void SubscribeToInput(InputReader reader)
        {
            switch (binding)
            {
                case PressBinding.AimPrimary:
                    reader.AimPrimary += actionTrigger;
                    break;
                case PressBinding.AimSecondary:
                    reader.AimSecondary += actionTrigger;
                    break;
                case PressBinding.Interact:
                    reader.Interact += actionTrigger;
                    break;
                case PressBinding.Dash:
                    reader.Dash += actionTrigger;
                    break;
                case PressBinding.Sprint:
                    reader.Sprint += actionTrigger;
                    break;
                case PressBinding.PrimaryAction:
                    reader.PrimaryAction += actionTrigger;
                    break;
                case PressBinding.SecondaryAction:
                    reader.SecondaryAction += actionTrigger;
                    break;
            }

            if (UsesAimInput)
            {
                reader.Aim += HandleAimInput;
            }
        }

        protected virtual void UnsubscribeFromInput(InputReader reader)
        {
            switch (binding)
            {
                case PressBinding.AimPrimary:
                    reader.AimPrimary -= actionTrigger;
                    break;
                case PressBinding.AimSecondary:
                    reader.AimSecondary -= actionTrigger;
                    break;
                case PressBinding.Interact:
                    reader.Interact -= actionTrigger;
                    break;
                case PressBinding.Dash:
                    reader.Dash -= actionTrigger;
                    break;
                case PressBinding.Sprint:
                    reader.Sprint -= actionTrigger;
                    break;
                case PressBinding.PrimaryAction:
                    reader.PrimaryAction -= actionTrigger;
                    break;
                case PressBinding.SecondaryAction:
                    reader.SecondaryAction -= actionTrigger;
                    break;
            }

            if (UsesAimInput)
            {
                reader.Aim -= HandleAimInput;
            }
        }

        protected virtual void Reset()
        {
            actionMagnitude = Mathf.Max(0f, actionMagnitude);
            skipIfActionInProgress = true;
        }

        protected void RecordInputDirection(Vector3 direction)
        {
            if (direction.sqrMagnitude > 0.0001f && InputReader != null)
            {
                Vector2 planar = new Vector2(direction.x, direction.z);
                if (planar.sqrMagnitude > 0.0001f)
                {
                    InputReader.InvokeMove(planar.normalized);
                }
            }
        }

        protected void PreparePendingExecution(Context context, Transform target, float magnitudeMultiplier = 1f)
        {
            pendingExecution = new PendingExecution
            {
                HasValue = true,
                Context = context,
                Target = target,
                MagnitudeMultiplier = Mathf.Max(0f, magnitudeMultiplier)
            };
        }

        protected void ClearPendingExecution()
        {
            pendingExecution = default;
        }

        protected bool ExecuteConfiguredAction(IDamageable targetOverride = null, float magnitudeMultiplier = 1f)
        {
            if (!actionDefinition || !Controller)
            {
                ClearPendingExecution();
                return false;
            }

            if (skipIfActionInProgress && Controller.IsPerformingAction)
            {
                ClearPendingExecution();
                return false;
            }

            IDamageable runtimeTarget = targetOverride;
            Context runtimeContext = pendingExecution.Context;

            float runtimeMagnitude = Mathf.Max(0f, actionMagnitude);
            if (pendingExecution.HasValue && pendingExecution.MagnitudeMultiplier > 0f)
            {
                runtimeMagnitude *= pendingExecution.MagnitudeMultiplier;
            }

            float extraMultiplier = Mathf.Max(0f, magnitudeMultiplier);
            if (extraMultiplier > 0f)
            {
                runtimeMagnitude *= extraMultiplier;
            }

            if (runtimeMagnitude <= 0f)
            {
                runtimeMagnitude = actionMagnitude > 0f ? actionMagnitude : 1f;
            }

            var runtime = new AgentActionRuntime(Controller, this, runtimeTarget, runtimeMagnitude);
            Controller.ExecuteAction(actionDefinition, runtime).Forget();

            ClearPendingExecution();
            return true;
        }

        public virtual void TriggerFromAI(Context context, Transform target, Vector3 direction, float magnitudeMultiplier = 1f,
            float aimDelay = 0f, bool followTarget = false)
        {
            PreparePendingExecution(context, target, magnitudeMultiplier);

            if (UsesAimInput && InputReader != null)
            {
                Vector3 aimPosition = ResolveAimWorldPosition(direction, target);
                lastAimWorldPosition = aimPosition;
                Vector3 aimVector = aimPosition - GetAimOrigin();
                if (aimVector.sqrMagnitude <= 0.0001f && Controller)
                {
                    aimVector = Controller.AimDirection;
                }

                RecordInputDirection(aimVector);
                StartAiAimRoutine(aimPosition, target, aimDelay, followTarget).Forget();
            }
            else
            {
                Vector3 aimVector = direction;
                if (aimVector.sqrMagnitude <= 0.0001f && Controller)
                {
                    aimVector = Controller.AimDirection;
                }

                RecordInputDirection(aimVector);
                if (aimVector.sqrMagnitude > 0.0001f)
                {
                    lastAimWorldPosition = GetAimOrigin() + aimVector;
                }

                InvokeActionTrigger(true);
            }
        }

        public virtual void ReleaseFromAI(Context context, Transform target)
        {
            if (UsesAimInput && InputReader != null)
            {
                Vector2 aimDirection = InputReader.aimDirection;
                Vector3 aimVector = new Vector3(aimDirection.x, 0f, aimDirection.y);
                if (aimVector.sqrMagnitude <= 0.0001f)
                {
                    Vector3 aimPosition = ResolveAimWorldPosition(Vector3.zero, target);
                    aimVector = aimPosition - GetAimOrigin();
                }

                RecordInputDirection(aimVector);
                lastAimWorldPosition = GetAimOrigin() + aimVector;
                InputReader.CancelAim();
            }

            CancelAiAimRoutine();
            InvokeActionTrigger(false);
        }

        protected Vector3 ResolveAimWorldPosition(Vector3 directionOrPosition, Transform target)
        {
            if (target)
            {
                return target.position;
            }

            Vector3 origin = GetAimOrigin();

            if (directionOrPosition.sqrMagnitude > 0.0001f)
            {
                return origin + directionOrPosition;
            }

            if (Controller)
            {
                Vector3 controllerAim = Controller.AimDirection;
                if (controllerAim.sqrMagnitude > 0.0001f)
                {
                    return origin + controllerAim;
                }
            }

            return origin + Vector3.forward;
        }

        protected Vector3 ResolveAimLocalPosition(Vector3 directionOrPosition, Transform target)
        {
            if (target)
            {
                return Controller.transform.InverseTransformPoint(target.position);
            }

            if (directionOrPosition.sqrMagnitude > 0.0001f)
            {
                return directionOrPosition;
            }

            if (Controller)
            {
                Vector3 controllerAim = Controller.AimDirection;
                if (controllerAim.sqrMagnitude > 0.0001f)
                {
                    return controllerAim;
                }
            }

            return Vector3.forward;
        }

        protected Vector3 GetAimOrigin()
        {
            if (Controller)
            {
                return Controller.AimOrigin;
            }

            return transform.position;
        }

        async UniTaskVoid StartAiAimRoutine(Vector3 initialAimPosition, Transform target, float aimDelay, bool followTarget)
        {
            CancelAiAimRoutine();

            if (InputReader == null)
            {
                InvokeActionTrigger(true);
                return;
            }

            aiAimCancellation = new CancellationTokenSource();
            CancellationToken token = aiAimCancellation.Token;

            try
            {
                float remaining = Mathf.Max(0f, aimDelay);
                float elapsed = 0f;

                Vector3 initialVector = initialAimPosition - GetAimOrigin();
                lastAimWorldPosition = initialAimPosition;
                Vector2 initialPlanar = new Vector2(initialVector.x, initialVector.z);
                InputReader.InvokeAim(initialPlanar);

                while (!token.IsCancellationRequested && elapsed < remaining)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    elapsed += Time.deltaTime;

                    Vector3 nextPosition = initialAimPosition;
                    if (followTarget && target)
                    {
                        nextPosition = target.position;
                    }

                    Vector3 aimVector = nextPosition - GetAimOrigin();
                    if (aimVector.sqrMagnitude <= 0.0001f && Controller)
                    {
                        aimVector = Controller.AimDirection;
                    }

                    RecordInputDirection(aimVector);
                    lastAimWorldPosition = nextPosition;
                    Vector2 planar = new Vector2(aimVector.x, aimVector.z);
                    InputReader.InvokeAim(planar);
                }

                if (!token.IsCancellationRequested)
                {
                    InvokeActionTrigger(true);
                }
            }
            catch (OperationCanceledException)
            {
                // Swallow cancellation triggered by ReleaseFromAI.
            }
            finally
            {
                if (aiAimCancellation != null)
                {
                    aiAimCancellation.Dispose();
                    aiAimCancellation = null;
                }
            }
        }

        void CancelAiAimRoutine()
        {
            if (aiAimCancellation != null)
            {
                aiAimCancellation.Cancel();
                aiAimCancellation.Dispose();
                aiAimCancellation = null;
            }
            aimPressed = false;
        }

        void HandleAimInput(bool pressed, Vector2 direction)
        {
            Vector3 worldOffset = new Vector3(direction.x, 0f, direction.y);
            Vector3 worldPosition = GetAimOrigin() + worldOffset;
            lastAimWorldPosition = worldPosition;

            if (pressed)
            {
                if (!aimPressed)
                {
                    aimPressed = true;
                    OnAimStarted(worldPosition);
                }

                OnAimUpdated(worldPosition);
            }
            else
            {
                if (aimPressed)
                {
                    OnAimReleased(worldPosition);
                }

                aimPressed = false;
            }
        }

        protected virtual void OnAimStarted(Vector3 worldPosition)
        {
        }

        protected virtual void OnAimUpdated(Vector3 worldPosition)
        {
        }

        protected virtual void OnAimReleased(Vector3 worldPosition)
        {
        }

        protected void InvokeActionTrigger(bool pressed)
        {
            actionTrigger?.Invoke(pressed);
        }

        protected virtual void OnActionPressed()
        {
            ExecuteConfiguredAction();
        }

        protected virtual void OnActionReleased()
        {
        }

        protected virtual void DefaultActionTrigger(bool pressed)
        {
            if (pressed)
            {
                OnActionPressed();
            }
            else
            {
                OnActionReleased();
            }
        }

        struct PendingExecution
        {
            public bool HasValue;
            public Context Context;
            public Transform Target;
            public float MagnitudeMultiplier;
        }
    }
}
