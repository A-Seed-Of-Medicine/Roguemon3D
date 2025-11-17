using System;
using UnityEngine;

namespace ImprovedTimers {
    /// <summary>
    /// Timer that counts down from a specific value to zero.
    /// </summary>
    [Serializable]
    public class MyCountTimer : Timer {
        public MyCountTimer(float value) : base(value) { }
        
        public Action OnTimerFinish = delegate { };
        public Action<float> OnTimerTick = delegate { };

        public override void Tick()
        {
            if (!IsRunning || CurrentTime < 0)
                return;
            if (IsRunning && CurrentTime > 0) {
                CurrentTime -= Time.deltaTime;
                OnTimerTick.Invoke(Time.deltaTime);
            }

            if (IsRunning && CurrentTime <= 0) {
                Finish();
            }
        }
        
        public void Start (float value) {
            initialTime = value;
            CurrentTime = value;
            Start();
        }
        
        public void Finish() {
            CurrentTime = 0;
            OnTimerFinish.Invoke();
            Stop();
        }
        
        public void Cancel() {
            Stop();
            Reset();
        }
        
        public float TimePassed => initialTime - CurrentTime;

        public override bool IsFinished => CurrentTime == 0 && !IsRunning;
    }
}
