using System.Collections.Generic;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Effects;
using UnityEngine;
using UtilityAI;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    public sealed class AgentActionRuntime
    {
        public AgentActionRuntime(Context aiContext, CharacterAction action, IDamageable target, float magnitude)
            : this(aiContext, action, aiContext?.Controller, target, magnitude)
        {
        }

        public AgentActionRuntime(AgentController controller, CharacterAction action, IDamageable target, float magnitude)
            : this(null, action, controller, target, magnitude)
        {
        }

        AgentActionRuntime(Context aiContext, CharacterAction action, AgentController controller, IDamageable target, float magnitude)
        {
            AIContext = aiContext;
            Action = action;
            Controller = controller ? controller : aiContext?.Controller;
            Source = Controller;
            Target = target;
            Magnitude = Mathf.Max(0f, magnitude);
        }

        public Context AIContext { get; }
        public AgentController Controller { get; }
        public CharacterAction Action { get; }
        public ActionState ActionState => Action?.ActionState;
        public IDamager Source { get; }
        public IDamageable Target { get; }
        public float Magnitude { get; set; }

        public Vector3 SourcePosition => Source != null ? Source.transform.position : Vector3.zero;
        public Vector3 TargetPosition => Target != null ? Target.transform.position : SourcePosition;
        public Vector3 Direction
        {
            get
            {
                if (Target != null)
                {
                    Vector3 dir = TargetPosition - SourcePosition;
                    if (dir.sqrMagnitude > 0.0001f)
                    {
                        return dir.normalized;
                    }
                }

                if (Controller != null)
                {
                    Vector3 aim = Controller.AimDirection;
                    if (aim.sqrMagnitude > 0.0001f)
                    {
                        return aim.normalized;
                    }
                }

                return Vector3.forward;
            }
        }

        public EffectContext CreateEffectContext(float magnitude)
        {
            float finalMagnitude = Mathf.Max(0f, magnitude);
            return new EffectContext(AIContext, Source, Target, SourcePosition, TargetPosition, Direction, finalMagnitude);
        }

        public void ApplyEffects(List<Effect> effectEntries, float baseMagnitude)
        {
            if (effectEntries == null || effectEntries.Count == 0)
            {
                return;
            }

            float actionMagnitude = Mathf.Max(0f, Magnitude);
            float baseValue = Mathf.Max(0f, baseMagnitude);
            foreach (var effect in effectEntries)
            {
                if (effect == null)
                {
                    continue;
                }
                
                float finalMagnitude = actionMagnitude;
                finalMagnitude *= baseValue;
                EffectContext context = CreateEffectContext(finalMagnitude);
                effect.Apply(context);
            }
        }

        public void FaceTarget()
        {
            if (!Controller)
                return;
            if (Target == null)
                return;
            Vector3 direction = Target.transform.position - Controller.transform.position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                Controller.ForceFacing(direction);
            }
        }

        public void FaceDirection(Vector3 direction)
        {
            if (!Controller || direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Controller.ForceFacing(direction);
        }

        public void SpawnVfx(GameObject prefab, Transform anchor, Vector3 offset, bool parentToAnchor, float lifetime)
        {
            if (!prefab)
            {
                return;
            }
            
            Vector3 basePosition = anchor ? anchor.position : Source != null ? Source.transform.position : Vector3.zero;
            GameObject instance = UnityEngine.Object.Instantiate(prefab, basePosition + offset, Quaternion.identity);
            if (parentToAnchor && anchor)
            {
                instance.transform.SetParent(anchor);
            }

            if (lifetime > 0f)
            {
                Object.Destroy(instance, lifetime);
            }
        }
    }
}
