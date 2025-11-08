using System.Collections.Generic;
using UnityEngine;

namespace ImprovedTimers {
    public interface IFixedUpdateTimer { }

    public static class TimerManager {
        static readonly List<Timer> updateTimers = new();
        static readonly List<Timer> updateSweep = new();
        static readonly List<Timer> fixedTimers = new();
        static readonly List<Timer> fixedSweep = new();

        public static void RegisterTimer(Timer timer) {
            if (timer is IFixedUpdateTimer) {
                fixedTimers.Add(timer);
            }
            else {
                updateTimers.Add(timer);
            }
        }

        public static void DeregisterTimer(Timer timer) {
            if (!updateTimers.Remove(timer)) {
                fixedTimers.Remove(timer);
            }
        }

        public static void UpdateTimers() {
            if (updateTimers.Count == 0) return;

            updateSweep.RefreshWith(updateTimers);
            foreach (var timer in updateSweep) {
                timer.Tick();
            }
        }

        public static void FixedUpdateTimers() {
            if (fixedTimers.Count == 0) return;

            fixedSweep.RefreshWith(fixedTimers);
            foreach (var timer in fixedSweep) {
                timer.Tick();
            }
        }

        public static void Clear() {
            updateSweep.RefreshWith(updateTimers);
            foreach (var timer in updateSweep) {
                timer.Dispose();
            }

            fixedSweep.RefreshWith(fixedTimers);
            foreach (var timer in fixedSweep) {
                timer.Dispose();
            }

            updateTimers.Clear();
            updateSweep.Clear();
            fixedTimers.Clear();
            fixedSweep.Clear();
        }
    }
}
