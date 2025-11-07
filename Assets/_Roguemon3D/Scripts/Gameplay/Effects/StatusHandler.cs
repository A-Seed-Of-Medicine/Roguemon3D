using System;
using System.Collections.Generic;
using System.Linq;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Effects;
using Cysharp.Threading.Tasks;
using ImprovedTimers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace AdvancedController
{
    /// <summary>
    /// A countdown manager for status effects
    /// </summary>
    [Serializable]
    public class StatusHandler
    {
        public readonly TimerStatus StunnedStatus;
        public readonly TimerStatus SilencedStatus;
        public readonly TimerStatus RootedStatus;

        public readonly IDamageable Damageable;
        
        public StatusHandler( IDamageable damageable)
        {
            Damageable = damageable;
            StunnedStatus = new TimerStatus(this);
            SilencedStatus = new TimerStatus(this);
            RootedStatus = new TimerStatus(this);
        }
        
    }
    
    public interface IStatusEffect
    {
        StatusHandler Handler { get; }
        public bool IsActive { get; }
        public float Amount { get; }
        public Action<IStatusEffect> OnStart { get; set; }
        public Action<IStatusEffect, float> OnTick { get; set; }
        public Action<IStatusEffect> OnEnd { get; set; }
        void StartStatus(float amount);
        IDamageable Damageable => Handler.Damageable;
    }
    
    public class TimerStatus: IStatusEffect
    {
        public StatusHandler Handler { get; private set; }
        public MyCountTimer Timer { get; private set; }
        public bool IsActive => Timer.IsRunning;
        public float Amount => Timer.CurrentTime;
        public Action<IStatusEffect> OnStart { get; set; } = delegate { };
        public Action<IStatusEffect, float> OnTick { get; set; } = delegate { };
        public Action<IStatusEffect> OnEnd { get; set; } = delegate { };

        public TimerStatus(StatusHandler handler)
        {
            Handler = handler;
            Timer = new MyCountTimer(0);
            Timer.OnTimerFinish += Finish;
        }
        
        protected virtual void Tick(float deltaTime)
        {
            OnTick?.Invoke(this, deltaTime);
        }
        
        protected virtual void Finish()
        {
            OnEnd?.Invoke(this);
        }

        public void StartStatus(float amount)
        {
            if (Timer.CurrentTime >= amount)
                return;
            Timer.Start(amount);
            OnStart?.Invoke(this);
        }
    }
}