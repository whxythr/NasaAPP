using System.Collections;
using NUnit.Framework;
using SpaceSim.Flight;
using SpaceSim.Planets;
using SpaceSim.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpaceSim.Tests
{
    /// <summary>
    /// Integration test of the whole slice: the bootstrap builds the star system without errors (any
    /// Debug.LogError fails the test), the probe rests on the pad of the start body, other bodies sit on their
    /// orbits, jumping to another body works, and an upward initial velocity makes the probe climb.
    /// </summary>
    public class BootstrapPlayModeTests
    {
        GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.Destroy(root);
            var cam = GameObject.Find("SpaceCamera");
            if (cam != null) Object.Destroy(cam);
        }

        [UnityTest]
        public IEnumerator Bootstrap_BuildsSystem_JumpsBodies_AndProbeFlies()
        {
            root = new GameObject("SpaceSimTest");
            var boot = root.AddComponent<SpaceSimBootstrap>();
            for (int i = 0; i < 20; i++) yield return null;

            // Start body: the fourth planet, rocket on the pad, space tools still locked.
            Assert.AreEqual(5, boot.System.Bodies.Count);
            Assert.AreEqual(HomeSystemCatalog.HomeWorldId, boot.System.ActiveBody.Id);
            Assert.AreEqual(8.27, boot.Planet.Model.Gravity.SurfaceGravity, 0.03);
            Assert.IsTrue(boot.Body.Simulation.IsLanded, "rocket starts on the pad");
            Assert.IsNotNull(boot.Body.Rocket);
            Assert.IsFalse(boot.System.SpaceUnlocked);
            Assert.AreEqual(FlightPhase.Ground, boot.FlightState.Phase);
            Assert.Greater(boot.PlanetVisual.Procedural.DisplayedChunkCount, 0, "surface chunks are displayed");
            Assert.Less(boot.CameraController.transform.position.magnitude, 2500f, "floating origin keeps the camera near 0");

            // Launch: the rocket lifts off under its own thrust.
            Assert.IsTrue(boot.Body.Launch());
            double launchTime = boot.Simulation.SimulationTime;
            float launchDeadline = Time.realtimeSinceStartup + 15f;
            while (boot.Simulation.SimulationTime - launchTime < 8.0 && Time.realtimeSinceStartup < launchDeadline) yield return null;
            Assert.IsFalse(boot.Body.Simulation.IsLanded, "lift-off");
            Assert.Greater(boot.FlightState.Telemetry.AltitudeAboveSurface, 30.0);
            Assert.Greater(boot.FlightState.Telemetry.ThrustAcceleration, 0.0);

            var moon = boot.System.Bodies[boot.System.IndexOf(HomeSystemCatalog.MoraMoonId)].Runtime;
            var mora = boot.System.Bodies[boot.System.IndexOf(HomeSystemCatalog.HermeousMoraId)].Runtime;
            Assert.AreEqual(3.0e7, (moon.Model.Position - mora.Model.Position).Magnitude, 10.0, "moon on its 30 000 km orbit");
            var teonanIndex = boot.System.IndexOf(HomeSystemCatalog.TeonanId);
            Assert.Less(boot.System.Bodies[teonanIndex].Runtime.RenderScale, 1.0, "distant planet drawn in scaled space");

            // Jump to Teonan (airless) and launch straight up.
            boot.SwitchToBody(teonanIndex);
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(HomeSystemCatalog.TeonanId, boot.System.ActiveBody.Id);
            Assert.AreEqual(0.0, boot.Planet.Model.Position.Magnitude, 1e-9, "active body is the frame centre");
            Assert.IsTrue(boot.Body.Simulation.IsLanded);

            var site = boot.Planet.Definition.launchSite;
            boot.Body.ApplyInitialConditions(site.latitudeDegrees, site.longitudeDegrees, 3000.0, 0.0, 0.0, 400.0, true);
            double start = boot.Simulation.SimulationTime;
            double startAlt = boot.FlightState.Telemetry.AltitudeAboveReference;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (boot.Simulation.SimulationTime - start < 3.0 && Time.realtimeSinceStartup < deadline) yield return null;

            Assert.GreaterOrEqual(boot.Simulation.SimulationTime - start, 3.0, "simulation advanced");
            Assert.IsFalse(boot.Body.Simulation.IsLanded);
            Assert.Greater(boot.FlightState.Telemetry.AltitudeAboveReference, startAlt + 1100.0, "≈ 400 m/s climb, g = 2.31");
            Assert.AreEqual(0.0, boot.FlightState.Telemetry.DragAcceleration, "no air on Teonan");
            Assert.AreEqual(FlightPhase.Spaceflight, boot.FlightState.Phase, "airless: ballistic arc, periapsis below the surface");
        }
    }
}
