using System;
using _PinBoy.Scripts.CharacterMovement;
using PrimeTween;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    [Serializable]
    public struct PhaseFX
    {
        public enum EffectType
        {
            None,
            ShakeLocalPosition,
            ShakeLocalRotation,
            ShakeScale,
            MaterialColor
        }

        [SerializeField] private string label;
        [SerializeField] private EffectType effectType;
        [SerializeField, Tooltip("If true, the phase duration is used for this effect. Otherwise, the custom duration below is used.")]
        private bool usePhaseDuration = true;
        [SerializeField, Min(0f), Tooltip("Duration used when not inheriting the phase duration.")]
        private float duration = 0.25f;

        [Header("Shake")]
        [SerializeField, Tooltip("The transform to shake. Defaults to the AgentController transform if not specified.")]
        private Transform shakeTarget;
        [SerializeField] private Vector3 shakeStrength = Vector3.one;
        [SerializeField] private float shakeFrequency = ShakeSettings.defaultFrequency;
        [SerializeField] private bool shakeEnableFalloff = true;
        [SerializeField] private Ease shakeEaseBetweenShakes = Ease.Default;
        [SerializeField, Range(0f, 1f)] private float shakeAsymmetry;
        [SerializeField, Min(1)] private int shakeCycles = 1;
        [SerializeField] private bool shakeUseUnscaledTime;
        [SerializeField] private UpdateType shakeUpdateType;

        [Header("Material Color")] 
        [SerializeField, Tooltip("Material to animate. Defaults to the AgentController animation material.")]
        private Material colorMaterial;
        [SerializeField, Tooltip("Renderer to pull a material from if no explicit material is set.")]
        private Renderer colorRenderer;
        [SerializeField, Min(0), Tooltip("Index into the renderer's materials array.")]
        private int colorMaterialIndex;
        [SerializeField, Tooltip("Shader property name used for the color animation.")]
        private string colorProperty = "_BaseColor";
        [SerializeField] private Color colorTarget = Color.white;
        [SerializeField, Tooltip("If true, use this value as the starting color instead of the material's current value.")]
        private bool overrideColorStart;
        [SerializeField] private Color colorStart;
        [SerializeField] private Ease colorEase = Ease.Default;
        [SerializeField, Tooltip("If true, shared materials on the renderer will be used instead of instantiated materials.")]
        private bool useSharedMaterial;

        public Tween Play(AgentController controller, float phaseDuration)
        {
            float runtimeDuration = ResolveDuration(phaseDuration);
            return effectType switch
            {
                EffectType.ShakeLocalPosition => PlayShake(controller, runtimeDuration, Tween.ShakeLocalPosition),
                EffectType.ShakeLocalRotation => PlayShake(controller, runtimeDuration, Tween.ShakeLocalRotation),
                EffectType.ShakeScale => PlayShake(controller, runtimeDuration, Tween.ShakeScale),
                EffectType.MaterialColor => PlayMaterialColor(controller, runtimeDuration),
                _ => default
            };
        }

        float ResolveDuration(float phaseDuration)
        {
            if (usePhaseDuration)
            {
                return Mathf.Max(0f, phaseDuration);
            }

            return Mathf.Max(0f, duration);
        }

        Tween PlayShake(AgentController controller, float runtimeDuration, Func<Transform, ShakeSettings, Tween> shakeMethod)
        {
            Transform target = shakeTarget ? shakeTarget : controller?.transform;
            if (!target)
            {
                return default;
            }

            ShakeSettings settings = new ShakeSettings(shakeStrength, runtimeDuration, shakeFrequency, shakeEnableFalloff,
                shakeEaseBetweenShakes, shakeAsymmetry, shakeCycles, 0f, 0f, shakeUseUnscaledTime, shakeUpdateType);

            if (runtimeDuration <= 0f)
            {
                return default;
            }

            return shakeMethod(target, settings);
        }

        Tween PlayMaterialColor(AgentController controller, float runtimeDuration)
        {
            Material targetMaterial = ResolveMaterial(controller);
            if (!targetMaterial)
            {
                return default;
            }

            int propertyId = string.IsNullOrWhiteSpace(colorProperty)
                ? Shader.PropertyToID("_BaseColor")
                : Shader.PropertyToID(colorProperty);

            if (runtimeDuration <= 0f)
            {
                targetMaterial.SetColor(propertyId, colorTarget);
                return default;
            }

            if (overrideColorStart)
            {
                return Tween.MaterialColor(targetMaterial, propertyId, colorStart, colorTarget, runtimeDuration, colorEase);
            }

            return Tween.MaterialColor(targetMaterial, propertyId, colorTarget, runtimeDuration, colorEase);
        }

        Material ResolveMaterial(AgentController controller)
        {
            if (colorMaterial)
            {
                return colorMaterial;
            }

            if (colorRenderer)
            {
                Material[] materials = useSharedMaterial ? colorRenderer.sharedMaterials : colorRenderer.materials;
                int clampedIndex = Mathf.Clamp(colorMaterialIndex, 0, materials.Length - 1);
                if (materials.Length > 0)
                {
                    return materials[clampedIndex];
                }
            }

            return controller ? controller.animationMaterial : null;
        }
    }
}
