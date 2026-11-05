using NUnit.Framework;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;

namespace SpaceSim.Tests
{
    /// <summary>Level B bookkeeping: what the engines delivered = what was gained + what was lost.</summary>
    public class FlightRecorderTests
    {
        static FreeFlightSimulation RocketOnPad(bool atmosphere, double pitch)
        {
            var planet = TestPlanets.Earth(rotating: false, atmosphere: atmosphere);
            var sim = TestPlanets.Simulation(planet, drag: true);
            sim.ContactRadius = 1.0;
            sim.Rocket = new RocketState(new RocketConfiguration()) { EngineOn = true, PitchDegrees = pitch };
            sim.PlaceOnSurface(Vector3d.UnitX, 0.0);
            return sim;
        }

        static void Burn(FreeFlightSimulation sim, double seconds)
        {
            for (int i = 0; i < (int)(seconds / 0.02); i++) sim.Step(0.02);
        }

        [Test]
        public void VerticalBurnInVacuum_GainPlusGravityLossEqualsExpended()
        {
            var sim = RocketOnPad(atmosphere: false, pitch: 0.0);
            Burn(sim, 60.0);
            var rec = sim.Recorder;
            double gain = sim.State.Velocity.Magnitude;
            Assert.Greater(rec.DeltaVExpended, 500.0);
            Assert.AreEqual(rec.DeltaVExpended, gain + rec.GravityLoss + rec.DragLoss + rec.SteeringLoss, rec.DeltaVExpended * 0.005);
            Assert.AreEqual(0.0, rec.DragLoss, 1e-9, "no air");
            Assert.Less(rec.SteeringLoss, 1.0, "thrust along velocity");
            // Vertical ascent: gravity loss ≈ g·t (g barely changes over a few km).
            Assert.AreEqual(9.82 * rec.FlightTime, rec.GravityLoss, 9.82 * rec.FlightTime * 0.01);
        }

        [Test]
        public void AscentThroughAir_AccountsForDragAndSteering()
        {
            var sim = RocketOnPad(atmosphere: true, pitch: 0.0);
            Burn(sim, 20.0);
            sim.Rocket.PitchDegrees = 30.0; // pitch over: thrust no longer along velocity
            Burn(sim, 40.0);
            var rec = sim.Recorder;
            double gain = sim.State.Velocity.Magnitude;
            Assert.Greater(rec.DragLoss, 1.0);
            Assert.Greater(rec.SteeringLoss, 1.0);
            Assert.AreEqual(rec.DeltaVExpended, gain + rec.GravityLoss + rec.DragLoss + rec.SteeringLoss, rec.DeltaVExpended * 0.01);
            Assert.Greater(rec.MaxAltitude, 1000.0);
            Assert.Greater(rec.MaxDynamicPressure, 0.0);
            Assert.Greater(rec.DistanceTravelled, rec.MaxAltitude * 0.99);
        }

        [Test]
        public void ExpendedDeltaV_MatchesDropInIdealDeltaVInVacuum()
        {
            // Level A vs Level B: the ideal Δv the rocket "had" minus what it "has" equals what the engine delivered.
            var sim = RocketOnPad(atmosphere: false, pitch: 0.0);
            double before = sim.Rocket.DeltaVRemaining();
            Burn(sim, 45.0);
            double after = sim.Rocket.DeltaVRemaining();
            Assert.AreEqual(before - after, sim.Recorder.DeltaVExpended, (before - after) * 0.005);
        }

        [Test]
        public void PoweredFlight_UsesRungeKutta_CoastUsesVerlet()
        {
            var sim = RocketOnPad(atmosphere: false, pitch: 0.0);
            Assert.AreEqual(IntegrationMethod.RungeKutta4, sim.PoweredMethod);
            Assert.AreEqual(IntegrationMethod.VelocityVerlet, sim.Method);
        }
    }
}
