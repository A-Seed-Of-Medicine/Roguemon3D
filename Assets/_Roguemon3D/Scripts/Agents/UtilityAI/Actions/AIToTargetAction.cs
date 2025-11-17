// _PinBoy/Scripts/AI/Actions/AIToTargetAction.cs
using System;
using _PinBoy.Scripts.CharacterMovement;
using AdvancedController;
using UnityEngine;
using UtilityAI.Pathfinding;

namespace UtilityAI
{
    [Serializable]
    public class AIToTargetAction : AIAction
    {
        [Header("Movement")]
        [SerializeField] float stoppingDistance = 0.3f;
        [SerializeField] float waypointTolerance = 0.15f;
        [SerializeField] bool useEightDirectionalMovement = true;

        [Header("Repath Overrides (optional)")]
        [SerializeField] bool overrideDiagonalForThisAgent = true; // Maintained for backwards compatibility

        AgentController controller;
        InputReader inputReader;
        Transform target;

        PathfindingManager.AgentTicket ticket;

        public override void Initialize(Context context)
        {
            base.Initialize(context);
            context?.RegisterTargetTag(targetTag);
            controller = context?.Controller;
            inputReader = controller?.inputReader;
            TryBindTicket(context);
        }

        void TryBindTicket(Context ctx)
        {
            if (!PathfindingManager.Instance || !controller) return;

            ticket = PathfindingManager.Instance.RegisterAgent(
                controller,
                () => controller.transform.position,
                () => target ? target.position : controller.transform.position,
                stoppingDistance,
                waypointTolerance,
                useEightDirectionalMovement,
                ctx?.brain ? $"{ctx.brain.name}:ToTarget" : "ToTarget"
            );
        }

        public override void Execute(Context context)
        {
            controller = context?.Controller ?? controller;
            inputReader = controller?.inputReader ?? inputReader;
            if (!controller)
            {
                inputReader?.InvokeMove(Vector2.zero);
                return;
            }

            target = context?.target;
            if (!target && !string.IsNullOrEmpty(targetTag)) target = context?.GetClosestTarget(targetTag);

            if (ticket == null) TryBindTicket(context);
            if (ticket == null)
            {
                inputReader.InvokeMove(Vector2.zero);
                return;
            }

            Vector3 agentPos3 = controller.transform.position;
            Vector3 targetPos3 = target ? target.position : agentPos3;
            Vector3 planarDelta = targetPos3 - agentPos3;
            planarDelta.y = 0f;
            if (planarDelta.sqrMagnitude <= stoppingDistance * stoppingDistance)
            {
                inputReader.InvokeMove(Vector2.zero);
                return;
            }

            Vector3 dir3 = ticket.ClosestPathDirection(agentPos3);
            Vector2 planarDir = new Vector2(dir3.x, dir3.z);
            //Debug.Log($"AIToTargetAction: Moving towards target at {targetPos3} from {agentPos3} with direction {planarDir}");
            inputReader.InvokeMove(planarDir);
        }

        public override void OnExit(Context context)
        {
            context.Controller.inputReader.InvokeMove(Vector2.zero);
            if (ticket != null && PathfindingManager.Instance) PathfindingManager.Instance.UnregisterAgent(ticket);
            ticket = null;
        }

        ~AIToTargetAction()
        {
            if (ticket != null && PathfindingManager.Instance) PathfindingManager.Instance.UnregisterAgent(ticket);
            ticket = null;
        }
    }
}
