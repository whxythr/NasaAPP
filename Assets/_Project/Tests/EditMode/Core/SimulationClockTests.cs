using NUnit.Framework;
using SpaceSim.Simulation;

namespace SpaceSim.Tests
{
    public class SimulationClockTests
    {
        [Test]
        public void ExecutesWholeStepsAndKeepsRemainder()
        {
            var clock = new SimulationClock { FixedStep = 0.02 };
            int steps = clock.Advance(0.05, (t, dt) => { });
            Assert.AreEqual(2, steps);
            Assert.AreEqual(0.04, clock.SimulationTime, 1e-12);
            Assert.AreEqual(0.5, clock.Alpha, 1e-9);
        }

        [Test]
        public void TimeScale_MultipliesSimulatedTime()
        {
            var clock = new SimulationClock { FixedStep = 0.02, TimeScale = 10.0 };
            for (int i = 0; i < 60; i++) clock.Advance(1.0 / 60.0, (t, dt) => { });
            Assert.AreEqual(10.0, clock.SimulationTime, 0.021);
        }

        [Test]
        public void Throttles_WhenStepBudgetExceeded()
        {
            var clock = new SimulationClock { FixedStep = 0.02, TimeScale = 1000.0, MaxStepsPerFrame = 100 };
            int steps = clock.Advance(0.05, (t, dt) => { });
            Assert.AreEqual(100, steps);
            Assert.IsTrue(clock.WasThrottled);
        }

        [Test]
        public void Pause_StopsTime()
        {
            var clock = new SimulationClock { Paused = true };
            Assert.AreEqual(0, clock.Advance(1.0, (t, dt) => { }));
            Assert.AreEqual(0.0, clock.SimulationTime);
        }

        [Test]
        public void SimulationTime_HasNoAccumulatedDrift()
        {
            var clock = new SimulationClock { FixedStep = 0.02, MaxStepsPerFrame = 1000000 };
            for (int i = 0; i < 1000; i++) clock.Advance(0.1, (t, dt) => { });
            // 100 s = exactly 5000 steps; time is counter × step, not a running float sum.
            Assert.AreEqual(5000, clock.StepCount, 1);
            Assert.AreEqual(clock.StepCount * 0.02, clock.SimulationTime, 0.0);
        }
    }
}
