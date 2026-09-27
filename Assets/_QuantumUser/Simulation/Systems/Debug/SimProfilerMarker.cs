namespace Quantum
{
    using System.Diagnostics;
    using Quantum.Profiling;

    // A named Unity Profiler marker usable from inside the simulation, to split one system's cost
    // into its stages (Quantum already wraps every whole system in its own marker). Goes through
    // Quantum's HostProfiler, which the Unity runtime maps onto ProfilerUnsafeUtility markers.
    //
    // Begin/End are [Conditional("ENABLE_PROFILER")], so in a Release player (no profiler) the calls
    // - and their argument evaluation - are compiled out entirely: zero cost in shipped builds.
    // Pure observation, no simulation state touched, so it can't affect determinism.
    //
    // Usage: one static instance per stage, Begin()/End() around a call that has no early return
    // between them (a missed End() unbalances the profiler stack for the rest of the frame).
    //
    //     private static readonly SimProfilerMarker ChasingMarker = new SimProfilerMarker("EnemySystem.Chasing");
    //     ChasingMarker.Begin();
    //     UpdateChasing(f, ref filter, data);
    //     ChasingMarker.End();
    //
    // Created lazily on first Begin() rather than in the constructor: static fields can initialize
    // before the Unity runtime has installed its IHostProfiler, and a marker created then is invalid.
    public sealed class SimProfilerMarker
    {
        private readonly string _name;
        private HostProfilerMarker _marker;

        public SimProfilerMarker(string name)
        {
            _name = name;
        }

        [Conditional("ENABLE_PROFILER")]
        public void Begin()
        {
            if (_marker.IsValid == false)
                _marker = HostProfiler.CreateMarker(_name);

            if (_marker.IsValid == true)
                _marker.Start();
        }

        [Conditional("ENABLE_PROFILER")]
        public void End()
        {
            if (_marker.IsValid == true)
                _marker.End();
        }
    }
}
