using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HSM {
    public enum ActivityMode { Inactive, Activating, Active, Deactivating }

    public interface IActivity {
        ActivityMode Mode { get; }
        Task ActivateAsync(CancellationToken ct);
        Task DeactivateAsync(CancellationToken ct);
    }

    public abstract class Activity : IActivity {
        public ActivityMode Mode { get; protected set; } = ActivityMode.Inactive;

        public virtual async Task ActivateAsync(CancellationToken ct) {
            if (Mode != ActivityMode.Inactive) return;
            
            Mode = ActivityMode.Activating;
            await Task.CompletedTask;
            Mode = ActivityMode.Active;
            Debug.Log($"Activated {GetType().Name} (mode={Mode})");
        }

        public virtual async Task DeactivateAsync(CancellationToken ct) {
            if (Mode != ActivityMode.Active) return;
            
            Mode = ActivityMode.Deactivating;
            await Task.CompletedTask;
            Mode = ActivityMode.Inactive;
            Debug.Log($"Deactivated {GetType().Name} (mode={Mode})");
        }
    }
}