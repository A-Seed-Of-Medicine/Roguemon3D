using System;
using System.Collections.Generic;
using _PinBoy.Scripts.Gameplay.Effects;
using UnityEngine;

namespace UtilityAI {
    [Serializable]
    public abstract class AIAction {
        public string targetTag;
        public AllegianceType allegianceMask;
        public int maxTargets = 3;
        [SerializeField]
        [SerializeReference]
        public Consideration consideration;

        bool hasWarnedForNullConsideration;

        public virtual void Initialize(Context context) { }

        public virtual float CalculateUtility(Context context, IReadOnlyList<TargetContext> targets) {
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
            TargetContext bestTarget = null;
            float highestUtility = 0;
            int count = 0;

            if (targets != null && targets.Count > 0) {
                for (int i = 0; i < targets.Count; i++) {
                    TargetContext target = targets[i];
                    Transform transform = target.transform;
                    if (!transform) {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(targetTag) && !transform.CompareTag(targetTag)) {
                        continue;
                    }
                    
                    if (allegianceMask != 0 && target.agentController && !context.Controller.IsAllegiance(allegianceMask, target.agentController.allegiance)) {
                        continue;
                    }

                    context.target = transform;
                    float utility = consideration.Evaluate(context, transform);
                    if (utility > highestUtility) {
                        Debug.Log($"AIAction {GetType().Name} on {context.brain.name} evaluated target {transform.name} with utility {utility}", context.brain);
                        highestUtility = utility;
                        bestTarget = target;
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
