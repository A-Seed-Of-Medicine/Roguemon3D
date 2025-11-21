using System;
using System.Collections.Generic;
using System.Threading;
using _PinBoy.Scripts.Animation;
using HSM;
using UnityEngine;
using UnityEngine.Serialization;

namespace _PinBoy.Scripts.CharacterMovement
{
    [Serializable]
    public struct AgentAnimationRequest : IEquatable<AgentAnimationRequest>
    {
        public enum DirectionMode
        {
            Single,
            FourWay,
            EightWay
        }

        public DirectionMode directionMode;
        public bool mirrorLeftRight;

        public AnimationClip singleClip;
        public AnimationClip northClip;
        public AnimationClip southClip;
        public AnimationClip eastClip;
        public AnimationClip westClip;
        public AnimationClip northEastClip;
        public AnimationClip southEastClip;
        public AnimationClip northWestClip;
        public AnimationClip southWestClip;

        [Min(0f)] public float crossFade;
        public float playbackSpeed;
        public bool overrideSpeed;

        public static AgentAnimationRequest None => new AgentAnimationRequest
        {
            directionMode = DirectionMode.Single,
            mirrorLeftRight = false,
            singleClip = null,
            northClip = null,
            southClip = null,
            eastClip = null,
            westClip = null,
            northEastClip = null,
            southEastClip = null,
            northWestClip = null,
            southWestClip = null,
            crossFade = 0f,
            playbackSpeed = 1f,
            overrideSpeed = false
        };

        public bool UsesDirectionalClips => directionMode != DirectionMode.Single;

        public bool IsValid
        {
            get
            {
                switch (directionMode)
                {
                    case DirectionMode.Single:
                        return ClipIsValid(singleClip);
                    case DirectionMode.FourWay:
                        return ClipIsValid(northClip) &&
                               ClipIsValid(southClip) &&
                               ClipIsValid(eastClip) &&
                               (mirrorLeftRight || ClipIsValid(westClip));
                    case DirectionMode.EightWay:
                        return ClipIsValid(northClip) &&
                               ClipIsValid(southClip) &&
                               ClipIsValid(eastClip) &&
                               ClipIsValid(northEastClip) &&
                               ClipIsValid(southEastClip) &&
                               (mirrorLeftRight || ClipIsValid(westClip)) &&
                               (mirrorLeftRight || ClipIsValid(northWestClip)) &&
                               (mirrorLeftRight || ClipIsValid(southWestClip));
                    default:
                        return false;
                }
            }
        }

        public AgentAnimationRequest Sanitized()
        {
            AgentAnimationRequest sanitized = this;
            sanitized.crossFade = Mathf.Max(0f, crossFade);
            sanitized.singleClip = SanitizeClip(singleClip);
            sanitized.northClip = SanitizeClip(northClip);
            sanitized.southClip = SanitizeClip(southClip);
            sanitized.eastClip = SanitizeClip(eastClip);
            sanitized.westClip = SanitizeClip(westClip);
            sanitized.northEastClip = SanitizeClip(northEastClip);
            sanitized.southEastClip = SanitizeClip(southEastClip);
            sanitized.northWestClip = SanitizeClip(northWestClip);
            sanitized.southWestClip = SanitizeClip(southWestClip);
            if (sanitized is { overrideSpeed: true, playbackSpeed: <= 0f })
            {
                sanitized.playbackSpeed = 1f;
            }

            return sanitized;
        }

        static AnimationClip SanitizeClip(AnimationClip clip)
        {
            return ClipIsValid(clip) ? clip : null;
        }

        static bool ClipIsValid(AnimationClip clip)
        {
            return clip;
        }

        public bool TryResolveClip(int directionIndex, out AnimationClip clip, SpriteAnimator spriteAnimator = null)
        {
            // Direction maps: 0 = South, 1 = South-East, 2 = East, 3 = North-East,
            //                 4 = North, 5 = North-West, 6 = West, 7 = South-West
            bool flipX = spriteAnimator && spriteAnimator.IsFlipped;
            clip = null;
            switch (directionMode)
            {
                case DirectionMode.Single:
                    clip = SanitizeClip(singleClip);
                    if (directionIndex is 5 or 6 or 7 && mirrorLeftRight)
                    {
                        flipX = true;
                    }
                    else if (!mirrorLeftRight || directionIndex is 1 or 2 or 3)
                    {
                        flipX = false;
                    }
                    
                    break;
                case DirectionMode.FourWay:
                    clip = ResolveFourWayClip(directionIndex, out flipX);
                    break;
                case DirectionMode.EightWay:
                    clip = ResolveEightWayClip(directionIndex, out flipX);
                    break;
            }

            if (!clip)
            {
                clip = SanitizeClip(singleClip);
            }
            
            if (spriteAnimator?.IsFlipped != flipX)
                spriteAnimator?.SetFlipX(flipX);
            
            return clip;
        }

        AnimationClip ResolveFourWayClip(int directionIndex, out bool flipX)
        {
            flipX = false;
            if (directionIndex < 0)
            {
                directionIndex = 4;
            }

            int cardinalIndex = ((directionIndex + 1) / 2) & 3;
            switch (cardinalIndex)
            {
                case 0:
                    return SanitizeClip(southClip);
                case 1:
                    return SanitizeClip(eastClip);
                case 2:
                    return SanitizeClip(northClip);
                case 3:
                    if (mirrorLeftRight)
                    {
                        flipX = true;
                        return SanitizeClip(eastClip);
                    }

                    return SanitizeClip(westClip);
                default:
                    return null;
            }
        }

        AnimationClip ResolveEightWayClip(int directionIndex, out bool flipX)
        {
            flipX = false;
            if (directionIndex < 0)
            {
                directionIndex = 4;
            }

            switch (directionIndex & 7)
            {
                case 0:
                    return SanitizeClip(southClip);
                case 1:
                    return SanitizeClip(southEastClip);
                case 2:
                    return SanitizeClip(eastClip);
                case 3:
                    return SanitizeClip(northEastClip);
                case 4:
                    return SanitizeClip(northClip);
                case 5:
                    if (mirrorLeftRight)
                    {
                        flipX = true;
                        return SanitizeClip(northEastClip);
                    }

                    return SanitizeClip(northWestClip);
                case 6:
                    if (mirrorLeftRight)
                    {
                        flipX = true;
                        return SanitizeClip(eastClip);
                    }

                    return SanitizeClip(westClip);
                case 7:
                    if (mirrorLeftRight)
                    {
                        flipX = true;
                        return SanitizeClip(southEastClip);
                    }

                    return SanitizeClip(southWestClip);
                default:
                    return null;
            }
        }

        public bool Equals(AgentAnimationRequest other)
        {
            return directionMode == other.directionMode &&
                   mirrorLeftRight == other.mirrorLeftRight &&
                   ReferenceEquals(singleClip, other.singleClip) &&
                   ReferenceEquals(northClip, other.northClip) &&
                   ReferenceEquals(southClip, other.southClip) &&
                   ReferenceEquals(eastClip, other.eastClip) &&
                   ReferenceEquals(westClip, other.westClip) &&
                   ReferenceEquals(northEastClip, other.northEastClip) &&
                   ReferenceEquals(southEastClip, other.southEastClip) &&
                   ReferenceEquals(northWestClip, other.northWestClip) &&
                   ReferenceEquals(southWestClip, other.southWestClip) &&
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
                int hash = (int)directionMode;
                hash = (hash * 397) ^ (mirrorLeftRight ? 1 : 0);
                hash = (hash * 397) ^ (singleClip != null ? singleClip.GetHashCode() : 0);
                hash = (hash * 397) ^ (northClip != null ? northClip.GetHashCode() : 0);
                hash = (hash * 397) ^ (southClip != null ? southClip.GetHashCode() : 0);
                hash = (hash * 397) ^ (eastClip != null ? eastClip.GetHashCode() : 0);
                hash = (hash * 397) ^ (westClip != null ? westClip.GetHashCode() : 0);
                hash = (hash * 397) ^ (northEastClip != null ? northEastClip.GetHashCode() : 0);
                hash = (hash * 397) ^ (southEastClip != null ? southEastClip.GetHashCode() : 0);
                hash = (hash * 397) ^ (northWestClip != null ? northWestClip.GetHashCode() : 0);
                hash = (hash * 397) ^ (southWestClip != null ? southWestClip.GetHashCode() : 0);
                hash = (hash * 397) ^ Mathf.RoundToInt(Mathf.Max(0f, crossFade) * 1000f);
                hash = (hash * 397) ^ Mathf.RoundToInt(playbackSpeed * 1000f);
                hash = (hash * 397) ^ (overrideSpeed ? 1 : 0);
                return hash;
            }
        }
    }

    public sealed class AgentAnimationController
    {
        readonly Dictionary<AgentState, AgentAnimationRequest> requests = new();
        SpriteAnimator spriteAnimator;
        float defaultSpeed = 1f;
        AgentState currentOwner;
        AgentAnimationRequest currentRequest = AgentAnimationRequest.None;
        Vector2 cachedInput;
        Vector3 cachedFacing;
        public int currentDirectionIndex { get; private set; } = -1;

        public void Initialize(SpriteAnimator targetAnimator)
        {
            spriteAnimator = targetAnimator;
            defaultSpeed = spriteAnimator ? spriteAnimator.SpeedMultiplier : 1f;
            requests.Clear();
            currentOwner = null;
            currentRequest = AgentAnimationRequest.None;
            cachedInput = Vector2.zero;
            cachedFacing = Vector3.forward;
            currentDirectionIndex = ComputeDirectionIndex(cachedInput, cachedFacing);
        }

        public void Register(AgentState owner, AgentAnimationRequest request)
        {
            if (owner == null)
                return;

            request = request.Sanitized();

            if (!request.IsValid)
            {
                Unregister(owner);
                return;
            }

            if (requests.TryGetValue(owner, out AgentAnimationRequest existing) && existing.Equals(request))
                return;

            requests[owner] = request;
            Evaluate();
        }

        public void Update(AgentState owner, AgentAnimationRequest request)
        {
            Register(owner, request);
        }

        public void Unregister(AgentState owner)
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
        
        public void Clear()
        {
            requests.Clear();
            RestoreDefaultSpeed();
            currentOwner = null;
            currentRequest = AgentAnimationRequest.None;
            spriteAnimator?.Stop();
        }

        void Evaluate()
        {
            AgentState bestOwner = null;
            AgentAnimationRequest bestRequest = AgentAnimationRequest.None;
            int bestDepth = -1;

            foreach ((AgentState owner, AgentAnimationRequest request) in requests)
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
                spriteAnimator?.Stop();
                return;
            }

            if (currentOwner == bestOwner && currentRequest.Equals(bestRequest) && spriteAnimator.IsPlaying())
            {
                return;
            }

            Apply(bestRequest);
            currentOwner = bestOwner;
            currentRequest = bestRequest;
        }

        void Apply(AgentAnimationRequest request, bool directionChanged = false)
        {
            if (!spriteAnimator)
                return;

            if (!request.IsValid)
            {
                RestoreDefaultSpeed();
                spriteAnimator.Stop();
                return;
            }

            if (request.overrideSpeed && request.playbackSpeed > 0f)
            {
                spriteAnimator.SetSpeed(request.playbackSpeed);
            }
            else
            {
                RestoreDefaultSpeed();
            }

            if (currentDirectionIndex < 0)
            {
                currentDirectionIndex = ComputeDirectionIndex(cachedInput, cachedFacing);
            }

            if (!request.TryResolveClip(currentDirectionIndex, out AnimationClip clip, spriteAnimator))
            {
                RestoreDefaultSpeed();
                spriteAnimator.Stop();
                return;
            }
            
            bool forceRestart = request.crossFade > 0f || directionChanged;
            spriteAnimator.SetClip(clip, 0f, forceRestart);
            if (!spriteAnimator.IsPlaying())
            {
                spriteAnimator.Play();
            }
        }

        public AnimationClip GetClip(AgentAnimationRequest request)
        {
            // Return animation clip based on the current direction index
            if (currentDirectionIndex < 0)
                currentDirectionIndex = ComputeDirectionIndex(cachedInput, cachedFacing);
            return request.TryResolveClip(currentDirectionIndex, out AnimationClip clip) ? clip : null;
        }

        void RestoreDefaultSpeed()
        {
            if (spriteAnimator)
            {
                spriteAnimator.SetSpeed(defaultSpeed);
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

        public void UpdateDirection(Vector2 input, Vector3 facing)
        {
            cachedInput = input;
            cachedFacing = facing;

            int previousIndex = currentDirectionIndex;
            int computed = ComputeDirectionIndex(input, facing);
            if (computed >= 0)
            {
                currentDirectionIndex = computed;
            }

            bool directionChanged = computed >= 0 && computed != previousIndex;
            if (directionChanged && currentRequest.IsValid && currentRequest.UsesDirectionalClips)
            {
                Apply(currentRequest, true);
            }
        }

        static int ComputeDirectionIndex(Vector2 input, Vector3 facing)
        {
            Vector3 direction = Vector3.zero;
            if (input.sqrMagnitude > 0.0001f)
            {
                direction = new Vector3(input.x, 0f, input.y);
            }
            else if (facing.sqrMagnitude > 0.0001f)
            {
                direction = new Vector3(facing.x, 0f, facing.z);
            }
            else
            {
                return -1;
            }

            Vector2 planar = new Vector2(direction.x, direction.z);
            if (planar.sqrMagnitude < 0.0001f)
            {
                return -1;
            }

            float angleDeg = Mathf.Atan2(planar.y, planar.x) * Mathf.Rad2Deg;
            angleDeg = Mathf.Repeat(angleDeg + 90f + 22.5f, 360f);
            Camera mainCam = Camera.main;
            // Offset by camera yaw
            if (mainCam != null)
            {
                angleDeg += mainCam.transform.eulerAngles.y;
                angleDeg = Mathf.Repeat(angleDeg, 360f);
            }
            return Mathf.FloorToInt(angleDeg / 45f) % 8;
        }
    }
}
