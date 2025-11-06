using System;
using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using AdvancedController;
using UnityEngine;

namespace UtilityAI {
    public class Context {
        public Brain brain { get; }
        public Transform target;
        public AgentController Controller => brain.controller;
        public Transform LastEvaluatedTarget { get; private set; }

        public Context(Brain brain) {
            this.brain = brain ? brain : throw new ArgumentNullException(nameof(brain));
        }

        public IReadOnlyList<Transform> GetPerceivedTargets() {
            return brain != null ? brain.GetPerceivedTargets() : Array.Empty<Transform>();
        }

        public Transform GetClosestTarget(string tag) {
            return brain != null ? brain.GetClosestTarget(tag) : null;
        }

        public void RegisterTargetTag(string tag) {
            brain?.RegisterTargetTag(tag);
        }

        internal void ResetLastEvaluatedTarget() {
            LastEvaluatedTarget = null;
        }

        internal void SetLastEvaluatedTarget(Transform target) {
            LastEvaluatedTarget = target;
        }
    }
}
