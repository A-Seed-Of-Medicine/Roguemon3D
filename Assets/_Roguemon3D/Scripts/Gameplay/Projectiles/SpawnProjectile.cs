using System;
using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Effects;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Projectiles
{
    /// <summary>
    /// Centralized projectile spawning behaviour that can be triggered from an Effect or directly
    /// from gameplay scripts. Supports multiple projectile presets and basic owner-aware launch
    /// setup (collider ignoring, inherited velocity, aim direction, etc.).
    /// </summary>
    [DisallowMultipleComponent]
    public class SpawnProjectile : MonoBehaviour
    {
        [Serializable]
        public class ProjectilePreset
        {
            public string id = "default";
            public Projectile projectilePrefab;
            public Transform spawnTransform;
            public Vector3 positionOffset;
            public Vector3 rotationOffset;
            public bool parentToSpawnTransform;
            public bool alignToDirection = true;
            public bool inheritOwnerVelocity = true;
            public bool ignoreOwnerColliders = true;
            public bool scaleSpeedWithMagnitude = true;
            public float speedMultiplier = 1f;
            public bool useContextDirection = true;
            public Vector3 fallbackDirection = Vector3.forward;
        }

        [Header("Configuration")]
        [SerializeField] private List<ProjectilePreset> projectilePresets = new();
        [SerializeField] private string defaultPresetId = "default";
        [SerializeField] private AgentController owner;

        readonly List<Collider> colliderCache = new();

        void Awake()
        {
            owner ??= GetComponent<AgentController>();
        }

        public Projectile SpawnFromContext(EffectContext context, string presetId = null)
        {
            ProjectilePreset preset = ResolvePreset(presetId);
            if (preset == null || !preset.projectilePrefab)
            {
                return null;
            }

            Transform sourceTransform = ResolveSourceTransform(context?.Source) ?? transform;
            Vector3 origin = ResolveSpawnPosition(preset, sourceTransform);
            Vector3 direction = ResolveLaunchDirection(preset, context, sourceTransform, origin);
            Quaternion rotation = ResolveSpawnRotation(preset, direction, sourceTransform);

            Projectile projectile = Instantiate(preset.projectilePrefab, origin, rotation);
            if (!projectile)
            {
                return null;
            }

            if (preset.parentToSpawnTransform && preset.spawnTransform)
            {
                projectile.transform.SetParent(preset.spawnTransform);
            }

            Projectile.LaunchData launchData = BuildLaunchData(preset, context, sourceTransform, origin, direction);
            projectile.Launch(launchData);

            return projectile;
        }

        ProjectilePreset ResolvePreset(string presetId)
        {
            if (projectilePresets == null || projectilePresets.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(presetId))
            {
                ProjectilePreset preset = projectilePresets.Find(p => p != null && p.id == presetId);
                if (preset != null)
                {
                    return preset;
                }
            }

            return projectilePresets.Find(p => p != null && p.id == defaultPresetId) ?? projectilePresets[0];
        }

        Transform ResolveSourceTransform(IDamager source)
        {
            if (source is Component component)
            {
                return component.transform;
            }

            return owner ? owner.transform : transform;
        }

        Vector3 ResolveSpawnPosition(ProjectilePreset preset, Transform sourceTransform)
        {
            if (preset.spawnTransform)
            {
                return preset.spawnTransform.TransformPoint(preset.positionOffset);
            }

            return sourceTransform.TransformPoint(preset.positionOffset);
        }

        Vector3 ResolveLaunchDirection(ProjectilePreset preset, EffectContext context, Transform sourceTransform, Vector3 origin)
        {
            if (preset.useContextDirection && context != null)
            {
                Vector3 dir = context.Direction;
                if (dir.sqrMagnitude > 0.0001f)
                {
                    return dir;
                }

                if (context.Target != null)
                {
                    return (context.TargetPosition - origin).normalized;
                }
            }

            Vector3 fallback = preset.fallbackDirection.sqrMagnitude > 0.0001f
                ? preset.fallbackDirection.normalized
                : sourceTransform.forward;

            return fallback;
        }

        Quaternion ResolveSpawnRotation(ProjectilePreset preset, Vector3 direction, Transform sourceTransform)
        {
            Quaternion baseRotation = preset.alignToDirection && direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.z), Vector3.up)
                : sourceTransform.rotation;

            return baseRotation * Quaternion.Euler(preset.rotationOffset);
        }

        Projectile.LaunchData BuildLaunchData(ProjectilePreset preset, EffectContext context, Transform sourceTransform,
            Vector3 origin, Vector3 direction)
        {
            Vector3 inheritedVelocity = Vector3.zero;
            if (preset.inheritOwnerVelocity && sourceTransform.TryGetComponent(out Rigidbody rigidbody))
            {
                inheritedVelocity = rigidbody.linearVelocity;
            }

            float magnitude = context?.Magnitude ?? 1f;
            float speedScale = preset.scaleSpeedWithMagnitude ? magnitude : 1f;
            var launchData = new Projectile.LaunchData
            {
                Origin = origin,
                Direction = direction,
                SpeedMultiplier = preset.speedMultiplier * (speedScale <= 0f ? 1f : speedScale),
                InitialVelocity = inheritedVelocity,
                Owner = sourceTransform,
                IgnoredColliders = preset.ignoreOwnerColliders ? CollectOwnerColliders(sourceTransform) : null,
                Damager = context?.Source,
                Target = context?.Target,
                Magnitude = magnitude
            };

            return launchData;
        }

        Collider[] CollectOwnerColliders(Transform sourceTransform)
        {
            colliderCache.Clear();
            sourceTransform.GetComponentsInChildren(true, colliderCache);

            if (colliderCache.Count == 0)
            {
                if (sourceTransform.TryGetComponent(out Collider collider))
                {
                    colliderCache.Add(collider);
                }
            }

            return colliderCache.Count > 0 ? colliderCache.ToArray() : null;
        }
    }
}
