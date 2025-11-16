using System;
using UnityEngine;

namespace _Roguemon3D.Scripts.ThirdParty.ImprovedTimers {
    public abstract class Timer : IDisposable {
        public float CurrentTime { get; protected set; }
        public bool IsRunning { get; private set; }

        protected float initialTime;

        public float Progress => Mathf.Clamp(CurrentTime / Mathf.Max(0.000001f, Mathf.Abs(initialTime)), 0f, 1f);

        public Action OnTimerStart = delegate { };
        public Action OnTimerStop = delegate { };

        protected Timer(float value) {
            initialTime = value;
        }

        public void Start() {
            CurrentTime = initialTime;
            if (!IsRunning) {
                IsRunning = true;
                _Roguemon3D.Scripts.ThirdParty.ImprovedTimers.TimerManager.RegisterTimer(this);
                OnTimerStart.Invoke();
            }
        }

        public void Stop() {
            if (IsRunning) {
                IsRunning = false;
                _Roguemon3D.Scripts.ThirdParty.ImprovedTimers.TimerManager.DeregisterTimer(this);
                OnTimerStop.Invoke();
            }
        }

        public abstract void Tick();
        public abstract bool IsFinished { get; }

        public void Resume() => IsRunning = true;
        public void Pause() => IsRunning = false;

        public virtual void Reset() => CurrentTime = initialTime;

        public virtual void Reset(float newTime) {
            initialTime = newTime;
            Reset();
        }

        bool disposed;

        ~Timer() {
            Dispose(false);
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) {
            if (disposed) return;

            if (disposing) {
                _Roguemon3D.Scripts.ThirdParty.ImprovedTimers.TimerManager.DeregisterTimer(this);
            }

            disposed = true;
        }
    }
}
