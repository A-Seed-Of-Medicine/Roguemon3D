using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HSM {
    public class DelayActivationActivity : Activity {
        public float seconds = 0.2f;

        public override async Task ActivateAsync(CancellationToken ct) {
            Debug.Log($"Activating {GetType().Name} (mode={this.Mode}) after {seconds} seconds");
            await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
            await base.ActivateAsync(ct);
        }
    }
}