using System.Collections;
using NUnit.Framework;
using SpaceSim.Flight;
using SpaceSim.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpaceSim.Tests
{
    /// <summary>
    /// Integration test of the whole Phase 1 slice: the bootstrap builds the world without errors (any
    /// Debug.LogError fails the test), the probe rests on the pad, the surface streams in, and an upward
    /// initial velocity makes the probe climb — continuously, in the same scene.
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
        public IEnumerator Bootstrap_BuildsWorld_AndProbeFliesUnderGravity()
        {
            root = new GameObject("SpaceSimTest");
            var boot = root.AddComponent<SpaceSimBootstrap>();
            for (int i = 0; i < 20; i++) yield return null;

            Assert.IsNotNull(boot.Planet.Model);
            Assert.IsTrue(boot.Body.Simulation.IsLanded, "probe starts on the pad");
            Assert.AreEqual(FlightPhase.Ground, boot.FlightState.Phase);
            Assert.Less(boot.FlightState.Telemetry.AltitudeAboveSurface, 0.01);
            Assert.AreEqual(boot.Planet.Model.Gravity.SurfaceGravity, 9.82, 0.01);
            Assert.IsNotNull(boot.PlanetVisual.Procedural);
            Assert.Greater(boot.PlanetVisual.Procedural.DisplayedChunkCount, 0, "surface chunks are displayed");

            // Camera is close to the Unity origin (floating origin) although the planet radius is 6.4·10⁶ m.
            Assert.Less(boot.CameraController.transform.position.magnitude, 2500f);

            var site = boot.Planet.Definition.launchSite;
            boot.Body.ApplyInitialConditions(site.latitudeDegrees, site.longitudeDegrees, 3000.0, 0.0, 0.0, 400.0, true);
            double start = boot.Simulation.SimulationTime;
            double startAlt = boot.FlightState.Telemetry.AltitudeAboveReference;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (boot.Simulation.SimulationTime - start < 3.0 && Time.realtimeSinceStartup < deadline) yield return null;

            Assert.GreaterOrEqual(boot.Simulation.SimulationTime - start, 3.0, "simulation advanced");
            Assert.IsFalse(boot.Body.Simulation.IsLanded);
            Assert.Greater(boot.FlightState.Telemetry.AltitudeAboveReference, startAlt + 1000.0, "climbing at ~400 m/s");
            Assert.AreEqual(FlightPhase.AtmosphericAscent, boot.FlightState.Phase);
            Assert.Less(boot.FlightState.Telemetry.VelocityUp, 400.0, "gravity and drag decelerate the climb");
        }
    }
}
