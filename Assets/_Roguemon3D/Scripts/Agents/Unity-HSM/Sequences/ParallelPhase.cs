using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HSM {
    public class ParallelPhase : ISequence {
        readonly List<PhaseStep> steps;
        readonly CancellationToken ct;
        List<Task> tasks;
        public bool IsDone { get; private set; }
        
        public ParallelPhase(List<PhaseStep> steps, CancellationToken ct) {
            this.steps = steps;
            this.ct = ct;
        }

        public void Start() {
            if (steps == null || steps.Count == 0) { IsDone = true; return; }
            tasks = new List<Task>(steps.Count);
            for (int i = 0; i < steps.Count; i++) tasks.Add(steps[i](ct));
        }

        public bool Update() {
            if (IsDone) return true;
            IsDone = tasks == null || tasks.TrueForAll(t => t.IsCompleted);
            return IsDone;
        }
    }
}