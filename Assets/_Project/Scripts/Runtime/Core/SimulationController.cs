using System.Collections.Generic;
using SpaceSim.Simulation;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>Anything advanced by the fixed-step simulation clock.</summary>
    public interface ISimulatedBody
    {
        /// <summary>Advance from <paramref name="time"/> by exactly <paramref name="dt"/> simulated seconds.</summary>
        void SimulationStep(double time, double dt);
    }

    /// <summary>
    /// Drives the deterministic simulation. Runs in Update with its own accumulator, not in FixedUpdate,
    /// so the physics step and time warp are fully under our control and independent of Unity's physics.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SimulationController : MonoBehaviour
    {
        [SerializeField] SimulationSettings settings = new SimulationSettings();

        readonly SimulationClock clock = new SimulationClock();
        readonly List<ISimulatedBody> bodies = new List<ISimulatedBody>();
        int warpIndex;

        public SimulationSettings Settings => settings;
        public double SimulationTime => clock.SimulationTime;
        /// <summary>Time between the last two physics steps that matches interpolated body positions.</summary>
        public double RenderTime => clock.RenderTime;
        public double InterpolationAlpha => clock.Alpha;
        public double TimeWarp => clock.TimeScale;
        public bool Paused => clock.Paused;
        public bool WarpLimited => clock.WasThrottled;
        public int StepsLastFrame { get; private set; }
        public double FixedStep => clock.FixedStep;

        void Awake()
        {
            ApplySettings();
            clock.Reset(settings.startTime);
        }

        void OnValidate()
        {
            if (Application.isPlaying) ApplySettings();
        }

        void ApplySettings()
        {
            if (settings.fixedStep > 0.0 && settings.fixedStep != clock.FixedStep) clock.FixedStep = settings.fixedStep;
            clock.MaxStepsPerFrame = Mathf.Max(1, settings.maxStepsPerFrame);
            clock.MaxRealDeltaTime = settings.maxRealDeltaTime;
            if (settings.timeWarpLevels == null || settings.timeWarpLevels.Length == 0) settings.timeWarpLevels = new double[] { 1 };
            warpIndex = Mathf.Clamp(warpIndex, 0, settings.timeWarpLevels.Length - 1);
            clock.TimeScale = settings.timeWarpLevels[warpIndex];
        }

        public void Register(ISimulatedBody body)
        {
            if (body != null && !bodies.Contains(body)) bodies.Add(body);
        }

        public void Unregister(ISimulatedBody body) => bodies.Remove(body);

        public void SetPaused(bool paused) => clock.Paused = paused;
        public void TogglePause() => clock.Paused = !clock.Paused;

        public void IncreaseWarp() => SetWarpIndex(warpIndex + 1);
        public void DecreaseWarp() => SetWarpIndex(warpIndex - 1);
        public void ResetWarp() => SetWarpIndex(0);

        void SetWarpIndex(int index)
        {
            warpIndex = Mathf.Clamp(index, 0, settings.timeWarpLevels.Length - 1);
            clock.TimeScale = settings.timeWarpLevels[warpIndex];
        }

        void Update()
        {
            if (!FlightInput.IsTyping)
            {
                if (FlightInput.GetKeyDown(InputKey.Period)) IncreaseWarp();
                if (FlightInput.GetKeyDown(InputKey.Comma)) DecreaseWarp();
                if (FlightInput.GetKeyDown(InputKey.P)) TogglePause();
            }

            StepsLastFrame = clock.Advance(Time.unscaledDeltaTime, Step);
        }

        void Step(double time, double dt)
        {
            for (int i = 0; i < bodies.Count; i++) bodies[i].SimulationStep(time, dt);
        }
    }
}
