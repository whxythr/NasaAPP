using System;
using NUnit.Framework;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Simulation;

namespace SpaceSim.Tests
{
    public class IntegratorTests
    {
        sealed class PointMass : IAccelerationModel
        {
            readonly PlanetaryGravity g;
            public PointMass(double mu) { g = new PlanetaryGravity(mu, TestPlanets.EarthRadius, Vector3d.Zero); }
            public Vector3d Acceleration(double time, Vector3d position, Vector3d velocity) => g.AccelerationAt(position);
        }

        static BodyState CircularLeo(out double radius, out double period)
        {
            radius = TestPlanets.EarthRadius + 400000.0;
            double v = OrbitalMechanics.CircularVelocity(TestPlanets.EarthMu, radius);
            period = OrbitalMechanics.CircularPeriod(TestPlanets.EarthMu, radius);
            return new BodyState(new Vector3d(radius, 0, 0), new Vector3d(0, v, 0), 0.0);
        }

        static double Energy(BodyState s) =>
            OrbitalMechanics.SpecificOrbitalEnergy(TestPlanets.EarthMu, s.Position.Magnitude, s.Velocity.Magnitude);

        static BodyState Run(BodyState s, double dt, double duration, IntegrationMethod method, Action<BodyState> onStep = null)
        {
            var model = new PointMass(TestPlanets.EarthMu);
            long steps = (long)Math.Round(duration / dt);
            for (long i = 0; i < steps; i++)
            {
                s = NumericalIntegrator.Step(s, dt, model, method);
                onStep?.Invoke(s);
            }
            return s;
        }

        [Test]
        public void VelocityVerlet_ConservesEnergyOverTenOrbits()
        {
            BodyState s0 = CircularLeo(out _, out double period);
            double e0 = Energy(s0);
            double maxErrFirstOrbit = 0.0, maxErrLastOrbit = 0.0;
            int stepsPerOrbit = (int)Math.Round(period / 1.0);
            int k = 0;
            Run(s0, 1.0, 10 * stepsPerOrbit, IntegrationMethod.VelocityVerlet, s =>
            {
                double err = Math.Abs((Energy(s) - e0) / e0);
                if (k < stepsPerOrbit) maxErrFirstOrbit = Math.Max(maxErrFirstOrbit, err);
                if (k >= 9 * stepsPerOrbit) maxErrLastOrbit = Math.Max(maxErrLastOrbit, err);
                k++;
            });
            Assert.Less(maxErrFirstOrbit, 1e-6, "energy error bound (Δt = 1 s)");
            // Symplectic: the error oscillates but does not grow from orbit to orbit.
            Assert.Less(maxErrLastOrbit, maxErrFirstOrbit * 1.5 + 1e-12, "no secular energy drift");
        }

        [Test]
        public void ExplicitEuler_DriftsFarMoreThanVerlet()
        {
            BodyState s0 = CircularLeo(out _, out double period);
            double e0 = Energy(s0);
            double euler = Math.Abs((Energy(Run(s0, 1.0, period, IntegrationMethod.ExplicitEuler)) - e0) / e0);
            double verlet = Math.Abs((Energy(Run(s0, 1.0, period, IntegrationMethod.VelocityVerlet)) - e0) / e0);
            Assert.Greater(euler, 1e-3, "Euler gains energy every orbit");
            Assert.Greater(euler, 1000.0 * verlet);
        }

        [Test]
        public void CircularOrbit_ReturnsToStartAfterOnePeriod()
        {
            BodyState s0 = CircularLeo(out double r, out double period);
            // The game's physics step (~0.02 s), adjusted so the period is an integer number of steps.
            int n = (int)Math.Round(period / 0.02);
            double dt = period / n;
            BodyState s = Run(s0, dt, period, IntegrationMethod.VelocityVerlet);
            double posError = (s.Position - s0.Position).Magnitude;
            // Verlet keeps the radius but accumulates a small phase (along-track) error ∝ (ω·Δt)².
            Assert.Less(posError, 0.05, "back at the start within 5 cm after 6771 km × 2π of travel");
            Assert.AreEqual(r, s.Position.Magnitude, 1e-3);
        }

        [Test]
        public void VelocityVerlet_IsSecondOrder()
        {
            BodyState s0 = CircularLeo(out _, out double period);
            double duration = period / 4.0;
            BodyState reference = Run(s0, duration / 20000.0, duration, IntegrationMethod.RungeKutta4);
            double e1 = (Run(s0, duration / 250.0, duration, IntegrationMethod.VelocityVerlet).Position - reference.Position).Magnitude;
            double e2 = (Run(s0, duration / 500.0, duration, IntegrationMethod.VelocityVerlet).Position - reference.Position).Magnitude;
            double ratio = e1 / e2;
            Assert.That(ratio, Is.InRange(3.5, 4.5), "halving Δt reduces the error ~4×");
        }

        [Test]
        public void Results_ConvergeWhenStepShrinks()
        {
            BodyState s0 = CircularLeo(out _, out double period);
            BodyState a = Run(s0, 0.02, 600.0, IntegrationMethod.VelocityVerlet);
            BodyState b = Run(s0, 0.01, 600.0, IntegrationMethod.VelocityVerlet);
            Assert.Less((a.Position - b.Position).Magnitude, 1e-3, "Δt = 0.02 s vs 0.01 s differ by < 1 mm after 10 min");
            Assert.AreEqual(Energy(a), Energy(b), Math.Abs(Energy(a)) * 1e-10);
        }

        [Test]
        public void SubstepCount_RespectsAccuracyLimit()
        {
            double limit = NumericalIntegrator.AccuracyLimitedStep(TestPlanets.EarthMu, 6.771e6, 0.002);
            Assert.AreEqual(0.002 * Math.Sqrt(Math.Pow(6.771e6, 3) / TestPlanets.EarthMu), limit, 1e-9);
            Assert.AreEqual(1, NumericalIntegrator.SubstepCount(0.02, limit, 1000));
            Assert.AreEqual(10, NumericalIntegrator.SubstepCount(10.0 * limit, limit, 1000));
            Assert.AreEqual(5, NumericalIntegrator.SubstepCount(100.0, 1.0, 5));
        }
    }
}
