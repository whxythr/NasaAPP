using NUnit.Framework;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Units;

namespace SpaceSim.Tests
{
    public class GravityTests
    {
        [Test]
        public void SurfaceGravity_EqualsMuOverRSquared()
        {
            var g = new PlanetaryGravity(TestPlanets.EarthMu, TestPlanets.EarthRadius, Vector3d.Zero);
            double expected = TestPlanets.EarthMu / (TestPlanets.EarthRadius * TestPlanets.EarthRadius);
            Assert.AreEqual(expected, g.SurfaceGravity, 1e-12);
            // Pure gravitation at the mean radius is ≈ 9.820 m/s² (the familiar 9.81 includes rotation/latitude).
            Assert.AreEqual(9.820, g.SurfaceGravity, 0.001);
            Assert.AreEqual(g.SurfaceGravity, g.AccelerationAt(new Vector3d(TestPlanets.EarthRadius, 0, 0)).Magnitude, 1e-12);
        }

        [Test]
        public void Gravity_FollowsInverseSquareLaw()
        {
            var g = new PlanetaryGravity(TestPlanets.EarthMu, TestPlanets.EarthRadius, Vector3d.Zero);
            double g1 = g.AccelerationAt(new Vector3d(TestPlanets.EarthRadius, 0, 0)).Magnitude;
            double g2 = g.AccelerationAt(new Vector3d(0, 2 * TestPlanets.EarthRadius, 0)).Magnitude;
            double g400 = g.AccelerationAt(new Vector3d(0, 0, TestPlanets.EarthRadius + 400000)).Magnitude;
            Assert.AreEqual(0.25, g2 / g1, 1e-12);
            Assert.AreEqual(8.69, g400, 0.01); // ISS altitude: still ~88 % of surface gravity
            Assert.Less(g400, g1);
        }

        [Test]
        public void Gravity_PointsToCentre()
        {
            var centre = new Vector3d(1e7, -2e6, 3e5);
            var g = new PlanetaryGravity(TestPlanets.EarthMu, TestPlanets.EarthRadius, centre);
            var p = centre + new Vector3d(3e6, 4e6, 5e6);
            Vector3d a = g.AccelerationAt(p);
            Vector3d toCentre = (centre - p).Normalized;
            Assert.AreEqual(1.0, Vector3d.Dot(a.Normalized, toCentre), 1e-14);
        }

        [Test]
        public void ForceOnMass_IsGMm_OverR2()
        {
            var g = new PlanetaryGravity(TestPlanets.EarthMu, TestPlanets.EarthRadius, Vector3d.Zero);
            double r = 7.0e6, m = 1234.0;
            double expected = TestPlanets.EarthMu * m / (r * r);
            Assert.AreEqual(expected, g.ForceOn(new Vector3d(r, 0, 0), m).Magnitude, expected * 1e-14);
        }

        [Test]
        public void ChangingMassOrRadius_UpdatesGravity()
        {
            var p = PlanetPhysicalProperties.CreateEarth();
            p.gravitySource = GravitationalParameterSource.FromMass;
            var earth = new PlanetModel("A", p, null, Vector3d.Zero);

            p.mass *= 2.0;
            var heavy = new PlanetModel("B", p, null, Vector3d.Zero);
            var point = new Vector3d(0, 0, 8.0e6);
            Assert.AreEqual(2.0, heavy.Gravity.AccelerationAt(point).Magnitude / earth.Gravity.AccelerationAt(point).Magnitude, 1e-12);

            p.mass /= 2.0;
            p.meanRadius *= 0.5;
            var small = new PlanetModel("C", p, null, Vector3d.Zero);
            Assert.AreEqual(4.0, small.Properties.SurfaceGravity / earth.Properties.SurfaceGravity, 1e-12);
        }

        [Test]
        public void MassAndMeasuredMu_AreConsistentForEarth()
        {
            var p = PlanetPhysicalProperties.CreateEarth();
            Assert.Less(p.GravitationalParameterMismatch, 1e-4);
            Assert.AreEqual(PhysicalConstants.GravitationalConstant * p.mass, p.GravitationalParameter, 1.0);
            Assert.AreEqual(5514.0, p.MeanDensity, 5.0);
        }

        [Test]
        public void GravityField_SumsSources()
        {
            var a = new PlanetaryGravity(1e14, 1e6, new Vector3d(-1e7, 0, 0));
            var b = new PlanetaryGravity(1e14, 1e6, new Vector3d(1e7, 0, 0));
            var field = new GravityField();
            field.Add(a);
            field.Add(b);
            // Midpoint between two equal masses: forces cancel.
            Assert.Less(field.AccelerationAt(Vector3d.Zero).Magnitude, 1e-15);
        }
    }
}
