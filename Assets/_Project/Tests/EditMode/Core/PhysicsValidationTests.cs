using System;
using NUnit.Framework;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Physics;

namespace SpaceSim.Tests
{
    /// <summary>
    /// Independent validation of the central physics API against analytical / textbook results.
    /// Expected values are derived here from first principles (formulas written out in the test, published
    /// constants below) — never from the implementation itself. Reference numbers were cross-checked offline
    /// with an independent script before being written down.
    /// </summary>
    public class PhysicsValidationTests
    {
        // ---- Reference constants (sources in comments) ----
        const double G0 = 9.80665;               // standard gravity, exact by definition (3rd CGPM 1901; ISO 80000-3)
        const double G = 6.67430e-11;            // Newtonian constant of gravitation, CODATA 2018
        const double MuEarth = 3.986004418e14;   // GM_Earth, WGS-84 / IERS Conventions 2010 [m^3/s^2]
        const double REarth = 6378137.0;         // WGS-84 equatorial radius [m]
        const double MEarth = 5.9722e24;         // Earth mass [kg] (IAU 2015 nominal GM / CODATA G)
        const double MuSun = 1.32712440018e20;   // GM_Sun, IAU 2015 / JPL DE430 [m^3/s^2]
        const double AU = 1.495978707e11;        // astronomical unit, IAU 2012 Resolution B2 (exact) [m]
        const double Day = 86400.0;
        const double TEarthDays = 365.256;       // sidereal year [d]
        const double TMarsDays = 686.98;         // Mars sidereal period [d]
        const double Deg = Math.PI / 180.0;
        const double TwoPi = 2.0 * Math.PI;

        static void AssertRelative(double expected, double actual, double relativeTolerance, string message)
        {
            Assert.AreEqual(expected, actual, Math.Abs(expected) * relativeTolerance, message);
        }

        // =====================================================================================================
        // 1. Tsiolkovsky rocket equation
        // =====================================================================================================

        [Test]
        public void StandardGravityConstant_IsExactDefinition()
        {
            // g0 is the SI-defined conversion between Isp [s] and exhaust velocity [m/s] (exact 9.80665).
            Assert.AreEqual(G0, RocketEquations.G0, 0.0);
            // ve = Isp * g0 (Sutton & Biblarz, Rocket Propulsion Elements, eq. 2-6)
            Assert.AreEqual(300.0 * G0, RocketEquations.ExhaustVelocity(300.0), 1e-9);
        }

        [Test]
        public void Tsiolkovsky_Isp300_MassRatio2_Gives2039ms()
        {
            // Δv = Isp g0 ln(m0/mf) = 300 * 9.80665 * ln 2 = 2039.2355 m/s (Tsiolkovsky 1903; Sutton eq. 4-6)
            double expected = 300.0 * G0 * Math.Log(2.0);
            double dv = RocketEquations.DeltaV(300.0, 1000.0, 500.0);
            Assert.AreEqual(expected, dv, 1e-9);
            Assert.AreEqual(2039.2, dv, 0.1);
        }

        [Test]
        public void Tsiolkovsky_RoundTrips_PropellantAndMassRatio()
        {
            double dv = RocketEquations.DeltaV(300.0, 1000.0, 500.0);
            // Inverting Tsiolkovsky: m0/mf = exp(Δv/ve) must recover 2; propellant = mf (R − 1) must recover 500 kg.
            Assert.AreEqual(2.0, RocketEquations.MassRatio(dv, 300.0), 1e-12);
            Assert.AreEqual(500.0, RocketEquations.PropellantForDeltaV(dv, 300.0, 500.0), 1e-9);

            // Second case (hydrolox-like Isp 450 s, mass ratio 4): Δv = 450 g0 ln 4 = 6117.9 m/s.
            double dv2 = RocketEquations.DeltaV(450.0, 100000.0, 25000.0);
            Assert.AreEqual(450.0 * G0 * Math.Log(4.0), dv2, 1e-9);
            Assert.AreEqual(4.0, RocketEquations.MassRatio(dv2, 450.0), 1e-12);
            Assert.AreEqual(75000.0, RocketEquations.PropellantForDeltaV(dv2, 450.0, 25000.0), 1e-6);
        }

        [Test]
        public void Tsiolkovsky_ZeroPropellant_GivesZeroDeltaV()
        {
            // ln(1) = 0: no propellant burned, no velocity change.
            Assert.AreEqual(0.0, RocketEquations.DeltaV(300.0, 1000.0, 1000.0), 1e-12);
            Assert.AreEqual(1.0, RocketEquations.MassRatio(0.0, 300.0), 1e-12);
            Assert.AreEqual(0.0, RocketEquations.PropellantForDeltaV(0.0, 300.0, 500.0), 1e-12);
        }

        [Test]
        public void Tsiolkovsky_RejectsUnphysicalMasses()
        {
            // A rocket cannot end heavier than it started, and masses must be positive.
            Assert.Catch<ArgumentException>(() => RocketEquations.DeltaV(300.0, 500.0, 1000.0));
            Assert.Catch<ArgumentException>(() => RocketEquations.DeltaV(300.0, 1000.0, 0.0));
            Assert.Catch<ArgumentException>(() => RocketEquations.DeltaV(300.0, -1.0, -2.0));
        }

        // =====================================================================================================
        // 2. Engine relations: F = ṁ Isp g0
        // =====================================================================================================

        [Test]
        public void Engine_MerlinLike_MassFlow305kgPerSecond()
        {
            // Merlin 1D-like sea-level figures: F = 845 kN, Isp = 282 s → ṁ = F/(Isp g0) = 305.55 kg/s (Sutton eq. 2-17).
            double mdot = RocketEquations.MassFlow(845e3, 282.0);
            Assert.AreEqual(845e3 / (282.0 * G0), mdot, 1e-9);
            Assert.AreEqual(305.5, mdot, 0.1);
        }

        [Test]
        public void Engine_MassFlowAndThrust_AreInverses()
        {
            double[] thrusts = { 1.0, 1.0e4, 845e3, 7.6e6 };
            double[] isps = { 1.0, 282.0, 311.0, 450.0 };
            foreach (double f in thrusts)
                foreach (double isp in isps)
                {
                    double mdot = RocketEquations.MassFlow(f, isp);
                    Assert.AreEqual(f, RocketEquations.ThrustFromMassFlow(mdot, isp), f * 1e-12, "F → ṁ → F");
                    Assert.AreEqual(mdot * isp * G0, RocketEquations.ThrustFromMassFlow(mdot, isp), f * 1e-12, "F = ṁ Isp g0");
                }
        }

        [Test]
        public void Engine_BurnTime_IsPropellantOverMassFlow()
        {
            // t = m_prop / ṁ: 30 555.3 kg at 305.553 kg/s lasts 100 s.
            Assert.AreEqual(100.0, RocketEquations.BurnTime(30555.3, 305.553), 1e-9);
            // No flow ⇒ never burns out.
            Assert.IsTrue(double.IsPositiveInfinity(RocketEquations.BurnTime(1000.0, 0.0)));
        }

        [Test]
        public void Engine_BurnTimeForDeltaV_MatchesPropellantOverFlow()
        {
            // m0 = 1000 kg, Isp 300 s, Δv = 300 g0 ln 2 ⇒ 500 kg burned. F = 10 kN ⇒ ṁ = 1e4/(300 g0),
            // t = 500 / ṁ = 500·300·g0/1e4 = 147.0998 s.
            double dv = 300.0 * G0 * Math.Log(2.0);
            double expected = 500.0 * 300.0 * G0 / 1.0e4;
            Assert.AreEqual(expected, RocketEquations.BurnTimeForDeltaV(dv, 300.0, 1000.0, 1.0e4), 1e-6);
            Assert.AreEqual(147.10, expected, 0.01);
        }

        // =====================================================================================================
        // 3. Thrust-to-weight and Newton's second law
        // =====================================================================================================

        [Test]
        public void ThrustToWeight_IsThrustOverLocalWeight()
        {
            const double m = 5.0e5, g = 9.80665;
            Assert.AreEqual(1.5, RocketEquations.ThrustToWeight(1.5 * m * g, m, g), 1e-12);

            double low = RocketEquations.ThrustToWeight(0.8 * m * g, m, g);
            Assert.AreEqual(0.8, low, 1e-12);
            Assert.Less(low, 1.0, "TWR < 1: the rocket cannot lift off vertically");

            // Same rocket on the Moon (g = 1.62 m/s²): weight is local, so TWR = F/(m·1.62).
            Assert.AreEqual(0.8 * m * g / (m * 1.62), RocketEquations.ThrustToWeight(0.8 * m * g, m, 1.62), 1e-12);
            Assert.Greater(RocketEquations.ThrustToWeight(0.8 * m * g, m, 1.62), 1.0);
        }

        [Test]
        public void Acceleration_IsForceOverMass()
        {
            // Newton II: a = F/m. 7.6 MN on 550 t ⇒ 13.818 m/s².
            Assert.AreEqual(7.6e6 / 5.5e5, RocketEquations.Acceleration(7.6e6, 5.5e5), 1e-12);
            Assert.AreEqual(13.818, RocketEquations.Acceleration(7.6e6, 5.5e5), 0.001);
            // Net upward acceleration at TWR 1.5 is 0.5 g.
            const double m = 1000.0;
            Assert.AreEqual(0.5 * G0, RocketEquations.Acceleration(1.5 * m * G0 - m * G0, m), 1e-12);
        }

        // =====================================================================================================
        // 4. Newtonian gravity
        // =====================================================================================================

        [Test]
        public void Gravity_EarthEquatorialRadius_Gives9798()
        {
            // g = μ/R² = 3.986004418e14 / 6378137² = 9.79829 m/s² (pure gravitation, no rotation; WGS-84 constants).
            var earth = new PlanetaryGravity(MuEarth, REarth, Vector3d.Zero);
            Vector3d a = earth.AccelerationAt(new Vector3d(REarth, 0.0, 0.0));
            Assert.AreEqual(MuEarth / (REarth * REarth), a.Magnitude, 1e-12);
            Assert.AreEqual(9.798, a.Magnitude, 0.001);
            // Attractive: points from the point towards the centre (−x here).
            Assert.Less(a.x, 0.0);
            Assert.AreEqual(0.0, a.y, 1e-12);
            Assert.AreEqual(0.0, a.z, 1e-12);
        }

        [Test]
        public void Gravity_ForceOn1000kg_IsGMmOverRSquared()
        {
            // F = G M m / r² with CODATA G and Earth mass 5.9722e24 kg, m = 1000 kg ⇒ ≈ 9798 N.
            double mu = G * MEarth;
            // Sanity on the reference data itself: G·M_Earth agrees with the WGS-84 GM to < 1e-5.
            Assert.AreEqual(MuEarth, mu, MuEarth * 1e-5);

            var earth = new PlanetaryGravity(PlanetaryGravity.GravitationalParameterFromMass(MEarth), REarth, Vector3d.Zero);
            Vector3d f = earth.ForceOn(new Vector3d(0.0, REarth, 0.0), 1000.0);
            double expected = G * MEarth * 1000.0 / (REarth * REarth);
            Assert.AreEqual(expected, f.Magnitude, expected * 1e-12);
            Assert.AreEqual(9798.0, f.Magnitude, 5.0);
            Assert.Less(f.y, 0.0, "force points at the centre");

            // Force scales linearly with the test mass (weight is m·g).
            Vector3d f2 = earth.ForceOn(new Vector3d(0.0, REarth, 0.0), 2000.0);
            Assert.AreEqual(2.0 * f.Magnitude, f2.Magnitude, 1e-9);
        }

        [Test]
        public void Gravity_InverseSquare_AtTwoAndThreeRadii()
        {
            var earth = new PlanetaryGravity(MuEarth, REarth, Vector3d.Zero);
            double g1 = earth.AccelerationAt(new Vector3d(REarth, 0, 0)).Magnitude;
            double g2 = earth.AccelerationAt(new Vector3d(0, 0, 2.0 * REarth)).Magnitude;
            double g3 = earth.AccelerationAt(new Vector3d(-3.0 * REarth, 0, 0)).Magnitude;
            Assert.AreEqual(0.25, g2 / g1, 1e-12);       // (1/2)²
            Assert.AreEqual(1.0 / 9.0, g3 / g1, 1e-12);  // (1/3)²
            Assert.AreEqual(9.79829 / 4.0, g2, 0.0005);
        }

        [Test]
        public void Gravity_IsMeasuredFromThePlanetCentre_NotTheOrigin()
        {
            // Field of a planet sitting at (1 AU, 0, 0): at 2R above it, g = μ/(2R)² regardless of the offset.
            var offset = new Vector3d(AU, 0.0, 0.0);
            var earth = new PlanetaryGravity(MuEarth, REarth, offset);
            Vector3d a = earth.AccelerationAt(offset + new Vector3d(0.0, 2.0 * REarth, 0.0));
            Assert.AreEqual(MuEarth / (4.0 * REarth * REarth), a.Magnitude, 1e-9);
            Assert.Less(a.y, 0.0);
        }

        // =====================================================================================================
        // 5. Circular orbits, escape, period, energy
        // =====================================================================================================

        [Test]
        public void CircularVelocity_At400km_Is7668ms()
        {
            // v = √(μ/r), r = 6378137 + 400000 m ⇒ 7668.56 m/s (ISS-like orbit).
            const double r = 6778137.0;
            double v = OrbitalMechanics.CircularVelocity(MuEarth, r);
            Assert.AreEqual(Math.Sqrt(MuEarth / r), v, 1e-9);
            Assert.AreEqual(7668.6, v, 0.1);
        }

        [Test]
        public void EscapeVelocity_AtSurface_Is11_18kms()
        {
            // v_esc = √(2μ/R) = 11 179.9 m/s; v_esc / v_circ = √2 at the same radius.
            double vEsc = OrbitalMechanics.EscapeVelocity(MuEarth, REarth);
            Assert.AreEqual(11180.0, vEsc, 5.0);
            Assert.AreEqual(Math.Sqrt(2.0), vEsc / OrbitalMechanics.CircularVelocity(MuEarth, REarth), 1e-12);
        }

        [Test]
        public void CircularPeriod_LeoIs92_6Minutes_GeoIsOneSiderealDay()
        {
            // T = 2π √(r³/μ): r = 6778.137 km ⇒ 5553.6 s = 92.56 min.
            double tLeo = OrbitalMechanics.CircularPeriod(MuEarth, 6778137.0);
            Assert.AreEqual(92.56, tLeo / 60.0, 0.05);
            // GEO r = 42 164 km ⇒ 86 163.6 s; sidereal day = 86 164.09 s (IERS).
            double tGeo = OrbitalMechanics.CircularPeriod(MuEarth, 42164e3);
            Assert.AreEqual(86164.09, tGeo, 5.0);
        }

        [Test]
        public void SpecificOrbitalEnergy_CircularAndEscape()
        {
            // Circular orbit: ε = v²/2 − μ/r = −μ/(2r). At escape speed: ε = 0 (parabolic).
            const double r = 6778137.0;
            double vc = Math.Sqrt(MuEarth / r);
            Assert.AreEqual(-MuEarth / (2.0 * r), OrbitalMechanics.SpecificOrbitalEnergy(MuEarth, r, vc), 1e-3);
            double ve = Math.Sqrt(2.0 * MuEarth / r);
            Assert.AreEqual(0.0, OrbitalMechanics.SpecificOrbitalEnergy(MuEarth, r, ve), 1e-3);
            Assert.Greater(OrbitalMechanics.SpecificOrbitalEnergy(MuEarth, r, 1.01 * ve), 0.0, "hyperbolic");
        }

        // =====================================================================================================
        // 6. Hohmann transfer LEO → GEO
        // =====================================================================================================

        [Test]
        public void Hohmann_LeoToGeo_MatchesTextbook()
        {
            // r1 = 6678 km (300 km altitude), r2 = 42164 km. Vis-viva derivation:
            //   a = 24 421 km, v_c1 = 7725.8, v_p = 10 151.6 ⇒ Δv1 = 2425.8 m/s
            //   v_c2 = 3074.7, v_a = 1607.8 ⇒ Δv2 = 1466.8 m/s, total 3892.6 m/s
            //   TOF = π √(a³/μ) = 18 990 s = 5.275 h
            // Commonly quoted: ≈2.42 / 1.47 / 3.89 km/s, ~5.3 h (Curtis, Orbital Mechanics for Engineering
            // Students ch. 6; Vallado, Fundamentals of Astrodynamics ch. 6).
            const double r1 = 6678e3, r2 = 42164e3;
            HohmannTransfer h = TransferCalculator.Hohmann(MuEarth, r1, r2);

            AssertRelative(2425.8, h.DeltaV1, 0.005, "Δv1");
            AssertRelative(1466.8, h.DeltaV2, 0.005, "Δv2");
            AssertRelative(3892.6, h.TotalDeltaV, 0.005, "total Δv");
            AssertRelative(5.275 * 3600.0, h.TransferTime, 0.005, "transfer time");
            Assert.AreEqual(h.DeltaV1 + h.DeltaV2, h.TotalDeltaV, 1e-9);

            // Exact closed forms, written out independently:
            double a = 0.5 * (r1 + r2);
            double vp = Math.Sqrt(MuEarth * (2.0 / r1 - 1.0 / a));
            double va = Math.Sqrt(MuEarth * (2.0 / r2 - 1.0 / a));
            Assert.AreEqual(a, h.SemiMajorAxis, 1e-6);
            Assert.AreEqual(vp, h.DepartureSpeed, 1e-6);
            Assert.AreEqual(va, h.ArrivalSpeed, 1e-6);
            Assert.AreEqual(vp - Math.Sqrt(MuEarth / r1), h.DeltaV1, 1e-6);
            Assert.AreEqual(Math.Sqrt(MuEarth / r2) - va, h.DeltaV2, 1e-6);
            Assert.AreEqual(Math.PI * Math.Sqrt(a * a * a / MuEarth), h.TransferTime, 1e-6);
        }

        [Test]
        public void Hohmann_ConservesAngularMomentumAndEnergyOnTransferEllipse()
        {
            // Kepler II at the apsides: r_p v_p = r_a v_a. Energy: v²/2 − μ/r = −μ/(2a) at both ends.
            const double r1 = 6678e3, r2 = 42164e3;
            HohmannTransfer h = TransferCalculator.Hohmann(MuEarth, r1, r2);
            Assert.AreEqual(r1 * h.DepartureSpeed, r2 * h.ArrivalSpeed, r1 * h.DepartureSpeed * 1e-12);
            double eps = -MuEarth / (2.0 * h.SemiMajorAxis);
            Assert.AreEqual(eps, 0.5 * h.DepartureSpeed * h.DepartureSpeed - MuEarth / r1, Math.Abs(eps) * 1e-12);
            Assert.AreEqual(eps, 0.5 * h.ArrivalSpeed * h.ArrivalSpeed - MuEarth / r2, Math.Abs(eps) * 1e-12);
        }

        [Test]
        public void Hohmann_InwardTransfer_IsMirrorOfOutward()
        {
            // GEO → LEO uses the same ellipse: same total Δv and time, burns swapped. Δv magnitudes are positive.
            HohmannTransfer up = TransferCalculator.Hohmann(MuEarth, 6678e3, 42164e3);
            HohmannTransfer down = TransferCalculator.Hohmann(MuEarth, 42164e3, 6678e3);
            Assert.AreEqual(up.TotalDeltaV, down.TotalDeltaV, 1e-6);
            Assert.AreEqual(up.TransferTime, down.TransferTime, 1e-6);
            Assert.AreEqual(up.DeltaV1, down.DeltaV2, 1e-6);
            Assert.AreEqual(up.DeltaV2, down.DeltaV1, 1e-6);
            Assert.Greater(down.DeltaV1, 0.0);
            Assert.Greater(down.DeltaV2, 0.0);
        }

        [Test]
        public void Hohmann_SameOrbit_CostsNothing()
        {
            HohmannTransfer h = TransferCalculator.Hohmann(MuEarth, 7000e3, 7000e3);
            Assert.AreEqual(0.0, h.TotalDeltaV, 1e-9);
            // Half a circular period.
            Assert.AreEqual(0.5 * TwoPi * Math.Sqrt(Math.Pow(7000e3, 3) / MuEarth), h.TransferTime, 1e-6);
        }

        [Test]
        public void Hohmann_RejectsInvalidInput()
        {
            Assert.Catch<ArgumentException>(() => TransferCalculator.Hohmann(0.0, 7000e3, 8000e3));
            Assert.Catch<ArgumentException>(() => TransferCalculator.Hohmann(MuEarth, -1.0, 8000e3));
            Assert.Catch<ArgumentException>(() => TransferCalculator.Hohmann(MuEarth, 7000e3, 0.0));
        }

        // =====================================================================================================
        // 7. Heliocentric Hohmann Earth → Mars, phase angle, synodic period
        // =====================================================================================================

        [Test]
        public void Hohmann_EarthToMars_Heliocentric()
        {
            // r1 = 1 AU, r2 = 1.524 AU, μ☉ = 1.32712440018e20:
            //   v_E = 29 784.7 m/s, v_p = 32 730.8 ⇒ Δv1 (= v∞ at Earth) = 2946.1 m/s
            //   v_M = 24 126.7 m/s, v_a = 21 476.7 ⇒ Δv2 (= v∞ at Mars) = 2650.0 m/s
            //   TOF = π √(a³/μ☉) = 258.9 d   (Curtis Example 8.x; Bate-Mueller-White ch. 8: ≈2.94 / 2.65 km/s, ≈259 d)
            HohmannTransfer h = TransferCalculator.Hohmann(MuSun, AU, 1.524 * AU);
            AssertRelative(2946.1, h.DeltaV1, 0.005, "v∞ departure");
            AssertRelative(2650.0, h.DeltaV2, 0.005, "v∞ arrival");
            AssertRelative(258.9, h.TransferTime / Day, 0.005, "transfer days");
            Assert.AreEqual(2.94, h.DeltaV1 / 1000.0, 0.02);
            Assert.AreEqual(2.65, h.DeltaV2 / 1000.0, 0.02);
            Assert.AreEqual(259.0, h.TransferTime / Day, 1.0);

            // Excess speeds of a heliocentric Hohmann are exactly its two burns.
            TransferCalculator.HohmannExcessSpeeds(MuSun, AU, 1.524 * AU, out double vInfDep, out double vInfArr);
            Assert.AreEqual(h.DeltaV1, vInfDep, 1e-9);
            Assert.AreEqual(h.DeltaV2, vInfArr, 1e-9);
        }

        [Test]
        public void HohmannPhaseAngle_EarthToMars_Is44DegreesTargetLeading()
        {
            // θ = π − n_M · t_H. With n_M = √(μ☉/r_M³) (T_M = 687.19 d) and t_H = 258.92 d ⇒ θ = 44.36°.
            // Textbook value ≈ 44.3° (Curtis ch. 8; Braeunig "Rocket & Space Technology"). Positive ⇒ Mars leads.
            double theta = TransferCalculator.HohmannPhaseAngle(MuSun, AU, 1.524 * AU);
            Assert.AreEqual(44.36, theta / Deg, 0.1, "radians expected; compare in degrees");
            Assert.AreEqual(44.3, theta / Deg, 0.15);
            Assert.Greater(theta, 0.0);
        }

        [Test]
        public void HohmannPhaseAngle_EarthToVenus_IsNegativeTargetTrailing()
        {
            // Inward transfer to r = 0.723 AU: t_H = 146.0 d, θ = π − n_V t_H = −54.1° (Venus must trail Earth).
            // Commonly quoted ≈ −54° (Braeunig; Curtis ch. 8 problems).
            double theta = TransferCalculator.HohmannPhaseAngle(MuSun, AU, 0.723 * AU);
            Assert.AreEqual(-54.1, theta / Deg, 0.2);
            Assert.That(theta, Is.InRange(-Math.PI, Math.PI));
        }

        [Test]
        public void SynodicPeriod_EarthMars_Is780Days()
        {
            // S = 1 / |1/T_E − 1/T_M| = 1/(1/365.256 − 1/686.98) = 779.93 d (well-known ≈ 780 d / 26 months).
            double s = TransferCalculator.SynodicPeriod(TEarthDays * Day, TMarsDays * Day);
            Assert.AreEqual(779.93, s / Day, 0.05);
            Assert.AreEqual(780.0, s / Day, 1.0);
            // Order of the two bodies does not matter.
            Assert.AreEqual(s, TransferCalculator.SynodicPeriod(TMarsDays * Day, TEarthDays * Day), 1e-6);
            // Identical periods: geometry never changes.
            Assert.IsTrue(double.IsPositiveInfinity(TransferCalculator.SynodicPeriod(100.0, 100.0)));
        }

        // =====================================================================================================
        // 8. Patched conics: departure / capture
        // =====================================================================================================

        [Test]
        public void DepartureDeltaV_FromLeoToMarsTransfer_Is3_6kms()
        {
            // Δv = √(v∞² + 2μ/r) − √(μ/r), r = 6678 km, v∞ = 2.94 km/s ⇒ 11 314.6 − 7725.8 = 3588.8 m/s
            // (Curtis eq. 8.42; ≈3.6 km/s trans-Mars injection from LEO).
            const double r = 6678e3, vInf = 2940.0;
            double expected = Math.Sqrt(vInf * vInf + 2.0 * MuEarth / r) - Math.Sqrt(MuEarth / r);
            double dv = TransferCalculator.DepartureDeltaV(MuEarth, r, vInf);
            Assert.AreEqual(expected, dv, 1e-6);
            Assert.AreEqual(3588.8, dv, 0.5);
            Assert.AreEqual(3.6, dv / 1000.0, 0.05);
        }

        [Test]
        public void DepartureDeltaV_WithZeroExcess_IsEscapeBurn()
        {
            // v∞ = 0 ⇒ Δv = v_esc − v_circ = (√2 − 1) √(μ/r) = 3200.1 m/s from 6678 km.
            const double r = 6678e3;
            double dv = TransferCalculator.DepartureDeltaV(MuEarth, r, 0.0);
            Assert.AreEqual((Math.Sqrt(2.0) - 1.0) * Math.Sqrt(MuEarth / r), dv, 1e-6);
            Assert.AreEqual(3200.1, dv, 0.2);
        }

        [Test]
        public void CaptureDeltaV_AtMars_IsSymmetricHyperbolicFormula()
        {
            // Capture into a 300 km circular Mars orbit from v∞ = 2.65 km/s (μ_Mars = 4.282837e13, R = 3396.2 km):
            // Δv = √(v∞² + 2μ/r) − √(μ/r) ≈ 2091 m/s — same expression as departure, by time reversal.
            const double muMars = 4.282837e13, r = 3396.2e3 + 300e3, vInf = 2650.0;
            double expected = Math.Sqrt(vInf * vInf + 2.0 * muMars / r) - Math.Sqrt(muMars / r);
            double dv = TransferCalculator.CaptureDeltaV(muMars, r, vInf);
            Assert.AreEqual(expected, dv, 1e-6);
            Assert.AreEqual(2091.0, dv, 5.0);
            Assert.AreEqual(TransferCalculator.DepartureDeltaV(muMars, r, vInf), dv, 1e-12);
        }

        // =====================================================================================================
        // 9. Waiting for a launch window
        // =====================================================================================================

        [Test]
        public void TimeToPhaseAngle_ConstructedCase()
        {
            // Origin period 100 s, target period 200 s ⇒ phase rate = n2 − n1 = −π/100 rad/s, synodic period 200 s.
            double n1 = TwoPi / 100.0, n2 = TwoPi / 200.0;
            Assert.AreEqual(200.0, TransferCalculator.SynodicPeriod(100.0, 200.0), 1e-9);

            // From 0 down to −π/2 at −π/100 rad/s: 50 s.
            Assert.AreEqual(50.0, TransferCalculator.TimeToPhaseAngle(0.0, -Math.PI / 2.0, n1, n2), 1e-9);
            // +π/2 ≡ −3π/2: the phase must fall 3π/2 ⇒ 150 s.
            Assert.AreEqual(150.0, TransferCalculator.TimeToPhaseAngle(0.0, Math.PI / 2.0, n1, n2), 1e-9);
            // An unwrapped target (+π/2 + 6π) is the same geometry ⇒ same 150 s.
            Assert.AreEqual(150.0, TransferCalculator.TimeToPhaseAngle(0.0, Math.PI / 2.0 + 3.0 * TwoPi, n1, n2), 1e-9);
            // Already at the required phase ⇒ go now.
            Assert.AreEqual(0.0, TransferCalculator.TimeToPhaseAngle(0.7, 0.7, n1, n2), 1e-9);
            // Reverse rates (target faster, e.g. inward transfer): +π/2 at +π/100 rad/s ⇒ 50 s.
            Assert.AreEqual(50.0, TransferCalculator.TimeToPhaseAngle(0.0, Math.PI / 2.0, n2, n1), 1e-9);
        }

        [Test]
        public void TimeToPhaseAngle_EarthMarsFromOpposition()
        {
            // Phase = λ_Mars − λ_Earth falls at (n_E − n_M). From 0 (opposition) to +44.36° it must fall by
            // 360° − 44.36° ⇒ t = 0.87678 × 779.93 d = 683.8 d.
            double nE = TwoPi / (TEarthDays * Day), nM = TwoPi / (TMarsDays * Day);
            double theta = 44.36 * Deg;
            double t = TransferCalculator.TimeToPhaseAngle(0.0, theta, nE, nM);
            Assert.AreEqual((TwoPi - theta) / (nE - nM), t, 1.0);
            Assert.AreEqual(683.8, t / Day, 0.5);
        }

        [Test]
        public void TimeToPhaseAngle_IsAlwaysWithinOneSynodicPeriod_AndHitsTheTarget()
        {
            // Deterministic sweep over phases (including unwrapped ones) and both rate signs.
            double[] periods = { 100.0, 173.0, 365.256 * Day, 686.98 * Day };
            for (int a = 0; a < periods.Length; a++)
                for (int b = 0; b < periods.Length; b++)
                {
                    if (a == b) continue;
                    double n1 = TwoPi / periods[a], n2 = TwoPi / periods[b];
                    double rate = n2 - n1;
                    double synodic = 1.0 / Math.Abs(1.0 / periods[a] - 1.0 / periods[b]);
                    for (int i = -7; i <= 7; i++)
                        for (int j = -7; j <= 7; j++)
                        {
                            double current = 0.913 * i + 0.017;
                            double required = 1.237 * j - 0.031;
                            double t = TransferCalculator.TimeToPhaseAngle(current, required, n1, n2);
                            Assert.GreaterOrEqual(t, 0.0, "t ≥ 0");
                            Assert.Less(t, synodic * (1.0 + 1e-12), "t < synodic period");
                            // After waiting t, the phase equals the required one (mod 2π).
                            double d = current + rate * t - required;
                            d -= TwoPi * Math.Round(d / TwoPi);
                            Assert.AreEqual(0.0, d, 1e-7, "phase reached");
                        }
                }
        }

        [Test]
        public void TimeToPhaseAngle_EqualMeanMotions_NeverAligns()
        {
            Assert.IsTrue(double.IsPositiveInfinity(TransferCalculator.TimeToPhaseAngle(0.0, 1.0, 0.01, 0.01)));
        }

        // =====================================================================================================
        // 10. Rocket design analysis (Level A) vs. hand calculation and the live rocket
        // =====================================================================================================

        const double LaunchGravity = 9.81;

        /// <summary>
        /// Hand-built two-stage rocket:
        ///   S1: dry 10 t, prop 90 t, F_vac 2.0 MN, Isp 320 s vac / 280 s SL
        ///   S2: dry  2 t, prop 18 t, F_vac 0.2 MN, Isp 350 s vac / 300 s SL
        ///   payload 1 t
        /// </summary>
        static RocketConfiguration TwoStageRocket(double stage1VacuumThrust = 2.0e6)
        {
            return new RocketConfiguration
            {
                payloadMass = 1000.0,
                stages = new[]
                {
                    new RocketStage { name = "S1", dryMass = 10000.0, propellantMass = 90000.0, vacuumThrust = stage1VacuumThrust, vacuumIsp = 320.0, seaLevelIsp = 280.0, length = 30.0 },
                    new RocketStage { name = "S2", dryMass = 2000.0, propellantMass = 18000.0, vacuumThrust = 2.0e5, vacuumIsp = 350.0, seaLevelIsp = 300.0, length = 10.0 }
                }
            };
        }

        static bool HasWarning(RocketDesignReport r, DesignWarningCode code)
        {
            foreach (DesignWarning w in r.Warnings) if (w.Code == code) return true;
            return false;
        }

        [Test]
        public void DesignAnalysis_TwoStage_MatchesHandCalculation()
        {
            RocketDesignReport r = RocketDesignAnalysis.Analyze(TwoStageRocket(), LaunchGravity, 0.0);

            // Masses: dry = 10 + 2 + 1 (payload) = 13 t; propellant = 108 t; launch = 121 t.
            Assert.AreEqual(13000.0, r.DryMass, 1e-9);
            Assert.AreEqual(108000.0, r.PropellantMass, 1e-9);
            Assert.AreEqual(121000.0, r.LaunchMass, 1e-9);
            Assert.AreEqual(2, r.Stages.Count);

            // Stage 1 carries everything: 121 t → 31 t. Δv1 = 320 g0 ln(121/31) = 4273.51 m/s.
            StageReport s1 = r.Stages[0];
            Assert.AreEqual(121000.0, s1.MassAtIgnition, 1e-9);
            Assert.AreEqual(31000.0, s1.MassAtBurnout, 1e-9);
            double dv1 = 320.0 * G0 * Math.Log(121000.0 / 31000.0);
            Assert.AreEqual(dv1, s1.DeltaVVacuum, 1e-6);
            Assert.AreEqual(4273.51, s1.DeltaVVacuum, 0.01);
            // Same mass flow, lower SL exhaust velocity: Δv_SL = 280 g0 ln(121/31).
            Assert.AreEqual(280.0 * G0 * Math.Log(121000.0 / 31000.0), s1.DeltaVSurface, 1e-6);

            // Stage 1 engine: ṁ = 2e6/(320 g0) = 637.32 kg/s, t_burn = 90 000/ṁ = 141.22 s,
            // F_SL = ṁ·280·g0 = 2e6·280/320 = 1.75 MN.
            double mdot1 = 2.0e6 / (320.0 * G0);
            Assert.AreEqual(mdot1, s1.MassFlow, 1e-9);
            Assert.AreEqual(90000.0 / mdot1, s1.BurnTime, 1e-6);
            Assert.AreEqual(141.22, s1.BurnTime, 0.01);
            Assert.AreEqual(1.75e6, s1.SurfaceThrust, 1e-3);
            Assert.AreEqual(2.0e6, s1.VacuumThrust, 1e-9);

            // Stage 2 after staging: 2 + 18 + 1 = 21 t → 3 t. Δv2 = 350 g0 ln 7 = 6679.00 m/s.
            StageReport s2 = r.Stages[1];
            Assert.AreEqual(21000.0, s2.MassAtIgnition, 1e-9);
            Assert.AreEqual(3000.0, s2.MassAtBurnout, 1e-9);
            double dv2 = 350.0 * G0 * Math.Log(7.0);
            Assert.AreEqual(dv2, s2.DeltaVVacuum, 1e-6);
            Assert.AreEqual(6679.00, s2.DeltaVVacuum, 0.01);
            // Upper-stage TWR in vacuum thrust: 2e5/(21 000·9.81) = 0.9708.
            Assert.AreEqual(2.0e5 / (21000.0 * LaunchGravity), s2.IgnitionTwr, 1e-9);

            // Total ideal Δv = Σ stages = 10 952.51 m/s.
            Assert.AreEqual(dv1 + dv2, r.TotalDeltaVVacuum, 1e-6);
            Assert.AreEqual(10952.51, r.TotalDeltaVVacuum, 0.01);

            // Liftoff TWR uses SL thrust and local gravity: 1.75e6 / (121 000 · 9.81) = 1.4743.
            Assert.AreEqual(1.75e6 / (121000.0 * LaunchGravity), r.LiftoffTwr, 1e-9);
            Assert.AreEqual(1.4743, r.LiftoffTwr, 0.0001);
            Assert.IsTrue(r.CanLiftOff);

            // A sound design gets no warnings.
            Assert.AreEqual(0, r.Warnings.Count, r.Warnings.Count > 0 ? r.Warnings[0].Message : "");
        }

        [Test]
        public void DesignAnalysis_Staging_BeatsSingleStageEquivalent()
        {
            // Staging check: one stage with all 108 t propellant and 13 t dry mass would give at most
            // 350 g0 ln(121/13) = 7657 m/s even at the better Isp — dropping 10 t of dead mass must beat it.
            RocketDesignReport r = RocketDesignAnalysis.Analyze(TwoStageRocket(), LaunchGravity, 0.0);
            double singleStage = 350.0 * G0 * Math.Log(121000.0 / 13000.0);
            Assert.Greater(r.TotalDeltaVVacuum, singleStage);
        }

        [Test]
        public void DesignAnalysis_WeakFirstStage_WarnsCannotLiftOff()
        {
            // F_vac 1.0 MN ⇒ F_SL = 0.875 MN < weight 121 000 · 9.81 = 1.187 MN ⇒ TWR 0.737.
            RocketDesignReport r = RocketDesignAnalysis.Analyze(TwoStageRocket(1.0e6), LaunchGravity, 0.0);
            Assert.AreEqual(0.875e6 / (121000.0 * LaunchGravity), r.LiftoffTwr, 1e-9);
            Assert.Less(r.LiftoffTwr, 1.0);
            Assert.IsFalse(r.CanLiftOff);
            Assert.IsTrue(HasWarning(r, DesignWarningCode.CannotLiftOff));
            // Thrust does not enter Tsiolkovsky: Δv is unchanged by the weaker engine.
            Assert.AreEqual(10952.51, r.TotalDeltaVVacuum, 0.01);
        }

        [Test]
        public void DesignAnalysis_RequiredDeltaV_TriggersInsufficientDeltaV()
        {
            RocketDesignReport tooMuch = RocketDesignAnalysis.Analyze(TwoStageRocket(), LaunchGravity, 12000.0);
            Assert.IsFalse(tooMuch.MeetsRequirement);
            Assert.IsTrue(HasWarning(tooMuch, DesignWarningCode.InsufficientDeltaV));
            Assert.IsFalse(HasWarning(tooMuch, DesignWarningCode.CannotLiftOff));

            RocketDesignReport enough = RocketDesignAnalysis.Analyze(TwoStageRocket(), LaunchGravity, 9400.0);
            Assert.IsTrue(enough.MeetsRequirement);
            Assert.IsFalse(HasWarning(enough, DesignWarningCode.InsufficientDeltaV));
        }

        [Test]
        public void DesignAnalysis_AgreesWithLiveRocketDeltaV()
        {
            // Level A (hangar) and the live rocket must use identical physics.
            RocketConfiguration config = TwoStageRocket();
            RocketDesignReport r = RocketDesignAnalysis.Analyze(config, LaunchGravity, 0.0);
            var live = new RocketState(config);
            Assert.AreEqual(r.TotalDeltaVVacuum, live.DeltaVRemaining(), 1e-9);
            Assert.AreEqual(r.LaunchMass, live.Mass, 1e-9);
            // Liftoff TWR as the live rocket sees it at sea level (p/p0 = 1).
            Assert.AreEqual(r.LiftoffTwr, live.AvailableThrustToWeight(1.0, LaunchGravity), 1e-9);

            // Drop stage 1 (full or empty, it is gone either way): the rest equals the stage-2 report.
            Assert.IsTrue(live.Separate());
            Assert.AreEqual(r.Stages[1].DeltaVVacuum, live.DeltaVRemaining(), 1e-9);
            Assert.AreEqual(r.Stages[1].MassAtIgnition, live.Mass, 1e-9);
        }

        [Test]
        public void LiveRocket_IntegratedBurn_ReproducesTsiolkovsky()
        {
            // Numerically integrate a = F/m over the stage-1 vacuum burn using only the live state's thrust and
            // mass (trapezoid rule, independent of the closed form). Must equal 320 g0 ln(121/31) = 4273.51 m/s.
            var state = new RocketState(TwoStageRocket()) { EngineOn = true, Throttle = 1.0 };
            double thrust = state.Thrust(0.0);
            double mdot = state.MassFlow;
            const double dt = 0.01;
            double dv = 0.0;
            int guard = 0;
            while (state.IsBurning && guard++ < 1000000)
            {
                double mBefore = state.Mass;
                state.Burn(dt);
                double mAfter = state.Mass;
                double dtEff = (mBefore - mAfter) / mdot; // the last step may be shortened by flame-out
                dv += thrust * dtEff * 0.5 * (1.0 / mBefore + 1.0 / mAfter);
            }
            Assert.IsFalse(state.EngineOn, "flame-out at the end of the stage");
            Assert.AreEqual(320.0 * G0 * Math.Log(121000.0 / 31000.0), dv, 0.01);
            Assert.AreEqual(31000.0, state.Mass, 1e-6);
        }

        // =====================================================================================================
        // 11. Live rocket state
        // =====================================================================================================

        [Test]
        public void RocketState_BurnForExactlyTheBurnTime_EmptiesStageAndShutsDown()
        {
            var state = new RocketState(TwoStageRocket()) { EngineOn = true, Throttle = 1.0 };
            double mdot = 2.0e6 / (320.0 * G0); // ṁ = F_vac/(Isp_vac g0)
            Assert.AreEqual(mdot, state.MassFlow, 1e-9);
            double burnTime = 90000.0 / mdot;   // 141.22 s

            state.Burn(burnTime);
            Assert.AreEqual(0.0, state.Propellant[0], 1e-6, "stage empty");
            Assert.IsFalse(state.EngineOn, "engine flamed out");
            Assert.IsFalse(state.IsBurning);
            Assert.AreEqual(0.0, state.MassFlow, 0.0);
            Assert.AreEqual(0.0, state.Thrust(0.0), 0.0);
            Assert.AreEqual(31000.0, state.Mass, 1e-6, "121 t − 90 t propellant");
            // Upper stage untouched.
            Assert.AreEqual(18000.0, state.Propellant[1], 0.0);
        }

        [Test]
        public void RocketState_OverlongBurn_NeverGoesNegative()
        {
            var state = new RocketState(TwoStageRocket()) { EngineOn = true, Throttle = 1.0 };
            state.Burn(10000.0);
            Assert.AreEqual(0.0, state.Propellant[0], 0.0);
            Assert.IsFalse(state.EngineOn);
            Assert.AreEqual(31000.0, state.Mass, 1e-9);
        }

        [Test]
        public void RocketState_ThrustDependsOnAmbientPressure()
        {
            // Constant ṁ; F = ṁ g0 Isp(p), Isp(p) linear between vacuum (p/p0 = 0) and sea level (p/p0 = 1).
            var state = new RocketState(TwoStageRocket()) { EngineOn = true, Throttle = 1.0 };
            double mdot = 2.0e6 / (320.0 * G0);
            Assert.AreEqual(2.0e6, state.Thrust(0.0), 1e-6, "vacuum thrust");
            Assert.AreEqual(mdot * 280.0 * G0, state.Thrust(1.0), 1e-6, "sea-level Isp");
            Assert.AreEqual(1.75e6, state.Thrust(1.0), 1e-3);
            Assert.AreEqual(mdot * 300.0 * G0, state.Thrust(0.5), 1e-6, "half pressure ⇒ Isp 300 s");
            Assert.AreEqual(320.0, state.Isp(0.0), 1e-12);
            Assert.AreEqual(280.0, state.Isp(1.0), 1e-12);
        }

        [Test]
        public void RocketState_ThrottleScalesMassFlowAndThrust_EngineOffGivesNothing()
        {
            var state = new RocketState(TwoStageRocket()) { EngineOn = true, Throttle = 0.5 };
            double mdotFull = 2.0e6 / (320.0 * G0);
            Assert.AreEqual(0.5 * mdotFull, state.MassFlow, 1e-9);
            Assert.AreEqual(1.0e6, state.Thrust(0.0), 1e-6);

            // Half throttle burns half as fast: after 10 s, 10·ṁ/2 kg consumed.
            state.Burn(10.0);
            Assert.AreEqual(90000.0 - 5.0 * mdotFull, state.Propellant[0], 1e-6);

            state.EngineOn = false;
            Assert.AreEqual(0.0, state.MassFlow, 0.0);
            Assert.AreEqual(0.0, state.Thrust(0.0), 0.0);
            double before = state.Propellant[0];
            state.Burn(100.0);
            Assert.AreEqual(before, state.Propellant[0], 0.0, "no burn with the engine off");
        }

        [Test]
        public void RocketState_SeparationDropsStageMass()
        {
            var state = new RocketState(TwoStageRocket());
            Assert.AreEqual(121000.0, state.Mass, 1e-9);
            Assert.IsTrue(state.Separate());
            Assert.AreEqual(21000.0, state.Mass, 1e-9);
            Assert.AreEqual(1, state.StageIndex);
            Assert.IsTrue(state.Separate());
            Assert.AreEqual(1000.0, state.Mass, 1e-9, "payload only");
            Assert.AreEqual(0.0, state.DeltaVRemaining(), 0.0);
            Assert.IsFalse(state.Separate(), "nothing left to drop");
        }
    }
}
