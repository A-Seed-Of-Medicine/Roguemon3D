using System;
using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using AdvancedController;
using NUnit.Framework;
using UnityEngine;
using UtilityAI;
using UnityUtils;

namespace UtilityAI {
    public class Brain : MonoBehaviour {
        public readonly struct ActionEvaluation {
            public ActionEvaluation(AIAction action, float utility, Transform evaluatedTarget, int targetsInRange, bool isCurrentBest, float time) {
                Action = action;
                Utility = utility;
                EvaluatedTarget = evaluatedTarget;
                TargetsInRange = targetsInRange;
                IsCurrentBest = isCurrentBest;
                Time = time;
            }

            public AIAction Action { get; }
            public float Utility { get; }
            public Transform EvaluatedTarget { get; }
            public int TargetsInRange { get; }
            public bool IsCurrentBest { get; }
            public float Time { get; }
        }

        [SerializeField]
        [SerializeReference]
        public List<AIAction> actions = new List<AIAction>();
        public LayerMask detectionLayerMask = Physics.DefaultRaycastLayers;
        public SphereCollider detectionCollider;
        [Min(0f)] public float detectionRadius = 10f;
        public float tickCooldown = 0.2f;
        public AgentController controller;
        public Context context;
        public event Action<ActionEvaluation> ActionEvaluated;
        private float tickCount;

        [SerializeField]
        List<TargetContext> detectedTargets = new(10);
        readonly List<TargetContext> orderedTargetsBuffer = new(10);
        
        private AIAction currentAction;

        void Awake() {
            ConfigureCollider();

            context = new Context(this);

            foreach (var action in actions) {
                if (action == null) {
                    continue;
                }

                action.Initialize(context);
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
            tickCount += Time.deltaTime;
            if (tickCount < tickCooldown) 
                return;
            if (context?.Controller) {
                context.Controller.InputRedirector = null;
            }

            context.target = null;

            AIAction bestAction = null;
            Transform bestTarget = null;
            float highestUtility = 0f;
            var targets = GetPerceivedTargets();

            foreach (var action in actions) {
                if (action == null) {
                    continue;
                }

                context.ResetLastEvaluatedTarget();
                float utility = action.CalculateUtility(context, targets);
                Transform evaluatedTarget = context.LastEvaluatedTarget;
                bool isCurrentBest = utility > highestUtility;
                ActionEvaluated?.Invoke(new ActionEvaluation(action, utility, evaluatedTarget, targets?.Count ?? 0, isCurrentBest, Time.time));

                if (isCurrentBest) {
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

            if (!Application.isPlaying) {
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
            TargetContext targetContext = new TargetContext {
                transform = candidate,
                agentController = other.GetComponent<AgentController>()
            };
            
            if (candidate && !detectedTargets.Contains(targetContext)) {
                detectedTargets.Add(targetContext);
            }
        }

        void TryRemoveDetectedTarget(Collider other) {
            if (!other) {
                return;
            }

            detectedTargets.Remove(other.transform);
        }

        bool IsValidTarget(Collider other) {
            if (!other || other.CompareTag("Untagged") || other.gameObject == controller.gameObject) {
                return false;
            }
            // Check that layer is in the detectionLayerMask
            if (!detectionLayerMask.Contains(other.gameObject.layer)) {
                return false;
            }

            return true;
        }
        
        bool IsValidTarget(TargetContext other) {
            if (!other.transform || other.transform.CompareTag("Untagged") || other.transform.gameObject == controller.gameObject)
                return false;
            if (!other.transform.gameObject.activeInHierarchy) 
                return false;
            if (!detectionLayerMask.Contains(other.transform.gameObject.layer)) 
                return false;

            return true;
        }
        
        

        void OnTriggerEnter(Collider other) {
            TryAddDetectedTarget(other);
        }

        void OnTriggerExit(Collider other) {
            TryRemoveDetectedTarget(other);
        }

        public IReadOnlyList<TargetContext> GetPerceivedTargets() {
            orderedTargetsBuffer.Clear();

            for (int i = detectedTargets.Count - 1; i >= 0; i--) {
                TargetContext target = detectedTargets[i];
                if (target == null) {
                    detectedTargets.RemoveAt(i);
                    continue;
                }
                
                if (!IsValidTarget(target)) {
                    detectedTargets.RemoveAt(i);
                    continue;
                }

                orderedTargetsBuffer.Add(target);
            }

            orderedTargetsBuffer.Sort((a, b) => {
                float distA = (a.transform.position - transform.position).sqrMagnitude;
                float distB = (b.transform.position - transform.position).sqrMagnitude;
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
                TargetContext potentialTarget = detectedTargets[i];
                if (potentialTarget == null) {
                    detectedTargets.RemoveAt(i);
                    continue;
                }

                if (!potentialTarget.transform.CompareTag(tag)) {
                    continue;
                }

                Vector3 directionToTarget = potentialTarget.transform.position - currentPosition;
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
