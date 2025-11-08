using System;
using System.Collections.Generic;
using HSM;
using UnityEngine;

namespace _PinBoy.Scripts.CharacterMovement
{
    [Serializable]
    public struct AgentAnimationRequest : IEquatable<AgentAnimationRequest>
    {
        public ArrayFlipbookAnimationClip clip;
        [Min(0f)] public float crossFade;
        public float playbackSpeed;
        public bool overrideSpeed;

        public static AgentAnimationRequest None => new AgentAnimationRequest
        {
            clip = null,
            crossFade = 0f,
            playbackSpeed = 1f,
            overrideSpeed = false
        };

        public bool IsValid => clip != null && clip.IsValid;

        public bool Equals(AgentAnimationRequest other)
        {
            return ReferenceEquals(clip, other.clip) &&
                   Mathf.Approximately(Mathf.Max(0f, crossFade), Mathf.Max(0f, other.crossFade)) &&
                   Mathf.Approximately(playbackSpeed, other.playbackSpeed) &&
                   overrideSpeed == other.overrideSpeed;
        }

        public override bool Equals(object obj)
        {
            return obj is AgentAnimationRequest other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = clip != null ? clip.GetHashCode() : 0;
                hash = (hash * 397) ^ Mathf.RoundToInt(Mathf.Max(0f, crossFade) * 1000f);
                hash = (hash * 397) ^ Mathf.RoundToInt(playbackSpeed * 1000f);
                hash = (hash * 397) ^ (overrideSpeed ? 1 : 0);
                return hash;
            }
        }
    }

    public sealed class AgentAnimationController
    {
        readonly Dictionary<State, AgentAnimationRequest> requests = new();
        ArrayFlipbookAVS flipbook;
        float defaultSpeed = 1f;
        State currentOwner;
        AgentAnimationRequest currentRequest = AgentAnimationRequest.None;

        public void Initialize(ArrayFlipbookAVS targetFlipbook)
        {
            flipbook = targetFlipbook;
            defaultSpeed = flipbook ? flipbook.speedMultiplier : 1f;
            requests.Clear();
            currentOwner = null;
            currentRequest = AgentAnimationRequest.None;
        }

        public void Register(State owner, AgentAnimationRequest request)
        {
            if (owner == null)
            {
                return;
            }

            if (!request.IsValid)
            {
                Unregister(owner);
                return;
            }

            if (requests.TryGetValue(owner, out AgentAnimationRequest existing) && existing.Equals(request))
            {
                return;
            }

            requests[owner] = request;
            Evaluate();
        }

        public void Update(State owner, AgentAnimationRequest request)
        {
            Register(owner, request);
        }

        public void Unregister(State owner)
        {
            if (owner == null)
            {
                return;
            }

            if (requests.Remove(owner))
            {
                Evaluate();
            }
        }

        void Evaluate()
        {
            State bestOwner = null;
            AgentAnimationRequest bestRequest = AgentAnimationRequest.None;
            int bestDepth = -1;

            foreach ((State owner, AgentAnimationRequest request) in requests)
            {
                if (owner == null)
                {
                    continue;
                }

                int depth = GetDepth(owner);
                if (depth > bestDepth)
                {
                    bestDepth = depth;
                    bestOwner = owner;
                    bestRequest = request;
                }
            }

            if (bestOwner == null)
            {
                RestoreDefaultSpeed();
                currentOwner = null;
                currentRequest = AgentAnimationRequest.None;
                flipbook?.Stop();
                return;
            }

            if (currentOwner == bestOwner && currentRequest.Equals(bestRequest))
            {
                return;
            }

            Apply(bestRequest);
            currentOwner = bestOwner;
            currentRequest = bestRequest;
        }

        void Apply(AgentAnimationRequest request)
        {
            if (!flipbook)
            {
                return;
            }

            if (!request.IsValid)
            {
                RestoreDefaultSpeed();
                flipbook.Stop();
                return;
            }

            if (request.overrideSpeed && request.playbackSpeed > 0f)
            {
                flipbook.SetSpeed(request.playbackSpeed);
            }
            else
            {
                RestoreDefaultSpeed();
            }

            bool forceRestart = request.crossFade > 0f;
            flipbook.SetClip(request.clip, 0f, forceRestart);
            if (!flipbook.IsPlaying())
            {
                flipbook.Play();
            }
        }

        void RestoreDefaultSpeed()
        {
            if (flipbook)
            {
                flipbook.SetSpeed(defaultSpeed);
            }
        }

        static int GetDepth(State state)
        {
            int depth = 0;
            for (State current = state; current != null; current = current.Parent)
            {
                depth++;
            }

            return depth;
        }
    }
}
