using System;
using System.Threading.Tasks;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Navigation;
using SpaceSim.Physics;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>Burn guidance for the HUD (display only: the player flies the burn).</summary>
    public sealed class NodeGuidance
    {
        public bool Active;
        /// <summary>Seconds until the engine should be lit (node time − half the burn); negative = burn in progress/overdue.</summary>
        public double TimeToIgnition;
        public double TimeToNode;
        /// <summary>Δv still to apply [m/s] (before the node: the whole node; after ignition: planned − actual velocity).</summary>
        public double Remaining;
        /// <summary>Estimated burn duration at full thrust [s].</summary>
        public double BurnTime;
        /// <summary>Attitude that points the thrust along the remaining Δv.</summary>
        public double Pitch, Heading;
        public bool Started;
    }

    /// <summary>
    /// The player's flight plan and its live checks. All numbers come from Core navigation (<see cref="FlightPlanner"/>,
    /// <see cref="TrajectoryPredictor"/>, <see cref="PatchedConicSystem"/>) using a snapshot of the real vehicle. Lives at the
    /// bootstrap level, so the plan survives sphere-of-influence handoffs.
    /// </summary>
    [DefaultExecutionOrder(160)]
    public sealed class NavigationService : MonoBehaviour
    {
        const float PredictInterval = 0.5f;
        const double Day = 86400.0;

        SpaceSimBootstrap boot;
        float nextPredict;
        Task<(ManeuverNode node, TrajectoryPrediction prediction)> suggestTask;
        CoastState plannedAfter;
        bool havePlannedAfter;
        bool frozen;

        public FlightPlanInput Plan { get; } = new FlightPlanInput();
        public FlightPlanResult LastResult { get; private set; }
        /// <summary>Live Level A numbers for the selected target (refreshed every half second).</summary>
        public TransferRequirements Requirements { get; private set; }
        /// <summary>Live patched-conic prediction from the vehicle's current state (with the planned node).</summary>
        public TrajectoryPrediction Prediction { get; private set; }
        public NodeGuidance Guidance { get; } = new NodeGuidance();
        public bool Suggesting => suggestTask != null && !suggestTask.IsCompleted;
        public string Status { get; private set; } = "";
        /// <summary>Raised after the plan, the check or the suggestion changed.</summary>
        public event Action Changed;

        public PatchedConicSystem System => boot != null && boot.System != null ? boot.System.Navigation : null;
        public double Now => boot.Simulation.SimulationTime;
        public bool Assisted => boot.Session == null || boot.Session.Profile.settings.assistedPlanning;

        /// <summary>Body the vehicle departs from: its current primary, or the frame body while cruising around the star.</summary>
        public int Origin
        {
            get
            {
                FreeFlightSimulation sim = boot.Body != null ? boot.Body.Simulation : null;
                if (sim == null) return boot.System.ActiveIndex;
                return sim.Primary >= 0 ? sim.Primary : sim.FrameBody;
            }
        }

        public void Initialize(SpaceSimBootstrap bootstrap)
        {
            boot = bootstrap;
            Plan.Target = PatchedConicSystem.Star;
        }

        public void SetAssisted(bool assisted)
        {
            if (boot.Session == null) return;
            boot.Session.SetAssistedPlanning(assisted);
            Changed?.Invoke();
        }

        public void SetTarget(int index)
        {
            Plan.Target = index;
            LastResult = null;
            Refresh();
        }

        public void SetNode(ManeuverNode node)
        {
            Plan.Departure = node?.Clone();
            LastResult = null;
            havePlannedAfter = frozen = false;
            Refresh();
        }

        public void ClearNode() => SetNode(null);

        /// <summary>Checks the plan now (FLIGHT PLAN: PASSED / FAILED with reasons).</summary>
        public FlightPlanResult Validate()
        {
            if (System == null) return null;
            LastResult = FlightPlanner.Validate(System, Origin, Plan, Now, Vehicle());
            Status = LastResult.Verdict;
            Changed?.Invoke();
            return LastResult;
        }

        /// <summary>Assisted mode: searches a departure burn in the background (takes a second or two).</summary>
        public void SuggestAsync()
        {
            if (!Assisted)
            {
                Status = "Ручной режим: подбор манёвра недоступен — рассчитайте его сами.";
                Changed?.Invoke();
                return;
            }
            if (Suggesting || System == null) return;
            PatchedConicSystem sys = System;
            int origin = Origin;
            FlightPlanInput plan = Copy(Plan);
            double now = Now;
            PlannerVehicle vehicle = Vehicle();
            if (!FlightPlanner.InParkingOrbit(sys, origin, vehicle))
            {
                Status = "Подбор манёвра — с орбиты ожидания: сначала выйдите на круговую орбиту.";
                Changed?.Invoke();
                return;
            }
            Status = "Подбираю манёвр…";
            suggestTask = Task.Run(() =>
            {
                ManeuverNode n = FlightPlanner.SuggestDeparture(sys, origin, plan, now, vehicle, out TrajectoryPrediction p);
                return (n, p);
            });
            Changed?.Invoke();
        }

        static FlightPlanInput Copy(FlightPlanInput p) => new FlightPlanInput
        {
            Target = p.Target, ParkingAltitude = p.ParkingAltitude, CaptureAltitude = p.CaptureAltitude, Land = p.Land,
            Departure = p.Departure?.Clone(), PlannedCaptureDeltaV = p.PlannedCaptureDeltaV
        };

        /// <summary>Vehicle position relative to the star now [m].</summary>
        public Vector3d VehicleStarPosition()
        {
            FreeFlightSimulation sim = boot.Body != null ? boot.Body.Simulation : null;
            if (sim == null || sim.Navigation == null) return Vector3d.Zero;
            return sim.State.Position + sim.Navigation.Position(sim.FrameBody, sim.State.Time);
        }

        /// <summary>Distance from the vehicle to a body's centre now [m] (index −1 = the star).</summary>
        public double DistanceToBody(int index) =>
            System == null ? double.NaN : (VehicleStarPosition() - System.Position(index, Now)).Magnitude;

        /// <summary>Level A requirements from the origin's surface to a body's orbit and surface ("mission requirements").</summary>
        public TransferRequirements MissionRequirementsFor(int index)
        {
            if (System == null) return null;
            PlannerVehicle v = Vehicle();
            return FlightPlanner.MissionFromSurface(System, Origin, index, Now, v.SiteRotationSpeed, true);
        }

        /// <summary>Snapshot of the real vehicle for the planner.</summary>
        public PlannerVehicle Vehicle()
        {
            var v = new PlannerVehicle();
            FlightBody body = boot.Body;
            if (body == null || body.Simulation == null) return v;
            FreeFlightSimulation sim = body.Simulation;
            RocketState r = body.Rocket;
            v.Mass = sim.CurrentMass;
            v.DeltaV = r != null ? r.DeltaVRemaining() : 0.0;
            double propellant = 0.0;
            if (r != null)
                for (int i = r.StageIndex; i < r.StageCount; i++) propellant += r.Propellant[i];
            v.MassWithoutPropellant = Math.Max(1.0, v.Mass - propellant);
            v.LastStageIsp = r != null && r.StageCount > 0 ? r.Configuration.stages[r.StageCount - 1].vacuumIsp : 0.0;
            v.DragArea = r != null ? r.Configuration.dragCoefficient * r.Configuration.ReferenceArea : 3.2;
            var caps = body.Capabilities;
            v.RadiationShielding = caps.RadiationShielding;
            v.AcidResistance = caps.AcidResistance;
            v.Landed = sim.IsLanded;
            v.HasState = sim.Navigation != null;
            if (v.HasState) v.State = sim.CoastStateNow();
            PlanetRuntime planet = body.Planet;
            double lat = planet.Definition.launchSite.latitudeDegrees * MathD.DegToRad;
            v.SiteRotationSpeed = planet.Model.Rotation.AngularSpeed * planet.Model.Radius * Math.Cos(lat);
            return v;
        }

        void Update()
        {
            if (boot == null || System == null || boot.Body == null) return;
            if (suggestTask != null && suggestTask.IsCompleted)
            {
                var task = suggestTask;
                suggestTask = null;
                if (task.IsFaulted || task.Result.node == null)
                {
                    Status = "Не удалось подобрать манёвр" + (task.IsFaulted ? ": " + task.Exception?.GetBaseException().Message : ".");
                    Debug.LogWarning("[SpaceSim] " + Status);
                }
                else
                {
                    SetNode(task.Result.node);
                    Validate();
                    Status = task.Result.prediction != null && task.Result.prediction.Encounter
                        ? "Манёвр подобран: попадание в сферу влияния цели. " + LastResult.Verdict
                        : "Лучший найденный манёвр не попадает в сферу влияния цели. " + LastResult.Verdict;
                }
                Changed?.Invoke();
            }
            if (Time.unscaledTime >= nextPredict)
            {
                nextPredict = Time.unscaledTime + PredictInterval;
                Refresh();
            }
            UpdateGuidance();
        }

        /// <summary>Recomputes the live requirements and prediction.</summary>
        void Refresh()
        {
            PatchedConicSystem sys = System;
            if (sys == null || boot.Body == null || boot.Body.Simulation == null) return;
            PlannerVehicle vehicle = Vehicle();
            Requirements = Plan.Target >= 0 ? FlightPlanner.Requirements(sys, Origin, Plan, Now, vehicle) : null;
            if (!vehicle.HasState || vehicle.Landed)
            {
                Prediction = null;
                return;
            }
            double horizon = PredictionHorizon(sys, vehicle);
            try
            {
                Prediction = TrajectoryPredictor.Predict(sys, vehicle.State, horizon, Plan.Target, Plan.Departure);
            }
            catch (ArgumentException e)
            {
                Prediction = null;
                Debug.LogWarning("[SpaceSim] prediction: " + e.Message);
            }
        }

        double PredictionHorizon(PatchedConicSystem sys, PlannerVehicle vehicle)
        {
            double h = 0.0;
            if (Plan.Departure != null) h = Math.Max(0.0, Plan.Departure.Time - vehicle.State.Time);
            if (Requirements != null && Requirements.Ok) return h + 1.6 * Requirements.TransferTime + 10.0 * Day;
            double mu = sys.Mu(vehicle.State.Primary);
            double alpha = 2.0 / vehicle.State.Position.Magnitude - vehicle.State.Velocity.SqrMagnitude / mu;
            double period = alpha > 0.0 ? MathD.TwoPi / (Math.Sqrt(mu) * Math.Pow(alpha, 1.5)) : 30.0 * Day;
            return h + Math.Min(period * 1.05, 400.0 * Day);
        }

        /// <summary>
        /// Δv still to apply: before ignition — the node's full Δv; once burning — the difference between the velocity the
        /// planned trajectory has now and the actual velocity (both exact patched conics), so finite burns are tracked honestly.
        /// </summary>
        void UpdateGuidance()
        {
            ManeuverNode node = Plan.Departure;
            FlightBody body = boot.Body;
            FreeFlightSimulation sim = body.Simulation;
            Guidance.Active = node != null && sim.Navigation != null && !sim.IsLanded;
            if (!Guidance.Active) return;
            PatchedConicSystem sys = sim.Navigation;
            double now = sim.State.Time;
            if (now > node.Time + Math.Max(600.0, 2.0 * Guidance.BurnTime))
            {
                Guidance.Active = false; // the burn window is over: the plan check now looks at the actual trajectory
                return;
            }
            RocketState r = body.Rocket;
            double thrust = r != null && r.HasStages ? RocketEquations.ThrustFromMassFlow(r.FullThrottleMassFlow, r.Isp(0.0)) : 0.0;
            double isp = r != null && r.HasStages ? r.CurrentStage.vacuumIsp : 0.0;
            Guidance.BurnTime = thrust > 0.0 && isp > 0.0 ? RocketEquations.BurnTimeForDeltaV(node.Magnitude, isp, sim.CurrentMass, thrust) : 0.0;
            Guidance.TimeToNode = node.Time - now;
            Guidance.TimeToIgnition = Guidance.TimeToNode - 0.5 * Guidance.BurnTime;

            CoastState current = sim.CoastStateNow();
            if (!frozen)
            {
                // Planned post-burn state from the current trajectory (refreshed until ignition is due).
                CoastState s = current;
                double until = node.Time;
                for (int guard = 0; guard < 10000 && s.Time < until; guard++)
                    sys.Coast(ref s, Math.Min(until - s.Time, Math.Max(1e-3, sys.Horizon(s, until - s.Time, true))));
                Vector3d dv = node.DeltaVector(s.Position, s.Velocity);
                plannedAfter = new CoastState(s.Primary, s.Position, s.Velocity + dv, s.Time);
                havePlannedAfter = true;
                Vector3d nodePosInFrame = s.Position, nodeVelInFrame = s.Velocity;
                sys.Transform(s.Primary, sim.FrameBody, s.Time, ref nodePosInFrame, ref nodeVelInFrame);
                sim.AttitudeFor(dv, nodePosInFrame, out Guidance.Pitch, out Guidance.Heading);
                Guidance.Remaining = node.Magnitude;
                Guidance.Started = false;
                if (Guidance.TimeToIgnition <= 0.0 || (r != null && r.IsBurning && Guidance.TimeToNode < Guidance.BurnTime)) frozen = true;
                return;
            }
            if (!havePlannedAfter) return;
            // After ignition: compare with the planned trajectory at the same moment.
            CoastState planned = plannedAfter;
            if (now >= planned.Time)
            {
                for (int guard = 0; guard < 10000 && planned.Time < now; guard++)
                    sys.Coast(ref planned, Math.Min(now - planned.Time, Math.Max(1e-3, sys.Horizon(planned, now - planned.Time, true))));
            }
            else ConicPropagator.Propagate(sys.Mu(planned.Primary), planned.Position, planned.Velocity, now - planned.Time, out planned.Position, out planned.Velocity);
            Vector3d actualV = current.Velocity, actualR = current.Position;
            sys.Transform(current.Primary, planned.Primary, now, ref actualR, ref actualV);
            Vector3d remaining = planned.Velocity - actualV;
            Guidance.Remaining = remaining.Magnitude;
            Guidance.Started = true;
            if (Guidance.Remaining > 1e-6) sim.AttitudeFor(remaining, sim.State.Position, out Guidance.Pitch, out Guidance.Heading);
        }
    }
}
