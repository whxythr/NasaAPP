using NUnit.Framework;
using SpaceSim.Flight;
using SpaceSim.Physics;
using SpaceSim.Planets;

namespace SpaceSim.Tests
{
    public class FlightStateTests
    {
        PlanetModel planet;
        FreeFlightSimulation sim;
        FlightStateClassifier classifier;
        FlightTelemetry telemetry;

        [SetUp]
        public void SetUp()
        {
            planet = TestPlanets.Earth(rotating: true, atmosphere: true);
            sim = TestPlanets.Simulation(planet);
            classifier = new FlightStateClassifier(new FlightPhaseThresholds());
            telemetry = new FlightTelemetry();
        }

        FlightPhase Classify(double altitude, double vEast, double vUp, bool relativeToSurface = false)
        {
            sim.SetLocalState(0.0, 0.0, altitude, vEast, 0.0, vUp, relativeToSurface, 0.0);
            FlightTelemetryCalculator.Compute(sim, telemetry);
            return classifier.Update(telemetry, planet);
        }

        double Vc(double altitude) => OrbitalMechanics.CircularVelocity(planet.GravitationalParameter, planet.Radius + altitude);

        [Test]
        public void RestingOnPad_IsGround()
        {
            sim.PlaceOnSurface(new Mathematics.Vector3d(1, 0, 0), 0.0);
            FlightTelemetryCalculator.Compute(sim, telemetry);
            Assert.AreEqual(FlightPhase.Ground, classifier.Update(telemetry, planet));
        }

        [Test]
        public void Above100km_WithoutHorizontalSpeed_IsNotOrbit()
        {
            Assert.AreEqual(FlightPhase.Spaceflight, Classify(150000.0, 0.0, 0.0));
            Assert.AreEqual(FlightPhaseReason.AboveBoundaryPeriapsisBelowSurface, classifier.Reason);
            Assert.AreEqual(FlightPhase.Spaceflight, Classify(101000.0, 0.0, 2000.0, true), "fast vertical climb past 100 km is suborbital");
        }

        [Test]
        public void CircularSpeedAboveAtmosphere_IsOrbit()
        {
            Assert.AreEqual(FlightPhase.OrbitalFlight, Classify(200000.0, Vc(200000.0), 0.0));
        }

        [Test]
        public void LowCircularOrbit_WithPeriapsisInDragZone_IsNotSustained()
        {
            Assert.AreEqual(FlightPhase.Spaceflight, Classify(110000.0, Vc(110000.0), 0.0));
            Assert.AreEqual(FlightPhaseReason.AboveBoundaryPeriapsisInAtmosphere, classifier.Reason);
        }

        [Test]
        public void PositiveEnergy_IsEscape()
        {
            double ve = OrbitalMechanics.EscapeVelocity(planet.GravitationalParameter, planet.Radius + 300000.0);
            Assert.AreEqual(FlightPhase.EscapeTrajectory, Classify(300000.0, 1.1 * ve, 0.0));
        }

        [Test]
        public void AtmosphericPhases_DependOnDirectionAndDensity()
        {
            Assert.AreEqual(FlightPhase.AtmosphericAscent, Classify(5000.0, 0.0, 300.0, true));
            Assert.AreEqual(FlightPhase.AtmosphericDescent, Classify(5000.0, 0.0, -300.0, true));
            Assert.AreEqual(FlightPhase.Landing, Classify(800.0, 0.0, -50.0, true));
            Assert.AreEqual(FlightPhase.UpperAtmosphere, Classify(60000.0, 0.0, 500.0, true));
        }
    }
}
