using System;
using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Physics;
using SpaceSim.Planets;

namespace SpaceSim.Academy
{
    /// <summary>A celestial body as the Academy sees it: Level A parameters only (SI).</summary>
    public sealed class AcademyBody
    {
        public string Id = "";
        public string Name = "";
        /// <summary>Body it orbits; null/empty = the star.</summary>
        public string ParentId;
        /// <summary>μ = G·M [m³/s²].</summary>
        public double Mu;
        /// <summary>Mean radius R [m].</summary>
        public double Radius;
        /// <summary>Semi-major axis of the orbit around the parent [m].</summary>
        public double OrbitRadius;
        /// <summary>Orbital period around the parent [s] (0 = not given).</summary>
        public double OrbitPeriod;
        public bool HasAtmosphere;

        public bool OrbitsStar => string.IsNullOrEmpty(ParentId);

        public static AcademyBody From(string id, string name, string parentId, PlanetPhysicalProperties physical, OrbitProperties orbit) =>
            new AcademyBody
            {
                Id = id ?? "",
                Name = name ?? "",
                ParentId = parentId,
                Mu = physical.GravitationalParameter,
                Radius = physical.meanRadius,
                OrbitRadius = orbit != null ? orbit.semiMajorAxis : 0.0,
                OrbitPeriod = orbit != null ? orbit.period : 0.0,
                HasAtmosphere = physical.atmosphere != null && physical.atmosphere.hasAtmosphere && physical.atmosphere.surfaceDensity > 0.0
            };
    }

    /// <summary>
    /// The numbers the Academy's worked examples, exercises, calculator presets and hints use: the current planet,
    /// the star system and (optionally) the player's rocket. Built from the same data the simulation uses, so the
    /// lessons always describe the game the player is actually in.
    /// </summary>
    public sealed class AcademyContext
    {
        public string PlanetId = "";
        public string PlanetName = "";
        /// <summary>μ [m³/s²].</summary>
        public double Mu;
        /// <summary>R [m].</summary>
        public double Radius;
        /// <summary>Speed of the ground at the launch site due to rotation, ω·R·cos φ [m/s].</summary>
        public double SiteRotationSpeed;
        /// <summary>Conventional edge of space [m] above R.</summary>
        public double SpaceBoundary;
        /// <summary>The atmosphere profile the simulation uses.</summary>
        public IAtmosphereProfile Atmosphere = VacuumAtmosphere.Instance;
        public string StarName = "";
        /// <summary>μ of the star [m³/s²].</summary>
        public double StarMu;
        public readonly List<AcademyBody> Bodies = new List<AcademyBody>();
        /// <summary>Hangar analysis of the current rocket; null when there is none.</summary>
        public RocketDesignReport Design;
        /// <summary>The current rocket's configuration (drag coefficient, size); null when there is none.</summary>
        public RocketConfiguration Rocket;

        /// <summary>g = μ/R² [m/s²] (pure gravitation).</summary>
        public double SurfaceGravity => PlanetaryGravity.AccelerationMagnitude(Mu, Radius);
        public bool HasAtmosphere => Atmosphere != null && Atmosphere.ReferenceDensity > 0.0;
        /// <summary>Scale height H [m] of an exponential atmosphere; NaN for other profiles or vacuum.</summary>
        public double ScaleHeight => Atmosphere is ExponentialAtmosphere e ? e.ScaleHeight : double.NaN;
        /// <summary>Altitude of the "standard" low orbit used by the hangar's capability estimate [m].</summary>
        public double LowOrbitAltitude => Math.Max(SpaceBoundary * 2.0, 50000.0);

        public AcademyBody Find(string id)
        {
            foreach (var b in Bodies)
                if (b.Id == id) return b;
            return null;
        }

        /// <summary>Planets that orbit the star directly (transfer targets).</summary>
        public IEnumerable<AcademyBody> Planets()
        {
            foreach (var b in Bodies)
                if (b.OrbitsStar && b.OrbitRadius > 0.0) yield return b;
        }

        public static AcademyContext Create(string planetId, string planetName, PlanetPhysicalProperties physical, double launchLatitudeDeg,
            StarProperties star, IEnumerable<AcademyBody> bodies, RocketDesignReport design)
        {
            if (physical == null) throw new ArgumentNullException(nameof(physical));
            var rotation = new PlanetRotation(physical.siderealRotationPeriod, physical.axialTiltDegrees, physical.rotationPhaseAtEpochDegrees);
            var c = new AcademyContext
            {
                PlanetId = planetId ?? "",
                PlanetName = planetName ?? "",
                Mu = physical.GravitationalParameter,
                Radius = physical.meanRadius,
                SiteRotationSpeed = rotation.AngularSpeed * physical.meanRadius * Math.Cos(launchLatitudeDeg * Math.PI / 180.0),
                SpaceBoundary = physical.spaceBoundaryAltitude,
                Atmosphere = AtmosphericModel.Create(physical.atmosphere),
                StarName = star != null ? star.name : "",
                StarMu = star != null ? star.GravitationalParameter : 0.0,
                Design = design
            };
            if (bodies != null) c.Bodies.AddRange(bodies);
            return c;
        }

        /// <summary>Context for a body of the built-in home system (tests, and a fallback when no scene data exists).</summary>
        public static AcademyContext FromHomeSystem(string activeBodyId, RocketDesignReport design = null)
        {
            List<CelestialBodyPreset> presets = HomeSystemCatalog.Bodies();
            var bodies = new List<AcademyBody>();
            CelestialBodyPreset active = null;
            foreach (var p in presets)
            {
                bodies.Add(AcademyBody.From(p.Id, p.DisplayName, p.ParentId, p.Physical, p.Orbit));
                if (p.Id == activeBodyId) active = p;
            }
            if (active == null) active = presets.Find(p => p.Id == HomeSystemCatalog.HomeWorldId) ?? presets[0];
            return Create(active.Id, active.DisplayName, active.Physical, active.LaunchLatitude, HomeSystemCatalog.Star(), bodies, design);
        }
    }
}
