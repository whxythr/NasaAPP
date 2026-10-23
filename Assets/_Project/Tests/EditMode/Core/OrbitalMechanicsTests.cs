using System;
using NUnit.Framework;
using SpaceSim.Mathematics;
using SpaceSim.Physics;

namespace SpaceSim.Tests
{
    public class OrbitalMechanicsTests
    {
        const double Mu = TestPlanets.EarthMu;
        const double R = TestPlanets.EarthRadius;

        [Test]
        public void CircularVelocity_AtIssAltitude()
        {
            Assert.AreEqual(7672.6, OrbitalMechanics.CircularVelocity(Mu, R + 400000.0), 0.5);
            Assert.AreEqual(Math.Sqrt(Mu / (R + 400000.0)), OrbitalMechanics.CircularVelocity(Mu, R + 400000.0), 1e-12);
        }

        [Test]
        public void EscapeVelocity_AtSurface()
        {
            Assert.AreEqual(11185.7, OrbitalMechanics.EscapeVelocity(Mu, R), 1.0);
            Assert.AreEqual(Math.Sqrt(2.0), OrbitalMechanics.EscapeVelocity(Mu, R) / OrbitalMechanics.CircularVelocity(Mu, R), 1e-12);
        }

        [Test]
        public void CircularPeriod_AtIssAltitude()
        {
            double r = R + 400000.0;
            Assert.AreEqual(2.0 * Math.PI * Math.Sqrt(r * r * r / Mu), OrbitalMechanics.CircularPeriod(Mu, r), 1e-9);
            Assert.AreEqual(5545.0, OrbitalMechanics.CircularPeriod(Mu, r), 2.0); // ≈ 92.4 min
        }

        [Test]
        public void SpecificEnergy_SignClassifiesTrajectory()
        {
            double r = R + 200000.0;
            double vc = OrbitalMechanics.CircularVelocity(Mu, r);
            double ve = OrbitalMechanics.EscapeVelocity(Mu, r);
            Assert.Less(OrbitalMechanics.SpecificOrbitalEnergy(Mu, r, vc), 0.0);
            Assert.AreEqual(0.0, OrbitalMechanics.SpecificOrbitalEnergy(Mu, r, ve), 1e-6);
            Assert.Greater(OrbitalMechanics.SpecificOrbitalEnergy(Mu, r, ve * 1.01), 0.0);
            Assert.AreEqual(-Mu / (2.0 * r), OrbitalMechanics.SpecificOrbitalEnergy(Mu, r, vc), 1e-6, "circular: ε = −μ/2r");
        }

        [Test]
        public void Elements_ForCircularOrbit()
        {
            double r = R + 400000.0;
            var el = OrbitalMechanics.ComputeElements(Mu, new Vector3d(r, 0, 0),
                new Vector3d(0, OrbitalMechanics.CircularVelocity(Mu, r), 0), Vector3d.UnitZ);
            Assert.Less(el.Eccentricity, 1e-12);
            Assert.AreEqual(r, el.PeriapsisRadius, 1e-6);
            Assert.AreEqual(r, el.ApoapsisRadius, 1e-6);
            Assert.AreEqual(r, el.SemiMajorAxis, 1e-6);
            Assert.AreEqual(0.0, el.Inclination, 1e-12);
            Assert.IsTrue(el.IsBound);
        }

        [Test]
        public void Elements_ForEllipseFromPeriapsis()
        {
            double rp = R + 200000.0;
            double v = 1.05 * OrbitalMechanics.CircularVelocity(Mu, rp);
            var el = OrbitalMechanics.ComputeElements(Mu, new Vector3d(rp, 0, 0), new Vector3d(0, 0, v), Vector3d.UnitZ);
            // At periapsis: e = r·v²/μ − 1
            Assert.AreEqual(rp * v * v / Mu - 1.0, el.Eccentricity, 1e-12);
            Assert.AreEqual(rp, el.PeriapsisRadius, 1e-4);
            Assert.AreEqual(el.SemiMajorAxis * (1.0 + el.Eccentricity), el.ApoapsisRadius, 1e-3);
            Assert.AreEqual(v, OrbitalMechanics.VisVivaSpeed(Mu, rp, el.SemiMajorAxis), 1e-6);
            Assert.AreEqual(Math.PI / 2.0, el.Inclination, 1e-12, "polar orbit");
        }

        [Test]
        public void Elements_ForHyperbola()
        {
            double r = R + 300000.0;
            double v = 1.2 * OrbitalMechanics.EscapeVelocity(Mu, r);
            var el = OrbitalMechanics.ComputeElements(Mu, new Vector3d(r, 0, 0), new Vector3d(0, v, 0), Vector3d.UnitZ);
            Assert.IsFalse(el.IsBound);
            Assert.Greater(el.Eccentricity, 1.0);
            Assert.Less(el.SemiMajorAxis, 0.0);
            Assert.IsTrue(double.IsPositiveInfinity(el.ApoapsisRadius));
            Assert.IsTrue(double.IsPositiveInfinity(el.Period));
        }

        [Test]
        public void VerticalShot_HasPeriapsisAtCentre()
        {
            // Purely radial motion: zero angular momentum → degenerate ellipse through the centre.
            var el = OrbitalMechanics.ComputeElements(Mu, new Vector3d(R + 150000, 0, 0), new Vector3d(2000, 0, 0), Vector3d.UnitZ);
            Assert.AreEqual(0.0, el.PeriapsisRadius, 1e-6);
            Assert.IsTrue(el.IsBound);
        }
    }
}
