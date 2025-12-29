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

        [Header("Pathing Constraints")]
        [SerializeField, Tooltip("Enable avoidance tests against other agents while planning.")]
        bool avoidOtherAgents = true;
        [SerializeField, Tooltip("Minimum planar distance to keep from avoided agents.")]
        float agentAvoidanceRadius = 0.6f;
        [SerializeField, Tooltip("Determines which agents are considered when avoiding.")]
        AgentAvoidanceScope agentAvoidanceScope = AgentAvoidanceScope.AllOthers;

        [Header("Line of Sight")]
        [SerializeField, Tooltip("Require a clear line of sight across the path when planning.")]
        bool requireLineOfSight;
        [SerializeField] LayerMask lineOfSightMask = Physics.DefaultRaycastLayers;
        [SerializeField, Tooltip("Offset from ground used when casting for line of sight.")]
        float lineOfSightHeightOffset = 0.4f;
        [SerializeField, Tooltip("Sphere radius for the line-of-sight test. Use 0 for a thin ray.")]
        float lineOfSightRadius = 0.05f;
        [SerializeField] QueryTriggerInteraction lineOfSightTriggers = QueryTriggerInteraction.Ignore;

        [Header("Repath Overrides (optional)")]
        [SerializeField] bool overrideDiagonalForThisAgent = true; // Maintained for backwards compatibility

        AgentController controller;
        InputReader inputReader;
        TargetContext target;

        PathfindingManager.PathRequestOptions requestOptions;
        PathfindingManager.AgentTicket ticket;

        enum AgentAvoidanceScope
        {
            AllOthers,
            SameAllegiance,
            DifferentAllegiance
        }

        public override void Initialize(Context context)
        {
            base.Initialize(context);
            controller = context?.Controller;
            inputReader = controller?.inputReader;
            TryBindTicket(context);
        }

        void TryBindTicket(Context ctx)
        {
            if (!PathfindingManager.Instance || !controller) return;

            requestOptions = BuildRequestOptions();
            ticket = PathfindingManager.Instance.RegisterAgent(
                controller,
                () => controller.transform.position,
                () => target != null ? target.position : controller.transform.position,
                stoppingDistance,
                waypointTolerance,
                useEightDirectionalMovement,
                ctx?.Controller ? $"{ctx.Controller.name}:ToTarget" : "ToTarget",
                requestOptions
            );
        }

        PathfindingManager.PathRequestOptions BuildRequestOptions()
        {
            var options = PathfindingManager.PathRequestOptions.Defaults.Clone();

            if (avoidOtherAgents)
            {
                options.avoidance.radius = Mathf.Max(0f, agentAvoidanceRadius);
                options.avoidance.filter = BuildAgentFilter();
                options.goalSampleRadius = Mathf.Max(options.goalSampleRadius, agentAvoidanceRadius);
            }
            else
            {
                options.avoidance.radius = 0f;
                options.avoidance.filter = null;
            }

            options.lineOfSight = requireLineOfSight
                ? new PathfindingManager.LineOfSightSettings
                {
                    required = true,
                    mask = lineOfSightMask,
                    verticalOffset = lineOfSightHeightOffset,
                    radius = Mathf.Max(0f, lineOfSightRadius),
                    triggerInteraction = lineOfSightTriggers
                }
                : PathfindingManager.LineOfSightSettings.Disabled;

            return options;
        }

        Func<AgentController, bool> BuildAgentFilter()
        {
            if (!avoidOtherAgents)
            {
                return null;
            }

            return other =>
            {
                if (!other || other == controller)
                {
                    return false;
                }

                return agentAvoidanceScope switch
                {
                    AgentAvoidanceScope.SameAllegiance => controller && other.allegiance == controller.allegiance,
                    AgentAvoidanceScope.DifferentAllegiance => !controller || other.allegiance != controller.allegiance,
                    _ => true
                };
            };
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
            if (target == null && !string.IsNullOrEmpty(targetTag)) target = context?.GetClosestTarget(targetTag);

            if (ticket == null) TryBindTicket(context);
            if (ticket == null)
            {
                inputReader.InvokeMove(Vector2.zero);
                return;
            }

            Vector3 agentPos3 = controller.transform.position;
            Vector3 targetPos3 = target != null ? target.position : agentPos3;
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
