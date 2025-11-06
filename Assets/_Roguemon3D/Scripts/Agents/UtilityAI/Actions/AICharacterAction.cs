using System;
using _PinBoy.Scripts.Gameplay.Actions;
using UnityEngine;

namespace UtilityAI
{
    [Serializable]
    public class AICharacterAction : AIAction
    {
        [SerializeField] private CharacterAction characterAction;
        [SerializeField] private bool requireTarget;
        [SerializeField, Min(0f)] private float aimDelay;
        [SerializeField] private bool followTargetWhileAiming = true;

        public override void Initialize(Context context)
        {
            base.Initialize(context);
            if (!characterAction && context?.Controller)
            {
                characterAction = context.Controller.GetComponent<CharacterAction>();
            }
        }

        public override void Execute(Context context)
        {
            if (context == null)
            {
                return;
            }

            var controller = context.Controller;
            if (!controller)
            {
                Debug.LogWarning("AICharacterAction requires a controller to drive input.", context.brain);
                return;
            }

            if (!characterAction)
            {
                characterAction = controller.GetComponent<CharacterAction>();
                if (!characterAction)
                {
                    Debug.LogWarning($"AICharacterAction on {context.brain?.name} is missing a CharacterAction reference.", context.brain);
                    return;
                }
            }

            var inputReader = controller.inputReader;
            if (inputReader == null)
            {
                Debug.LogWarning($"AICharacterAction could not locate an InputReader for {controller.name}.", controller);
                return;
            }

            Transform targetTransform = context.target;
            if (!targetTransform && !string.IsNullOrEmpty(targetTag))
            {
                targetTransform = context.GetClosestTarget(targetTag);
            }

            if (requireTarget && !targetTransform)
            {
                return;
            }
            
            Vector3 direction = Vector3.zero;
            if (targetTransform)
            {
                direction = targetTransform.position - controller.transform.position;
            }

            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = controller.AimDirection;
            }

            bool followTarget = followTargetWhileAiming && targetTransform != null;
            characterAction.TriggerFromAI(context, targetTransform, direction, 1f, aimDelay, followTarget);
        }

        public override void OnExit(Context context)
        {
            // Intentionally left blank
        }
    }
}
