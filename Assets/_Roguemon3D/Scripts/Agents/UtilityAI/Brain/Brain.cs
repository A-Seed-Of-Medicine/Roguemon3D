using System;
using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using AdvancedController;
using UnityEngine;

namespace UtilityAI {
    public class Brain : MonoBehaviour {
        [SerializeField]
        [SerializeReference]
        public List<AIAction> actions = new List<AIAction>();
        public SphereCollider detectionCollider;
        [Min(0f)] public float detectionRadius = 10f;
        public AgentController controller;
        public Context context;

        [SerializeField]
        List<Transform> detectedTargets = new(10);
        readonly List<Transform> orderedTargetsBuffer = new(10);
        readonly HashSet<string> registeredTags = new();
        
        private AIAction currentAction;

        void Awake() {
            ConfigureCollider();

            context = new Context(this);

            registeredTags.Clear();

            foreach (var action in actions) {
                if (action == null) {
                    continue;
                }

                action.Initialize(context);
                Debug.Log($"Action {action.GetType().Name} initialized.");
            }

            RefreshDetectedTargets();
        }

        void OnEnable() {
            if (Application.isPlaying) {
                RefreshDetectedTargets();
            }
        }

        void OnValidate() {
            detectionRadius = Mathf.Max(0f, detectionRadius);
            if (!detectionCollider)
                detectionCollider = GetComponent<SphereCollider>();
            ConfigureCollider();
        }

        void OnDisable() {
            detectedTargets.Clear();
        }

        void Update() {
            if (context?.Controller) {
                context.Controller.InputRedirector = null;
            }

            context.target = null;

            AIAction bestAction = null;
            Transform bestTarget = null;
            float highestUtility = float.MinValue;
            var targets = GetPerceivedTargets();

            foreach (var action in actions) {
                if (action == null) {
                    continue;
                }

                context.ResetLastEvaluatedTarget();
                float utility = action.CalculateUtility(context, targets);
                Transform evaluatedTarget = context.LastEvaluatedTarget;

                if (utility > highestUtility) {
                    highestUtility = utility;
                    bestAction = action;
                    bestTarget = evaluatedTarget;
                }
            }

            context.target = bestTarget;
            // /Debug.Log($"Best Action: {(bestAction != null ? bestAction.GetType().Name : "None")} with Utility: {highestUtility:0.###}");
            if (bestAction != null) {
                if (currentAction != bestAction)
                {
                    currentAction?.OnExit(context);
                    currentAction = bestAction;
                }
                bestAction.Execute(context);
            }
        }

        void ConfigureCollider() {
            if (!detectionCollider) {
                return;
            }

            detectionCollider.isTrigger = true;
            detectionCollider.radius = detectionRadius;
        }

        void RefreshDetectedTargets() {
            detectedTargets.Clear();

            if (!Application.isPlaying || registeredTags.Count == 0) {
                return;
            }

            Collider[] colliders = Physics.OverlapSphere(transform.position, detectionRadius);
            foreach (var c in colliders) {
                TryAddDetectedTarget(c);
            }
        }

        void TryAddDetectedTarget(Collider other) {
            if (!other || !IsValidTarget(other)) {
                return;
            }

            Transform candidate = other.transform;
            if (candidate && !detectedTargets.Contains(candidate)) {
                detectedTargets.Add(candidate);
            }
        }

        void TryRemoveDetectedTarget(Collider other) {
            if (!other) {
                return;
            }

            detectedTargets.Remove(other.transform);
        }

        bool IsValidTarget(Collider other) {
            if (!other || other.CompareTag("Untagged")) {
                return false;
            }

            return registeredTags.Contains(other.tag);
        }

        void OnTriggerEnter(Collider other) {
            TryAddDetectedTarget(other);
        }

        void OnTriggerExit(Collider other) {
            TryRemoveDetectedTarget(other);
        }

        internal void RegisterTargetTag(string tag) {
            if (string.IsNullOrWhiteSpace(tag)) {
                return;
            }

            if (registeredTags.Add(tag) && Application.isPlaying && isActiveAndEnabled) {
                RefreshDetectedTargets();
            }
        }

        public IReadOnlyList<Transform> GetPerceivedTargets() {
            orderedTargetsBuffer.Clear();

            for (int i = detectedTargets.Count - 1; i >= 0; i--) {
                Transform target = detectedTargets[i];
                if (!target) {
                    detectedTargets.RemoveAt(i);
                    continue;
                }

                if (!registeredTags.Contains(target.tag)) {
                    detectedTargets.RemoveAt(i);
                    continue;
                }

                orderedTargetsBuffer.Add(target);
            }

            orderedTargetsBuffer.Sort((a, b) => {
                float distA = (a.position - transform.position).sqrMagnitude;
                float distB = (b.position - transform.position).sqrMagnitude;
                return distA.CompareTo(distB);
            });

            return orderedTargetsBuffer;
        }

        public Transform GetClosestTarget(string tag) {
            if (string.IsNullOrWhiteSpace(tag)) {
                return null;
            }

            Transform closestTarget = null;
            float closestDistanceSqr = Mathf.Infinity;
            Vector3 currentPosition = transform.position;

            for (int i = detectedTargets.Count - 1; i >= 0; i--) {
                Transform potentialTarget = detectedTargets[i];
                if (!potentialTarget) {
                    detectedTargets.RemoveAt(i);
                    continue;
                }

                if (!potentialTarget.CompareTag(tag)) {
                    continue;
                }

                Vector3 directionToTarget = potentialTarget.position - currentPosition;
                float dSqrToTarget = directionToTarget.sqrMagnitude;
                if (dSqrToTarget < closestDistanceSqr) {
                    closestDistanceSqr = dSqrToTarget;
                    closestTarget = potentialTarget;
                }
            }

            return closestTarget;
        }
    }
}
