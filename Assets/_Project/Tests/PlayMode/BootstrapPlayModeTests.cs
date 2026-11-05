using System.Collections;
using NUnit.Framework;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
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
        string saveDir;

        [SetUp]
        public void SetUp()
        {
            // Never read or write the player's real profile from tests.
            saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SpaceSimPlayModeTest_" + System.Guid.NewGuid().ToString("N"));
            PlayerSession.SaveDirectoryOverride = saveDir;
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.Destroy(root);
            var cam = GameObject.Find("SpaceCamera");
            if (cam != null) Object.Destroy(cam);
            PlayerSession.SaveDirectoryOverride = null;
            if (System.IO.Directory.Exists(saveDir)) System.IO.Directory.Delete(saveDir, true);
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
            Assert.IsNotNull(boot.Session);
            Assert.AreEqual(SpaceSim.Economy.PlayerProfile.StartingCredits, boot.Session.Profile.credits, "fresh test profile");
            Assert.IsTrue(System.IO.File.Exists(PlayerSession.SavePath), "profile saved on start");
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

            // Flight Academy: lessons for the start planet and the player's rocket; opening it mid-flight pauses the flight.
            Assert.IsNotNull(boot.Academy);
            Assert.AreEqual(13, boot.Academy.Topics.Count);
            Assert.AreEqual(13, boot.Academy.Calculators.Count);
            Assert.AreEqual(HomeSystemCatalog.HomeWorldId, boot.Academy.Context.PlanetId);
            Assert.IsNotNull(boot.Academy.Context.Design, "lessons know the player's rocket");
            AcademyUI[] academyWindows = root.GetComponentsInChildren<AcademyUI>();
            Assert.AreEqual(1, academyWindows.Length);
            var academyUi = academyWindows[0];
            Assert.IsNotNull(academyUi);
            academyUi.OpenTopic(SpaceSim.Academy.AcademyTopics.Tsiolkovsky);
            yield return null;
            Assert.IsTrue(academyUi.IsOpen);
            Assert.IsTrue(boot.Simulation.Paused, "paused while reading mid-flight");
            double pausedAt = boot.Simulation.SimulationTime;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(pausedAt, boot.Simulation.SimulationTime, "no simulated time passes");
            academyUi.Toggle();
            yield return null;
            Assert.IsFalse(academyUi.IsOpen);
            Assert.IsFalse(boot.Simulation.Paused, "flight resumes on close");

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

        [UnityTest]
        public IEnumerator Navigation_RailsWarp_And_SoiHandoff()
        {
            root = new GameObject("SpaceSimTest");
            var boot = root.AddComponent<SpaceSimBootstrap>();
            for (int i = 0; i < 20; i++) yield return null;
            var nav = boot.System.Navigation;
            Assert.IsNotNull(nav);
            int home = boot.System.IndexOf(HomeSystemCatalog.HomeWorldId);
            int mora = boot.System.IndexOf(HomeSystemCatalog.HermeousMoraId);

            // Circular orbit at 300 km (above the drag zone) → time warp ×10⁶ coasts on rails.
            var site = boot.Planet.Definition.launchSite;
            double r = boot.Planet.Model.Radius + 300000.0;
            boot.Body.ApplyInitialConditions(site.latitudeDegrees, site.longitudeDegrees, 300000.0, System.Math.Sqrt(boot.Planet.Model.GravitationalParameter / r), 0, 0, false);
            for (int k = 0; k < 12; k++) boot.Simulation.IncreaseWarp();
            double t0 = boot.Simulation.SimulationTime;
            for (int i = 0; i < 10; i++) yield return null;
            Assert.IsTrue(boot.Simulation.OnRails, "coasting on rails at ×" + boot.Simulation.TimeWarp);
            Assert.Greater(boot.Simulation.SimulationTime - t0, 1000.0, "far more than ×1000 would give");
            Assert.AreEqual(r, boot.Body.Simulation.State.Position.Magnitude, 50.0, "the circular orbit stays circular");
            boot.Simulation.ResetWarp();

            // Put the vehicle just inside Mora's sphere of influence, at rest relative to Mora → the frame moves to Mora.
            double t = boot.Body.Simulation.State.Time;
            Vector3d offset = new Vector3d(nav[mora].SoiRadius * 0.5, 0, 0);
            Vector3d pos = nav.Position(mora, t) - nav.Position(home, t) + offset;
            Vector3d vel = nav.Velocity(mora, t) - nav.Velocity(home, t);
            boot.Body.Simulation.SetInertialState(pos, vel, t);
            for (int k = 0; k < 6; k++) boot.Simulation.IncreaseWarp(); // ×1000
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(HomeSystemCatalog.HermeousMoraId, boot.System.ActiveBody.Id, "handoff to Mora's frame");
            Assert.AreEqual(1.0, boot.Simulation.TimeWarp, "warp stops at the sphere change");
            Assert.AreEqual(mora, boot.Body.Simulation.FrameBody);
            Assert.IsFalse(boot.Body.Simulation.IsLanded);
            Assert.AreEqual(offset.Magnitude, boot.Body.Simulation.State.Position.Magnitude, offset.Magnitude * 1e-3, "same place, new centre");
        }

        [UnityTest]
        public IEnumerator Exploration_Eva_Sample_Crash_NewRocket()
        {
            root = new GameObject("SpaceSimTest");
            var boot = root.AddComponent<SpaceSimBootstrap>();
            for (int i = 0; i < 20; i++) yield return null;

            // A collector and a spectrometer (free modules), through the real hangar path.
            var design = SpaceSim.Vehicles.RocketBlueprint.CreateDefault();
            design.payloadModules.Add(SpaceSim.Vehicles.ModuleCatalog.CollectorBasic);
            design.payloadModules.Add(SpaceSim.Vehicles.ModuleCatalog.Spectrometer);
            Assert.IsTrue(boot.Body.ApplyBlueprint(design));
            yield return null;

            // Outside on the start planet: first-person, the planet's own gravity, walking moves the astronaut.
            var ex = boot.Explorer;
            Assert.AreEqual("", ex.TryStartEva());
            Assert.IsTrue(ex.Eva);
            Assert.AreEqual(8.27, ex.Walker.Gravity, 0.03);
            Assert.IsNotNull(boot.CameraController.FirstPerson, "the camera looks through the astronaut's eyes");
            ex.Drive(1.0, 0.0, 1.0f, false);
            float walkEnd = Time.realtimeSinceStartup + 1.2f; // the walk lasts one real second, whatever the frame rate
            while (Time.realtimeSinceStartup < walkEnd) yield return null;
            Assert.Greater(ex.Walker.DistanceWalked, 0.5, "walked ≈ 1.36 m/s × 1 s");
            Assert.IsTrue(ex.Walker.OnGround);

            // The suited body: drawn and casting shadows; in first person the helmet only casts its shadow.
            Assert.IsNotNull(ex.Model, "astronaut body outside");
            Assert.IsTrue(ex.Model.FirstPerson);
            foreach (Renderer r in ex.Model.BodyRenderers) Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.On, r.shadowCastingMode, r.name);
            foreach (Renderer r in ex.Model.HeadRenderers) Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly, r.shadowCastingMode, r.name);
            Assert.Less((ex.Model.Root.position - boot.CameraController.transform.position).magnitude, 2f, "the body stands under the camera");

            // Sprint (Shift): faster than walking, spends the reserve, leans the body forward.
            ex.Drive(1.0, 0.0, 1.5f, true);
            float sprintEnd = Time.realtimeSinceStartup + 1.4f;
            bool leaned = false;
            while (Time.realtimeSinceStartup < sprintEnd)
            {
                leaned |= ex.Model.LeanDegrees > 5f;
                yield return null;
            }
            Assert.IsTrue(ex.Walker.Sprinting);
            Assert.Greater(ex.Walker.HorizontalSpeed, ex.Walker.WalkSpeed * 2.0, "sprint ≈ 4.5 m/s at 8.27 m/s²");
            Assert.Less(ex.Walker.Stamina.Fraction, 1.0, "a sprint at home costs more than the critical power");
            Assert.AreEqual(SpaceSim.Exploration.Gait.Running, ex.Walker.Gait);
            Assert.IsTrue(leaned, "the body leans into the acceleration");
            ex.ToggleThirdPerson();
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.On, ex.Model.HeadRenderers[0].shadowCastingMode, "the helmet is drawn from behind");
            ex.ToggleThirdPerson();
            float stopEnd = Time.realtimeSinceStartup + 1.6f; // let the sprint end before the sample is taken
            while (Time.realtimeSinceStartup < stopEnd) yield return null;
            Assert.IsNotNull(ex.Sites);
            Assert.Greater(ex.Sites.Count, 0, "sample sites around the lander");

            // Take a sample (rules from the core): at home it is delivered at once and can be sold.
            SpaceSim.Exploration.SampleSite rock = new System.Collections.Generic.List<SpaceSim.Exploration.SampleSite>(ex.Sites).Find(s => s.Category == SpaceSim.Economy.SampleCategory.Rock || s.Category == SpaceSim.Economy.SampleCategory.Soil);
            Assert.IsNotNull(rock, "rock or soil near the pad");
            ex.TryCollect(rock);
            Assert.AreEqual(1, boot.Session.Profile.samples.Count);
            Assert.IsTrue(boot.Session.Profile.samples[0].delivered);
            Assert.IsTrue(boot.Session.Profile.samples[0].analysed, "spectrometer on board");
            Assert.IsTrue(boot.Session.Profile.HasMilestone(SpaceSim.Economy.ResearchMilestones.FirstSample));
            ex.EndEva();
            Assert.IsNull(boot.CameraController.FirstPerson);
            Assert.IsNull(ex.Model, "the body is removed inside the rocket");

            // A fall from 300 m at 40 m/s down is a crash: no engine any more; R gives a new rocket.
            var site = boot.Planet.Definition.launchSite;
            Vector3d spot = SpaceSim.Physics.PlanetRotation.DirectionFromLatLon((site.latitudeDegrees + 0.01) * SpaceSim.Mathematics.MathD.DegToRad, site.longitudeDegrees * SpaceSim.Mathematics.MathD.DegToRad);
            boot.Body.ApplyInitialConditions(site.latitudeDegrees + 0.01, site.longitudeDegrees, 300.0 + boot.Planet.Model.Surface.SurfaceHeight(spot), 0, 0, -40, true);
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!boot.Body.Simulation.IsLanded && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Assert.IsTrue(boot.Body.LastLanding.HasValue);
            Assert.AreEqual(SpaceSim.Physics.LandingResult.Crash, boot.Body.LastLanding.Value.Result);
            Assert.IsTrue(boot.Body.Destroyed);
            Assert.IsFalse(boot.Body.Launch(), "a wreck does not ignite");
            boot.NewRocketAtHome();
            yield return null;
            Assert.IsFalse(boot.Body.Destroyed);
            Assert.IsTrue(boot.Body.IsOnLaunchPad);
        }
    }
}
