using System.Collections.Generic;
using UnityEngine;

namespace UtilityAI {
    public class CompositeConsideration : Consideration {
        public enum OperationType { Average, Multiply, Add, Subtract, Divide, Max, Min }
        
        public bool allMustBeNonZero = true;
        
        public OperationType operation = OperationType.Max;
        [SerializeReference]
        public List<Consideration> considerations = new List<Consideration>();

        public override float Evaluate(Context context, Transform target) {
            if (considerations == null || considerations.Count == 0) return 0f;
            
            float result = considerations[0].Evaluate(context, target);
            if (result == 0f && allMustBeNonZero) return 0f;

            // Suggestion: Only 2 Considerations per Composite
            for (int i = 1; i < considerations.Count; i++) {
                float value = considerations[i].Evaluate(context, target);
                
                if (value == 0f && allMustBeNonZero) return 0f;

                switch (operation) {
                    case OperationType.Average:
                        result = (result + value) / 2;
                        break;
                    case OperationType.Multiply:
                        result *= value;
                        break;
                    case OperationType.Add:
                        result += value;
                        break;
                    case OperationType.Subtract:
                        result -= value;
                        break;
                    case OperationType.Divide:
                        result = value != 0 ? result / value : result; // Prevent division by zero
                        break;
                    case OperationType.Max:
                        result = Mathf.Max(result, value);
                        break;
                    case OperationType.Min:
                        result = Mathf.Min(result, value);
                        break;
                }
            }
            
            return Mathf.Clamp01(result);
        }
    }
}