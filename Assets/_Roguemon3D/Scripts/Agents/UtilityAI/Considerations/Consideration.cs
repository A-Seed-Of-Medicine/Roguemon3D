using System;
using UnityEngine;

namespace UtilityAI {
    [Serializable]
    public abstract class Consideration {
        public abstract float Evaluate(Context context, Transform target);
    }
}