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
            float targetValue = value / scale;
            float t = 0f;
            float step = 0.001f;
            for (t = 0f; t <= 1f; t += step)
            {
                if (Mathf.Approximately(curve.Evaluate(t), targetValue) || curve.Evaluate(t) > targetValue)
                {
                    return t;
                }
            }
            return 1f;
        }

        public float UnScaledEvaluate(float t) => curve.Evaluate(t);
    
        public AnimCurveScale(AnimationCurve curve, float scale)
        {
            this.curve = curve;
            this.scale = scale;
        }
    }
}

