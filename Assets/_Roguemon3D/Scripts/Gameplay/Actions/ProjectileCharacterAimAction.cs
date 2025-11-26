using System;
using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Effects;
using _PinBoy.Scripts.Gameplay.Projectiles;
using UnityEngine;
using UnityEngine.Events;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    /// <summary>
    /// Character aim action that spawns and launches a projectile towards the current aim direction.
    /// Works with both player input and AI driven contexts.
    /// </summary>
    public class ProjectileCharacterAimAction : CharacterAction
    {
        [Serializable]
        public class ProjectileConfiguration
        {
            public string id = "default";
            public Projectile projectilePrefab;
            public Transform projectileSpawnPoint;
            public Vector3 spawnPositionOffset;
            public Vector3 spawnRotationOffset;
            public bool parentProjectileToSpawnPoint;
            public bool alignSpawnRotation = true;
            public bool inheritControllerVelocity = true;
            public bool ignoreOwnerColliders = true;
            public float projectileSpeedMultiplier = 1f;
        }

        [Header("Projectile")]
        [SerializeField] private Projectile projectilePrefab;
        [SerializeField] private Transform projectileSpawnPoint;
        [SerializeField] private Vector3 spawnPositionOffset;
        [SerializeField] private Vector3 spawnRotationOffset;
        [SerializeField] private bool parentProjectileToSpawnPoint;
        [SerializeField, Min(0f)] private float fireCooldown = 0.1f;
        [SerializeField] private bool fireOnRelease;
        [SerializeField] private bool alignSpawnRotation = true;
        [SerializeField] private bool inheritControllerVelocity = true;
        [SerializeField] private bool ignoreOwnerColliders = true;
        [SerializeField] private float projectileSpeedMultiplier = 1f;
        [SerializeField] private ProjectileConfiguration[] projectileConfigurations = Array.Empty<ProjectileConfiguration>();

        [Header("Events")]
        [SerializeField] private UnityEvent<Projectile> onProjectileFired;

        [Header("AimRenderer")] 
        [Min(0)] public float aimDistance;
        public LineRenderer aimLineRenderer;

        float nextFireTime;
        bool actionHeld;
        readonly List<Collider> cachedOwnerColliders = new List<Collider>();

        protected override bool UsesAimInput => true;

        protected override void Awake()
        {
            actionTrigger = HandleFireInput;
            base.Awake();
        }

        protected void Update()
        {
            UpdateAimLine();
        }

        public bool TryFireConfiguredProjectile(string configurationId = null, Vector3? directionOverride = null,
            IDamager sourceOverride = null, float? effectMagnitudeOverride = null)
        {
            ProjectileConfiguration configuration = ResolveConfiguration(configurationId);
            if (configuration == null)
            {
                Debug.LogWarning($"{nameof(ProjectileCharacterAimAction)} on {name} could not resolve configuration '{configurationId ?? "default"}'.", this);
                return false;
            }

            Vector3? normalizedDirection = directionOverride;
            if (normalizedDirection.HasValue && normalizedDirection.Value.sqrMagnitude > 0.0001f)
            {
                normalizedDirection = normalizedDirection.Value.normalized;
            }

            return TryFire(configuration, GetCurrentAimWorldPosition(), normalizedDirection, sourceOverride,
                effectMagnitudeOverride);
        }

        void HandleFireInput(bool pressed)
        {
            if (pressed)
            {
                actionHeld = true;
                if (!fireOnRelease)
                {
                    TryFire(ResolveConfiguration(null), GetCurrentAimWorldPosition(), null, null, null);
                }
            }
            else
            {
                if (!actionHeld)
                {
                    return;
                }

                actionHeld = false;
                if (fireOnRelease)
                {
                    TryFire(ResolveConfiguration(null), GetCurrentAimWorldPosition(), null, null, null);
                }
            }
        }

        private void UpdateAimLine()
        {
            if (!aimLineRenderer)
                return;

            if (!Controller.inputReader.isAiming || aimDistance == 0f)
                aimLineRenderer.enabled = false;
            else
                aimLineRenderer.enabled = true;

            ProjectileConfiguration configuration = ResolveConfiguration(null);
            Vector3 origin = GetSpawnPosition(configuration);
            Vector3 target = GetCurrentAimWorldPosition();
            Vector3 direction = ResolveAimDirection(origin, target);

            aimLineRenderer.positionCount = 2;
            aimLineRenderer.SetPosition(0, origin);
            aimLineRenderer.SetPosition(1, origin + direction * aimDistance);
        }

        ProjectileConfiguration ResolveConfiguration(string configurationId)
        {
            if (!string.IsNullOrEmpty(configurationId))
            {
                foreach (ProjectileConfiguration configuration in projectileConfigurations)
                {
                    if (configuration != null && string.Equals(configuration.id, configurationId, StringComparison.OrdinalIgnoreCase))
                    {
                        return configuration;
                    }
                }
            }

            return BuildDefaultConfiguration();
        }

        ProjectileConfiguration BuildDefaultConfiguration()
        {
            return new ProjectileConfiguration
            {
                id = "default",
                projectilePrefab = projectilePrefab,
                projectileSpawnPoint = projectileSpawnPoint,
                spawnPositionOffset = spawnPositionOffset,
                spawnRotationOffset = spawnRotationOffset,
                parentProjectileToSpawnPoint = parentProjectileToSpawnPoint,
                alignSpawnRotation = alignSpawnRotation,
                inheritControllerVelocity = inheritControllerVelocity,
                ignoreOwnerColliders = ignoreOwnerColliders,
                projectileSpeedMultiplier = projectileSpeedMultiplier
            };
        }

        bool TryFire(ProjectileConfiguration configuration, Vector3 worldPosition, Vector3? directionOverride,
            IDamager sourceOverride, float? effectMagnitudeOverride)
        {
            if (configuration == null || !configuration.projectilePrefab)
            {
                Debug.LogWarning($"{nameof(ProjectileCharacterAimAction)} on {name} requires a projectile prefab.", this);
                return false;
            }

            if (Time.time < nextFireTime)
            {
                return false;
            }

            Vector3 origin = GetSpawnPosition(configuration);
            Vector3 direction = directionOverride ?? ResolveAimDirection(origin, worldPosition);

            if (direction.sqrMagnitude <= 0.0001f)
                return false;

            if (configuration.ignoreOwnerColliders)
            {
                CollectOwnerColliders();
            }
            else
            {
                cachedOwnerColliders.Clear();
            }

            Quaternion spawnRotation = GetSpawnRotation(configuration, direction);
            Transform parent = configuration.parentProjectileToSpawnPoint ? GetSpawnRoot(configuration) : null;
            Projectile projectileInstance = Instantiate(configuration.projectilePrefab, origin, spawnRotation, parent);

            if (!projectileInstance)
                return false;

            Projectile.LaunchData launchData = BuildLaunchData(direction, origin, configuration, sourceOverride,
                effectMagnitudeOverride);
            projectileInstance.Launch(launchData);

            onProjectileFired?.Invoke(projectileInstance);
            ExecuteConfiguredAction();

            nextFireTime = Time.time + fireCooldown;
            return true;
        }

        Vector3 GetCurrentAimWorldPosition()
        {
            if (InputReader != null)
            {
                Vector2 aimVector = InputReader.aimDirection;
                if (aimVector.sqrMagnitude > 0.0001f)
                {
                    Vector3 aimWorld = new Vector3(aimVector.x, 0f, aimVector.y);
                    return GetAimOrigin() + aimWorld;
                }
            }

            return ResolveAimWorldPosition(Vector3.zero, null);
        }

        Transform GetSpawnRoot(ProjectileConfiguration configuration)
        {
            if (configuration?.projectileSpawnPoint)
            {
                return configuration.projectileSpawnPoint;
            }

            if (projectileSpawnPoint)
            {
                return projectileSpawnPoint;
            }

            return Controller ? Controller.transform : transform;
        }

        Vector3 GetSpawnPosition(ProjectileConfiguration configuration)
        {
            Transform root = GetSpawnRoot(configuration);
            Vector3 offset = configuration?.spawnPositionOffset ?? spawnPositionOffset;
            return root ? root.TransformPoint(offset) : offset;
        }

        Vector3 ResolveAimDirection(Vector3 origin, Vector3 worldPosition)
        {
            Vector3 direction = worldPosition - origin;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f && Controller)
                direction = Controller.AimDirection;

            return direction.normalized;
        }

        Quaternion GetSpawnRotation(ProjectileConfiguration configuration, Vector3 direction)
        {
            Transform root = GetSpawnRoot(configuration);
            Quaternion baseRotation = root ? root.rotation : Quaternion.identity;

            if (configuration.alignSpawnRotation)
            {
                Vector3 planar = new Vector3(direction.x, 0f, direction.z);
                if (planar.sqrMagnitude > 0.0001f)
                {
                    baseRotation = Quaternion.LookRotation(planar, Vector3.up);
                }
            }

            Vector3 rotationOffset = configuration.spawnRotationOffset;
            if (rotationOffset != Vector3.zero)
            {
                baseRotation *= Quaternion.Euler(rotationOffset);
            }

            return baseRotation;
        }

        Projectile.LaunchData BuildLaunchData(Vector3 direction, Vector3 origin, ProjectileConfiguration configuration,
            IDamager sourceOverride, float? effectMagnitudeOverride)
        {
            Rigidbody ownerBody = Controller ? Controller.GetComponent<Rigidbody>() : GetComponent<Rigidbody>();
            Vector3 inheritedVelocity = configuration.inheritControllerVelocity && ownerBody ? ownerBody.linearVelocity : Vector3.zero;
            // inherited velocity should only contribute to speed if the direction aligns with the aim direction
            float actionMagnitude = Vector3.Dot(inheritedVelocity.normalized, direction);
            if (actionMagnitude < 1f)
                actionMagnitude = 1f;

            return new Projectile.LaunchData
            {
                Origin = origin,
                Direction = direction,
                SpeedMultiplier = configuration.projectileSpeedMultiplier * (actionMagnitude <= 0f ? 1f : actionMagnitude),
                Owner = Controller ? Controller.transform : transform,
                IgnoredColliders = configuration.ignoreOwnerColliders && cachedOwnerColliders.Count > 0
                    ? cachedOwnerColliders.ToArray()
                    : null,
                InitialVelocity = inheritedVelocity,
                Damager = sourceOverride ?? Controller,
                EffectMagnitude = effectMagnitudeOverride ?? 1f
            };
        }

        void CollectOwnerColliders()
        {
            cachedOwnerColliders.Clear();
            AgentController controller = Controller;
            if (!controller)
                return;

            controller.GetComponentsInChildren(true, cachedOwnerColliders);

            if (cachedOwnerColliders.Count == 0)
            {
                Collider ownerCollider = controller.GetComponent<Collider>();
                if (ownerCollider)
                    cachedOwnerColliders.Add(ownerCollider);
            }
        }
    }
}

