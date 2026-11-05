using System;
using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Physics;

namespace SpaceSim.Navigation
{
    /// <summary>What the planner knows about the vehicle (copied from the rocket and the simulation — no physics here).</summary>
    public sealed class PlannerVehicle
    {
        /// <summary>Ideal Δv left in all remaining stages [m/s].</summary>
        public double DeltaV;
        /// <summary>Current mass [kg].</summary>
        public double Mass;
        /// <summary>Mass after burning all remaining propellant [kg].</summary>
        public double MassWithoutPropellant;
        /// <summary>Vacuum Isp of the last stage [s].</summary>
        public double LastStageIsp;
        /// <summary>C_d·A [m²] (landing estimate through an atmosphere).</summary>
        public double DragArea = 3.2;
        public int RadiationShielding, AcidResistance;
        public bool Landed;
        /// <summary>Current trajectory relative to its primary.</summary>
        public bool HasState;
        public CoastState State;
        /// <summary>Ground speed of the pad due to rotation [m/s] (ascent estimate when landed).</summary>
        public double SiteRotationSpeed;
    }

    public enum PlanIssueCode
    {
        NoTarget,
        InsufficientDeltaV,
        InsufficientFuel,
        DepartureVelocity,
        InvalidTrajectory,
        MissedWindow,
        InsertionVelocity,
        ImpactCourse,
        Protection,
        NotInOrbit,
        NoDeparture
    }

    public sealed class PlanIssue
    {
        public PlanIssue(PlanIssueCode code, string reason, string fix)
        {
            Code = code;
            Reason = reason;
            Fix = fix;
        }

        public PlanIssueCode Code { get; }
        /// <summary>What is wrong — with the actual numbers.</summary>
        public string Reason { get; }
        /// <summary>How to correct it.</summary>
        public string Fix { get; }
    }

    /// <summary>The player's flight plan.</summary>
    [Serializable]
    public sealed class FlightPlanInput
    {
        public int Target = PatchedConicSystem.Star;
        /// <summary>Altitude of the parking orbit at the origin [m] (used when the vehicle is not in orbit yet).</summary>
        public double ParkingAltitude = 250000.0;
        /// <summary>Altitude of the orbit to capture into at the target [m].</summary>
        public double CaptureAltitude = 200000.0;
        public bool Land;
        /// <summary>Departure burn; null = not planned yet.</summary>
        public ManeuverNode Departure;
        /// <summary>The player's own capture Δv [m/s]; 0 = not given.</summary>
        public double PlannedCaptureDeltaV;
    }

    /// <summary>Level A numbers of a transfer (ideal: circular coplanar orbits, impulsive burns, patched conics).</summary>
    public sealed class TransferRequirements
    {
        public int Origin, Target;
        public string Error;
        public HohmannTransfer Heliocentric;
        public double VInfDeparture, VInfArrival;
        public double ParkingAltitude, CaptureAltitude;
        public bool IncludesAscent, IncludesLanding;
        public double AscentDeltaV, DepartureDeltaV, CaptureDeltaV, LandingDeltaV, TotalDeltaV;
        public double TransferTime;
        /// <summary>Phase angle the target must have at departure [rad] (θ = π − n₂·t).</summary>
        public double RequiredPhase;
        public double CurrentPhase;
        public double TimeToWindow, WindowTime, SynodicPeriod;
        /// <summary>Δv missing for the whole plan [m/s] (0 = enough).</summary>
        public double DeltaVDeficit;
        /// <summary>Propellant that would cover the deficit on the last stage [kg].</summary>
        public double PropellantForDeficit;
        public int TargetRadiation, TargetAcid;
        public bool ProtectionShort;
        public string LandingNote = "";
        public bool Ok => Error == null;
    }

    public sealed class FlightPlanResult
    {
        public bool Passed;
        public TransferRequirements Requirements;
        public readonly List<PlanIssue> Issues = new List<PlanIssue>();
        public TrajectoryPrediction Prediction;
        /// <summary>Capture Δv for the predicted arrival [m/s] (0 without an encounter).</summary>
        public double ActualCaptureDeltaV;
        public string Verdict => Passed ? "ПЛАН ПОЛЁТА: ПРИНЯТ" : "ПЛАН ПОЛЁТА: FAILED";
    }

    /// <summary>
    /// Flight planning and validation. Level A numbers come from <see cref="TransferCalculator"/>,
    /// <see cref="MissionRequirements"/>, <see cref="RocketEquations"/>; the check of the player's burn uses the patched-conic
    /// <see cref="TrajectoryPredictor"/> with the planets' real positions and motion (never a straight line).
    /// </summary>
    public static class FlightPlanner
    {
        const double Day = 86400.0;

        static string N(double v) => Academy.AcademyNumber.Format(v);
        static string Km(double m) => Academy.AcademyNumber.Format(m / 1000.0) + " км";
        static string Mkm(double m) => Academy.AcademyNumber.Format(m / 1e9) + " млн км";
        static string Days(double s) => Academy.AcademyNumber.Format(s / Day) + " сут";
        static string Deg(double rad) => Academy.AcademyNumber.Format(rad * 180.0 / Math.PI) + "°";

        /// <summary>Signed angle from body a to body b around the star, positive in a's direction of motion (−π, π].</summary>
        public static double PhaseAngle(PatchedConicSystem sys, int a, int b, double time)
        {
            Vector3d ra = sys.Position(a, time), rb = sys.Position(b, time);
            Vector3d n = Vector3d.Cross(ra, sys.Velocity(a, time)).Normalized;
            return Math.Atan2(Vector3d.Dot(Vector3d.Cross(ra, rb), n), Vector3d.Dot(ra, rb));
        }

        static bool Escaping(PatchedConicSystem sys, CoastState s) =>
            0.5 * s.Velocity.SqrMagnitude - sys.Mu(s.Primary) / s.Position.Magnitude >= 0.0;

        /// <summary>Is the vehicle in a closed orbit around the origin, above its numeric zone?</summary>
        public static bool InParkingOrbit(PatchedConicSystem sys, int origin, PlannerVehicle v)
        {
            if (v == null || !v.HasState || v.Landed || v.State.Primary != origin || origin < 0) return false;
            double mu = sys.Mu(origin);
            double eps = 0.5 * v.State.Velocity.SqrMagnitude - mu / v.State.Position.Magnitude;
            if (eps >= 0.0) return false;
            ConicPropagator.Shape(mu, v.State.Position, v.State.Velocity, out _, out _, out double rp);
            return rp > sys[origin].Radius + 0.5 * (sys[origin].ZoneRadius - sys[origin].Radius);
        }

        public static TransferRequirements Requirements(PatchedConicSystem sys, int origin, FlightPlanInput input, double now, PlannerVehicle vehicle)
        {
            var q = new TransferRequirements { Origin = origin, Target = input.Target, CaptureAltitude = input.CaptureAltitude };
            if (origin < 0 || input.Target < 0 || input.Target == origin || input.Target >= sys.Count)
            {
                q.Error = "Выберите цель — другую планету.";
                return q;
            }
            NavBody o = sys[origin], t = sys[input.Target];
            if (o.ParentIndex != t.ParentIndex)
            {
                q.Error = "Перелёт рассчитывается между телами с общим центром обращения (" + t.Name + " обращается вокруг " + sys.NameOf(t.ParentIndex) +
                          "): сначала долетите до " + sys.NameOf(t.ParentIndex) + ".";
                return q;
            }
            double muCentral = sys.Mu(o.ParentIndex);
            double r1 = o.Orbit.SemiMajorAxis, r2 = t.Orbit.SemiMajorAxis;

            bool inOrbit = InParkingOrbit(sys, origin, vehicle);
            double parkingRadius = o.Radius + input.ParkingAltitude;
            if (inOrbit)
            {
                double mu = sys.Mu(origin);
                double alpha = 2.0 / vehicle.State.Position.Magnitude - vehicle.State.Velocity.SqrMagnitude / mu;
                parkingRadius = 1.0 / alpha; // semi-major axis of the actual orbit
            }
            q.ParkingAltitude = parkingRadius - o.Radius;

            q.Heliocentric = TransferCalculator.Hohmann(muCentral, r1, r2);
            TransferCalculator.HohmannExcessSpeeds(muCentral, r1, r2, out q.VInfDeparture, out q.VInfArrival);
            q.DepartureDeltaV = TransferCalculator.DepartureDeltaV(o.Mu, parkingRadius, q.VInfDeparture);
            q.CaptureDeltaV = TransferCalculator.CaptureDeltaV(t.Mu, t.Radius + input.CaptureAltitude, q.VInfArrival);
            q.TransferTime = q.Heliocentric.TransferTime;
            q.RequiredPhase = TransferCalculator.HohmannPhaseAngle(muCentral, r1, r2);
            q.CurrentPhase = PhaseAngle(sys, origin, input.Target, now);
            double n1 = MathD.TwoPi / o.Orbit.Period, n2 = MathD.TwoPi / t.Orbit.Period;
            q.TimeToWindow = TransferCalculator.TimeToPhaseAngle(q.CurrentPhase, q.RequiredPhase, n1, n2);
            q.WindowTime = now + q.TimeToWindow;
            q.SynodicPeriod = TransferCalculator.SynodicPeriod(o.Orbit.Period, t.Orbit.Period);

            q.IncludesAscent = vehicle != null && vehicle.Landed;
            if (q.IncludesAscent) q.AscentDeltaV = MissionRequirements.LowOrbit(o.Mu, o.Radius, input.ParkingAltitude, vehicle.SiteRotationSpeed);
            q.IncludesLanding = input.Land;
            if (input.Land) q.LandingDeltaV = LandingEstimate(t, input.CaptureAltitude, vehicle, out q.LandingNote);
            q.TotalDeltaV = q.AscentDeltaV + q.DepartureDeltaV + q.CaptureDeltaV + q.LandingDeltaV;

            if (vehicle != null)
            {
                q.DeltaVDeficit = Math.Max(0.0, q.TotalDeltaV - vehicle.DeltaV);
                if (q.DeltaVDeficit > 0.0 && vehicle.LastStageIsp > 0.0 && vehicle.MassWithoutPropellant > 0.0)
                    q.PropellantForDeficit = RocketEquations.PropellantForDeltaV(q.DeltaVDeficit, vehicle.LastStageIsp, vehicle.MassWithoutPropellant);
                q.TargetRadiation = t.Hazards.radiationLevel;
                q.TargetAcid = t.Hazards.acidLevel;
                q.ProtectionShort = vehicle.RadiationShielding < t.Hazards.radiationLevel || vehicle.AcidResistance < t.Hazards.acidLevel;
            }
            return q;
        }

        /// <summary>Level A landing Δv from the capture orbit: airless — the ascent in reverse; with air — deorbit burn + braking from the terminal speed.</summary>
        static double LandingEstimate(NavBody t, double orbitAltitude, PlannerVehicle v, out string note)
        {
            double rho0 = t.Atmosphere.ReferenceDensity;
            if (rho0 <= 0.0)
            {
                note = "Без атмосферы весь спуск — двигателем (идеально: как подъём на орбиту в обратном порядке, без вращения).";
                return MissionRequirements.LowOrbit(t.Mu, t.Radius, orbitAltitude, 0.0);
            }
            double entry = t.Radius + 0.5 * t.Physical.spaceBoundaryAltitude;
            double deorbit = TransferCalculator.Hohmann(t.Mu, t.Radius + orbitAltitude, entry).DeltaV1;
            double mass = v != null && v.MassWithoutPropellant > 0.0 ? v.MassWithoutPropellant : 5000.0;
            double area = v != null && v.DragArea > 0.0 ? v.DragArea : 3.2;
            double g = PlanetaryGravity.AccelerationMagnitude(t.Mu, t.Radius);
            double terminal = AtmosphericDrag.TerminalVelocity(mass, g, rho0, 1.0, area);
            note = "Атмосфера тормозит аппарат: сход с орбиты " + N(deorbit) + " м/с + гашение предельной скорости у поверхности ≈ " + N(terminal) +
                   " м/с. Нагрев и парашюты не учтены (посадка — этап M6).";
            return deorbit + terminal;
        }

        /// <summary>Checks the player's plan: Level A budget, protection, and the departure burn through the patched-conic predictor.</summary>
        public static FlightPlanResult Validate(PatchedConicSystem sys, int origin, FlightPlanInput input, double now, PlannerVehicle vehicle)
        {
            var result = new FlightPlanResult();
            TransferRequirements q = Requirements(sys, origin, input, now, vehicle);
            result.Requirements = q;
            if (!q.Ok)
            {
                result.Issues.Add(new PlanIssue(PlanIssueCode.NoTarget, q.Error, "Выберите планету в списке «Цель»."));
                return result;
            }
            NavBody o = sys[origin], t = sys[input.Target];

            if (vehicle != null && q.DeltaVDeficit > 0.0)
                result.Issues.Add(new PlanIssue(PlanIssueCode.InsufficientDeltaV,
                    "Недостаточно Δv: на весь план нужно ≈ " + N(q.TotalDeltaV) + " м/с (идеально), у ракеты " + N(vehicle.DeltaV) + " м/с — не хватает " + N(q.DeltaVDeficit) + " м/с.",
                    q.PropellantForDeficit > 0.0
                        ? "Добавьте ≈ " + N(q.PropellantForDeficit / 1000.0) + " т топлива на последнюю ступень (или легче груз, выше Isp). Реальный полёт потребует ещё больше — потери."
                        : "Увеличьте запас Δv ракеты в ангаре."));
            if (vehicle != null && q.ProtectionShort)
                result.Issues.Add(new PlanIssue(PlanIssueCode.Protection,
                    "Недостаточная защита: у " + t.Name + " радиация " + q.TargetRadiation + ", кислота " + q.TargetAcid + "; у ракеты защита " + vehicle.RadiationShielding +
                    " и " + vehicle.AcidResistance + " — оборудование будет деградировать.",
                    "Купите и установите защиту нужного уровня (магазин K, ангар B)."));

            ManeuverNode node = input.Departure;
            // En route: around another primary, or already on an escape trajectory from the origin (the departure burn is done) —
            // then the actual trajectory is checked and a past node is ignored.
            bool cruising = vehicle != null && vehicle.HasState && !vehicle.Landed &&
                            (vehicle.State.Primary != origin || Escaping(sys, vehicle.State));
            if (cruising && node != null && node.Time < now) node = null;
            if (node == null && !cruising)
            {
                result.Issues.Add(new PlanIssue(PlanIssueCode.NoDeparture,
                    "Не задан импульс отлёта: без него проверить траекторию нельзя.",
                    "Задайте время и Δv импульса (по Академии: окно через " + Days(q.TimeToWindow) + ", Δv ≈ " + N(q.DepartureDeltaV) + " м/с) — или «Подобрать» в режиме с помощником."));
                return result;
            }
            if (!cruising && !InParkingOrbit(sys, origin, vehicle))
            {
                result.Issues.Add(new PlanIssue(PlanIssueCode.NotInOrbit,
                    "Импульс отлёта даётся с орбиты ожидания, а аппарат " + (vehicle != null && vehicle.Landed ? "на поверхности" : "не на устойчивой орбите") + ".",
                    "Сначала выйдите на круговую орбиту ≈ " + Km(q.ParkingAltitude) + " (выше " + Km(o.ZoneRadius - o.Radius) + " — там можно ускорять время)."));
                return result;
            }
            if (node != null && node.Time < now)
            {
                result.Issues.Add(new PlanIssue(PlanIssueCode.MissedWindow, "Время импульса уже прошло.", "Задайте время в будущем."));
                return result;
            }
            if (node != null && vehicle != null && node.Magnitude > vehicle.DeltaV)
                result.Issues.Add(new PlanIssue(PlanIssueCode.InsufficientFuel,
                    "Недостаточно топлива: импульс " + N(node.Magnitude) + " м/с, а запас Δv " + N(vehicle.DeltaV) + " м/с — двигатель остановится на " +
                    N(100.0 * vehicle.DeltaV / node.Magnitude) + " % манёвра.",
                    "Больше топлива или меньший импульс (точнее выберите время в окне)."));

            double start = vehicle.State.Time;
            double horizon = (node != null ? Math.Max(0.0, node.Time - start) : 0.0) + 1.6 * q.TransferTime + 10.0 * Day;
            TrajectoryPrediction p = TrajectoryPredictor.Predict(sys, vehicle.State, horizon, input.Target, node);
            result.Prediction = p;

            if (p.Impact && p.ImpactBody == origin)
            {
                result.Issues.Add(new PlanIssue(PlanIssueCode.InvalidTrajectory, "После манёвра аппарат падает обратно на " + o.Name + ".", "Импульс по направлению движения (прогрейд), а не вниз."));
                return result;
            }

            if (p.Encounter)
            {
                double rp = p.ArrivalPeriapsis;
                double captureRadius = rp > t.Radius ? rp : t.Radius + input.CaptureAltitude;
                result.ActualCaptureDeltaV = TransferCalculator.CaptureDeltaV(t.Mu, captureRadius, p.ArrivalVInfinity);
                double left = vehicle != null ? vehicle.DeltaV - (node != null ? node.Magnitude : 0.0) : double.PositiveInfinity;
                if (result.ActualCaptureDeltaV > left)
                    result.Issues.Add(new PlanIssue(PlanIssueCode.InsertionVelocity,
                        "Недостаточно Δv для выхода на орбиту у " + t.Name + ": прилёт с v∞ = " + N(p.ArrivalVInfinity) + " м/с, захват на перицентре " + Km(captureRadius - t.Radius) +
                        " стоит " + N(result.ActualCaptureDeltaV) + " м/с, а останется " + N(Math.Max(0.0, left)) + " м/с.",
                        "Больше топлива для захвата — или пролёт мимо (без захвата) как разведка."));
                if (input.PlannedCaptureDeltaV > 0.0 && input.PlannedCaptureDeltaV < 0.98 * result.ActualCaptureDeltaV)
                    result.Issues.Add(new PlanIssue(PlanIssueCode.InsertionVelocity,
                        "Запланированного импульса захвата " + N(input.PlannedCaptureDeltaV) + " м/с не хватит: нужно " + N(result.ActualCaptureDeltaV) + " м/с.",
                        "Δv = √(v∞² + 2μ/r_p) − √(μ/r_p) — Академия, тема «Окна запуска»."));
                if (rp < t.Radius && !input.Land)
                    result.Issues.Add(new PlanIssue(PlanIssueCode.ImpactCourse,
                        "Траектория прилёта врезается в " + t.Name + " (перицентр " + Km(rp - t.Radius) + ").",
                        "Небольшая коррекция курса на полпути (радиальный/нормальный импульс) поднимет перицентр."));
            }
            else if (node != null || cruising)
            {
                AddMissReasons(sys, origin, input, q, p, node, result);
            }
            result.Passed = p.Encounter && !result.Issues.Exists(i => i.Code != PlanIssueCode.NoTarget);
            return result;
        }

        static void AddMissReasons(PatchedConicSystem sys, int origin, FlightPlanInput input, TransferRequirements q, TrajectoryPrediction p,
            ManeuverNode node, FlightPlanResult result)
        {
            NavBody o = sys[origin], t = sys[input.Target];
            double dv = node != null ? node.Magnitude : 0.0;
            if (node != null && !p.HasHeliocentric)
            {
                result.Issues.Add(new PlanIssue(PlanIssueCode.DepartureVelocity,
                    "Скорость отлёта недостаточна: после импульса " + N(dv) + " м/с аппарат остаётся в сфере влияния " + o.Name + ".",
                    "Нужно ≈ " + N(q.DepartureDeltaV) + " м/с с орбиты " + Km(q.ParkingAltitude) + " (вторая космическая + v∞ = " + N(q.VInfDeparture) + " м/с)."));
                return;
            }
            if (p.HasHeliocentric)
            {
                ConicPropagator.Shape(sys.Mu(p.Heliocentric.Primary), p.Heliocentric.Position, p.Heliocentric.Velocity, out double e, out double semiLatus, out double peri);
                double apo = e < 1.0 ? semiLatus / (1.0 - e) : double.PositiveInfinity;
                double r2 = t.Orbit.SemiMajorAxis;
                if (r2 < peri * 0.99 || r2 > apo * 1.01)
                {
                    bool inward = r2 < o.Orbit.SemiMajorAxis;
                    string orbit = inward ? "перицентр " + Mkm(peri) : "апоцентр " + Mkm(apo);
                    result.Issues.Add(new PlanIssue(node != null && dv < 0.97 * q.DepartureDeltaV ? PlanIssueCode.DepartureVelocity : PlanIssueCode.InvalidTrajectory,
                        (node != null && dv < 0.97 * q.DepartureDeltaV ? "Скорость отлёта недостаточна для выхода на переходную орбиту: " : "Переходная орбита не достигает орбиты цели: ") +
                        orbit + " вокруг звезды, а орбита " + t.Name + " — " + Mkm(r2) + ".",
                        "Δv отлёта ≈ " + N(q.DepartureDeltaV) + " м/с в момент, когда скорость вокруг " + o.Name + " направлена " +
                        (inward ? "против" : "по") + " движения планеты (ваш импульс: " + N(dv) + " м/с)."));
                    return;
                }
            }
            double miss = p.ClosestDistance;
            double windowError = node != null ? node.Time - q.WindowTime : 0.0;
            result.Issues.Add(new PlanIssue(PlanIssueCode.MissedWindow,
                "Промах: орбита цели достигнута, но " + t.Name + " в этот момент не там — сближение лишь до " + Mkm(miss) + " (сфера влияния " + Mkm(t.SoiRadius) + ")." +
                (node != null && Math.Abs(windowError) > 0.5 * Day ? " Импульс " + (windowError < 0 ? "раньше" : "позже") + " окна на " + Days(Math.Abs(windowError)) + "." : ""),
                "Окно запуска — когда " + t.Name + " опережает " + o.Name + " на θ = " + Deg(q.RequiredPhase) + " (сейчас " + Deg(q.CurrentPhase) +
                "); ближайшее через " + Days(q.TimeToWindow) + ". Точнее подберите момент импульса на орбите (точку выхода)."));
        }

        /// <summary>
        /// Assisted mode: finds a prograde (+ small normal) departure burn near the launch window that leads into the target's
        /// sphere of influence, by searching the burn time over one parking orbit and the Δv around the Level A value.
        /// Returns null when the vehicle is not in a parking orbit around the origin.
        /// </summary>
        public static ManeuverNode SuggestDeparture(PatchedConicSystem sys, int origin, FlightPlanInput input, double now, PlannerVehicle vehicle,
            out TrajectoryPrediction prediction)
        {
            prediction = null;
            if (!InParkingOrbit(sys, origin, vehicle)) return null;
            TransferRequirements q = Requirements(sys, origin, input, now, vehicle);
            if (!q.Ok) return null;
            double mu = sys.Mu(origin);
            double a = 1.0 / (2.0 / vehicle.State.Position.Magnitude - vehicle.State.Velocity.SqrMagnitude / mu);
            double period = MathD.TwoPi * Math.Sqrt(a * a * a / mu);
            double center = Math.Max(now + 0.25 * period, q.WindowTime);
            double horizon = Math.Max(0.0, center + period - vehicle.State.Time) + 1.6 * q.TransferTime + 10.0 * Day;
            NavBody t = sys[input.Target];
            double targetRadius = t.Radius + input.CaptureAltitude;

            double Cost(ManeuverNode n, out TrajectoryPrediction pr)
            {
                pr = TrajectoryPredictor.Predict(sys, vehicle.State, horizon, input.Target, n, 120);
                if (pr.Impact && pr.ImpactBody == origin) return double.MaxValue;
                if (pr.Encounter) return Math.Abs(pr.ArrivalPeriapsis - targetRadius);
                return t.SoiRadius + pr.ClosestDistance;
            }

            ManeuverNode best = null;
            double bestCost = double.MaxValue;
            TrajectoryPrediction bestPrediction = null;
            foreach (double k in new[] { 0.98, 1.0, 1.03, 1.07, 1.12 })
                for (int i = 0; i < 48; i++)
                {
                    var n = new ManeuverNode { Time = center - 0.5 * period + period * i / 48.0, Prograde = k * q.DepartureDeltaV };
                    if (n.Time < now) continue;
                    double c = Cost(n, out TrajectoryPrediction pr);
                    if (c < bestCost)
                    {
                        bestCost = c;
                        best = n;
                        bestPrediction = pr;
                    }
                }
            if (best == null) return null;

            // Pattern search on (time, prograde, normal) with shrinking steps.
            double dt = period / 96.0, dp = 0.01 * q.DepartureDeltaV, dn = 0.01 * q.DepartureDeltaV;
            for (int iter = 0; iter < 40 && (dt > 1.0 || dp > 0.05); iter++)
            {
                bool improved = false;
                foreach (var trial in Neighbours(best, dt, dp, dn))
                {
                    if (trial.Time < now) continue;
                    double c = Cost(trial, out TrajectoryPrediction pr);
                    if (c < bestCost)
                    {
                        bestCost = c;
                        best = trial;
                        bestPrediction = pr;
                        improved = true;
                    }
                }
                if (!improved)
                {
                    dt *= 0.5;
                    dp *= 0.5;
                    dn *= 0.5;
                }
            }
            prediction = bestPrediction;
            return best;
        }

        static IEnumerable<ManeuverNode> Neighbours(ManeuverNode n, double dt, double dp, double dn)
        {
            yield return new ManeuverNode { Time = n.Time + dt, Prograde = n.Prograde, Normal = n.Normal };
            yield return new ManeuverNode { Time = n.Time - dt, Prograde = n.Prograde, Normal = n.Normal };
            yield return new ManeuverNode { Time = n.Time, Prograde = n.Prograde + dp, Normal = n.Normal };
            yield return new ManeuverNode { Time = n.Time, Prograde = n.Prograde - dp, Normal = n.Normal };
            yield return new ManeuverNode { Time = n.Time, Prograde = n.Prograde, Normal = n.Normal + dn };
            yield return new ManeuverNode { Time = n.Time, Prograde = n.Prograde, Normal = n.Normal - dn };
        }

        /// <summary>Ideal Δv from the origin's surface to an orbit (and the surface) of a body — the "mission requirements" column.</summary>
        public static TransferRequirements MissionFromSurface(PatchedConicSystem sys, int origin, int target, double now, double siteRotationSpeed, bool land)
        {
            var input = new FlightPlanInput { Target = target, Land = land };
            var v = new PlannerVehicle { Landed = true, SiteRotationSpeed = siteRotationSpeed };
            return Requirements(sys, origin, input, now, v);
        }
    }
}
