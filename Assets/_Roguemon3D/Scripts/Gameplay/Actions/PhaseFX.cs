using System;
using _PinBoy.Scripts.CharacterMovement;
using PrimeTween;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    public interface IPhaseFxInstance
    {
        void Cancel();
    }

    [Serializable]
    public abstract class PhaseFX
    {
        [SerializeField] private bool enabled = true;

        public IPhaseFxInstance Play(AgentController controller, float duration)
        {
            if (!enabled || controller == null)
            {
                return null;
            }

            float clampedDuration = Mathf.Max(0f, duration);
            return OnPlay(controller, clampedDuration);
        }

        protected abstract IPhaseFxInstance OnPlay(AgentController controller, float duration);
    }

    sealed class TweenPhaseFxInstance : IPhaseFxInstance
    {
        readonly Tween tween;

        public TweenPhaseFxInstance(Tween tween)
        {
            this.tween = tween;
        }

        public void Cancel()
        {
            if (tween.isAlive)
            {
                tween.Stop();
            }
        }
    }

    public enum ShakePhaseFxTarget
    {
        Position,
        Rotation,
        Scale
    }

    [Serializable]
    public class ShakePhaseFX : PhaseFX
    {
        [SerializeField] private Transform target;
        [SerializeField] private ShakePhaseFxTarget shakeTarget = ShakePhaseFxTarget.Position;
        [SerializeField] private Vector3 strength = Vector3.one * 0.1f;
        [SerializeField] private float frequency = ShakeSettings.defaultFrequency;
        [SerializeField] private bool enableFalloff = true;
        [SerializeField] private Ease easeBetweenShakes = Ease.Default;
        [SerializeField, Range(0f, 1f)] private float asymmetry = 0f;

        protected override IPhaseFxInstance OnPlay(AgentController controller, float duration)
        {
            Transform resolvedTarget = target ? target : controller.transform;
            ShakeSettings settings = new(strength, duration, frequency, enableFalloff, easeBetweenShakes, asymmetry);

            Tween tween = shakeTarget switch
            {
                ShakePhaseFxTarget.Position => Tween.ShakeLocalPosition(resolvedTarget, settings),
                ShakePhaseFxTarget.Rotation => Tween.ShakeLocalRotation(resolvedTarget, settings),
                ShakePhaseFxTarget.Scale => Tween.ShakeScale(resolvedTarget, settings),
                _ => default
            };

            return tween.isAlive ? new TweenPhaseFxInstance(tween) : null;
        }
    }

    [Serializable]
    public class MaterialColorPhaseFX : PhaseFX
    {
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private Material materialOverride;
        [SerializeField] private string colorProperty = "_Color";
        [SerializeField] private Color startColor = Color.white;
        [SerializeField] private Color targetColor = Color.white;
        [SerializeField] private bool useCurrentAsStart = true;
        [SerializeField] private Ease ease = Ease.Linear;

        protected override IPhaseFxInstance OnPlay(AgentController controller, float duration)
        {
            Material material = materialOverride;
            if (material == null && targetRenderer)
            {
                material = targetRenderer.material;
            }

            if (material == null)
            {
                material = controller.animationMaterial;
            }

            if (material == null)
            {
                return null;
            }

            int propertyId = Shader.PropertyToID(string.IsNullOrWhiteSpace(colorProperty) ? "_Color" : colorProperty);
            Color resolvedStart = useCurrentAsStart ? material.GetColor(propertyId) : startColor;
            Tween tween = Tween.MaterialColor(material, propertyId, resolvedStart, targetColor, duration, ease);
            return tween.isAlive ? new TweenPhaseFxInstance(tween) : null;
        }
    }
}
