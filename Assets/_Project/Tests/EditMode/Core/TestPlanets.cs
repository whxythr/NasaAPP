using SpaceSim.Flight;
using SpaceSim.Physics;
using SpaceSim.Planets;

namespace SpaceSim.Tests
{
    /// <summary>Planet fixtures for tests (Earth parameters, with optional simplifications).</summary>
    internal static class TestPlanets
    {
        public const double EarthMu = 3.986004418e14;
        public const double EarthRadius = 6371000.0;

        public static PlanetPhysicalProperties EarthProperties(bool rotating = true, bool atmosphere = true)
        {
            var p = PlanetPhysicalProperties.CreateEarth();
            p.gravitySource = GravitationalParameterSource.Measured;
            p.measuredGravitationalParameter = EarthMu;
            if (!rotating) p.siderealRotationPeriod = 0.0;
            p.atmosphere.hasAtmosphere = atmosphere;
            return p;
        }

        public static PlanetModel Earth(bool rotating = true, bool atmosphere = true, ISurfaceHeightProvider surface = null) =>
            new PlanetModel("Earth", EarthProperties(rotating, atmosphere), surface ?? ReferenceSphereSurface.Instance, Mathematics.Vector3d.Zero);

        public static FreeFlightSimulation Simulation(PlanetModel planet, bool drag = false)
        {
            var field = new GravityField();
            field.Add(planet.Gravity);
            var sim = new FreeFlightSimulation(planet, field)
            {
                Drag = new DragProperties(drag, 1.0, 1.0),
                ContactRadius = 0.0
            };
            return sim;
        }
    }
}
