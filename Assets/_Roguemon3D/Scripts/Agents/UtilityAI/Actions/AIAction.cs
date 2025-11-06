using System;
using System.Collections.Generic;
using UnityEngine;

namespace UtilityAI {
    [Serializable]
    public abstract class AIAction {
        public string targetTag;
        public int maxTargets = 3;
        [SerializeField]
        [SerializeReference]
        public Consideration consideration;

        bool hasWarnedForNullConsideration;

        public virtual void Initialize(Context context) { }

        public float CalculateUtility(Context context, IReadOnlyList<Transform> targets) {
            if (context == null || consideration == null) {
                if (context != null && consideration == null && !hasWarnedForNullConsideration) {
                    hasWarnedForNullConsideration = true;
                    Debug.LogWarning($"AIAction {GetType().Name} on {context.brain.name} has no consideration configured.", context.brain);
                }
                context?.SetLastEvaluatedTarget(null);
                return 0f;
            }

            hasWarnedForNullConsideration = false;

            Transform originalTarget = context.target;
            Transform bestTarget = null;
            float highestUtility = float.MinValue;
            int count = 0;

            if (targets != null && targets.Count > 0) {
                for (int i = 0; i < targets.Count; i++) {
                    Transform candidate = targets[i];
                    if (!candidate) {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(targetTag) && !candidate.CompareTag(targetTag)) {
                        continue;
                    }

                    context.target = candidate;
                    float utility = consideration.Evaluate(context, candidate);
                    if (utility > highestUtility) {
                        highestUtility = utility;
                        bestTarget = candidate;
                    }

                    count++;
                    if (count >= maxTargets) {
                        break;
                    }
                }
            } else if (string.IsNullOrEmpty(targetTag)) {
                highestUtility = consideration.Evaluate(context, null);
            }

            context.target = originalTarget;
            context.SetLastEvaluatedTarget(bestTarget);

            if (highestUtility == float.MinValue) {
                highestUtility = 0f;
            }

            return highestUtility;
        }

        public abstract void Execute(Context context);
        
        public abstract void OnExit(Context context);
    }
}
