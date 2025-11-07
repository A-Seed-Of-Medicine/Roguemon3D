using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace ImprovedTimers {
    internal static class TimerBootstrapper {
        static PlayerLoopSystem updateSystem;
        static PlayerLoopSystem fixedUpdateSystem;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        internal static void Initialize() {
            PlayerLoopSystem currentPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();

            if (!InsertTimerManager<Update>(ref currentPlayerLoop, ref updateSystem, typeof(TimerManager), TimerManager.UpdateTimers, 0)) {
                Debug.LogWarning("Improved Timers not initialized, unable to register TimerManager into the Update loop.");
                return;
            }

            InsertTimerManager<FixedUpdate>(ref currentPlayerLoop, ref fixedUpdateSystem, typeof(FixedTimerManagerMarker), TimerManager.FixedUpdateTimers, 0);

            PlayerLoop.SetPlayerLoop(currentPlayerLoop);

    #if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeState;
            EditorApplication.playModeStateChanged += OnPlayModeState;
    #endif
        }

    #if UNITY_EDITOR
        static void OnPlayModeState(PlayModeStateChange state) {
            if (state != PlayModeStateChange.ExitingPlayMode) return;

            PlayerLoopSystem currentPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();
            RemoveTimerManager<Update>(ref currentPlayerLoop, in updateSystem);
            RemoveTimerManager<FixedUpdate>(ref currentPlayerLoop, in fixedUpdateSystem);
            PlayerLoop.SetPlayerLoop(currentPlayerLoop);

            TimerManager.Clear();
        }
    #endif

        static void RemoveTimerManager<T>(ref PlayerLoopSystem loop, in PlayerLoopSystem system) {
            if (system.updateDelegate == null) return;
            PlayerLoopUtils.RemoveSystem<T>(ref loop, in system);
        }

        static bool InsertTimerManager<T>(ref PlayerLoopSystem loop, ref PlayerLoopSystem storage, System.Type systemType, PlayerLoopSystem.UpdateFunction updateDelegate, int index) {
            storage = new PlayerLoopSystem {
                type = systemType,
                updateDelegate = updateDelegate,
                subSystemList = null
            };
            return PlayerLoopUtils.InsertSystem<T>(ref loop, in storage, index);
        }

        struct FixedTimerManagerMarker { }
    }
}
