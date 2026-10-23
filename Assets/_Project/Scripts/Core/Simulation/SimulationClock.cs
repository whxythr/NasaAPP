using System;

namespace SpaceSim.Simulation
{
    /// <summary>
    /// Fixed-step simulation clock (accumulator pattern). Physics advances in identical steps of
    /// <see cref="FixedStep"/> seconds of simulated time regardless of frame rate, so results do not
    /// depend on FPS. Rendering interpolates between the last two steps using <see cref="Alpha"/>.
    /// </summary>
    public sealed class SimulationClock
    {
        double fixedStep = 0.02;
        double timeScale = 1.0;
        double accumulator;
        long stepCount;
        double startTime;

        /// <summary>Physics step in simulated seconds.</summary>
        public double FixedStep
        {
            get => fixedStep;
            set
            {
                if (!(value > 0.0)) throw new ArgumentOutOfRangeException(nameof(value), "Fixed step must be positive.");
                // Re-base the step counter so simulated time stays continuous.
                startTime = SimulationTime;
                stepCount = 0;
                fixedStep = value;
            }
        }

        /// <summary>Simulated seconds per real second (time warp). 0 is equivalent to pause.</summary>
        public double TimeScale
        {
            get => timeScale;
            set => timeScale = Math.Max(0.0, value);
        }

        public bool Paused { get; set; }

        /// <summary>Upper bound on physics steps per frame, protects against a spiral of death.</summary>
        public int MaxStepsPerFrame { get; set; } = 5000;

        /// <summary>Real frame times above this are clamped (e.g. after a hitch or breakpoint) [s].</summary>
        public double MaxRealDeltaTime { get; set; } = 0.1;

        /// <summary>Simulated time of the latest completed physics step [s]. Computed from an integer
        /// step counter, so it does not accumulate floating-point drift.</summary>
        public double SimulationTime => startTime + stepCount * fixedStep;

        /// <summary>Fraction (0..1) of a step elapsed since the latest physics step; used for interpolation.</summary>
        public double Alpha => Math.Min(1.0, accumulator / fixedStep);

        /// <summary>Time to use for rendering: between the previous and the latest physics step.</summary>
        public double RenderTime => SimulationTime - fixedStep + Alpha * fixedStep;

        /// <summary>True if the last <see cref="Advance"/> could not keep up with the requested time scale.</summary>
        public bool WasThrottled { get; private set; }

        public long StepCount => stepCount;

        /// <summary>Resets simulated time to <paramref name="time"/>.</summary>
        public void Reset(double time)
        {
            startTime = time;
            stepCount = 0;
            accumulator = 0.0;
        }

        /// <summary>
        /// Advances the clock by one rendered frame. Invokes <paramref name="step"/> (time, dt) for every
        /// whole physics step that fits in the accumulated time. Returns the number of steps executed.
        /// </summary>
        public int Advance(double realDeltaTime, Action<double, double> step)
        {
            WasThrottled = false;
            if (Paused || timeScale <= 0.0 || realDeltaTime <= 0.0) return 0;

            accumulator += Math.Min(realDeltaTime, MaxRealDeltaTime) * timeScale;

            int executed = 0;
            while (accumulator >= fixedStep && executed < MaxStepsPerFrame)
            {
                step(SimulationTime, fixedStep);
                stepCount++;
                accumulator -= fixedStep;
                executed++;
            }

            if (accumulator >= fixedStep)
            {
                // Could not keep up: drop the backlog instead of falling ever further behind.
                WasThrottled = true;
                accumulator = Math.Min(accumulator, fixedStep * 0.999);
            }

            return executed;
        }
    }
}
