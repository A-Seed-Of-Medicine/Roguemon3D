using System;
using UnityEngine;

namespace _Roguemon3D.Scripts.Utils
{
    [Serializable]
    public struct AnimCurveScale
    {
        public AnimationCurve curve;
        public float scale;
        public float Evaluate(float t)
        {
            if (scale == 0f) return 0f;
            return curve.Evaluate(t) * scale;
        }
        
        public float InverseEvaluate(float value)
        {
            if (scale == 0f) return 0f;
            return curve.Evaluate(value / scale);
        }

        public float UnScaledEvaluate(float t) => curve.Evaluate(t);
    
        public AnimCurveScale(AnimationCurve curve, float scale)
        {
            this.curve = curve;
            this.scale = scale;
        }
    }
}

