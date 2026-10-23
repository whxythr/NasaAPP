using System;
using NUnit.Framework;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Simulation;

namespace SpaceSim.Tests
{
    public class FreeFlightTests
    {
        /// <summary>Exact radial free-fall time from rest at r0 to r1 in a point-mass field.</summary>
        static double RadialFallTime(double mu, double r0, double r1)
        {
            double x = r1 / r0;
            return Math.Sqrt(r0 * r0 * r0 / (2.0 * mu)) * (Math.Sqrt(x * (1.0 - x)) + Math.Acos(Math.Sqrt(x)));
        }

        [Test]
        public void BodyReleasedAtRest_FallsAndLandsAtAnalyticTime()
        {
            var planet = TestPlanets.Earth(rotating: false, atmosphere: false);
            var sim = TestPlanets.Simulation(planet);
            double h = 1000.0;
            sim.SetInertialState(new Vector3d(0, 0, planet.Radius + h), Vector3d.Zero, 0.0);

            while (!sim.IsLanded && sim.State.Time < 100.0) sim.Step(0.02);

            Assert.IsTrue(sim.IsLanded);
            double expected = RadialFallTime(planet.GravitationalParameter, planet.Radius + h, planet.Radius);
            Assert.AreEqual(expected, sim.LastImpact.Time, 1e-4, "contact time resolved by bisection");
            Assert.AreEqual(Math.Sqrt(2.0 * h / planet.Properties.SurfaceGravity), expected, 0.01, "≈ √(2h/g) for small h");
            double vExpected = Math.Sqrt(2.0 * planet.GravitationalParameter * (1.0 / planet.Radius - 1.0 / (planet.Radius + h)));
            Assert.AreEqual(vExpected, sim.LastImpact.SurfaceRelativeSpeed, 1e-3);
        }

        [Test]
        public void VerticalLaunch_BelowEscapeSpeed_ComesBack()
        {
            var planet = TestPlanets.Earth(rotating: false, atmosphere: false);
            var sim = TestPlanets.Simulation(planet);
            sim.SetLocalState(0.0, 0.0, 0.0, 0.0, 0.0, 3000.0, true, 0.0);
            double maxAltitude = 0.0;
            while (!sim.IsLanded && sim.State.Time < 2000.0)
            {
                sim.Step(0.02);
                maxAltitude = Math.Max(maxAltitude, planet.AltitudeAboveReference(sim.State.Position));
            }
            Assert.IsTrue(sim.IsLanded, "a 3 km/s vertical shot without atmosphere falls back");
            // Energy conservation: v²/2 − μ/R = −μ/(R+h_max)
            double mu = planet.GravitationalParameter, R = planet.Radius;
            double hMax = 1.0 / (1.0 / R - 3000.0 * 3000.0 / (2.0 * mu)) - R;
            Assert.AreEqual(hMax, maxAltitude, 2.0);
        }

        [Test]
        public void HorizontalCircularSpeed_StaysInOrbit()
        {
            var planet = TestPlanets.Earth(rotating: false, atmosphere: false);
            var sim = TestPlanets.Simulation(planet);
            double r = planet.Radius + 300000.0;
            double v = OrbitalMechanics.CircularVelocity(planet.GravitationalParameter, r);
            sim.SetInertialState(new Vector3d(r, 0, 0), new Vector3d(0, v, 0), 0.0);
            double period = OrbitalMechanics.CircularPeriod(planet.GravitationalParameter, r);
            double minR = double.MaxValue, maxR = 0.0;
            while (sim.State.Time < 3.0 * period)
            {
                sim.Step(0.05);
                double d = sim.State.Position.Magnitude;
                minR = Math.Min(minR, d);
                maxR = Math.Max(maxR, d);
            }
            Assert.IsFalse(sim.IsLanded);
            Assert.Less(maxR - minR, 1.0, "radius stays constant within 1 m over 3 orbits");
        }

        [Test]
        public void Drag_LimitsFallToTerminalVelocity()
        {
            var planet = TestPlanets.Earth(rotating: false, atmosphere: true);
            var sim = TestPlanets.Simulation(planet, drag: true);
            sim.Mass = 100.0;
            sim.SetInertialState(new Vector3d(planet.Radius + 3000.0, 0, 0), Vector3d.Zero, 0.0);
            while (!sim.IsLanded && sim.State.Time < 1000.0) sim.Step(0.02);
            Assert.IsTrue(sim.IsLanded);
            double vt = AtmosphericDrag.TerminalVelocity(100.0, planet.Properties.SurfaceGravity, 1.225, 1.0, 1.0);
            Assert.AreEqual(vt, sim.LastImpact.SurfaceRelativeSpeed, vt * 0.01);
        }

        [Test]
        public void Drag_UsesAirRelativeVelocity()
        {
            var planet = TestPlanets.Earth(rotating: true, atmosphere: true);
            var sim = TestPlanets.Simulation(planet, drag: true);
            // Co-moving with the rotating air at 1 km altitude over the equator: ~465 m/s inertial, 0 relative.
            sim.SetLocalState(0.0, 0.0, 1000.0, 0.0, 0.0, 0.0, true, 0.0);
            Assert.Greater(sim.State.Velocity.Magnitude, 400.0);
            Assert.Less(sim.DragAcceleration(sim.State.Position, sim.State.Velocity).Magnitude, 1e-9);
        }

        [Test]
        public void LandedBody_CoRotatesWithPlanet()
        {
            var planet = TestPlanets.Earth(rotating: true, atmosphere: true);
            var sim = TestPlanets.Simulation(planet);
            sim.PlaceOnSurface(new Vector3d(1, 0, 0), 0.0);
            for (int i = 0; i < 1000; i++) sim.Step(0.02);
            Assert.IsTrue(sim.IsLanded);
            double expected = planet.Rotation.AngularSpeed * planet.Radius;
            Assert.AreEqual(expected, sim.State.Velocity.Magnitude, 1e-6, "surface speed ω·R at the mean radius");
            // 464.6 m/s at R = 6371 km (the often-quoted 465.1 m/s uses the equatorial radius 6378 km).
            Assert.AreEqual(464.6, expected, 0.1);
        }

        [Test]
        public void Physics_IsIndependentOfFrameRate()
        {
            const int targetStep = 30000; // 10 simulated minutes at Δt = 0.02 s
            BodyState RunWithFrames(Func<int, double> frameTime)
            {
                var planet = TestPlanets.Earth(rotating: true, atmosphere: true);
                var sim = TestPlanets.Simulation(planet, drag: true);
                sim.SetLocalState(0.3, 1.0, 0.0, 1500.0, 300.0, 2500.0, true, 0.0);
                var clock = new SimulationClock { FixedStep = 0.02, TimeScale = 1.0, MaxStepsPerFrame = 100000 };
                BodyState captured = default;
                int frame = 0;
                while (clock.StepCount < targetStep)
                {
                    clock.Advance(frameTime(frame++), (t, dt) =>
                    {
                        sim.Step(dt);
                        if (clock.StepCount + 1 == targetStep) captured = sim.State;
                    });
                }
                return captured;
            }

            var rng = new Procedural.DeterministicRandom(7);
            BodyState at60 = RunWithFrames(_ => 1.0 / 60.0);
            BodyState at144 = RunWithFrames(_ => 1.0 / 144.0);
            BodyState jittery = RunWithFrames(_ => rng.Range(0.004, 0.09));

            Assert.AreEqual(at60.Position, at144.Position, "bit-identical regardless of FPS");
            Assert.AreEqual(at60.Position, jittery.Position);
            Assert.AreEqual(at60.Velocity, jittery.Velocity);
        }
    }
}
