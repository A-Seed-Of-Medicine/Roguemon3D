using System;
using UnityEngine;
using System.Collections.Generic;
using _PinBoy.Scripts.Utils;

namespace _PinBoy.Scripts.CharacterMovement
{
    [CreateAssetMenu(menuName="TopDown/MovementProfile")]
    public sealed class MovementProfile : ScriptableObject
    {
        [Serializable]
        public struct AttributeModifier
        {
            public enum ModifierType { Add, Multiply, Override }
            public float value;
            public float decayDuration;
            public ModifierType type;
            public AttributeModifier(float value, float decayDuration = 0, ModifierType type = ModifierType.Override)
            {
                this.value = value;
                this.type = type;
                this.decayDuration = decayDuration;
            }

            public static implicit operator float(AttributeModifier am) => am.value;

            float ApplyWithoutDecay(float baseValue)
            {
                return type switch
                {
                    ModifierType.Add => baseValue + value,
                    ModifierType.Multiply => baseValue * value,
                    ModifierType.Override => value,
                    _ => baseValue
                };
            }

            public float ApplyTo(float baseValue, bool isDecaying, CountdownTimer decayTimer)
            {
                if (!isDecaying)
                {
                    return ApplyWithoutDecay(baseValue);
                }

                if (decayDuration <= 0f || decayTimer == null)
                    return baseValue;

                float elapsed = decayTimer.TimePassed;
                if (elapsed <= 0f)
                    return ApplyWithoutDecay(baseValue);

                if (elapsed >= decayDuration)
                    return baseValue;

                float t = Mathf.Clamp01(elapsed / decayDuration);
                return type switch
                {
                    ModifierType.Add => baseValue + Mathf.Lerp(value, 0f, t),
                    ModifierType.Multiply => baseValue * Mathf.Lerp(value, 1f, t),
                    ModifierType.Override => Mathf.Lerp(value, baseValue, t),
                    _ => baseValue
                };
            }
        }
        
        public int executionOrder = 0; // for sorting in editor
        public bool cumulative = true; // if true, multiple instances can be applied at once
        public AttributeModifier moveSpeed = new (4.5f);
        public AttributeModifier acceleration = new(30f);
        public AttributeModifier turnAcceleration = new(60f); // if > 0, used when changing direction
        public AttributeModifier deceleration = new(40f);
        [Tooltip("Deceleration applied when input is pressed but the character exceeds the desired speed.")]
        public AttributeModifier inputDeceleration = new(40f);
        public AttributeModifier maxSpeedMultiplier = new(1f); // optional cap
    }
    
    
    [Serializable]
    public class MovementParams
    {
        private class Profile
        {
            internal enum AttributeSlot
            {
                MoveSpeed,
                Acceleration,
                TurnAcceleration,
                Deceleration,
                InputDeceleration,
                MaxSpeedMultiplier,
                Count
            }

            // Set equals operator for easy removal
            public static bool operator ==(Profile a, MovementProfile b) => a?.profile == b;
            public static bool operator !=(Profile a, MovementProfile b) => a?.profile != b;
            public override bool Equals(object obj)
            {
                if (obj is MovementProfile mp)
                    return profile == mp;
                return false;
            }
            public override int GetHashCode() => profile.GetHashCode();
            
            public Profile(MovementProfile profile)
            {
                this.profile = profile;
                decayTimer = new CountdownTimer(0f);
                ResetDecay();
            }

            public MovementProfile profile;
            readonly CountdownTimer decayTimer;
            bool isDecaying;
            bool hasExpired;

            public bool HasExpired => hasExpired;

            public void ResetDecay()
            {
                isDecaying = false;
                hasExpired = false;
                decayTimer.Cancel();
            }

            public void TriggerDecay()
            {
                float longestDecay = GetLongestDecayDuration();
                if (longestDecay <= 0f)
                {
                    isDecaying = false;
                    hasExpired = true;
                    decayTimer.Cancel();
                    return;
                }

                isDecaying = true;
                hasExpired = false;
                decayTimer.Start(longestDecay);
            }

            public void UpdateDecay()
            {
                if (!isDecaying)
                    return;

                decayTimer.Tick();

                if (decayTimer.IsFinished)
                {
                    isDecaying = false;
                    hasExpired = true;
                }
            }

            float GetLongestDecayDuration()
            {
                float longest = 0f;
                foreach (AttributeSlot slot in Enum.GetValues(typeof(AttributeSlot)))
                {
                    MovementProfile.AttributeModifier modifier = GetModifier(slot);
                    if (modifier.decayDuration > longest)
                        longest = modifier.decayDuration;
                }

                return longest;
            }
            
            MovementProfile.AttributeModifier GetModifier(AttributeSlot slot)
            {
                return slot switch
                {
                    AttributeSlot.MoveSpeed => profile.moveSpeed,
                    AttributeSlot.Acceleration => profile.acceleration,
                    AttributeSlot.TurnAcceleration => profile.turnAcceleration,
                    AttributeSlot.Deceleration => profile.deceleration,
                    AttributeSlot.InputDeceleration => profile.inputDeceleration,
                    AttributeSlot.MaxSpeedMultiplier => profile.maxSpeedMultiplier,
                    _ => profile.moveSpeed
                };
            }

            public float Apply(float value, MovementProfile.AttributeModifier modifier, AttributeSlot slot)
            {
                return modifier.ApplyTo(value, isDecaying, decayTimer);
            }

            public float ApplyMoveSpeed(float value) => Apply(value, profile.moveSpeed, AttributeSlot.MoveSpeed);
            public float ApplyAcceleration(float value) => Apply(value, profile.acceleration, AttributeSlot.Acceleration);
            public float ApplyTurnAcceleration(float value) => Apply(value, profile.turnAcceleration, AttributeSlot.TurnAcceleration);
            public float ApplyDeceleration(float value) => Apply(value, profile.deceleration, AttributeSlot.Deceleration);
            public float ApplyInputDeceleration(float value) => Apply(value, profile.inputDeceleration, AttributeSlot.InputDeceleration);
            public float ApplyMaxSpeedMultiplier(float value) => Apply(value, profile.maxSpeedMultiplier, AttributeSlot.MaxSpeedMultiplier);
        }
        
        public MovementProfile baseProfile;
        public float moveSpeed, acceleration, turnAcceleration, deceleration, inputDeceleration, maxSpeedMult;
        private List<Profile> _overrideProfiles;
        
        public MovementParams(MovementProfile baseProfile)
        {
            this.baseProfile = baseProfile;
            moveSpeed = baseProfile.moveSpeed;
            acceleration = baseProfile.acceleration;
            turnAcceleration = baseProfile.turnAcceleration;
            deceleration = baseProfile.deceleration;
            inputDeceleration = baseProfile.inputDeceleration;
            maxSpeedMult = baseProfile.maxSpeedMultiplier;
            _overrideProfiles = new List<Profile>();
        }
        
        public void AddOverride(MovementProfile profile)
        {
            // Add profile in sorted order
            _overrideProfiles ??= new List<Profile>();
            foreach (Profile p in _overrideProfiles)
            {
                if (p != profile) continue;
                p.ResetDecay();
                return; // already exists, just refresh decay
            }

            int i = 0;
            while (i < _overrideProfiles.Count && _overrideProfiles[i].profile.executionOrder <= profile.executionOrder)
                i++;
            _overrideProfiles.Insert(i, new Profile(profile));
        }

        public void RemoveOverride(MovementProfile profile)
        {
            if (_overrideProfiles == null)
                return;

            foreach (Profile p in _overrideProfiles)
            {
                if (p != profile) continue;
                p.TriggerDecay();
                break;
            }
        }

        public MovementParams WithOverrides()
        {
            moveSpeed = baseProfile.moveSpeed;
            acceleration = baseProfile.acceleration;
            turnAcceleration = baseProfile.turnAcceleration;
            deceleration = baseProfile.deceleration;
            inputDeceleration = baseProfile.inputDeceleration;
            maxSpeedMult = baseProfile.maxSpeedMultiplier;
            if (_overrideProfiles == null || _overrideProfiles.Count == 0)
                return this;

            bool overrideApplied = false;
            
            for (int i = 0 ; i < _overrideProfiles.Count; i++)
            {
                Profile profile = _overrideProfiles[i];

                if (profile.HasExpired)
                {
                    _overrideProfiles.RemoveAt(i);
                    continue;
                }
                if (overrideApplied && !profile.profile.cumulative)
                    continue;

                moveSpeed = profile.ApplyMoveSpeed(moveSpeed);
                acceleration = profile.ApplyAcceleration(acceleration);
                turnAcceleration = profile.ApplyTurnAcceleration(turnAcceleration);
                deceleration = profile.ApplyDeceleration(deceleration);
                inputDeceleration = profile.ApplyInputDeceleration(inputDeceleration);
                maxSpeedMult = profile.ApplyMaxSpeedMultiplier(maxSpeedMult);

                profile.UpdateDecay();

                if (profile.HasExpired)
                {
                    _overrideProfiles.RemoveAt(i);
                }
                overrideApplied = true;
            }
            return this;
        }
    }
}