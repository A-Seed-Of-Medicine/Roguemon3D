using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Effects;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Actions
{
    [CreateAssetMenu(menuName = "TopDown/Actions/Agent Action", fileName = "AgentAction")]
    public sealed class AgentActionDefinition : ScriptableObject
    {
        public enum VfxAnchor
        {
            Source,
            Target
        }

        public enum VfxTiming
        {
            OnStart,
            OnEffect,
            OnEnd
        }

        [SerializeField] private string actionName;
        [SerializeField] private float duration = 0.5f;
        [SerializeField] private float effectDelay = 0.1f;
        [SerializeField] private bool lockMovement = true;
        [SerializeField] private float movementLockDuration = -1f;
        [SerializeField] private bool zeroVelocityOnLock = true;
        [SerializeField] private bool faceTargetOnStart = true;
        [SerializeField] private bool faceAimDirectionWhenNoTarget = true;
        [SerializeField] private AnimationClip animationClip;
        [SerializeField] private float animationCrossFade = 0.05f;
        [SerializeField] private float animationPlaybackSpeed = 1f;
        [SerializeReference] [SerializeField] private List<Effect> effects = new();
        [SerializeField] private float baseMagnitude = 1f;
        [Header("VFX")] [SerializeField] private GameObject vfxPrefab;
        [SerializeField] private VfxAnchor vfxAnchor = VfxAnchor.Target;
        [SerializeField] private VfxTiming vfxTiming = VfxTiming.OnEffect;
        [SerializeField] private bool parentVfxToAnchor = false;
        [SerializeField] private Vector3 vfxOffset = Vector3.zero;
        [SerializeField] private float vfxLifetime = 1.5f;

        public string ActionName => actionName;
        public float Duration => Mathf.Max(0f, duration);

        public async UniTask ExecuteAsync(AgentActionRuntime runtime, CancellationToken cancellationToken = default)
        {
            if (runtime == null)
            {
                return;
            }

            var controller = runtime.Controller;
            if (!controller)
            {
                return;
            }

            float lockTime = movementLockDuration > 0f ? movementLockDuration : Duration;
            if (lockMovement && lockTime > 0f)
            {
                controller.LockMovement(lockTime, zeroVelocityOnLock);
            }

            if (faceTargetOnStart)
            {
                runtime.FaceTarget();
            }
            else if (faceAimDirectionWhenNoTarget)
            {
                runtime.FaceDirection(runtime.Direction);
            }

            float originalAnimatorSpeed = controller.AnimatorSpeed;
            bool restoreAnimatorSpeed = false;
            if (animationClip)
            {
                float playbackSpeed = animationPlaybackSpeed > 0f ? animationPlaybackSpeed : 1f;

                float crossFade = Mathf.Max(0f, animationCrossFade);

                if (runtime.Action != null)
                {
                    runtime.Action.ApplyAnimationRequest(new AgentAnimationRequest
                    {
                        clip = animationClip,
                        crossFade = crossFade,
                        playbackSpeed = playbackSpeed,
                        overrideSpeed = animationPlaybackSpeed > 0f
                    });
                }
                else
                {
                    if (animationPlaybackSpeed > 0f)
                    {
                        controller.AnimatorSpeed = animationPlaybackSpeed;
                        restoreAnimatorSpeed = true;
                    }

                    controller.PlayActionAnimation(animationClip, crossFade);
                }
            }

            try
            {
                Transform vfxAnchor = this.vfxAnchor == VfxAnchor.Source ? runtime.Source?.transform : runtime.Target?.transform;
                
                if (vfxPrefab && vfxTiming == VfxTiming.OnStart)
                {
                    runtime.SpawnVfx(vfxPrefab, vfxAnchor, vfxOffset, parentVfxToAnchor, vfxLifetime);
                }

                float clampedDelay = Mathf.Max(0f, effectDelay);
                if (clampedDelay > 0f)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(clampedDelay), cancellationToken: cancellationToken);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (vfxPrefab && vfxTiming == VfxTiming.OnEffect)
                {
                    runtime.SpawnVfx(vfxPrefab, vfxAnchor, vfxOffset, parentVfxToAnchor, vfxLifetime);
                }

                runtime.ApplyEffects(effects, baseMagnitude);

                float totalDuration = Mathf.Max(Duration, clampedDelay);
                float remaining = Mathf.Max(0f, totalDuration - clampedDelay);
                if (remaining > 0f)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(remaining), cancellationToken: cancellationToken);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (vfxPrefab && vfxTiming == VfxTiming.OnEnd)
                {
                    runtime.SpawnVfx(vfxPrefab, vfxAnchor, vfxOffset, parentVfxToAnchor, vfxLifetime);
                }
            }
            finally
            {
                if (restoreAnimatorSpeed && animationClip != null)
                {
                    controller.AnimatorSpeed = originalAnimatorSpeed;
                }
            }
        }
    }
}
