using System.Threading;
using System.Threading.Tasks;

namespace HSM {
    public interface ISequence {
        bool IsDone { get; }
        void Start();
        bool Update();
    }
    
    // One activity operation (activate OR deactivate) to run for this phase.
    public delegate Task PhaseStep(CancellationToken ct);

    public class NoopPhase : ISequence {
        public bool IsDone { get; private set; }
        public void Start() => IsDone = true; // completes immediately
        public bool Update() => IsDone;
    }
}