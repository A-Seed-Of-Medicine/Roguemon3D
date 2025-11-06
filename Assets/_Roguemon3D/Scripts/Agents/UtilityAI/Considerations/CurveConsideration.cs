using System;
using UnityEngine;

namespace UtilityAI {
    [Serializable]
    public class CurveConsideration : Consideration {
        public AnimationCurve curve;
        public string contextKey;

        public override float Evaluate(Context context, Transform target) {
            //TODO: Utilize properties API for value references  float inputValue =
            //float utility = curve.Evaluate(inputValue);
            //return Mathf.Clamp01(utility);
            return default;
        }

        void Reset() {
            curve = new AnimationCurve(
                new Keyframe(0f, 1f), // At normalized distance 0, utility is 1
                new Keyframe(1f, 0f)  // At normalized distance 1, utility is 0
            );
        }
    }
}