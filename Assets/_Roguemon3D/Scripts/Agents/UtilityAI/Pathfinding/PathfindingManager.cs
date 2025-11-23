// _PinBoy/Scripts/AI/Pathfinding/PathfindingManager.cs
using System;
using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using UnityEngine;
using UnityEngine.AI;

namespace UtilityAI.Pathfinding
{
    [DefaultExecutionOrder(-150)]
    [DisallowMultipleComponent]
    public sealed class PathfindingManager : MonoBehaviour
    {
        public static PathfindingManager Instance { get; private set; }

        [Header("NavMesh")]
        [Tooltip("Maximum distance used when sampling positions onto the NavMesh.")]
        [Min(0.01f)] public float sampleMaxDistance = 2f;
        [Tooltip("NavMesh area mask used when calculating paths.")]
        public int areaMask = NavMesh.AllAreas;
        [Tooltip("Allow partial paths returned by the NavMesh when a full path is not available.")]
        public bool allowPartialPaths = true;

        [Header("Polling")]
        [Tooltip("Global poll frequency for all agents.")]
        [Min(0.05f)] public float pollInterval = 0.15f;
        [Tooltip("Repath if agent moved this far from the path segment.")]
        [Min(0f)] public float repathFromDrift = 0.75f;
        [Tooltip("Repath if target moved this far since last solve.")]
        [Min(0f)] public float repathFromTargetDelta = 0.75f;
        [Tooltip("Hard cap solves per frame to spread cost.")]
        [Min(1)] public int maxSolvesPerFrame = 16;

        [Header("Debug")]
        public bool drawFinalPaths = true;
        public Color pathColor = new(0.2f, 1f, 0.2f, 0.9f);

        public readonly struct AgentAvoidanceSettings
        {
            public static AgentAvoidanceSettings Disabled => default;

            public readonly float radius;
            public readonly Func<AgentController, bool> filter;

            public AgentAvoidanceSettings(float radius, Func<AgentController, bool> filter)
            {
                this.radius = Mathf.Max(0f, radius);
                this.filter = filter;
            }

            public bool Enabled => radius > 0f && filter != null;
            public bool ShouldAvoid(AgentController controller) => Enabled && controller != null && filter(controller);
        }

        public readonly struct LineOfSightSettings
        {
            public static LineOfSightSettings Disabled => default;

            public readonly bool requireLineOfSight;
            public readonly float radius;
            public readonly LayerMask obstacleMask;
            public readonly QueryTriggerInteraction triggerInteraction;
            public readonly Vector3 originOffset;
            public readonly Vector3 targetOffset;

            public LineOfSightSettings(bool requireLineOfSight,
                                       float radius,
                                       LayerMask obstacleMask,
                                       QueryTriggerInteraction triggerInteraction,
                                       Vector3 originOffset,
                                       Vector3 targetOffset)
            {
                this.requireLineOfSight = requireLineOfSight;
                this.radius = Mathf.Max(0f, radius);
                this.obstacleMask = obstacleMask;
                this.triggerInteraction = triggerInteraction;
                this.originOffset = originOffset;
                this.targetOffset = targetOffset;
            }

            public bool Enabled => requireLineOfSight;
        }

        public sealed class AgentTicket
        {
            public readonly AgentController agent;
            public readonly Func<Vector3> GetAgentPos;
            public readonly Func<Vector3> GetTargetPos;
            public readonly float stoppingDistance;
            public readonly float waypointTolerance;
            public readonly bool snapTo8;

            public readonly AgentAvoidanceSettings avoidance;
            public readonly LineOfSightSettings lineOfSight;

            internal readonly List<Vector3> path = new();
            internal readonly NavMeshPath navPath = new();
            internal int waypointIndex = -1;
            internal Vector3 lastAgentPos;
            internal Vector3 lastTargetPos;
            internal float nextPollAt;
            internal bool hasPath;
            internal string label;

            public AgentTicket(AgentController agent,
                               Func<Vector3> getAgentPos,
                               Func<Vector3> getTargetPos,
                               float stopDist,
                               float waypointTol,
                               bool snap8,
                               string label,
                               AgentAvoidanceSettings avoidance,
                               LineOfSightSettings lineOfSight)
            {
                this.agent = agent;
                GetAgentPos = getAgentPos;
                GetTargetPos = getTargetPos;
                stoppingDistance = Mathf.Max(0f, stopDist);
                waypointTolerance = Mathf.Max(0.01f, waypointTol);
                snapTo8 = snap8;
                this.label = label;
                this.avoidance = avoidance;
                this.lineOfSight = lineOfSight;
                lastAgentPos = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                lastTargetPos = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            }
        }

        readonly List<AgentTicket> _agents = new(128);
        readonly List<AgentController> _agentBuffer = new(128);
        static readonly RaycastHit[] lineOfSightHits = new RaycastHit[8];
        int _solvesThisFrame;

        void Awake()
        {
            if (Instance && Instance != this)
            {
                DestroyImmediate(gameObject);
                return;
            }

            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        void LateUpdate()
        {
            if (_agents.Count == 0)
            {
                return;
            }

            float now = Time.time;
            _solvesThisFrame = 0;

            for (int i = 0; i < _agents.Count; i++)
            {
                AgentTicket ticket = _agents[i];
                if (ticket?.agent == null)
                {
                    continue;
                }

                Vector3 agentPos = ticket.GetAgentPos();
                Vector3 targetPos = ticket.GetTargetPos();

                if (PlanarDistanceSqr(agentPos, targetPos) <= ticket.stoppingDistance * ticket.stoppingDistance)
                {
                    ClearAgentPath(ticket);
                    continue;
                }

                bool needPoll = now >= ticket.nextPollAt;
                bool movedEnough = !IsFinite(ticket.lastAgentPos) || PlanarDistanceSqr(agentPos, ticket.lastAgentPos) >= repathFromDrift * repathFromDrift;
                bool targetShift = !IsFinite(ticket.lastTargetPos) || PlanarDistanceSqr(targetPos, ticket.lastTargetPos) >= repathFromTargetDelta * repathFromTargetDelta;

                if (!needPoll && !movedEnough && !targetShift && ticket.hasPath)
                {
                    continue;
                }

                if (_solvesThisFrame >= maxSolvesPerFrame)
                {
                    continue;
                }

                TryResolvePath(agentPos, targetPos, ticket);
                ticket.lastAgentPos = agentPos;
                ticket.lastTargetPos = targetPos;
                ticket.nextPollAt = now + pollInterval;
                _solvesThisFrame++;
            }
        }

        public AgentTicket RegisterAgent(AgentController agent,
                                         Func<Vector3> getAgentPos,
                                         Func<Vector3> getTargetPos,
                                         float stoppingDistance,
                                         float waypointTolerance,
                                         bool snapTo8,
                                         string label,
                                         AgentAvoidanceSettings avoidance = default,
                                         LineOfSightSettings lineOfSight = default)
        {
            var ticket = new AgentTicket(agent, getAgentPos, getTargetPos, stoppingDistance, waypointTolerance, snapTo8, label, avoidance, lineOfSight);
            _agents.Add(ticket);
            return ticket;
        }

        public void UnregisterAgent(AgentTicket ticket)
        {
            if (ticket == null)
            {
                return;
            }

            _agents.Remove(ticket);
        }

        public Vector3 GetMoveDirection(AgentTicket ticket, Vector3 agentPos)
        {
            if (ticket == null)
            {
                return Vector3.zero;
            }

            AdvanceWaypoint(ticket, agentPos);
            if (ticket.waypointIndex < 0 || ticket.waypointIndex >= ticket.path.Count)
            {
                return Vector3.zero;
            }

            Vector3 delta = Planar(ticket.path[ticket.waypointIndex] - agentPos);
            if (delta.sqrMagnitude < 0.0001f)
            {
                return Vector3.zero;
            }

            Vector3 direction = delta.normalized;
            return ticket.snapTo8 ? SnapTo8(direction) : direction;
        }

        bool TryResolvePath(Vector3 agentPos, Vector3 targetPos, AgentTicket ticket)
        {
            if (!TrySamplePosition(agentPos, out Vector3 sampledStart) ||
                !TrySamplePosition(targetPos, out Vector3 sampledGoal))
            {
                ClearAgentPath(ticket);
                return false;
            }

            Vector3 resolvedGoal = ResolveGoalAgainstAgents(sampledGoal, ticket);

            if (ticket.lineOfSight.Enabled && !HasLineOfSight(sampledStart, resolvedGoal, ticket.lineOfSight))
            {
                ClearAgentPath(ticket);
                return false;
            }

            if (!CalculatePath(sampledStart, resolvedGoal, ticket, out Vector3[] corners))
            {
                ClearAgentPath(ticket);
                return false;
            }

            if (ticket.avoidance.Enabled && PathBlockedByAgents(corners, ticket))
            {
                ClearAgentPath(ticket);
                return false;
            }

            ApplyPathToAgent(corners, ticket, agentPos);
            return true;
        }

        bool CalculatePath(Vector3 start, Vector3 goal, AgentTicket ticket, out Vector3[] corners)
        {
            corners = null;
            NavMeshPath path = ticket.navPath;
            path.ClearCorners();

            if (!NavMesh.CalculatePath(start, goal, areaMask, path))
            {
                return false;
            }

            if (path.status == NavMeshPathStatus.PathInvalid)
            {
                return false;
            }

            if (path.status == NavMeshPathStatus.PathPartial && !allowPartialPaths)
            {
                return false;
            }

            corners = path.corners;
            if (corners == null || corners.Length == 0)
            {
                return false;
            }

            return true;
        }

        void ApplyPathToAgent(IReadOnlyList<Vector3> corners, AgentTicket ticket, Vector3 agentPos)
        {
            ticket.path.Clear();
            if (corners != null)
            {
                ticket.path.AddRange(corners);
            }

            ticket.hasPath = ticket.path.Count > 0;
            ticket.waypointIndex = ticket.path.Count > 1 ? 1 : 0;
            AdvanceWaypoint(ticket, agentPos);
        }

        void ClearAgentPath(AgentTicket ticket)
        {
            ticket.hasPath = false;
            ticket.path.Clear();
            ticket.waypointIndex = -1;
        }

        void AdvanceWaypoint(AgentTicket ticket, Vector3 agentPos)
        {
            if (!ticket.hasPath || ticket.waypointIndex < 0)
            {
                return;
            }

            float toleranceSqr = ticket.waypointTolerance * ticket.waypointTolerance;
            while (ticket.waypointIndex < ticket.path.Count &&
                   PlanarDistanceSqr(ticket.path[ticket.waypointIndex], agentPos) <= toleranceSqr)
            {
                ticket.waypointIndex++;
            }

            if (ticket.waypointIndex >= ticket.path.Count)
            {
                ticket.waypointIndex = -1;
                ticket.hasPath = false;
            }
        }

        bool TrySamplePosition(Vector3 position, out Vector3 sampled)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, sampleMaxDistance, areaMask))
            {
                sampled = hit.position;
                return true;
            }

            sampled = position;
            return false;
        }

        Vector3 ResolveGoalAgainstAgents(Vector3 sampledGoal, AgentTicket ticket)
        {
            if (!ticket.avoidance.Enabled)
            {
                return sampledGoal;
            }

            Vector3 adjusted = sampledGoal;
            bool pushed = false;

            FillAgentBuffer(ticket);
            for (int i = 0; i < _agentBuffer.Count; i++)
            {
                AgentController other = _agentBuffer[i];
                if (!ticket.avoidance.ShouldAvoid(other))
                {
                    continue;
                }

                Vector3 otherPos = other.transform.position;
                Vector3 planarDelta = Planar(adjusted - otherPos);
                float sqrMagnitude = planarDelta.sqrMagnitude;
                if (sqrMagnitude < ticket.avoidance.radius * ticket.avoidance.radius && sqrMagnitude > 0.0001f)
                {
                    float distance = Mathf.Sqrt(sqrMagnitude);
                    float pushAmount = ticket.avoidance.radius - distance;
                    adjusted += planarDelta.normalized * pushAmount;
                    pushed = true;
                }
            }

            if (pushed && TrySamplePosition(adjusted, out Vector3 sampledAdjusted))
            {
                return sampledAdjusted;
            }

            return sampledGoal;
        }

        bool PathBlockedByAgents(IReadOnlyList<Vector3> corners, AgentTicket ticket)
        {
            if (corners == null || corners.Count < 2)
            {
                return false;
            }

            FillAgentBuffer(ticket);
            float avoidanceRadiusSqr = ticket.avoidance.radius * ticket.avoidance.radius;

            for (int i = 0; i < _agentBuffer.Count; i++)
            {
                AgentController other = _agentBuffer[i];
                if (!ticket.avoidance.ShouldAvoid(other))
                {
                    continue;
                }

                Vector3 otherPos = other.transform.position;
                for (int corner = 1; corner < corners.Count; corner++)
                {
                    float distanceSqr = DistancePointToSegmentSqr(otherPos, corners[corner - 1], corners[corner]);
                    if (distanceSqr <= avoidanceRadiusSqr)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        void FillAgentBuffer(AgentTicket ticket)
        {
            _agentBuffer.Clear();
            for (int i = 0; i < _agents.Count; i++)
            {
                AgentTicket other = _agents[i];
                if (other == null || other.agent == null || other == ticket || other.agent == ticket.agent)
                {
                    continue;
                }

                _agentBuffer.Add(other.agent);
            }
        }

        static bool HasLineOfSight(Vector3 start, Vector3 goal, LineOfSightSettings settings)
        {
            Vector3 origin = start + settings.originOffset;
            Vector3 target = goal + settings.targetOffset;
            Vector3 delta = target - origin;
            float distance = delta.magnitude;
            if (distance <= 0.001f)
            {
                return true;
            }

            Vector3 dir = delta / distance;

            if (settings.radius <= 0.001f)
            {
                int hits = Physics.RaycastNonAlloc(origin, dir, lineOfSightHits, distance, settings.obstacleMask, settings.triggerInteraction);
                return hits == 0;
            }

            int sphereHits = Physics.SphereCastNonAlloc(origin, settings.radius, dir, lineOfSightHits, distance, settings.obstacleMask, settings.triggerInteraction);
            return sphereHits == 0;
        }

        static float DistancePointToSegmentSqr(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
        {
            Vector3 a = Planar(segmentStart);
            Vector3 b = Planar(segmentEnd);
            Vector3 p = Planar(point);

            Vector3 ab = b - a;
            float abSqrMag = ab.sqrMagnitude;
            if (abSqrMag <= 0.0001f)
            {
                return (p - a).sqrMagnitude;
            }

            float t = Vector3.Dot(p - a, ab) / abSqrMag;
            t = Mathf.Clamp01(t);
            Vector3 closest = a + ab * t;
            return (p - closest).sqrMagnitude;
        }

        static Vector3 Planar(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        static float PlanarDistanceSqr(Vector3 a, Vector3 b)
        {
            Vector3 delta = a - b;
            delta.y = 0f;
            return delta.sqrMagnitude;
        }

        static bool IsFinite(Vector3 v) => !(float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        static Vector3 SnapTo8(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.0001f)
            {
                return Vector3.zero;
            }

            Vector2 planar = new(direction.x, direction.z);
            if (planar.sqrMagnitude < 0.0001f)
            {
                return Vector3.zero;
            }

            float angle = Mathf.Atan2(planar.y, planar.x);
            float sector = Mathf.Round(angle / (Mathf.PI / 4f));
            float snapped = sector * (Mathf.PI / 4f);
            Vector2 snapped2D = new(Mathf.Cos(snapped), Mathf.Sin(snapped));
            return new Vector3(snapped2D.x, 0f, snapped2D.y).normalized;
        }

        void OnDrawGizmos()
        {
            if (!drawFinalPaths)
            {
                return;
            }

            Gizmos.color = pathColor;
            foreach (AgentTicket ticket in _agents)
            {
                if (ticket == null || !ticket.hasPath || ticket.path.Count < 2)
                {
                    continue;
                }

                for (int i = 1; i < ticket.path.Count; i++)
                {
                    Gizmos.DrawLine(ticket.path[i - 1], ticket.path[i]);
                }
            }
        }
    }
}
