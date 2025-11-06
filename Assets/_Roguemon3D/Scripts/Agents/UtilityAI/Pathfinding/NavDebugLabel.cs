// _PinBoy/Scripts/AI/Pathfinding/NavDebugLabel.cs
using UnityEngine;

namespace UtilityAI.Pathfinding
{
    [ExecuteAlways]
    public sealed class NavDebugLabel : MonoBehaviour
    {
        public PathfindingManager.AgentTicket ticket;
        public Color color = Color.white;
        public Vector3 offset = new(0f, 0.5f, 0f);

        void OnDrawGizmos()
        {
            if (ticket == null) return;
#if UNITY_EDITOR
            UnityEditor.Handles.color = color;
            Vector3 pos = transform.position + offset;
            string text = ticket.label + (ticket.hasPath ? $"  wp:{ticket.waypointIndex}/{ticket.path.Count}" : "  idle");
            UnityEditor.Handles.Label(pos, text);
#endif
        }
    }
}
