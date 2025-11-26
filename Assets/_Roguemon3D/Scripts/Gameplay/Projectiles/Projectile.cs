using System.Collections.Generic;
using _PinBoy.Scripts.Gameplay.Effects;
using UnityEngine;
using UnityEngine.Events;

namespace _PinBoy.Scripts.Gameplay.Projectiles
{
    /// <summary>
    /// Generic top-down projectile behaviour for isometric games.
    /// Handles launch setup, motion, collision filtering and lifetime management.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Projectile : MonoBehaviour
    {
        [System.Serializable]
        public struct LaunchData
        {
            public Vector3 Origin;
            public Vector3 Direction;
            public Vector3 InitialVelocity;
            public float SpeedMultiplier;
            public Transform Owner;
            public Collider[] IgnoredColliders;
            public IDamager Damager;
            public IDamageable Target;
            public float Magnitude;
        }

        [Header("Motion")]
        [SerializeField, Min(0f)] private float baseSpeed = 12f;
        [SerializeField] private float acceleration;
        [SerializeField, Min(0f)] private float maxLifetime = 5f;
        [SerializeField, Min(0f)] private float maxTravelDistance;
        [SerializeField] private bool alignToVelocity = true;
        [SerializeField] private bool maintainVelocityEveryFrame = true;

        [Header("Impact")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private bool destroyOnImpact = true;
        [SerializeField] private GameObject impactPrefab;
        [SerializeField, Min(0f)] private float impactPrefabLifetime = 2f;

        [Header("Events")]
        [SerializeField] private UnityEvent<Collider> onHit;
        [SerializeField] private UnityEvent onExpired;

        Rigidbody body;
        Collider projectileCollider;
        readonly HashSet<Collider> ignoredColliders = new HashSet<Collider>();

        Vector3 launchDirection = Vector3.forward;
        Vector3 inheritedVelocity;
        float currentSpeed;
        float lifetime;
        Vector3 launchPosition;
        protected bool launched;
        protected Transform owner;
        protected IDamager damager;
        protected IDamageable target;
        protected float launchMagnitude = 1f;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            projectileCollider = GetComponent<Collider>();
        }

        void OnEnable()
        {
            lifetime = 0f;
            launched = false;
        }

        void OnDisable()
        {
            RestoreIgnoredCollisions();
        }

        public void Launch(LaunchData data)
        {
            if (data.Direction.sqrMagnitude <= 0.0001f)
            {
                Debug.LogWarning($"{nameof(Projectile)} launched with invalid direction.", this);
                return;
            }

            launchDirection = data.Direction.normalized;
            inheritedVelocity = data.InitialVelocity;
            currentSpeed = Mathf.Max(0f, baseSpeed * (data.SpeedMultiplier <= 0f ? 1f : data.SpeedMultiplier));
            launchPosition = data.Origin;
            owner = data.Owner;
            damager = data.Damager;
            target = data.Target;
            launchMagnitude = data.Magnitude <= 0f ? 1f : data.Magnitude;

            transform.position = data.Origin;
            lifetime = 0f;
            launched = true;

            ignoredColliders.Clear();
            if (data.IgnoredColliders != null)
            {
                for (int i = 0; i < data.IgnoredColliders.Length; i++)
                {
                    Collider col = data.IgnoredColliders[i];
                    if (!col)
                    {
                        continue;
                    }

                    ignoredColliders.Add(col);
                    if (projectileCollider)
                    {
                        Physics.IgnoreCollision(projectileCollider, col, true);
                    }
                }
            }

            ApplyVelocity();
        }

        void Update()
        {
            if (!launched)
            {
                return;
            }

            lifetime += Time.deltaTime;
            if (maxLifetime > 0f && lifetime >= maxLifetime)
            {
                Expire();
                return;
            }

            if (maxTravelDistance > 0f)
            {
                float traveled = Vector3.Distance(launchPosition, transform.position);
                if (traveled >= maxTravelDistance)
                {
                    Expire();
                    return;
                }
            }
        }

        void FixedUpdate()
        {
            if (!launched)
            {
                return;
            }

            bool velocityChanged = false;
            if (Mathf.Abs(acceleration) > 0.0001f)
            {
                currentSpeed = Mathf.Max(0f, currentSpeed + acceleration * Time.fixedDeltaTime);
                velocityChanged = true;
            }

            if (maintainVelocityEveryFrame || velocityChanged)
            {
                ApplyVelocity();
            }

            if (alignToVelocity)
            {
                Vector3 velocity = body.linearVelocity;
                Vector3 planar = new Vector3(velocity.x, 0f, velocity.z);
                if (planar.sqrMagnitude > 0.0001f)
                {
                    Quaternion lookRotation = Quaternion.LookRotation(planar, Vector3.up);
                    body.MoveRotation(lookRotation);
                }
            }
        }

        void ApplyVelocity()
        {
            Vector3 velocity = launchDirection * currentSpeed + inheritedVelocity;
            body.linearVelocity = velocity;
        }

        void Expire()
        {
            onExpired?.Invoke();
            if (destroyOnImpact)
            {
                Destroy(gameObject);
            }
            else
            {
                launched = false;
            }
        }

        void RestoreIgnoredCollisions()
        {
            if (!projectileCollider || ignoredColliders.Count == 0)
            {
                return;
            }

            foreach (Collider col in ignoredColliders)
            {
                if (!col)
                {
                    continue;
                }

                Physics.IgnoreCollision(projectileCollider, col, false);
            }

            ignoredColliders.Clear();
        }

        protected virtual void HandleHit(Collider other, Vector3? hitPoint = null)
        {
            if (!launched)
            {
                return;
            }

            if (!IsValidTarget(other))
            {
                return;
            }

            onHit?.Invoke(other);

            if (impactPrefab)
            {
                Vector3 spawnPosition = hitPoint ?? transform.position;
                GameObject impactInstance = Instantiate(impactPrefab, spawnPosition, transform.rotation);
                if (impactPrefabLifetime > 0f)
                {
                    Destroy(impactInstance, impactPrefabLifetime);
                }
            }

            if (destroyOnImpact)
            {
                Destroy(gameObject);
            }
        }

        protected virtual bool IsValidTarget(Collider other)
        {
            if (!other || ignoredColliders.Contains(other))
            {
                return false;
            }

            if (owner && other.transform.IsChildOf(owner))
            {
                return false;
            }

            if (((1 << other.gameObject.layer) & collisionMask) == 0)
            {
                return false;
            }

            return true;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision.collider)
            {
                Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : collision.collider.bounds.ClosestPoint(transform.position);
                HandleHit(collision.collider, point);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            Vector3 point = other.bounds.ClosestPoint(transform.position);
            HandleHit(other, point);
        }

        protected Vector3 LaunchDirection => launchDirection;
        protected Vector3 LaunchOrigin => launchPosition;
        protected float LaunchMagnitude => launchMagnitude;
        protected IDamager LaunchDamager => damager;
        protected IDamageable LaunchTarget => target;
    }
}

