using System;
using System.Collections.Generic;
using SpaceSim.Mathematics;
using SpaceSim.Planets;

namespace SpaceSim.Physics
{
    /// <summary>What a body contributes to navigation (built from the same planet data the simulation uses).</summary>
    public sealed class NavBodySpec
    {
        public string Id = "";
        public string Name = "";
        /// <summary>Body it orbits; null/empty = the star.</summary>
        public string ParentId;
        public PlanetPhysicalProperties Physical;
        public OrbitProperties Orbit;
        /// <summary>Highest terrain above the reference radius [m] (impacts are always integrated numerically).</summary>
        public double MaxTerrainHeight = 15000.0;
        public EnvironmentHazards Hazards = new EnvironmentHazards();
        public string Description = "";
    }

    public sealed class NavBody
    {
        public int Index;
        public string Id = "", Name = "";
        /// <summary>Index of the body it orbits; −1 = the star.</summary>
        public int ParentIndex = -1;
        public double Mu, Radius;
        public KeplerOrbit Orbit;
        /// <summary>Sphere of influence r_SOI = a·(μ/μ_parent)^(2/5) [m].</summary>
        public double SoiRadius;
        /// <summary>Below this distance from the centre motion is always integrated numerically (drag, terrain) [m].</summary>
        public double ZoneRadius;
        public IAtmosphereProfile Atmosphere = VacuumAtmosphere.Instance;
        public PlanetPhysicalProperties Physical;
        public EnvironmentHazards Hazards = new EnvironmentHazards();
        public string Description = "";
    }

    /// <summary>A trajectory state relative to its current primary (−1 = the star).</summary>
    public struct CoastState
    {
        public int Primary;
        public Vector3d Position, Velocity;
        public double Time;

        public CoastState(int primary, Vector3d position, Vector3d velocity, double time)
        {
            Primary = primary;
            Position = position;
            Velocity = velocity;
            Time = time;
        }
    }

    public enum PatchEvent
    {
        /// <summary>Left the primary's sphere of influence.</summary>
        SoiExit,
        /// <summary>Entered another body's sphere of influence.</summary>
        SoiEntry
    }

    public readonly struct PatchTransition
    {
        public PatchTransition(PatchEvent kind, double time, int from, int to)
        {
            Kind = kind;
            Time = time;
            From = from;
            To = to;
        }

        public PatchEvent Kind { get; }
        public double Time { get; }
        public int From { get; }
        public int To { get; }
    }

    /// <summary>
    /// The star system as seen by navigation: bodies on Kepler rails (<see cref="KeplerOrbit"/>), spheres of influence and the
    /// patched-conic gravity model. Inside a body's SOI the vehicle feels only that body; outside every planet's SOI — only
    /// the star. Motion relative to the current primary is exact two-body motion, so the numerical integrator and the
    /// analytic coast (<see cref="Coast"/>, via <see cref="ConicPropagator"/>) solve the same equations.
    /// Positions are star-centric unless stated otherwise; body index −1 stands for the star (at the origin, at rest).
    /// </summary>
    public sealed class PatchedConicSystem
    {
        /// <summary>Density below which drag is negligible for coasting on rails [kg/m³] (≈ 10⁻⁶·g of drag in a low orbit).</summary>
        public const double RailsDensity = 1e-10;
        /// <summary>Entering a SOI needs to be this far inside it; leaving — outside it (no flapping at the boundary).</summary>
        public const double EntryFactor = 0.9999;
        public const int Star = -1;

        readonly List<NavBody> bodies = new List<NavBody>();

        public PatchedConicSystem(StarProperties star, IList<NavBodySpec> specs)
        {
            StarProperties = star ?? throw new ArgumentNullException(nameof(star));
            StarMu = star.GravitationalParameter;
            for (int i = 0; i < specs.Count; i++)
            {
                NavBodySpec s = specs[i];
                bodies.Add(new NavBody
                {
                    Index = i, Id = s.Id ?? "", Name = s.Name ?? "", Mu = s.Physical.GravitationalParameter, Radius = s.Physical.meanRadius,
                    Atmosphere = AtmosphericModel.Create(s.Physical.atmosphere), Physical = s.Physical,
                    Hazards = s.Hazards ?? new EnvironmentHazards(), Description = s.Description ?? ""
                });
            }
            for (int i = 0; i < specs.Count; i++)
            {
                NavBody b = bodies[i];
                b.ParentIndex = string.IsNullOrEmpty(specs[i].ParentId) ? Star : IndexOf(specs[i].ParentId);
                double parentMu = Mu(b.ParentIndex);
                b.Orbit = new KeplerOrbit(specs[i].Orbit, parentMu);
                // Tiny moons next to big planets can have a Laplace sphere hardly larger than themselves (Phobos: ~7 km vs 11 km);
                // patched conics need room to approach and orbit, so the sphere is never smaller than 3 radii (documented approximation).
                b.SoiRadius = Math.Max(SphereOfInfluence(b.Orbit.SemiMajorAxis, b.Mu, parentMu), 3.0 * b.Radius);
                b.ZoneRadius = Math.Min(b.Radius + NumericZoneAltitude(b.Atmosphere, specs[i].MaxTerrainHeight), 0.5 * b.SoiRadius);
            }
        }

        public StarProperties StarProperties { get; }
        public double StarMu { get; }
        public IReadOnlyList<NavBody> Bodies => bodies;
        public int Count => bodies.Count;
        public NavBody this[int index] => bodies[index];

        public int IndexOf(string id)
        {
            for (int i = 0; i < bodies.Count; i++)
                if (bodies[i].Id == id) return i;
            return -1;
        }

        public string NameOf(int index) => index < 0 ? StarProperties.name : bodies[index].Name;

        /// <summary>Laplace's sphere of influence r = a·(m/M)^(2/5) [m].</summary>
        public static double SphereOfInfluence(double semiMajorAxis, double mu, double parentMu) =>
            semiMajorAxis * Math.Pow(mu / parentMu, 0.4);

        /// <summary>Altitude below which coasting must be integrated: atmosphere denser than <see cref="RailsDensity"/>, or terrain + 5 km.</summary>
        public static double NumericZoneAltitude(IAtmosphereProfile atmosphere, double maxTerrainHeight)
        {
            double h = Math.Max(0.0, maxTerrainHeight) + 5000.0;
            if (atmosphere != null && atmosphere.ReferenceDensity > RailsDensity)
            {
                double step = 1000.0, alt = 0.0;
                while (alt < 3.0e6 && atmosphere.Density(alt) > RailsDensity) alt += step;
                h = Math.Max(h, alt);
            }
            return h;
        }

        public double Mu(int index) => index < 0 ? StarMu : bodies[index].Mu;
        public int Parent(int index) => index < 0 ? Star : bodies[index].ParentIndex;

        // ---- Bodies on rails (star-centric) ------------------------------------------------------------------

        public Vector3d Position(int index, double time)
        {
            Vector3d p = Vector3d.Zero;
            int guard = 0;
            while (index >= 0 && guard++ < 16)
            {
                p += bodies[index].Orbit.Position(time);
                index = bodies[index].ParentIndex;
            }
            return p;
        }

        public Vector3d Velocity(int index, double time)
        {
            Vector3d v = Vector3d.Zero;
            int guard = 0;
            while (index >= 0 && guard++ < 16)
            {
                v += bodies[index].Orbit.Velocity(time);
                index = bodies[index].ParentIndex;
            }
            return v;
        }

        /// <summary>Acceleration of the body's centre along its rails (the frame acceleration of a frame centred on it).</summary>
        public Vector3d Acceleration(int index, double time)
        {
            Vector3d a = Vector3d.Zero;
            int guard = 0;
            while (index >= 0 && guard++ < 16)
            {
                a += bodies[index].Orbit.Acceleration(time);
                index = bodies[index].ParentIndex;
            }
            return a;
        }

        // ---- Frames ------------------------------------------------------------------------------------------

        /// <summary>Re-expresses a state given relative to body <paramref name="from"/> relative to body <paramref name="to"/>.</summary>
        public void Transform(int from, int to, double time, ref Vector3d position, ref Vector3d velocity)
        {
            if (from == to) return;
            position += Position(from, time) - Position(to, time);
            velocity += Velocity(from, time) - Velocity(to, time);
        }

        public bool IsAncestorOrSelf(int ancestor, int index)
        {
            int guard = 0;
            while (guard++ < 16)
            {
                if (index == ancestor) return true;
                if (index < 0) return false;
                index = bodies[index].ParentIndex;
            }
            return false;
        }

        /// <summary>
        /// The primary at a star-centric point: the smallest sphere of influence containing it. <paramref name="current"/> gives
        /// hysteresis: a SOI that already contains the vehicle is kept up to its full radius, a new one needs
        /// <see cref="EntryFactor"/>.
        /// </summary>
        public int PrimaryAt(Vector3d starCentric, double time, int current)
        {
            int primary = Star;
            for (int depth = 0; depth < 8; depth++)
            {
                int found = -1;
                for (int i = 0; i < bodies.Count; i++)
                {
                    if (bodies[i].ParentIndex != primary) continue;
                    double limit = bodies[i].SoiRadius * (IsAncestorOrSelf(i, current) ? 1.0 : EntryFactor);
                    if ((starCentric - Position(i, time)).SqrMagnitude < limit * limit)
                    {
                        found = i;
                        break;
                    }
                }
                if (found < 0) break;
                primary = found;
            }
            return primary;
        }

        /// <summary>
        /// Patched-conic gravitational acceleration in a (non-rotating, translating) frame centred on body <paramref name="frame"/>:
        /// g_primary(r − R_primary) + A_primary − A_frame. Equals the frame body's own gravity when primary = frame.
        /// </summary>
        public Vector3d GravityInFrame(int frame, int primary, Vector3d positionInFrame, double time)
        {
            Vector3d rel = positionInFrame + Position(frame, time) - Position(primary, time);
            double d = rel.Magnitude;
            Vector3d g = d > 0.0 ? rel * (-Mu(primary) / (d * d * d)) : Vector3d.Zero;
            return g + Acceleration(primary, time) - Acceleration(frame, time);
        }

        // ---- Coasting ----------------------------------------------------------------------------------------

        /// <summary>
        /// How long the state can coast analytically [s], at most <paramref name="maxDt"/>: 0 inside a numeric zone (atmosphere,
        /// near the ground); stops before the zone is reached; never so long that a sphere of influence could be skipped.
        /// </summary>
        public double Horizon(CoastState s, double maxDt, bool ignoreZones = false)
        {
            double horizon = maxDt;
            if (s.Primary >= 0 && !ignoreZones)
            {
                double zone = bodies[s.Primary].ZoneRadius;
                if (s.Position.Magnitude <= zone) return 0.0;
                double toZone = ConicPropagator.TimeToRadius(Mu(s.Primary), s.Position, s.Velocity, zone);
                horizon = Math.Min(horizon, toZone * (1.0 - 1e-9));
            }
            return Math.Min(horizon, EncounterBound(s));
        }

        /// <summary>Conservative lower bound on the time to enter any sphere of influence the state is outside of [s].</summary>
        double EncounterBound(CoastState s)
        {
            double bound = double.PositiveInfinity;
            int parent = Parent(s.Primary);
            Vector3d rStar = s.Position + Position(s.Primary, s.Time);
            Vector3d vStar = s.Velocity + Velocity(s.Primary, s.Time);
            for (int i = 0; i < bodies.Count; i++)
            {
                NavBody b = bodies[i];
                bool child = b.ParentIndex == s.Primary;
                bool sibling = s.Primary >= 0 && b.ParentIndex == parent && i != s.Primary;
                if (!child && !sibling) continue;
                double gap = (rStar - Position(i, s.Time)).Magnitude - b.SoiRadius;
                if (gap <= 0.0) continue;
                double closing = (vStar - Velocity(i, s.Time)).Magnitude + 1.0;
                bound = Math.Min(bound, Math.Max(1.0, 0.5 * gap / closing));
            }
            return bound;
        }

        /// <summary>
        /// Advances a state analytically by <paramref name="dt"/> (patched conics), switching primary at SOI exits and entries
        /// (exact exit time; entry found by bisection). The caller limits <paramref name="dt"/> with <see cref="Horizon"/>.
        /// </summary>
        public void Coast(ref CoastState s, double dt, List<PatchTransition> transitions = null)
        {
            double remaining = dt;
            for (int guard = 0; guard < 16 && remaining > 0.0; guard++)
            {
                double mu = Mu(s.Primary);
                double toExit = s.Primary >= 0 ? ConicPropagator.TimeToRadius(mu, s.Position, s.Velocity, bodies[s.Primary].SoiRadius) : double.PositiveInfinity;
                bool exits = toExit <= remaining;
                double seg = exits ? toExit : remaining;
                ConicPropagator.Propagate(mu, s.Position, s.Velocity, seg, out Vector3d r1, out Vector3d v1);

                int entered = EnteredAt(s.Primary, r1, s.Time + seg);
                if (entered >= 0)
                {
                    // First moment inside: bisection on [0, seg] (the start is outside, the end inside).
                    double lo = 0.0, hi = seg;
                    for (int k = 0; k < 48; k++)
                    {
                        double mid = 0.5 * (lo + hi);
                        ConicPropagator.Propagate(mu, s.Position, s.Velocity, mid, out Vector3d rm, out _);
                        if (Inside(entered, s.Primary, rm, s.Time + mid)) hi = mid;
                        else lo = mid;
                    }
                    ConicPropagator.Propagate(mu, s.Position, s.Velocity, hi, out Vector3d re, out Vector3d ve);
                    double te = s.Time + hi;
                    Transform(s.Primary, entered, te, ref re, ref ve);
                    transitions?.Add(new PatchTransition(PatchEvent.SoiEntry, te, s.Primary, entered));
                    s = new CoastState(entered, re, ve, te);
                    remaining -= hi;
                    continue;
                }

                double t1 = s.Time + seg;
                if (exits)
                {
                    int parent = Parent(s.Primary);
                    Transform(s.Primary, parent, t1, ref r1, ref v1);
                    transitions?.Add(new PatchTransition(PatchEvent.SoiExit, t1, s.Primary, parent));
                    s = new CoastState(parent, r1, v1, t1);
                }
                else s = new CoastState(s.Primary, r1, v1, t1);
                remaining -= seg;
            }
        }

        int EnteredAt(int primary, Vector3d position, double time)
        {
            int parent = Parent(primary);
            for (int i = 0; i < bodies.Count; i++)
            {
                bool child = bodies[i].ParentIndex == primary;
                bool sibling = primary >= 0 && bodies[i].ParentIndex == parent && i != primary;
                if ((child || sibling) && Inside(i, primary, position, time)) return i;
            }
            return -1;
        }

        bool Inside(int body, int primary, Vector3d positionRelPrimary, double time)
        {
            double limit = bodies[body].SoiRadius * EntryFactor;
            return (positionRelPrimary + Position(primary, time) - Position(body, time)).SqrMagnitude < limit * limit;
        }

        // ---- Construction from the built-in system ------------------------------------------------------------

        public static PatchedConicSystem FromHomeSystem()
        {
            var specs = new List<NavBodySpec>();
            foreach (CelestialBodyPreset p in HomeSystemCatalog.Bodies())
                specs.Add(new NavBodySpec
                {
                    Id = p.Id, Name = p.DisplayName, ParentId = p.ParentId, Physical = p.Physical, Orbit = p.Orbit,
                    Hazards = p.Hazards, Description = p.Description
                });
            return new PatchedConicSystem(HomeSystemCatalog.Star(), specs);
        }
    }
}
