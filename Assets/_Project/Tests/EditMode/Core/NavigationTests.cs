using System;
using NUnit.Framework;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Navigation;
using SpaceSim.Physics;
using SpaceSim.Planets;

namespace SpaceSim.Tests
{
    /// <summary>
    /// Milestone 5 physics validation: exact two-body propagation, rails bodies, spheres of influence, patched-conic coasting,
    /// agreement of the analytic coast with numerical integration, launch windows with moving planets, and the flight-plan check.
    /// </summary>
    public class NavigationTests
    {
        const double Day = 86400.0;
        static readonly PatchedConicSystem Sys = PatchedConicSystem.FromHomeSystem();
        static int Home => Sys.IndexOf(HomeSystemCatalog.HomeWorldId);
        static int Mora => Sys.IndexOf(HomeSystemCatalog.HermeousMoraId);

        static void Rk4(Func<Vector3d, double, Vector3d> accel, ref Vector3d r, ref Vector3d v, double t, double h)
        {
            Vector3d k1v = accel(r, t), k1r = v;
            Vector3d k2v = accel(r + k1r * (h / 2), t + h / 2), k2r = v + k1v * (h / 2);
            Vector3d k3v = accel(r + k2r * (h / 2), t + h / 2), k3r = v + k2v * (h / 2);
            Vector3d k4v = accel(r + k3r * h, t + h), k4r = v + k3v * h;
            r += (k1r + k2r * 2 + k3r * 2 + k4r) * (h / 6);
            v += (k1v + k2v * 2 + k3v * 2 + k4v) * (h / 6);
        }

        // ---- Exact two-body motion -----------------------------------------------------------------------------

        [Test]
        public void Conic_MatchesNumericalIntegration_Ellipse_And_Hyperbola()
        {
            double mu = TestPlanets.EarthMu;
            foreach (double speedFactor in new[] { 1.0, 1.2, 1.6 }) // circle, ellipse, hyperbola (escape is √2 ≈ 1.414)
            {
                var r0 = new Vector3d(7.0e6, 0.0, 0.0);
                var v0 = new Vector3d(0.0, 0.3, 1.0).Normalized * (Math.Sqrt(mu / 7.0e6) * speedFactor);
                Vector3d r = r0, v = v0;
                double t = 0.0, h = 1.0;
                for (int i = 0; i < 4000; i++, t += h) Rk4((p, _) => p * (-mu / Math.Pow(p.Magnitude, 3)), ref r, ref v, t, h);
                ConicPropagator.Propagate(mu, r0, v0, 4000.0, out Vector3d ra, out Vector3d va);
                Assert.Less((ra - r).Magnitude, 1.0, "position after 4000 s, v/v_c = " + speedFactor);
                Assert.Less((va - v).Magnitude, 1e-3, "velocity");
            }
        }

        [Test]
        public void Conic_ConservesEnergyAndMomentum_ReturnsAfterOnePeriod_AndRunsBackwards()
        {
            double mu = TestPlanets.EarthMu;
            var r0 = new Vector3d(6.9e6, 1.0e5, 2.0e5);
            var v0 = new Vector3d(-200.0, 8200.0, 900.0);
            double eps0 = 0.5 * v0.SqrMagnitude - mu / r0.Magnitude;
            Vector3d h0 = Vector3d.Cross(r0, v0);
            double a = -mu / (2.0 * eps0), period = MathD.TwoPi * Math.Sqrt(a * a * a / mu);

            ConicPropagator.Propagate(mu, r0, v0, 0.37 * period, out Vector3d r, out Vector3d v);
            Assert.AreEqual(eps0, 0.5 * v.SqrMagnitude - mu / r.Magnitude, Math.Abs(eps0) * 1e-10);
            Assert.Less((Vector3d.Cross(r, v) - h0).Magnitude, h0.Magnitude * 1e-10);

            ConicPropagator.Propagate(mu, r0, v0, period * 1000.0 + 12.0, out Vector3d rMany, out _);
            ConicPropagator.Propagate(mu, r0, v0, 12.0, out Vector3d rOne, out _);
            Assert.Less((rMany - rOne).Magnitude, 1e-2, "1000 periods later — same point (ellipses repeat)");

            ConicPropagator.Propagate(mu, r, v, -0.37 * period, out Vector3d back, out _);
            Assert.Less((back - r0).Magnitude, 1e-3, "backwards in time returns to the start");
        }

        [Test]
        public void TimeToRadius_HalfPeriodToApoapsis_InfinityForCircles()
        {
            double mu = TestPlanets.EarthMu, rp = 7.0e6, ra = 1.4e7, a = 0.5 * (rp + ra);
            double vp = Math.Sqrt(mu * (2.0 / rp - 1.0 / a));
            var r0 = new Vector3d(rp, 0, 0);
            var v0 = new Vector3d(0, vp, 0);
            double period = MathD.TwoPi * Math.Sqrt(a * a * a / mu);
            Assert.AreEqual(0.5 * period, ConicPropagator.TimeToRadius(mu, r0, v0, ra * (1 - 1e-12)), 1.0);
            Assert.IsTrue(double.IsPositiveInfinity(ConicPropagator.TimeToRadius(mu, r0, v0, 2.0 * ra)), "beyond apoapsis");
            Assert.IsTrue(double.IsPositiveInfinity(ConicPropagator.TimeToRadius(mu, r0, new Vector3d(0, Math.Sqrt(mu / rp), 0), 8e6)), "circle");

            var vEsc = new Vector3d(0, 1.3 * Math.Sqrt(2 * mu / rp), 0);
            double tSoi = ConicPropagator.TimeToRadius(mu, r0, vEsc, 9.0e8);
            ConicPropagator.Propagate(mu, r0, vEsc, tSoi, out Vector3d rAt, out _);
            Assert.AreEqual(9.0e8, rAt.Magnitude, 1.0, "hyperbola reaches the SOI radius exactly then");
        }

        // ---- Bodies on rails and spheres of influence ----------------------------------------------------------

        [Test]
        public void KeplerOrbit_VelocityAndAcceleration_AreDerivativesOfPosition()
        {
            var orbit = new KeplerOrbit(new OrbitProperties { semiMajorAxis = 1.0e11, eccentricity = 0.2, inclinationDegrees = 7, meanAnomalyAtEpochDegrees = 40 }, 1.3e20);
            double t = 1.0e6, h = 10.0;
            Vector3d dv = (orbit.Position(t + h) - orbit.Position(t - h)) / (2 * h);
            Assert.Less((dv - orbit.Velocity(t)).Magnitude, 1e-4 * orbit.Velocity(t).Magnitude);
            Vector3d da = (orbit.Velocity(t + h) - orbit.Velocity(t - h)) / (2 * h);
            Assert.Less((da - orbit.Acceleration(t)).Magnitude, 1e-4 * orbit.Acceleration(t).Magnitude);
        }

        [Test]
        public void SphereOfInfluence_KnownValues_AndPrimaryHierarchy()
        {
            Assert.AreEqual(9.25e8, PatchedConicSystem.SphereOfInfluence(1.496e11, 3.986e14, 1.327e20), 0.01e8, "Earth's SOI ≈ 925 000 km");
            NavBody home = Sys[Home], mora = Sys[Mora];
            int moon = Sys.IndexOf(HomeSystemCatalog.MoraMoonId);
            double t = 1234.0;
            Assert.AreEqual(Home, Sys.PrimaryAt(Sys.Position(Home, t) + new Vector3d(home.Radius * 3, 0, 0), t, PatchedConicSystem.Star));
            Assert.AreEqual(PatchedConicSystem.Star, Sys.PrimaryAt(Sys.Position(Home, t) + new Vector3d(home.SoiRadius * 1.1, 0, 0), t, Home));
            Assert.AreEqual(moon, Sys.PrimaryAt(Sys.Position(moon, t) + new Vector3d(Sys[moon].Radius * 2, 0, 0), t, PatchedConicSystem.Star), "nested: star → Mora → moon");
            Assert.Less(Sys[moon].Orbit.SemiMajorAxis, mora.SoiRadius, "the moon orbits inside Mora's SOI");
            Assert.Greater(home.ZoneRadius - home.Radius, 150000.0, "numeric zone covers the atmosphere");
            Assert.Less(home.ZoneRadius - home.Radius, 300000.0);

            var r = new Vector3d(7.0e6, 1.0e6, 0);
            Vector3d g = Sys.GravityInFrame(Home, Home, r, t);
            Assert.Less((g - r * (-home.Mu / Math.Pow(r.Magnitude, 3))).Magnitude, 1e-12, "inside its SOI only the body pulls");
        }

        // ---- Patched-conic coasting ------------------------------------------------------------------------------

        static CoastState CircularAroundHome(double altitude, double t, double speedFactor = 1.0, double inclinationDeg = 0.0)
        {
            NavBody home = Sys[Home];
            double r = home.Radius + altitude;
            double v = Math.Sqrt(home.Mu / r) * speedFactor;
            double i = inclinationDeg * Math.PI / 180.0;
            return new CoastState(Home, new Vector3d(r, 0, 0), new Vector3d(0, v * Math.Cos(i), v * Math.Sin(i)), t);
        }

        [Test]
        public void Coast_LeavesTheSoi_AndAgreesWithNumericalPatchedGravity()
        {
            CoastState s = CircularAroundHome(400000.0, 0.0, 1.45); // hyperbolic escape
            CoastState analytic = s;
            var transitions = new System.Collections.Generic.List<PatchTransition>();
            double duration = 3.0 * Day;
            while (analytic.Time < duration)
            {
                double step = Math.Min(duration - analytic.Time, Math.Max(1.0, Sys.Horizon(analytic, duration - analytic.Time, true)));
                Sys.Coast(ref analytic, step, transitions);
            }
            Assert.AreEqual(PatchedConicSystem.Star, analytic.Primary, "left the home SOI");
            Assert.AreEqual(1, transitions.Count);
            Assert.AreEqual(PatchEvent.SoiExit, transitions[0].Kind);

            // Numerical RK4 of the same patched model in the star frame (gravity of the current primary + its rails acceleration).
            Vector3d r = s.Position + Sys.Position(Home, 0), v = s.Velocity + Sys.Velocity(Home, 0);
            int primary = Home;
            double h = 2.0;
            for (double t = 0.0; t < duration - 1e-9; t += h)
            {
                int p = primary;
                Rk4((x, tt) => Sys.GravityInFrame(PatchedConicSystem.Star, p, x, tt), ref r, ref v, t, h);
                primary = Sys.PrimaryAt(r, t + h, primary);
            }
            Vector3d rAnalytic = analytic.Position + Sys.Position(analytic.Primary, analytic.Time);
            Assert.Less((rAnalytic - r).Magnitude, 2000.0, "analytic coast = integrated patched conics (≈ 3·10⁹ m travelled)");
        }

        [Test]
        public void FreeFlight_RailsStep_EqualsNumericalOrbit()
        {
            CelestialBodyPreset home = HomeSystemCatalog.Bodies().Find(b => b.Id == HomeSystemCatalog.HomeWorldId);
            var planet = new PlanetModel(home.DisplayName, home.Physical, ReferenceSphereSurface.Instance, Vector3d.Zero);
            FreeFlightSimulation Make()
            {
                var field = new GravityField();
                field.Add(planet.Gravity);
                var sim = new FreeFlightSimulation(planet, field) { ContactRadius = 1.0 };
                sim.SetNavigation(Sys, Home);
                CoastState c = CircularAroundHome(400000.0, 0.0);
                sim.SetInertialState(c.Position, c.Velocity, 0.0);
                return sim;
            }
            var numeric = Make();
            var rails = Make();
            Assert.Greater(rails.RailsHorizon(1e6), 1e5, "400 km is above the numeric zone: may coast on rails");
            double period = MathD.TwoPi * Math.Sqrt(Math.Pow(Sys[Home].Radius + 400000.0, 3) / Sys[Home].Mu);
            int steps = (int)(period / 0.02);
            for (int i = 0; i < steps; i++) numeric.Step(0.02);
            rails.RailsStep(steps * 0.02, 0.02);
            Assert.Less((rails.State.Position - numeric.State.Position).Magnitude, 5.0, "one orbit: analytic and integrated agree");
            Assert.AreEqual(numeric.State.Time, rails.State.Time, 1e-6);

            var low = Make();
            low.SetInertialState(new Vector3d(planet.Radius + 100000.0, 0, 0), new Vector3d(0, 7600, 0), 0.0);
            Assert.AreEqual(0.0, low.RailsHorizon(1e6), "100 km: inside the atmosphere zone — always integrated");
        }

        // ---- Launch windows and the flight plan -----------------------------------------------------------------

        [Test]
        public void Window_IsWhereThePhaseAngleReallyIs_WithMovingPlanets()
        {
            var input = new FlightPlanInput { Target = Mora };
            TransferRequirements q = FlightPlanner.Requirements(Sys, Home, input, 1000.0, null);
            Assert.IsTrue(q.Ok, q.Error);
            HohmannTransfer h = TransferCalculator.Hohmann(Sys.StarMu, Sys[Home].Orbit.SemiMajorAxis, Sys[Mora].Orbit.SemiMajorAxis);
            Assert.AreEqual(h.TransferTime, q.TransferTime, 1e-6);
            Assert.AreEqual(46.0, q.TransferTime / Day, 1.0, "≈ 46 days inward to Mora");
            Assert.Less(q.RequiredPhase, 0.0, "inward transfer: the target must trail");
            double phaseAtWindow = FlightPlanner.PhaseAngle(Sys, Home, Mora, q.WindowTime);
            Assert.AreEqual(q.RequiredPhase, phaseAtWindow, 0.5 * Math.PI / 180.0, "at the window the planets really are at θ");
            Assert.LessOrEqual(q.TimeToWindow, q.SynodicPeriod + 1.0);
        }

        static PlannerVehicle Vehicle(CoastState s, double dv = 20000.0) => new PlannerVehicle
        {
            DeltaV = dv, Mass = 20000.0, MassWithoutPropellant = 6000.0, LastStageIsp = 350.0, HasState = true, State = s,
            RadiationShielding = 1, AcidResistance = 1
        };

        [TestCase(0.0)]
        [TestCase(30.0)]
        public void Assisted_FindsAnEncounter_AndTheSimulationFliesIt(double inclinationDeg)
        {
            CoastState park = CircularAroundHome(300000.0, 0.0, 1.0, inclinationDeg);
            var input = new FlightPlanInput { Target = Mora };
            ManeuverNode node = FlightPlanner.SuggestDeparture(Sys, Home, input, 0.0, Vehicle(park), out TrajectoryPrediction pr);
            Assert.IsNotNull(node);
            Assert.IsTrue(pr.Encounter, "suggested burn reaches Mora's SOI (closest " + pr.ClosestDistance + " m)");
            input.Departure = node;
            FlightPlanResult result = FlightPlanner.Validate(Sys, Home, input, 0.0, Vehicle(park));
            Assert.IsTrue(result.Passed, string.Join("; ", result.Issues.ConvertAll(i => i.Reason)));

            // The flight itself: rails coast to the burn, impulsive burn, rails coast — the vehicle arrives in Mora's SOI.
            CelestialBodyPreset home = HomeSystemCatalog.Bodies().Find(b => b.Id == HomeSystemCatalog.HomeWorldId);
            var planet = new PlanetModel(home.DisplayName, home.Physical, ReferenceSphereSurface.Instance, Vector3d.Zero);
            var field = new GravityField();
            field.Add(planet.Gravity);
            var sim = new FreeFlightSimulation(planet, field);
            sim.SetNavigation(Sys, Home);
            sim.SetInertialState(park.Position, park.Velocity, 0.0);
            while (sim.State.Time < node.Time - 1e-6) sim.RailsStep(Math.Min(node.Time - sim.State.Time, Math.Max(0.02, sim.RailsHorizon(node.Time - sim.State.Time))), 0.0);
            CoastState c = sim.CoastStateNow();
            sim.SetInertialState(sim.State.Position, sim.State.Velocity + node.DeltaVector(c.Position, c.Velocity), sim.State.Time);
            double until = pr.EncounterTime + 3600.0;
            while (sim.State.Time < until && sim.Primary != Mora)
                sim.RailsStep(Math.Min(until - sim.State.Time, Math.Max(0.02, sim.RailsHorizon(until - sim.State.Time))), 0.0);
            Assert.AreEqual(Mora, sim.Primary, "the simulated vehicle enters Mora's sphere of influence");
            Assert.AreEqual(pr.EncounterTime, sim.State.Time, 3600.0, "when the prediction said");
        }

        [Test]
        public void Validation_ExplainsWhatIsWrong()
        {
            CoastState park = CircularAroundHome(300000.0, 0.0);
            var input = new FlightPlanInput { Target = Mora };
            TransferRequirements q = FlightPlanner.Requirements(Sys, Home, input, 0.0, Vehicle(park));
            ManeuverNode good = FlightPlanner.SuggestDeparture(Sys, Home, input, 0.0, Vehicle(park), out _);

            input.Departure = null;
            Assert.IsTrue(FlightPlanner.Validate(Sys, Home, input, 0.0, Vehicle(park)).Issues.Exists(i => i.Code == PlanIssueCode.NoDeparture));

            input.Departure = new ManeuverNode { Time = good.Time, Prograde = 0.5 * q.DepartureDeltaV };
            FlightPlanResult weak = FlightPlanner.Validate(Sys, Home, input, 0.0, Vehicle(park));
            Assert.IsFalse(weak.Passed);
            Assert.IsTrue(weak.Issues.Exists(i => i.Code == PlanIssueCode.DepartureVelocity && i.Reason.Contains("недостаточна")), weak.Verdict);

            input.Departure = new ManeuverNode { Time = good.Time + 0.25 * q.SynodicPeriod, Prograde = good.Prograde, Normal = good.Normal };
            FlightPlanResult late = FlightPlanner.Validate(Sys, Home, input, 0.0, Vehicle(park));
            Assert.IsFalse(late.Passed);
            Assert.IsTrue(late.Issues.Exists(i => i.Code == PlanIssueCode.MissedWindow || i.Code == PlanIssueCode.InvalidTrajectory),
                string.Join("; ", late.Issues.ConvertAll(i => i.Reason)));

            input.Departure = good;
            FlightPlanResult poor = FlightPlanner.Validate(Sys, Home, input, 0.0, Vehicle(park, 3000.0));
            Assert.IsTrue(poor.Issues.Exists(i => i.Code == PlanIssueCode.InsufficientDeltaV));
            Assert.IsTrue(poor.Issues.Exists(i => i.Code == PlanIssueCode.InsufficientFuel));

            var landed = Vehicle(park);
            landed.Landed = true;
            Assert.IsTrue(FlightPlanner.Validate(Sys, Home, input, 0.0, landed).Issues.Exists(i => i.Code == PlanIssueCode.NotInOrbit));

            var bare = Vehicle(park);
            bare.RadiationShielding = 0;
            input.Target = Sys.IndexOf(HomeSystemCatalog.TeonanId);
            Assert.IsTrue(FlightPlanner.Validate(Sys, Home, input, 0.0, bare).Issues.Exists(i => i.Code == PlanIssueCode.Protection), "Teonan: radiation 2");
        }

        [Test]
        public void Prediction_LongHorizonInLowOrbit_StaysCheap()
        {
            // The live HUD/map prediction runs twice a second: 83 days ahead from a 300 km orbit must not sample every orbit.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            TrajectoryPrediction p = TrajectoryPredictor.Predict(Sys, CircularAroundHome(300000.0, 0.0), 83.0 * Day, Mora);
            sw.Stop();
            int points = 0;
            foreach (var patch in p.Patches) points += patch.Times.Count;
            Assert.Less(points, 2000, "samples");
            Assert.Less(sw.ElapsedMilliseconds, 200, "milliseconds");
            Assert.AreEqual(83.0 * Day, p.Final.Time, 1e-3, "still predicts to the end");
            Assert.AreEqual(Home, p.Final.Primary);
        }

        [Test]
        public void MissionRequirements_FromTheSurface_AddUp()
        {
            TransferRequirements q = FlightPlanner.MissionFromSurface(Sys, Home, Mora, 0.0, 400.0, true);
            Assert.IsTrue(q.Ok);
            Assert.AreEqual(q.AscentDeltaV + q.DepartureDeltaV + q.CaptureDeltaV + q.LandingDeltaV, q.TotalDeltaV, 1e-9);
            Assert.Greater(q.AscentDeltaV, 6000.0);
            Assert.Greater(q.LandingDeltaV, 0.0);
            Assert.IsNotEmpty(q.LandingNote);
        }
    }
}
