using System;
using NUnit.Framework;
using SpaceSim.Mathematics;
using SpaceSim.Physics;

namespace SpaceSim.Tests
{
    public class AtmosphereTests
    {
        static IAtmosphereProfile Earth() => AtmosphericModel.Create(new AtmosphereProperties());

        [Test]
        public void ExponentialDensity_MatchesFormula()
        {
            var a = Earth();
            Assert.AreEqual(1.225, a.Density(0.0), 1e-12);
            Assert.AreEqual(1.225 / Math.E, a.Density(8500.0), 1e-12);
            Assert.AreEqual(1.225 * Math.Exp(-100000.0 / 8500.0), a.Density(100000.0), 1e-15);
        }

        [Test]
        public void Density_IsNotZeroAtSpaceBoundary_AndContinuous()
        {
            var a = Earth();
            double below = a.Density(100000.0 - 1.0), above = a.Density(100000.0 + 1.0);
            Assert.Greater(a.Density(100000.0), 0.0, "air does not end at 100 km");
            Assert.AreEqual(Math.Exp(2.0 / 8500.0), below / above, 1e-12, "smooth across the conventional boundary");
            Assert.Greater(a.Density(300000.0), 0.0);
        }

        [Test]
        public void Density_DecreasesMonotonically()
        {
            var a = Earth();
            double prev = double.MaxValue;
            for (double h = 0; h <= 200000; h += 500)
            {
                double d = a.Density(h);
                Assert.Less(d, prev);
                prev = d;
            }
        }

        [Test]
        public void TabulatedAtmosphere_InterpolatesLogLinearly()
        {
            var t = new TabulatedAtmosphere(new[]
            {
                new AtmosphereTablePoint(0, 1.2, 100000, 288),
                new AtmosphereTablePoint(10000, 0.4, 26000, 223),
                new AtmosphereTablePoint(20000, 0.09, 5500, 217)
            });
            Assert.AreEqual(1.2, t.Density(0), 1e-12);
            Assert.AreEqual(Math.Sqrt(1.2 * 0.4), t.Density(5000), 1e-12, "geometric mean halfway in a log-linear segment");
            Assert.AreEqual((288 + 223) / 2.0, t.Temperature(5000), 1e-9);
            Assert.Greater(t.Density(80000), 0.0, "exponential extrapolation never reaches zero");
            Assert.Less(t.Density(80000), t.Density(20000));
        }

        [Test]
        public void DragForce_FollowsQuadraticLaw()
        {
            var v = new Vector3d(100.0, 0, 0);
            Vector3d f = AtmosphericDrag.DragForce(1.225, v, 1.0, 1.0);
            Assert.AreEqual(-6125.0, f.x, 1e-9);
            Assert.AreEqual(0.0, f.y);
            Assert.AreEqual(6125.0, AtmosphericDrag.DragForceMagnitude(1.225, 100.0, 1.0, 1.0), 1e-9);
            Assert.AreEqual(6125.0, AtmosphericDrag.DynamicPressure(1.225, 100.0), 1e-9);
        }

        [Test]
        public void IsothermalScaleHeight_ForEarthAir()
        {
            double h = AtmosphericModel.IsothermalScaleHeight(288.15, 0.0289644, 9.82);
            Assert.AreEqual(8424.0, h, 10.0);
            Assert.AreEqual(1.225, AtmosphericModel.IdealGasDensity(101325.0, 0.0289644, 288.15), 0.001);
        }

        [Test]
        public void VisualShell_IsDerivedFromDensityProfile()
        {
            var optics = new AtmosphereOpticalProperties();
            double shell = AtmosphereOptics.VisualShellHeight(Earth(), optics);
            Assert.AreEqual(8500.0 * Math.Log(1e6), shell, 1.0);
            Assert.AreNotEqual(100000.0, shell, "visual shell is not the Kármán line");
        }

        [Test]
        public void Transmittance_DependsOnPathThroughAir()
        {
            var a = Earth();
            var optics = new AtmosphereOpticalProperties();
            double R = 6371000.0, top = R + AtmosphereOptics.VisualShellHeight(a, optics);
            var ground = new Vector3d(R + 2.0, 0, 0);
            Vector3d zenith = AtmosphereOptics.Transmittance(a, optics, R, top, ground, Vector3d.UnitX);
            Vector3d horizon = AtmosphereOptics.Transmittance(a, optics, R, top, ground, new Vector3d(0.02, 1.0, 0).Normalized);
            Vector3d space = AtmosphereOptics.Transmittance(a, optics, R, top, new Vector3d(R + 500000, 0, 0), Vector3d.UnitX);
            Vector3d blocked = AtmosphereOptics.Transmittance(a, optics, R, top, new Vector3d(R + 500000, 0, 0), -Vector3d.UnitX);

            Assert.That(zenith.z, Is.InRange(0.6, 0.8), "blue zenith transmittance ≈ e^(−β·H)");
            Assert.Greater(zenith.x, zenith.z, "red passes better than blue");
            Assert.Less(horizon.z, zenith.z * 0.2, "long path near the horizon reddens sunlight");
            Assert.AreEqual(1.0, space.x, 1e-12);
            Assert.AreEqual(0.0, blocked.x, "planet shadow");
        }
    }
}
