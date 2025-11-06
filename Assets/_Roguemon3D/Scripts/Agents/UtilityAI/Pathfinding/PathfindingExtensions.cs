// _PinBoy/Scripts/AI/Pathfinding/PathfindingExtensions.cs
using UnityEngine;

namespace UtilityAI.Pathfinding
{
    public static class PathfindingExtensions
    {
        public static Vector3 ClosestPathDirection(this PathfindingManager.AgentTicket ticket, Vector3 agentPos)
        {
            return PathfindingManager.Instance
                ? PathfindingManager.Instance.GetMoveDirection(ticket, agentPos)
                : Vector3.zero;
        }
    }
}
