using System;
using System.Collections.Generic;
using SpaceSim.Mathematics;
using SpaceSim.Physics;

namespace SpaceSim.Navigation
{
    /// <summary>
    /// A planned impulsive burn: Δv components in the orbital frame of the primary at <see cref="Time"/>
    /// (prograde = along the velocity, normal = along r×v, radial = completes the right-handed set, outward).
    /// </summary>
    [Serializable]
    public sealed class ManeuverNode
    {
        /// <summary>Simulation time of the burn [s].</summary>
        public double Time;
        public double Prograde, Normal, Radial;

        public ManeuverNode Clone() => (ManeuverNode)MemberwiseClone();
        public double Magnitude => Math.Sqrt(Prograde * Prograde + Normal * Normal + Radial * Radial);

        public static void Basis(Vector3d r, Vector3d v, out Vector3d prograde, out Vector3d normal, out Vector3d radial)
        {
            prograde = v.Normalized;
            Vector3d h = Vector3d.Cross(r, v);
            normal = h.SqrMagnitude > 0.0 ? h.Normalized : Vector3d.UnitZ;
            radial = Vector3d.Cross(prograde, normal).Normalized; // v̂ × (r̂ × v̂) = r̂ − (r̂·v̂)v̂: outward
        }

        /// <summary>Δv vector [m/s] for a state relative to the primary.</summary>
        public Vector3d DeltaVector(Vector3d r, Vector3d v)
        {
            Basis(r, v, out Vector3d p, out Vector3d n, out Vector3d rad);
            return p * Prograde + n * Normal + rad * Radial;
        }
    }

    public sealed class PredictedPatch
    {
        public int Primary;
        public double StartTime, EndTime;
        /// <summary>Exact state at <see cref="StartTime"/> relative to <see cref="Primary"/> (any time inside follows from it).</summary>
        public CoastState Start;
        /// <summary>Sampled positions relative to the star [m].</summary>
        public readonly List<Vector3d> StarPoints = new List<Vector3d>();
        /// <summary>The same samples relative to the primary [m].</summary>
        public readonly List<Vector3d> RelativePoints = new List<Vector3d>();
        public readonly List<double> Times = new List<double>();
    }

    public sealed class TrajectoryPrediction
    {
        public readonly List<PredictedPatch> Patches = new List<PredictedPatch>();
        public readonly List<PatchTransition> Transitions = new List<PatchTransition>();
        public bool Impact;
        public double ImpactTime;
        public int ImpactBody = PatchedConicSystem.Star;
        /// <summary>Body of interest (−1 = none).</summary>
        public int Target = PatchedConicSystem.Star;
        /// <summary>Smallest centre-to-centre distance to the target over the prediction [m].</summary>
        public double ClosestDistance = double.PositiveInfinity;
        public double ClosestTime;
        /// <summary>The trajectory enters the target's sphere of influence.</summary>
        public bool Encounter;
        public double EncounterTime;
        /// <summary>State relative to the target when entering its SOI.</summary>
        public CoastState Arrival;
        /// <summary>Periapsis of the arrival conic around the target [m from its centre].</summary>
        public double ArrivalPeriapsis;
        /// <summary>Hyperbolic excess speed at arrival v∞ = √(v² − 2μ/r) [m/s] (0 if captured already).</summary>
        public double ArrivalVInfinity;
        /// <summary>The node was applied (its time was inside the prediction).</summary>
        public bool NodeApplied;
        /// <summary>Δv of the node as a vector relative to the primary at the node time.</summary>
        public Vector3d NodeVector;
        /// <summary>First heliocentric (star-primary) state after leaving the origin, if any.</summary>
        public bool HasHeliocentric;
        public CoastState Heliocentric;
        public CoastState Final;
    }

    /// <summary>
    /// Patched-conic prediction of where the vehicle will go — the same analytic coast the simulation uses on rails, with
    /// moving planets. Used for the map, the flight-plan check and the assisted-mode search. Drag and thrust are not
    /// included (impulsive node); the live flight is the authority.
    /// </summary>
    public static class TrajectoryPredictor
    {
        public static TrajectoryPrediction Predict(PatchedConicSystem sys, CoastState start, double duration, int target,
            ManeuverNode node = null, int samplesPerPatch = 160, bool stopAfterArrival = true)
        {
            var p = new TrajectoryPrediction { Target = target };
            CoastState s = start;
            double end = start.Time + Math.Max(0.0, duration);

            if (node != null && node.Time >= s.Time && node.Time <= end)
            {
                // Coast to the burn without sampling (exact), apply Δv, then predict with samples.
                CoastFast(sys, ref s, node.Time, p);
                if (!p.Impact)
                {
                    p.NodeVector = node.DeltaVector(s.Position, s.Velocity);
                    s.Velocity += p.NodeVector;
                    p.NodeApplied = true;
                }
            }
            if (!p.Impact) Sample(sys, ref s, end, p, samplesPerPatch, stopAfterArrival);
            p.Final = s;
            if (!p.Encounter && target >= 0) RefineClosest(sys, p);
            return p;
        }

        /// <summary>Exact coast to <paramref name="until"/> in as few analytic steps as the SOI bounds allow.</summary>
        static void CoastFast(PatchedConicSystem sys, ref CoastState s, double until, TrajectoryPrediction p)
        {
            for (int guard = 0; guard < 100000 && s.Time < until; guard++)
            {
                double step = Math.Max(1e-3, sys.Horizon(s, until - s.Time, ignoreZones: true));
                if (ImpactWithin(sys, s, step, out double tImpact))
                {
                    sys.Coast(ref s, tImpact, p.Transitions);
                    MarkImpact(p, s);
                    return;
                }
                sys.Coast(ref s, Math.Min(step, until - s.Time), p.Transitions);
            }
        }

        static void Sample(PatchedConicSystem sys, ref CoastState s, double end, TrajectoryPrediction p, int samples, bool stopAfterArrival)
        {
            PredictedPatch patch = NewPatch(p, sys, s);
            for (int guard = 0; guard < 200000 && s.Time < end; guard++)
            {
                double remaining = end - s.Time;
                // A closed orbit that cannot leave its sphere only repeats itself: after two revolutions of samples, coast the rest
                // exactly in as few steps as the encounter bounds allow (a long horizon in low orbit would otherwise cost 10⁵ steps).
                double sampleDt = patch.Times.Count > 2 * samples && Repeats(sys, s) ? remaining : SampleInterval(sys, s, remaining, samples);
                double step = Math.Max(1e-3, Math.Min(Math.Min(sampleDt, remaining), sys.Horizon(s, remaining, ignoreZones: true)));
                if (ImpactWithin(sys, s, step, out double tImpact))
                {
                    sys.Coast(ref s, tImpact, p.Transitions);
                    AddPoint(sys, patch, s);
                    MarkImpact(p, s);
                    patch.EndTime = s.Time;
                    return;
                }
                int before = p.Transitions.Count;
                sys.Coast(ref s, step, p.Transitions);
                if (p.Transitions.Count > before)
                {
                    PatchTransition tr = p.Transitions[p.Transitions.Count - 1];
                    patch.EndTime = tr.Time;
                    if (tr.Kind == PatchEvent.SoiExit && tr.To == PatchedConicSystem.Star && !p.HasHeliocentric)
                    {
                        p.HasHeliocentric = true;
                        p.Heliocentric = s;
                    }
                    patch = NewPatch(p, sys, s);
                    if (tr.Kind == PatchEvent.SoiEntry && tr.To == p.Target && !p.Encounter)
                    {
                        p.Encounter = true;
                        p.EncounterTime = tr.Time;
                        p.Arrival = s;
                        ConicPropagator.Shape(sys.Mu(s.Primary), s.Position, s.Velocity, out _, out _, out double rp);
                        p.ArrivalPeriapsis = rp;
                        double vInf2 = s.Velocity.SqrMagnitude - 2.0 * sys.Mu(s.Primary) / s.Position.Magnitude;
                        p.ArrivalVInfinity = vInf2 > 0.0 ? Math.Sqrt(vInf2) : 0.0;
                        if (stopAfterArrival)
                        {
                            // Keep the arrival conic until its periapsis (or a short while for the map), then stop.
                            double toPeri = ConicPropagator.TimeToRadius(sys.Mu(s.Primary), s.Position, s.Velocity, rp * 1.000001);
                            double extra = double.IsInfinity(toPeri) ? 0.0 : toPeri * 1.2;
                            end = Math.Min(end, s.Time + Math.Max(extra, 600.0));
                        }
                    }
                }
                AddPoint(sys, patch, s);
                TrackTarget(sys, p, s);
            }
            patch.EndTime = s.Time;
        }

        /// <summary>Closed conic that stays inside the primary's sphere of influence.</summary>
        static bool Repeats(PatchedConicSystem sys, CoastState s)
        {
            double mu = sys.Mu(s.Primary);
            ConicPropagator.Shape(mu, s.Position, s.Velocity, out double e, out double semiLatus, out _);
            if (e >= 1.0) return false;
            return s.Primary < 0 || semiLatus / (1.0 - e) < sys[s.Primary].SoiRadius;
        }

        static double SampleInterval(PatchedConicSystem sys, CoastState s, double remaining, int samples)
        {
            double mu = sys.Mu(s.Primary);
            double alpha = 2.0 / s.Position.Magnitude - s.Velocity.SqrMagnitude / mu;
            if (alpha > 0.0)
            {
                double period = MathD.TwoPi / (Math.Sqrt(mu) * Math.Pow(alpha, 1.5));
                return Math.Max(1.0, Math.Min(period, remaining) / samples);
            }
            if (s.Primary >= 0)
            {
                double toExit = ConicPropagator.TimeToRadius(mu, s.Position, s.Velocity, sys[s.Primary].SoiRadius);
                if (!double.IsInfinity(toExit)) return Math.Max(1.0, Math.Min(toExit, remaining) / samples);
            }
            return Math.Max(1.0, remaining / samples);
        }

        static bool ImpactWithin(PatchedConicSystem sys, CoastState s, double step, out double t)
        {
            t = double.PositiveInfinity;
            if (s.Primary < 0) return false;
            double radius = sys[s.Primary].Radius;
            if (s.Position.Magnitude <= radius)
            {
                t = 0.0;
                return true;
            }
            t = ConicPropagator.TimeToRadius(sys.Mu(s.Primary), s.Position, s.Velocity, radius);
            return t <= step;
        }

        static void MarkImpact(TrajectoryPrediction p, CoastState s)
        {
            p.Impact = true;
            p.ImpactTime = s.Time;
            p.ImpactBody = s.Primary;
        }

        static PredictedPatch NewPatch(TrajectoryPrediction p, PatchedConicSystem sys, CoastState s)
        {
            var patch = new PredictedPatch { Primary = s.Primary, StartTime = s.Time, EndTime = s.Time, Start = s };
            p.Patches.Add(patch);
            AddPoint(sys, patch, s);
            return patch;
        }

        static void AddPoint(PatchedConicSystem sys, PredictedPatch patch, CoastState s)
        {
            patch.RelativePoints.Add(s.Position);
            patch.StarPoints.Add(s.Position + sys.Position(s.Primary, s.Time));
            patch.Times.Add(s.Time);
        }

        static void TrackTarget(PatchedConicSystem sys, TrajectoryPrediction p, CoastState s)
        {
            if (p.Target < 0) return;
            double d = (s.Position + sys.Position(s.Primary, s.Time) - sys.Position(p.Target, s.Time)).Magnitude;
            if (d < p.ClosestDistance)
            {
                p.ClosestDistance = d;
                p.ClosestTime = s.Time;
            }
        }

        /// <summary>Golden-section refinement of the closest approach around the best sample (exact positions from the patch).</summary>
        static void RefineClosest(PatchedConicSystem sys, TrajectoryPrediction p)
        {
            PredictedPatch patch = null;
            foreach (var pt in p.Patches)
                if (p.ClosestTime >= pt.StartTime && p.ClosestTime <= pt.EndTime) patch = pt;
            if (patch == null || patch.Times.Count < 2) return;
            int i = patch.Times.IndexOf(p.ClosestTime);
            double lo = patch.Times[Math.Max(0, i - 1)], hi = patch.Times[Math.Min(patch.Times.Count - 1, Math.Max(i, 0) + 1)];
            double Dist(double t)
            {
                ConicPropagator.Propagate(sys.Mu(patch.Primary), patch.Start.Position, patch.Start.Velocity, t - patch.StartTime, out Vector3d r, out _);
                return (r + sys.Position(patch.Primary, t) - sys.Position(p.Target, t)).Magnitude;
            }
            const double g = 0.6180339887498949;
            double a = lo, b = hi, c = b - g * (b - a), d = a + g * (b - a);
            double fc = Dist(c), fd = Dist(d);
            for (int k = 0; k < 60; k++)
            {
                if (fc < fd)
                {
                    b = d;
                    d = c;
                    fd = fc;
                    c = b - g * (b - a);
                    fc = Dist(c);
                }
                else
                {
                    a = c;
                    c = d;
                    fc = fd;
                    d = a + g * (b - a);
                    fd = Dist(d);
                }
            }
            double best = Math.Min(fc, fd);
            if (best < p.ClosestDistance)
            {
                p.ClosestDistance = best;
                p.ClosestTime = fc < fd ? c : d;
            }
        }
    }
}
