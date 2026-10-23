using System;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>Simulation timing parameters (Inspector-editable).</summary>
    [Serializable]
    public class SimulationSettings
    {
        [Tooltip("Physics step in simulated seconds. Independent of frame rate and of Unity's Time.fixedDeltaTime.")]
        public double fixedStep = 0.02;

        [Tooltip("Upper bound of physics steps per rendered frame (protects against stalls at high time warp).")]
        [Min(1)] public int maxStepsPerFrame = 5000;

        [Tooltip("Real frame times above this are clamped [s].")]
        public double maxRealDeltaTime = 0.1;

        [Tooltip("Available time-warp factors (simulated seconds per real second).")]
        public double[] timeWarpLevels = { 1, 2, 5, 10, 50, 100, 1000 };

        [Tooltip("Simulated time at start [s]. Planet rotation phase is measured from t = 0.")]
        public double startTime = 0.0;
    }
}
