using System;
using UnityEngine;

namespace ImprovedTimers {
    public class FixedCountdownTimer : Timer, IFixedUpdateTimer {
        public FixedCountdownTimer(float value) : base(value) { }

        public Action OnTimerFinish = delegate { };
        public Action<float> OnTimerTick = delegate { };

        public override void Tick() {
            if (!IsRunning || CurrentTime < 0f) {
                return;
            }

            if (CurrentTime > 0f) {
                float delta = Time.fixedDeltaTime;
                CurrentTime -= delta;
                OnTimerTick.Invoke(delta);
            }

            if (IsRunning && CurrentTime <= 0f) {
                Finish();
            }
        }

        public void Start(float value) {
            initialTime = value;
            CurrentTime = value;
            Start();
        }

        public void Finish() {
            CurrentTime = 0f;
            OnTimerFinish.Invoke();
            Stop();
        }

        public void Cancel() {
            Stop();
            Reset();
        }

        public float TimePassed => Mathf.Max(0f, initialTime - CurrentTime);

        public override bool IsFinished => CurrentTime <= 0f && !IsRunning;
    }
}
