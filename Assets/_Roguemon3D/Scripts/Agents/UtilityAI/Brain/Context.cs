using System;
using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using AdvancedController;
using UnityEngine;

namespace UtilityAI {
    public class Context {
        public Brain brain { get; }
        public TargetContext target;
        public AgentController Controller => brain.controller;
        public TargetContext LastEvaluatedTarget { get; private set; }

        public Context(Brain brain) {
            this.brain = brain ? brain : throw new ArgumentNullException(nameof(brain));
        }

        public IReadOnlyList<TargetContext> GetPerceivedTargets() {
            return brain != null ? brain.GetPerceivedTargets() : Array.Empty<TargetContext>();
        }

        public TargetContext GetClosestTarget(string tag) {
            return brain != null ? brain.GetClosestTarget(tag) : null;
        }

        public void RegisterTargetTag(string tag) {
            brain?.RegisterTargetTag(tag);
        }

        internal void ResetLastEvaluatedTarget() {
            LastEvaluatedTarget = null;
        }

        internal void SetLastEvaluatedTarget(TargetContext target) {
            LastEvaluatedTarget = target;
        }
    }
    
    public class TargetContext
    {
        public Transform transform;
        public AgentController agentController;
        
        public bool IsAgent => agentController != null;
        public Vector3 position => transform != null ? transform.position : Vector3.zero;
    
        // Set equation and hash code to allow comparison based on transform only
        public static implicit operator Transform(TargetContext context) => context?.transform;
        public static implicit operator TargetContext(Transform transform) => new TargetContext { transform = transform };
        public override bool Equals(object obj)
        {
            if (obj is TargetContext other)
            {
                return transform == other.transform;
            }
            if (obj is Transform otherTransform)
            {
                return transform == otherTransform;
            }
            return false;
        }
        public override int GetHashCode()
        {
            return transform != null ? transform.GetHashCode() : 0;
        }
    
        public static bool operator ==(TargetContext a, TargetContext b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a is null || b is null)
                return false;
            return a.transform == b.transform;
        }
    
        public static bool operator !=(TargetContext a, TargetContext b)
        {
            return !(a == b);
        }
    }

}
