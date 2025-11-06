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
        [Tooltip("World-space quantization step used when caching NavMesh paths.")]
        [Min(0.01f)] public float cacheQuantization = 0.5f;

        [Header("Polling & Caching")]
        [Tooltip("Global poll frequency for all agents.")]
        [Min(0.05f)] public float pollInterval = 0.15f;
        [Tooltip("Repath if agent moved this far from the path segment.")]
        [Min(0f)] public float repathFromDrift = 0.75f;
        [Tooltip("Repath if target moved this far since last solve.")]
        [Min(0f)] public float repathFromTargetDelta = 0.75f;
        [Tooltip("Cache lifetime for identical path queries.")]
        [Min(0f)] public float cacheTTL = 0.75f;
        [Tooltip("Hard cap solves per frame to spread cost.")]
        [Min(1)] public int maxSolvesPerFrame = 16;

        [Header("Debug")]
        public bool drawFinalPaths = true;
        public Color pathColor = new(0.2f, 1f, 0.2f, 0.9f);
        public Color cachedPathColor = new(0.2f, 0.6f, 1f, 0.5f);

        struct CacheKey : IEquatable<CacheKey>
        {
            public readonly Vector3Int start;
            public readonly Vector3Int goal;
            public readonly int areaMask;

            public CacheKey(Vector3Int start, Vector3Int goal, int areaMask)
            {
                this.start = start;
                this.goal = goal;
                this.areaMask = areaMask;
            }

            public bool Equals(CacheKey other) => start.Equals(other.start) && goal.Equals(other.goal) && areaMask == other.areaMask;
            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = start.GetHashCode();
                    hash = (hash * 397) ^ goal.GetHashCode();
                    hash = (hash * 397) ^ areaMask;
                    return hash;
                }
            }
        }

        sealed class CacheEntry
        {
            public Vector3[] corners = Array.Empty<Vector3>();
            public float time;
        }

        public sealed class AgentTicket
        {
            public readonly AgentController agent;
            public readonly Func<Vector3> GetAgentPos;
            public readonly Func<Vector3> GetTargetPos;
            public readonly float stoppingDistance;
            public readonly float waypointTolerance;
            public readonly bool snapTo8;

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
                               string label)
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
            }
        }

        readonly List<AgentTicket> _agents = new(128);
        readonly Dictionary<CacheKey, CacheEntry> _cache = new(256);
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

                TryResolvePath(agentPos, targetPos, ticket, now);
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
                                         string label)
        {
            var ticket = new AgentTicket(agent, getAgentPos, getTargetPos, stoppingDistance, waypointTolerance, snapTo8, label);
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

        bool TryResolvePath(Vector3 agentPos, Vector3 targetPos, AgentTicket ticket, float now)
        {
            if (!TrySamplePosition(agentPos, out Vector3 sampledStart) ||
                !TrySamplePosition(targetPos, out Vector3 sampledGoal))
            {
                ClearAgentPath(ticket);
                return false;
            }

            CacheKey key = new CacheKey(Quantize(sampledStart), Quantize(sampledGoal), areaMask);
            if (_cache.TryGetValue(key, out CacheEntry entry))
            {
                if (cacheTTL <= 0f || (now - entry.time) <= cacheTTL)
                {
                    ApplyEntryToAgent(entry, ticket, agentPos);
                    return true;
                }

                _cache.Remove(key);
            }

            if (!CalculatePath(sampledStart, sampledGoal, ticket, out entry))
            {
                ClearAgentPath(ticket);
                return false;
            }

            entry.time = now;
            if (cacheTTL > 0f)
            {
                _cache[key] = entry;
            }
            ApplyEntryToAgent(entry, ticket, agentPos);
            return true;
        }

        bool CalculatePath(Vector3 start, Vector3 goal, AgentTicket ticket, out CacheEntry entry)
        {
            entry = null;
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

            Vector3[] corners = path.corners;
            if (corners == null || corners.Length == 0)
            {
                return false;
            }

            entry = new CacheEntry
            {
                corners = (Vector3[])corners.Clone()
            };

            return true;
        }

        void ApplyEntryToAgent(CacheEntry entry, AgentTicket ticket, Vector3 agentPos)
        {
            ticket.path.Clear();
            if (entry.corners != null)
            {
                ticket.path.AddRange(entry.corners);
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

        Vector3Int Quantize(Vector3 position)
        {
            float inv = 1f / Mathf.Max(0.01f, cacheQuantization);
            return new Vector3Int(
                Mathf.RoundToInt(position.x * inv),
                Mathf.RoundToInt(position.y * inv),
                Mathf.RoundToInt(position.z * inv));
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

#if UNITY_EDITOR
            if (_cache.Count > 0)
            {
                Gizmos.color = cachedPathColor;
                foreach (CacheEntry entry in _cache.Values)
                {
                    Vector3[] corners = entry.corners;
                    if (corners == null || corners.Length < 2)
                    {
                        continue;
                    }

                    for (int i = 1; i < corners.Length; i++)
                    {
                        Gizmos.DrawLine(corners[i - 1], corners[i]);
                    }
                }
            }
#endif
        }
    }
}
