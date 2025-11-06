using System;
using UnityEngine;

namespace UtilityAI {
    [Serializable]
    public class ConstantConsideration : Consideration {
        public float value;
        
        public override float Evaluate(Context context, Transform target) => value;
    }
}