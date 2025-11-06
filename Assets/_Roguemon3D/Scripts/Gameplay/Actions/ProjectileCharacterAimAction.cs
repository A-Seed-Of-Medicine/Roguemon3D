using System;
using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
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
        [Header("Projectile")]
        [SerializeField] private Projectile projectilePrefab;
        [SerializeField] private Transform projectileSpawnPoint;
        [SerializeField, Min(0f)] private float fireCooldown = 0.1f;
        [SerializeField] private bool fireOnRelease;
        [SerializeField] private bool alignSpawnRotation = true;
        [SerializeField] private bool inheritControllerVelocity = true;
        [SerializeField] private bool ignoreOwnerColliders = true;
        [SerializeField] private float projectileSpeedMultiplier = 1f;

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

        void HandleFireInput(bool pressed)
        {
            if (pressed)
            {
                actionHeld = true;
                if (!fireOnRelease)
                {
                    TryFire(GetCurrentAimWorldPosition());
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
                    TryFire(GetCurrentAimWorldPosition());
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

            Vector3 origin = GetSpawnPosition();
            Vector3 target = GetCurrentAimWorldPosition();
            Vector3 direction = ResolveAimDirection(origin, target);

            aimLineRenderer.positionCount = 2;
            aimLineRenderer.SetPosition(0, origin);
            aimLineRenderer.SetPosition(1, origin + direction * aimDistance);
        }

        bool TryFire(Vector3 worldPosition)
        {
            if (!projectilePrefab)
            {
                Debug.LogWarning($"{nameof(ProjectileCharacterAimAction)} on {name} requires a projectile prefab.", this);
                return false;
            }

            if (Time.time < nextFireTime)
            {
                return false;
            }

            Vector3 origin = GetSpawnPosition();
            Vector3 direction = ResolveAimDirection(origin, worldPosition);

            if (direction.sqrMagnitude <= 0.0001f)
                return false;

            Projectile projectileInstance = Instantiate(projectilePrefab, origin, Quaternion.identity);

            if (!projectileInstance)
                return false;

            if (alignSpawnRotation)
            {
                Vector3 planar = new Vector3(direction.x, 0f, direction.z);
                if (planar.sqrMagnitude > 0.0001f)
                {
                    projectileInstance.transform.rotation = Quaternion.LookRotation(planar, Vector3.up);
                }
            }

            Projectile.LaunchData launchData = BuildLaunchData(direction, origin);
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

        Vector3 GetSpawnPosition()
        {
            if (projectileSpawnPoint)
            {
                return projectileSpawnPoint.position;
            }

            return Controller ? Controller.transform.position : transform.position;
        }

        Vector3 ResolveAimDirection(Vector3 origin, Vector3 worldPosition)
        {
            Vector3 direction = worldPosition - origin;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f && Controller)
                direction = Controller.AimDirection;

            return direction.normalized;
        }

        Projectile.LaunchData BuildLaunchData(Vector3 direction, Vector3 origin)
        {
            Rigidbody ownerBody = Controller ? Controller.GetComponent<Rigidbody>() : GetComponent<Rigidbody>();
            Vector3 inheritedVelocity = inheritControllerVelocity && ownerBody ? ownerBody.linearVelocity : Vector3.zero;
            // inherited velocity should only contribute to speed if the direction aligns with the aim direction
            float actionMagnitude = Vector3.Dot(inheritedVelocity.normalized, direction);
            if (actionMagnitude < 1f)
                actionMagnitude = 1f;



            if (ignoreOwnerColliders)
            {
                CollectOwnerColliders();
                Collider[] colliders = cachedOwnerColliders.Count > 0 ? cachedOwnerColliders.ToArray() : null;
                return new Projectile.LaunchData
                {
                    Origin = origin,
                    Direction = direction,
                    SpeedMultiplier = projectileSpeedMultiplier * (actionMagnitude <= 0f ? 1f : actionMagnitude),
                    Owner = Controller ? Controller.transform : transform,
                    IgnoredColliders = colliders,
                    InitialVelocity = inheritedVelocity
                };
            }

            return new Projectile.LaunchData
            {
                Origin = origin,
                Direction = direction,
                SpeedMultiplier = projectileSpeedMultiplier * (actionMagnitude <= 0f ? 1f : actionMagnitude),
                Owner = Controller ? Controller.transform : transform,
                IgnoredColliders = null,
                InitialVelocity = inheritedVelocity
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

