using System;
using UnityEngine;
using UnityUtils;

namespace UtilityAI {
    [Serializable]
    public class InRangeConsideration : Consideration {
        public float minDistance = 0f;
        public float maxDistance = 10f;
        public float maxAngle = 360f;
        public float utilityScale = 1f;
        public bool requireLOS = false;
        public LayerMask lineOfSightMask = ~0;

        public override float Evaluate(Context context, Transform target) {
            if (!target) return 0f;

            Transform agentTransform = context.brain.transform;

            bool isInRange = agentTransform.InRangeOf(target, minDistance, maxDistance, maxAngle);
            if (!isInRange) return 0f;
            if (!requireLOS) return utilityScale;
            bool hasLOS = agentTransform.HasLineOfSightTo(target, lineOfSightMask);
            if (!hasLOS) return 0f;

            return utilityScale;
        }
    }
    
    [Serializable]
    public class InRangeCurveConsideration : InRangeConsideration {
        public AnimationCurve curve;
        
        public override float Evaluate(Context context, Transform target) {
            if (!target) return 0f;

            Transform agentTransform = context.brain.transform;

            bool isInRange = agentTransform.InRangeOf(target, maxDistance, maxAngle);
            if (!isInRange) return 0f;
            if (!requireLOS) return utilityScale;
            bool hasLOS = agentTransform.HasLineOfSightTo(target, lineOfSightMask);
            if (!hasLOS) return 0f;

            context.target = target;

            Vector3 directionToTarget = target.position - agentTransform.position;
            float distanceToTarget = directionToTarget.With(y:0).magnitude;
            
            float normalizedDistance = Mathf.Clamp01(distanceToTarget / maxDistance);
            
            float utility = curve.Evaluate(normalizedDistance);
            return Mathf.Clamp01(utility) * utilityScale;
        }
    }
}