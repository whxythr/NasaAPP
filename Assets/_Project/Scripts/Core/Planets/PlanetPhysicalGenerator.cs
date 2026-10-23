using System;
using SpaceSim.Physics;
using SpaceSim.Procedural;
using SpaceSim.Units;

namespace SpaceSim.Planets
{
    /// <summary>Ranges used when physical properties are generated for a new world.</summary>
    [Serializable]
    public class PlanetPhysicalGenerationRanges
    {
        public double minRadius = 1.5e6;
        public double maxRadius = 9.0e6;
        public double minMeanDensity = 3000.0;
        public double maxMeanDensity = 6000.0;
        public double minRotationPeriodHours = 8.0;
        public double maxRotationPeriodHours = 80.0;
        public double retrogradeProbability = 0.1;
        public double maxAxialTiltDegrees = 45.0;
        public double atmosphereProbability = 0.7;
        public double minSurfacePressure = 500.0;
        public double maxSurfacePressure = 5.0e5;
        public double minSurfaceTemperature = 160.0;
        public double maxSurfaceTemperature = 380.0;
        public double minMolarMass = 0.017;
        public double maxMolarMass = 0.044;
    }

    /// <summary>
    /// Generates physically self-consistent planet parameters from a seed (world-creation time only).
    /// The result is meant to be stored (see PlanetSaveData) and never re-derived during play, so later
    /// changes to this generator cannot alter existing worlds.
    /// </summary>
    public static class PlanetPhysicalGenerator
    {
        public const int AlgorithmVersion = 1;

        public static PlanetPhysicalProperties Generate(int physicalSeed, PlanetPhysicalGenerationRanges ranges)
        {
            ranges = ranges ?? new PlanetPhysicalGenerationRanges();
            var rng = new DeterministicRandom(DeterministicRandom.Mix((ulong)(uint)physicalSeed, 0x9E3779B97F4A7C15UL));
            var p = new PlanetPhysicalProperties();

            p.meanRadius = rng.Range(ranges.minRadius, ranges.maxRadius);
            double density = rng.Range(ranges.minMeanDensity, ranges.maxMeanDensity);
            p.mass = density * 4.0 / 3.0 * Math.PI * p.meanRadius * p.meanRadius * p.meanRadius;
            p.gravitySource = GravitationalParameterSource.FromMass;
            p.measuredGravitationalParameter = PhysicalConstants.GravitationalConstant * p.mass;

            double periodHours = rng.Range(ranges.minRotationPeriodHours, ranges.maxRotationPeriodHours);
            p.siderealRotationPeriod = periodHours * 3600.0 * (rng.NextDouble() < ranges.retrogradeProbability ? -1.0 : 1.0);
            p.axialTiltDegrees = rng.Range(0.0, ranges.maxAxialTiltDegrees);
            p.rotationPhaseAtEpochDegrees = rng.Range(0.0, 360.0);

            var a = new AtmosphereProperties();
            a.hasAtmosphere = rng.NextDouble() < ranges.atmosphereProbability;
            a.surfaceTemperature = rng.Range(ranges.minSurfaceTemperature, ranges.maxSurfaceTemperature);
            a.meanMolarMass = rng.Range(ranges.minMolarMass, ranges.maxMolarMass);
            // Log-uniform surface pressure.
            double logP = rng.Range(Math.Log(ranges.minSurfacePressure), Math.Log(ranges.maxSurfacePressure));
            a.surfacePressure = a.hasAtmosphere ? Math.Exp(logP) : 0.0;
            double g = p.SurfaceGravity;
            a.scaleHeight = AtmosphericModel.IsothermalScaleHeight(a.surfaceTemperature, a.meanMolarMass, g);
            a.surfaceDensity = a.hasAtmosphere ? AtmosphericModel.IdealGasDensity(a.surfacePressure, a.meanMolarMass, a.surfaceTemperature) : 0.0;
            a.coRotationFactor = 1.0;
            p.atmosphere = a;

            // Conventional boundary: ~12 scale heights (≈100 km for Earth) or 50 km for airless bodies.
            p.spaceBoundaryAltitude = a.hasAtmosphere ? Math.Round(12.0 * a.scaleHeight / 1000.0) * 1000.0 : 50000.0;
            return p;
        }
    }
}
