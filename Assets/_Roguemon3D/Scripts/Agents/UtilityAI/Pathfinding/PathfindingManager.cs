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

        [Header("Goal Sampling")]
        [Tooltip("Radius used to sample alternate targets around the requested destination when looking for valid paths.")]
        [Min(0f)] public float alternateGoalRadius = 1.25f;
        [Tooltip("Number of radial samples to try around the goal when a direct path fails validation.")]
        [Range(0, 16)] public int alternateGoalRays = 8;

        [Header("Debug")]
        public bool drawFinalPaths = true;
        public Color pathColor = new(0.2f, 1f, 0.2f, 0.9f);

        public sealed class LineOfSightSettings
        {
            public bool required;
            public LayerMask mask = Physics.DefaultRaycastLayers;
            [Tooltip("Height offset from the corners used when casting for line of sight.")]
            public float verticalOffset = 0.4f;
            [Tooltip("Optional sphere radius for the cast. Leave at 0 for a thin raycast.")]
            public float radius;
            public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

            public static LineOfSightSettings Disabled => new() { required = false };

            public LineOfSightSettings Clone()
            {
                return new LineOfSightSettings
                {
                    required = required,
                    mask = mask,
                    verticalOffset = verticalOffset,
                    radius = Mathf.Max(0f, radius),
                    triggerInteraction = triggerInteraction
                };
            }
        }

        public sealed class AgentAvoidanceSettings
        {
            [Tooltip("Minimum planar distance to maintain from other agents included by the filter.")]
            public float radius = 0.5f;
            [Tooltip("Optional filter used to select which agents should be considered for avoidance.")]
            public Func<AgentController, bool> filter;

            public float Radius => Mathf.Max(0f, radius);
            public float RadiusSqr => Radius * Radius;
            public bool Enabled => RadiusSqr > 0f;

            public AgentAvoidanceSettings Clone()
            {
                return new AgentAvoidanceSettings
                {
                    radius = Radius,
                    filter = filter
                };
            }
        }

        public sealed class PathRequestOptions
        {
            public AgentAvoidanceSettings avoidance = new();
            public LineOfSightSettings lineOfSight = LineOfSightSettings.Disabled;
            [Tooltip("Radius used when sampling alternative goals to steer around obstructions.")]
            public float goalSampleRadius = 1.25f;

            public static PathRequestOptions Defaults => new();

            public PathRequestOptions Clone()
            {
                return new PathRequestOptions
                {
                    avoidance = avoidance?.Clone() ?? new AgentAvoidanceSettings(),
                    lineOfSight = lineOfSight?.Clone() ?? LineOfSightSettings.Disabled,
                    goalSampleRadius = Mathf.Max(0f, goalSampleRadius)
                };
            }
        }

        public sealed class AgentTicket
        {
            public readonly AgentController agent;
            public readonly Func<Vector3> GetAgentPos;
            public readonly Func<Vector3> GetTargetPos;
            public readonly float stoppingDistance;
            public readonly float waypointTolerance;
            public readonly bool snapTo8;
            public readonly PathRequestOptions options;

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
                               PathRequestOptions opts)
            {
                this.agent = agent;
                GetAgentPos = getAgentPos;
                GetTargetPos = getTargetPos;
                stoppingDistance = Mathf.Max(0f, stopDist);
                waypointTolerance = Mathf.Max(0.01f, waypointTol);
                snapTo8 = snap8;
                this.label = label;
                lastAgentPos = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                lastTargetPos = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                options = opts ?? PathRequestOptions.Defaults;
            }
        }

        readonly List<AgentTicket> _agents = new(128);
        readonly List<AgentSample> _agentSamples = new(128);
        readonly RaycastHit[] _raycastHits = new RaycastHit[8];
        int _solvesThisFrame;

        struct AgentSample
        {
            public AgentController controller;
            public Vector3 position;
        }

        void Awake()
        {
            if (Instance && Instance != this)
            {
                Debug.LogWarning($"Multiple {nameof(PathfindingManager)} instances detected. Destroying duplicate on {name}.", this);
                Destroy(this);
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

        void Update()
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
                                         PathRequestOptions requestOptions = null)
        {
            var ticket = new AgentTicket(agent, getAgentPos, getTargetPos, stoppingDistance, waypointTolerance, snapTo8, label, requestOptions?.Clone());
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

            CollectAgentSamples(ticket);

            if (TryCalculatePath(sampledStart, sampledGoal, ticket, agentPos))
            {
                return true;
            }

            ClearAgentPath(ticket);
            return false;
        }

        bool TryCalculatePath(Vector3 start, Vector3 goal, AgentTicket ticket, Vector3 agentPos)
        {
            NavMeshPath path = ticket.navPath;
            path.ClearCorners();
            float goalRadius = Mathf.Max(alternateGoalRadius, ticket.options.goalSampleRadius);

            foreach (Vector3 candidateGoal in EnumerateGoalSamples(goal, goalRadius))
            {
                if (!NavMesh.CalculatePath(start, candidateGoal, areaMask, path))
                {
                    continue;
                }

                if (path.status == NavMeshPathStatus.PathInvalid)
                {
                    continue;
                }

                if (path.status == NavMeshPathStatus.PathPartial && !allowPartialPaths)
                {
                    continue;
                }

                Vector3[] corners = path.corners;
                if (corners == null || corners.Length == 0)
                {
                    continue;
                }

                if (!ValidateAgentAvoidance(corners, ticket))
                {
                    continue;
                }

                if (!ValidateLineOfSight(corners, ticket))
                {
                    continue;
                }

                ApplyPathToAgent(corners, ticket, agentPos);
                return true;
            }

            return false;
        }

        IEnumerable<Vector3> EnumerateGoalSamples(Vector3 goal, float radius)
        {
            yield return goal;

            if (radius <= 0f || alternateGoalRays <= 0)
            {
                yield break;
            }

            float angleStep = 360f / Mathf.Max(1, alternateGoalRays);
            for (int i = 0; i < alternateGoalRays; i++)
            {
                float angle = angleStep * i * Mathf.Deg2Rad;
                Vector3 offset = new(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (TrySamplePosition(goal + offset, out Vector3 sampled))
                {
                    yield return sampled;
                }
            }
        }

        bool ValidateAgentAvoidance(IReadOnlyList<Vector3> corners, AgentTicket ticket)
        {
            AgentAvoidanceSettings avoidance = ticket.options.avoidance;
            if (!avoidance.Enabled || _agentSamples.Count == 0 || corners.Count < 2)
            {
                return true;
            }

            float minDistSqr = avoidance.RadiusSqr;
            for (int segment = 1; segment < corners.Count; segment++)
            {
                if (corners.Count > 1 && segment == 1)
                    continue;
                Vector3 a = corners[segment - 1];
                Vector3 b = corners[segment];
                for (int i = 0; i < _agentSamples.Count; i++)
                {
                    AgentSample sample = _agentSamples[i];
                    if (!AvoidanceAllows(ticket, sample.controller))
                    {
                        continue;
                    }

                    float distSqr = DistancePointToSegmentPlanar(sample.position, a, b);
                    if (distSqr < minDistSqr)
                    {
                        //Debug.Log($"Path blocked by agent '{sample.controller.name}' for ticket '{ticket.label}'.", ticket.agent);
                        return false;
                    }
                }
            }

            return true;
        }

        bool ValidateLineOfSight(IReadOnlyList<Vector3> corners, AgentTicket ticket)
        {
            LineOfSightSettings lineOfSight = ticket.options.lineOfSight;
            if (!lineOfSight.required || corners.Count < 2)
            {
                return true;
            }

            Vector3 offset = Vector3.up * lineOfSight.verticalOffset;
            int mask = lineOfSight.mask;
            QueryTriggerInteraction triggers = lineOfSight.triggerInteraction;
            float radius = Mathf.Max(0f, lineOfSight.radius);

            for (int i = 1; i < corners.Count; i++)
            {
                Vector3 start = corners[i - 1] + offset;
                Vector3 end = corners[i] + offset;
                Vector3 direction = end - start;
                float distance = direction.magnitude;
                if (distance <= 0.001f)
                {
                    continue;
                }

                direction /= distance;
                int hits = radius > 0f
                    ? Physics.SphereCastNonAlloc(start, radius, direction, _raycastHits, distance, mask, triggers)
                    : Physics.RaycastNonAlloc(start, direction, _raycastHits, distance, mask, triggers);

                for (int h = 0; h < hits; h++)
                {
                    Collider collider = _raycastHits[h].collider;
                    if (collider == null)
                    {
                        continue;
                    }

                    if (ticket.agent && collider.transform.IsChildOf(ticket.agent.transform))
                    {
                        continue;
                    }

                    return false;
                }
            }

            return true;
        }

        void ApplyPathToAgent(IReadOnlyList<Vector3> corners, AgentTicket ticket, Vector3 agentPos)
        {
            ticket.path.Clear();
            for (int i = 0; i < corners.Count; i++)
            {
                ticket.path.Add(corners[i]);
            }

            ticket.hasPath = ticket.path.Count > 0;
            ticket.waypointIndex = ticket.path.Count > 1 ? 1 : 0;
            AdvanceWaypoint(ticket, agentPos);
        }

        void CollectAgentSamples(AgentTicket requester)
        {
            _agentSamples.Clear();
            AgentAvoidanceSettings avoidance = requester.options.avoidance;
            if (!avoidance.Enabled)
            {
                return;
            }

            for (int i = 0; i < _agents.Count; i++)
            {
                AgentTicket ticket = _agents[i];
                AgentController controller = ticket?.agent;
                if (controller == null || controller == requester.agent)
                {
                    continue;
                }

                if (!AvoidanceAllows(requester, controller))
                {
                    continue;
                }

                Vector3 pos = ticket.GetAgentPos();
                _agentSamples.Add(new AgentSample { controller = controller, position = pos });
            }
        }

        static bool AvoidanceAllows(AgentTicket requester, AgentController candidate)
        {
            AgentAvoidanceSettings settings = requester.options.avoidance;
            if (!settings.Enabled)
            {
                return false;
            }

            if (candidate == requester.agent)
            {
                return false;
            }

            return settings.filter == null || settings.filter(candidate);
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

        static float DistancePointToSegmentPlanar(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ap = Planar(point - a);
            Vector3 ab = Planar(b - a);
            float magnitudeSqr = ab.sqrMagnitude;
            if (magnitudeSqr < 0.0001f)
            {
                return ap.sqrMagnitude;
            }

            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / magnitudeSqr);
            Vector3 projection = a + ab * t;
            return PlanarDistanceSqr(point, projection);
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
