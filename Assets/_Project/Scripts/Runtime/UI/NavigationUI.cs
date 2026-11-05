using System;
using System.Collections.Generic;
using SpaceSim.Academy;
using SpaceSim.Mathematics;
using SpaceSim.Navigation;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Units;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Space Navigation window (IMGUI, NON-MODAL): «Планеты» (body reference + Level A mission requirements),
    /// «План полёта» (target, orbits, departure burn, check) and «Карта» (top-down ecliptic map with the predicted trajectory).
    /// Every number comes from <see cref="NavigationService"/> (which wraps <see cref="FlightPlanner"/>,
    /// <see cref="TrajectoryPredictor"/> and <see cref="PatchedConicSystem"/>); this class only formats and converts units.
    /// </summary>
    /// <remarks>
    /// IMGUI rules (same as <see cref="ShopUI"/> / <see cref="AcademyUI"/>): the displayed snapshot is rebuilt on Layout only,
    /// structural changes (tab switches, service calls) are queued with <see cref="Defer"/> and applied after the pass.
    /// The window never sets <c>FlightInput.ModalWindowOpen</c>: game hotkeys keep working, except while one of its text
    /// fields has keyboard focus (released on Enter and on every button press).
    /// </remarks>
    [DefaultExecutionOrder(217)]
    public sealed class NavigationUI : MonoBehaviour
    {
        public enum Page { Planets, Plan, Map }

        const float PreferredWidth = 1100f;
        const float PreferredHeight = 700f;
        const float RebuildInterval = 0.25f;
        const float MissionInterval = 2f;
        const double Day = 86400.0;
        const double AstronomicalUnit = 1.495978707e11;
        const double StandardAtmosphere = 101325.0;
        const int OrbitSamples = 120;
        const string FieldPrefix = "nav.f";

        static readonly string[] TabNames = { "Планеты", "План полёта", "Карта" };

        // Text fields of the plan tab.
        const int FPark = 0, FCap = 1, FCapDv = 2, FDt = 3, FPro = 4, FNor = 5, FRad = 6, FieldCount = 7;
        static readonly string[] FieldNames =
            { FieldPrefix + "0", FieldPrefix + "1", FieldPrefix + "2", FieldPrefix + "3", FieldPrefix + "4", FieldPrefix + "5", FieldPrefix + "6" };

        static readonly Color OrbitColor = new Color(0.6f, 0.7f, 0.8f, 0.45f);
        static readonly Color BodyColor = new Color(0.8f, 0.85f, 0.9f);
        static readonly Color StarColor = new Color(1f, 0.9f, 0.35f);
        static readonly Color SoiColor = new Color(0.55f, 0.85f, 1f, 0.35f);

        NavigationService service;
        PlayerSession session;
        FlightStateController state;
        StarSystemRuntime system;

        bool open;
        /// <summary>Opened mid-frame (e.g. from the HUD): draw nothing until a Layout pass has seen the window.</summary>
        bool awaitingLayout;
        Page tab = Page.Planets;
        /// <summary>Tab requested while open; applied on this window's next Layout pass.</summary>
        Page? requestedTab;
        readonly List<Action> pending = new List<Action>();

        View view;
        bool dirty = true;
        float nextBuild;

        int selectedBody = PatchedConicSystem.Star;
        bool selectionInitialized;
        int missionIndex = int.MinValue;
        float missionTime;
        TransferRequirements missionCache;

        readonly string[] fields = { "250", "200", "0", "600", "0", "0", "0" };
        readonly bool[] badField = new bool[FieldCount];
        ManeuverNode lastNode;
        bool planEditedSinceCheck;
        string localStatus = "";
        Color localStatusColor = Color.white;

        Vector2 listScroll, detailScroll, planScroll, mapScroll;
        float contentWidth;

        // Map state.
        bool mapAroundVehicle;
        double mapZoom = 1.0;
        PatchedConicSystem orbitSystem;
        List<Vector3d>[] orbitRelative;
        double systemExtent;
        List<Vector2>[] orbitScreen;
        (double half, double cx, double cy, float x, float y, float w, float h) orbitKey;
        float nextOrbitRebuild;
        readonly List<Vector2> predictionScreen = new List<Vector2>();
        TrajectoryPrediction predictionRef;
        (double half, double cx, double cy, float x, float y, float w, float h) predictionKey;

        GUIStyle note, compact, mapLabel, verdict;

        public bool IsOpen => open;
        /// <summary>Raised with the new state whenever the window opens or closes (also from its own close button).</summary>
        public event Action<bool> OpenChanged;

        public void Initialize(NavigationService navigation, PlayerSession playerSession, FlightStateController flightState, StarSystemRuntime starSystem)
        {
            if (service != null) service.Changed -= OnDataChanged;
            if (session != null) session.Changed -= OnDataChanged;
            service = navigation;
            session = playerSession;
            state = flightState;
            system = starSystem;
            if (service != null) service.Changed += OnDataChanged;
            if (session != null) session.Changed += OnDataChanged;
            dirty = true;
        }

        void OnDestroy()
        {
            if (service != null) service.Changed -= OnDataChanged;
            if (session != null) session.Changed -= OnDataChanged;
        }

        void OnDataChanged() => dirty = true;

        public void Toggle()
        {
            if (open) Close();
            else OpenWindow();
        }

        /// <summary>Opens the window on <paramref name="page"/> (switches the tab if it is already open).</summary>
        public void Open(Page page)
        {
            if (!open)
            {
                tab = page;
                OpenWindow();
            }
            else requestedTab = page;
        }

        void OpenWindow()
        {
            if (open || service == null) return;
            pending.Clear();
            localStatus = "";
            open = true;
            awaitingLayout = true;
            dirty = true;
            OpenChanged?.Invoke(true);
        }

        void Close()
        {
            if (!open) return;
            open = false;
            pending.Clear();
            requestedTab = null;
            GUIUtility.keyboardControl = 0;
            OpenChanged?.Invoke(false);
        }

        // ---- Deferred actions ------------------------------------------------------------------------------------

        void Defer(Action action)
        {
            if (action != null) pending.Add(action);
        }

        void ApplyPending()
        {
            if (pending.Count == 0) return;
            Action[] actions = pending.ToArray();
            pending.Clear();
            foreach (Action a in actions) a();
            dirty = true;
        }

        void SetLocalStatus(string text, Color color)
        {
            localStatus = text;
            localStatusColor = color;
        }

        void ChooseTarget(int index)
        {
            service.SetTarget(index);
            planEditedSinceCheck = false;
            SetLocalStatus("", Color.white);
        }

        void ApplyNode()
        {
            bool ok = AcademyNumber.TryParse(fields[FDt], out double dt);
            ok &= AcademyNumber.TryParse(fields[FPro], out double pro);
            ok &= AcademyNumber.TryParse(fields[FNor], out double nor);
            ok &= AcademyNumber.TryParse(fields[FRad], out double rad);
            if (!ok)
            {
                SetLocalStatus("Манёвр не задан: введите числа во все поля манёвра.", HudStyle.Danger);
                return;
            }
            if (dt < 0.0)
            {
                SetLocalStatus("Манёвр не задан: время импульса должно быть в будущем (≥ 0 с).", HudStyle.Danger);
                return;
            }
            service.SetNode(new ManeuverNode { Time = service.Now + dt, Prograde = pro, Normal = nor, Radial = rad });
            planEditedSinceCheck = true;
            SetLocalStatus("Манёвр задан. Нажмите «Проверить план».", HudStyle.Accent);
        }

        void RemoveNode()
        {
            service.ClearNode();
            planEditedSinceCheck = true;
            SetLocalStatus("Манёвр удалён.", Color.white);
        }

        void CheckPlan()
        {
            FlightPlanResult result = service.Validate();
            planEditedSinceCheck = false;
            if (result == null)
            {
                SetLocalStatus("Проверка недоступна: навигационная система не загружена.", HudStyle.Danger);
                return;
            }
            SetLocalStatus("", Color.white);
            if (state != null) state.Notify(result.Verdict);
        }

        // ---- Snapshot (rebuilt on Layout) ------------------------------------------------------------------------

        enum LineKind { Row, Text, Title, Gap }

        sealed class Line
        {
            public LineKind Kind;
            public string Label = "", Value = "";
            public Color Color = Color.white;
        }

        sealed class ListItem
        {
            public int Index;
            public string Label = "";
        }

        sealed class View
        {
            public bool HasSystem;
            public bool Assisted;
            public bool Suggesting;
            public string Status = "";
            public int Origin, Target;
            public string OriginName = "", TargetName = "";
            public readonly List<ListItem> Bodies = new List<ListItem>();
            public string DetailsTitle = "";
            public readonly List<Line> Details = new List<Line>();
            public bool CanTarget;
            public readonly List<ListItem> Targets = new List<ListItem>();
            public string NodePreview = "";
            public readonly List<Line> Node = new List<Line>();
            public bool HasNode;
            public string CalcTitle = "";
            public readonly List<Line> Calc = new List<Line>();
            public bool HasResult;
            public string Verdict = "";
            public Color VerdictColor = Color.white;
            public readonly List<Line> Result = new List<Line>();
            public readonly List<Line> MapSummary = new List<Line>();
            public string ScaleText = "";
            public string AssistHint = "";
        }

        static void Row(List<Line> l, string label, string value) => l.Add(new Line { Kind = LineKind.Row, Label = label, Value = value });
        static void Text(List<Line> l, string text, Color color) => l.Add(new Line { Kind = LineKind.Text, Value = text, Color = color });
        static void Title(List<Line> l, string text) => l.Add(new Line { Kind = LineKind.Title, Value = text });
        static void Gap(List<Line> l) => l.Add(new Line { Kind = LineKind.Gap });

        bool NeedsRebuild() => view == null || dirty || Time.unscaledTime >= nextBuild;

        void BuildView()
        {
            dirty = false;
            nextBuild = Time.unscaledTime + RebuildInterval;
            var v = new View();
            view = v;
            PatchedConicSystem sys = service != null ? service.System : null;
            if (sys == null) return;
            v.HasSystem = true;
            v.Assisted = service.Assisted;
            v.Suggesting = service.Suggesting;
            v.Status = service.Status ?? "";
            v.Origin = service.Origin;
            v.Target = service.Plan.Target;
            v.OriginName = v.Origin >= PatchedConicSystem.Star && v.Origin < sys.Count ? sys.NameOf(v.Origin) : "—";
            v.TargetName = v.Target >= 0 && v.Target < sys.Count ? sys.NameOf(v.Target) : "не выбрана";
            if (!selectionInitialized)
            {
                selectionInitialized = true;
                selectedBody = v.Target >= 0 ? v.Target : v.Origin;
            }
            if (selectedBody < PatchedConicSystem.Star || selectedBody >= sys.Count) selectedBody = PatchedConicSystem.Star;

            PlannerVehicle vehicle = service.Vehicle();
            SyncFields();
            BuildBodyList(v, sys);
            BuildDetails(v, sys);
            BuildPlan(v, sys, vehicle);
            BuildMapSummary(v, sys);
        }

        void BuildBodyList(View v, PatchedConicSystem sys)
        {
            v.Bodies.Add(new ListItem { Index = PatchedConicSystem.Star, Label = "☉ " + sys.NameOf(PatchedConicSystem.Star) + " (звезда)" });
            for (int i = 0; i < sys.Count; i++)
            {
                NavBody b = sys[i];
                string label = (b.ParentIndex >= 0 ? "    ↳ " : "") + b.Name;
                if (i == v.Origin) label += " · здесь";
                if (i == v.Target) label += " · цель";
                v.Bodies.Add(new ListItem { Index = i, Label = label });
                if (i != v.Origin) v.Targets.Add(new ListItem { Index = i, Label = b.Name });
            }
        }

        void BuildDetails(View v, PatchedConicSystem sys)
        {
            List<Line> d = v.Details;
            int i = selectedBody;
            if (i < 0)
            {
                StarProperties star = sys.StarProperties;
                string systemName = system != null && !string.IsNullOrEmpty(system.SystemName) ? " · система «" + system.SystemName + "»" : "";
                v.DetailsTitle = sys.NameOf(PatchedConicSystem.Star) + " — звезда" + systemName;
                v.CanTarget = false;
                if (star != null)
                {
                    Row(d, "Масса", UnitFormatter.Scientific(star.mass, 4) + " кг");
                    Row(d, "Радиус", N(star.radius / 1000.0, 0) + " км");
                    Row(d, "Температура поверхности", N(star.surfaceTemperature, 0) + " K (" + N(star.surfaceTemperature - 273.15, 0) + " °C)");
                }
                Row(d, "Расстояние от аппарата", Dist(service.DistanceToBody(PatchedConicSystem.Star)));
                Gap(d);
                Text(d, "Центр системы: вокруг звезды обращаются планеты. Справочная информация — целью полёта звезда быть не может.", Color.white);
                return;
            }

            NavBody b = sys[i];
            PlanetPhysicalProperties ph = b.Physical;
            v.DetailsTitle = b.Name + (i == v.Origin ? " · вы здесь" : "") + (i == v.Target ? " · цель" : "");
            v.CanTarget = i != v.Origin && i != v.Target;

            Title(d, "Физические свойства");
            if (ph != null) Row(d, "Масса", UnitFormatter.Scientific(ph.mass, 4) + " кг");
            Row(d, "Радиус", N(b.Radius / 1000.0, 0) + " км");
            if (ph != null)
            {
                Row(d, "Ускорение g у поверхности", N(ph.SurfaceGravity, 2) + " м/с²");
                double rot = ph.siderealRotationPeriod;
                Row(d, "Сутки (звёздные)", N(Math.Abs(rot) / Day, 3) + " сут" + (rot < 0.0 ? " (обратное вращение)" : ""));
            }

            Gap(d);
            Title(d, "Орбита");
            if (b.Orbit != null)
            {
                double a = b.Orbit.SemiMajorAxis;
                Row(d, "Большая полуось a", b.ParentIndex < 0
                    ? N(a / AstronomicalUnit, 4) + " а.е. (" + N(a / 1e9, 2) + " млн км)"
                    : N(a / 1000.0, 0) + " км");
                Row(d, "Эксцентриситет e", N(b.Orbit.Eccentricity, 4));
                Row(d, "Период обращения", N(b.Orbit.Period / Day, 2) + " сут");
            }
            Row(d, "Обращается вокруг", sys.NameOf(b.ParentIndex));
            Row(d, "Сфера влияния", double.IsInfinity(b.SoiRadius) ? "∞" : N(b.SoiRadius / 1000.0, 0) + " км");

            Gap(d);
            Title(d, "Атмосфера и поверхность");
            AtmosphereProperties atm = ph != null ? ph.atmosphere : null;
            if (atm == null) Row(d, "Атмосфера", "не задан");
            else
            {
                Row(d, "Температура у поверхности", N(atm.surfaceTemperature, 0) + " K (" + N(atm.surfaceTemperature - 273.15, 0) + " °C)");
                if (!atm.hasAtmosphere) Row(d, "Атмосфера", "нет атмосферы");
                else
                {
                    Row(d, "Состав", string.IsNullOrEmpty(atm.composition) ? "не задан" : atm.composition);
                    Row(d, "Давление у поверхности", PressureText(atm.surfacePressure) + " (" + N(atm.surfacePressure / StandardAtmosphere, 3) + " атм)");
                }
            }
            EnvironmentHazards hz = b.Hazards ?? new EnvironmentHazards();
            Row(d, "Радиация", hz.radiationLevel > 0 ? "уровень " + hz.radiationLevel : "нет (0)");
            Row(d, "Кислота", hz.acidLevel > 0 ? "уровень " + hz.acidLevel : "нет (0)");
            Text(d, "Уровни опасности: 0 = нет; чем выше уровень, тем более сильная защита аппарата нужна (магазин).", Color.white);
            if (!string.IsNullOrEmpty(b.Description)) Text(d, b.Description, Color.white);

            Gap(d);
            Row(d, "Расстояние от аппарата", Dist(service.DistanceToBody(i)));

            Gap(d);
            Title(d, "Требования миссии (уровень A, с поверхности: " + v.OriginName + ")");
            if (i == v.Origin)
            {
                Text(d, "Вы здесь.", HudStyle.Accent);
                return;
            }
            if (missionIndex != i || Time.unscaledTime >= missionTime)
            {
                missionIndex = i;
                missionTime = Time.unscaledTime + MissionInterval;
                missionCache = service.MissionRequirementsFor(i);
            }
            TransferRequirements m = missionCache;
            if (m == null) Text(d, "Нет данных.", HudStyle.Warning);
            else if (!m.Ok) Text(d, m.Error, HudStyle.Warning);
            else
            {
                if (m.IncludesAscent) Row(d, "Подъём на орбиту", Dv(m.AscentDeltaV));
                Row(d, "Отлёт (к цели)", Dv(m.DepartureDeltaV));
                Row(d, "Захват на орбиту цели", Dv(m.CaptureDeltaV));
                if (m.IncludesLanding) Row(d, "Посадка", Dv(m.LandingDeltaV));
                Row(d, "Итого", Dv(m.TotalDeltaV));
                Row(d, "Время перелёта", Days(m.TransferTime));
                if (m.IncludesLanding && !string.IsNullOrEmpty(m.LandingNote)) Text(d, m.LandingNote, Color.white);
                Text(d, "Идеально: без потерь на подъём (+1–2 км/с в атмосфере).", HudStyle.Warning);
            }
        }

        void BuildPlan(View v, PatchedConicSystem sys, PlannerVehicle vehicle)
        {
            FlightPlanInput plan = service.Plan;
            double now = service.Now;

            if (AcademyNumber.TryParse(fields[FDt], out double dt) && dt >= 0.0)
                v.NodePreview = "= " + UnitFormatter.Duration(dt) + " · момент импульса t = " + UnitFormatter.Duration(now + dt) + " (время симуляции)";
            else v.NodePreview = "введите число секунд ≥ 0";

            ManeuverNode node = plan.Departure;
            v.HasNode = node != null;
            if (node == null) Text(v.Node, "Манёвр не задан.", Color.white);
            else
            {
                double toNode = node.Time - now;
                Row(v.Node, "До манёвра", toNode >= 0.0 ? UnitFormatter.Duration(toNode) : "время прошло (" + UnitFormatter.Duration(-toNode) + " назад)");
                Row(v.Node, "Момент (время симуляции)", UnitFormatter.Duration(node.Time));
                Row(v.Node, "Прогрейд / нормаль / радиально", N(node.Prograde, 1) + " / " + N(node.Normal, 1) + " / " + N(node.Radial, 1) + " м/с");
                Row(v.Node, "|Δv| манёвра", Dv(node.Magnitude));
                NodeGuidance g = service.Guidance;
                if (g != null && g.Active && g.BurnTime > 0.0) Row(v.Node, "Работа двигателя (оценка)", UnitFormatter.Duration(g.BurnTime));
                if (toNode < 0.0) Text(v.Node, "Время манёвра прошло — задайте новый или удалите его.", HudStyle.Warning);
            }

            // Level A block: live in assisted mode, only after the check in manual mode.
            TransferRequirements q = null;
            if (v.Assisted)
            {
                v.CalcTitle = "Расчёт (уровень A) · обновляется постоянно";
                q = service.Requirements;
                v.AssistHint = "Подсказка: окно запуска — когда цель на угле θ; Δv отлёта — Академия, тема «Окна запуска».";
            }
            else
            {
                v.CalcTitle = "Расчёт (уровень A)";
                if (service.LastResult != null) q = service.LastResult.Requirements;
            }
            if (q != null) BuildCalc(v.Calc, q, vehicle);
            else if (v.Assisted) Text(v.Calc, plan.Target < 0 ? "Выберите цель." : "Расчёт ещё не готов.", Color.white);
            else Text(v.Calc, "Ручной режим: всё рассчитываете сами, автоматических подсказок и поправок до проверки нет. " +
                              "Подробная оценка — после нажатия «Проверить план».", Color.white);

            // Check result.
            FlightPlanResult r = service.LastResult;
            v.HasResult = r != null;
            if (r == null) return;
            v.Verdict = r.Verdict;
            v.VerdictColor = r.Passed ? HudStyle.Accent : HudStyle.Danger;
            foreach (PlanIssue issue in r.Issues)
            {
                if (issue == null) continue;
                Text(v.Result, issue.Reason, Color.white);
                if (!string.IsNullOrEmpty(issue.Fix)) Text(v.Result, "Как исправить: " + issue.Fix, HudStyle.Warning);
                Gap(v.Result);
            }
            TrajectoryPrediction p = r.Prediction;
            if (p != null && p.Encounter && plan.Target >= 0 && plan.Target < sys.Count)
            {
                Title(v.Result, "Попадание в сферу влияния: " + sys.NameOf(plan.Target));
                Row(v.Result, "Вход в сферу влияния", "через " + Days(p.EncounterTime - now));
                Row(v.Result, "v∞ прибытия", Dv(p.ArrivalVInfinity));
                Row(v.Result, "Перицентр над поверхностью", N((p.ArrivalPeriapsis - sys[plan.Target].Radius) / 1000.0, 0) + " км");
                Row(v.Result, "Δv захвата (по прогнозу)", Dv(r.ActualCaptureDeltaV));
            }
            else if (p != null && plan.Target >= 0 && !double.IsInfinity(p.ClosestDistance))
                Row(v.Result, "Ближайшее сближение с целью", Dist(p.ClosestDistance) + " через " + Days(p.ClosestTime - now));
            if (planEditedSinceCheck) Text(v.Result, "План изменён после проверки — нажмите «Проверить план» снова.", HudStyle.Warning);
        }

        static void BuildCalc(List<Line> c, TransferRequirements q, PlannerVehicle vehicle)
        {
            if (!q.Ok)
            {
                Text(c, q.Error, HudStyle.Warning);
                return;
            }
            Row(c, "Время перелёта (Хоман)", Days(q.TransferTime));
            Row(c, "Нужный фазовый угол θ", Deg(q.RequiredPhase));
            Row(c, "Текущий фазовый угол", Deg(q.CurrentPhase));
            Row(c, "До окна запуска", Days(q.TimeToWindow));
            Row(c, "Окно (время симуляции)", UnitFormatter.Duration(q.WindowTime));
            Row(c, "Синодический период", Days(q.SynodicPeriod));
            Row(c, "v∞ отлёта / прибытия", N(q.VInfDeparture, 0) + " / " + N(q.VInfArrival, 0) + " м/с");
            Gap(c);
            if (q.IncludesAscent) Row(c, "Δv подъём на орбиту", Dv(q.AscentDeltaV));
            Row(c, "Δv отлёт (орбита " + N(q.ParkingAltitude / 1000.0, 0) + " км)", Dv(q.DepartureDeltaV));
            Row(c, "Δv захват (орбита " + N(q.CaptureAltitude / 1000.0, 0) + " км)", Dv(q.CaptureDeltaV));
            if (q.IncludesLanding) Row(c, "Δv посадка", Dv(q.LandingDeltaV));
            Row(c, "Δv итого", Dv(q.TotalDeltaV));
            if (vehicle != null) Row(c, "Δv аппарата", Dv(vehicle.DeltaV));
            if (q.DeltaVDeficit > 0.0)
                Text(c, "Не хватает " + Dv(q.DeltaVDeficit) + (q.PropellantForDeficit > 0.0
                    ? " — это ≈ " + N(q.PropellantForDeficit / 1000.0, 1) + " т топлива на последней ступени." : "."), HudStyle.Danger);
            else Text(c, "Δv аппарата хватает на весь план (идеальные импульсы).", HudStyle.Accent);
            if (q.ProtectionShort)
                Text(c, "Защита слабее опасностей цели (радиация: " + q.TargetRadiation + ", кислота: " + q.TargetAcid + ") — нужна защита (магазин).",
                    HudStyle.Warning);
            if (q.IncludesLanding && !string.IsNullOrEmpty(q.LandingNote)) Text(c, q.LandingNote, Color.white);
        }

        void BuildMapSummary(View v, PatchedConicSystem sys)
        {
            List<Line> s = v.MapSummary;
            TrajectoryPrediction p = service.Prediction;
            double now = service.Now;
            int target = service.Plan.Target;
            if (p == null) Text(s, "Прогноз траектории недоступен (аппарат на поверхности или нет данных).", Color.white);
            else
            {
                if (p.Encounter && target >= 0 && target < sys.Count)
                    Text(s, "Попадание в сферу влияния " + sys.NameOf(target) + " через " + Days(p.EncounterTime - now) +
                            " · перицентр " + N((p.ArrivalPeriapsis - sys[target].Radius) / 1000.0, 0) + " км над поверхностью · v∞ " +
                            Dv(p.ArrivalVInfinity), HudStyle.Accent);
                else if (target >= 0 && target < sys.Count && !double.IsInfinity(p.ClosestDistance))
                    Text(s, "Ближайшее сближение с " + sys.NameOf(target) + ": " + Dist(p.ClosestDistance) + " через " +
                            Days(p.ClosestTime - now) + " (в сферу влияния не попадает).", HudStyle.Warning);
                else Text(s, "Цель не выбрана — показана только траектория.", Color.white);
                if (p.Impact)
                    Text(s, "Столкновение с " + sys.NameOf(p.ImpactBody) + " через " + UnitFormatter.Duration(p.ImpactTime - now) + ".", HudStyle.Danger);
                if (p.NodeApplied) Text(s, "Траектория учитывает заданный манёвр.", Color.white);
            }
            TransferRequirements q = v.Assisted ? service.Requirements : service.LastResult?.Requirements;
            if (q != null && q.Ok) Text(s, "Время перелёта (Хоман): " + Days(q.TransferTime), Color.white);
            else if (!v.Assisted && target >= 0) Text(s, "Время перелёта: ручной режим — рассчитайте сами (или проверьте план).", Color.white);

            MapFrame(sys, out double half, out _, out _);
            double division = NiceStep(half / 4.0);
            v.ScaleText = "Масштаб: 1 деление = " +
                          (division >= 1e8 ? N(division / 1e9, division >= 1e9 ? 0 : 1) + " млн км" : N(division / 1000.0, 0) + " км") +
                          (half > 0.2 * AstronomicalUnit ? " (" + N(division / AstronomicalUnit, 2) + " а.е.)" : "");
        }

        // ---- Text field buffers ----------------------------------------------------------------------------------

        double PlanFieldValue(int i)
        {
            FlightPlanInput plan = service.Plan;
            switch (i)
            {
                case FPark: return plan.ParkingAltitude / 1000.0;
                case FCap: return plan.CaptureAltitude / 1000.0;
                default: return plan.PlannedCaptureDeltaV;
            }
        }

        /// <summary>Layout only: unfocused plan fields follow the plan; node fields follow a newly set/suggested node.</summary>
        void SyncFields()
        {
            string focused = GUIUtility.keyboardControl != 0 ? GUI.GetNameOfFocusedControl() ?? "" : "";
            for (int i = FPark; i <= FCapDv; i++)
            {
                if (focused == FieldNames[i]) continue;
                double value = PlanFieldValue(i);
                if (!AcademyNumber.TryParse(fields[i], out double typed) || Math.Abs(typed - value) > 1e-9 * Math.Max(1.0, Math.Abs(value)))
                    fields[i] = AcademyNumber.FormatEditable(value);
                badField[i] = false;
            }
            ManeuverNode node = service.Plan.Departure;
            if (ReferenceEquals(node, lastNode)) return;
            lastNode = node;
            if (node == null) return; // keep the typed values after «Удалить манёвр»
            if (focused.StartsWith(FieldPrefix, StringComparison.Ordinal)) GUIUtility.keyboardControl = 0;
            fields[FDt] = AcademyNumber.FormatEditable(Math.Max(0.0, Math.Round(node.Time - service.Now, 1)));
            fields[FPro] = AcademyNumber.FormatEditable(node.Prograde);
            fields[FNor] = AcademyNumber.FormatEditable(node.Normal);
            fields[FRad] = AcademyNumber.FormatEditable(node.Radial);
            for (int i = FDt; i < FieldCount; i++) badField[i] = false;
        }

        void OnFieldEdited(int i)
        {
            bool ok = AcademyNumber.TryParse(fields[i], out double value);
            if (i <= FCapDv || i == FDt) ok &= value >= 0.0;
            badField[i] = !ok;
            if (!ok || i > FCapDv) return;
            FlightPlanInput plan = service.Plan;
            if (i == FPark) plan.ParkingAltitude = value * 1000.0;
            else if (i == FCap) plan.CaptureAltitude = value * 1000.0;
            else plan.PlannedCaptureDeltaV = value;
            planEditedSinceCheck = true;
        }

        // ---- Drawing ---------------------------------------------------------------------------------------------

        void OnGUI()
        {
            if (!open || service == null || FlightInput.PauseMenuOpen) return;
            Event e = Event.current;
            bool layout = e != null && e.type == EventType.Layout;
            if (awaitingLayout)
            {
                if (!layout) return;
                awaitingLayout = false;
            }
            if (layout)
            {
                if (requestedTab.HasValue)
                {
                    tab = requestedTab.Value;
                    requestedTab = null;
                    GUIUtility.keyboardControl = 0;
                }
                if (NeedsRebuild()) BuildView();
            }
            if (view == null) return;

            // Enter in one of our fields: keep the value, release the focus (game hotkeys work again).
            if (e != null && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) &&
                GUIUtility.keyboardControl != 0)
            {
                string focused = GUI.GetNameOfFocusedControl();
                if (!string.IsNullOrEmpty(focused) && focused.StartsWith(FieldPrefix, StringComparison.Ordinal))
                {
                    GUIUtility.keyboardControl = 0;
                    e.Use();
                }
            }

            GUI.depth = -10;
            HudStyle.Begin();
            EnsureStyles();

            Vector2 screen = HudStyle.VirtualScreen;
            float w = Mathf.Min(PreferredWidth, screen.x - 20f);
            float h = Mathf.Min(PreferredHeight, screen.y - 20f);
            var r = new Rect((screen.x - w) * 0.5f, (screen.y - h) * 0.5f, w, h);
            HudStyle.WindowFrame(r);

            float innerW = w - 20f, innerH = h - 16f;
            contentWidth = Mathf.Max(300f, innerW - 24f);
            float bodyHeight = Mathf.Max(160f, innerH - 148f);

            GUILayout.BeginArea(new Rect(r.x + 10f, r.y + 8f, innerW, innerH));
            DrawHeader();
            GUILayout.Space(4);
            DrawTabs(innerW);
            GUILayout.Space(6);
            if (!view.HasSystem)
                Colored("Навигационная система недоступна — звёздная система ещё не загружена.", note, HudStyle.Danger);
            else
            {
                switch (tab)
                {
                    case Page.Planets: DrawPlanets(innerW, bodyHeight); break;
                    case Page.Plan: DrawPlan(bodyHeight); break;
                    default: DrawMap(innerW, bodyHeight); break;
                }
            }
            GUILayout.FlexibleSpace();
            DrawFooter(innerW);
            GUILayout.EndArea();

            SwallowMouse(r);
            ApplyPending();
        }

        void EnsureStyles()
        {
            if (note != null && compact != null && mapLabel != null && verdict != null) return;
            // White base colour so GUI.color tints give exactly HudStyle.Accent / Warning / Danger.
            note = new GUIStyle(HudStyle.Small) { fontSize = 12, wordWrap = true };
            note.normal.textColor = Color.white;
            compact = new GUIStyle(HudStyle.Small) { wordWrap = false };
            mapLabel = new GUIStyle(HudStyle.Small) { wordWrap = false };
            mapLabel.normal.textColor = Color.white;
            verdict = new GUIStyle(HudStyle.Phase) { alignment = TextAnchor.MiddleLeft };
            verdict.normal.textColor = Color.white;
        }

        /// <summary>Clicks and scrolling inside the window must not reach HUD controls or the camera underneath.</summary>
        static void SwallowMouse(Rect window)
        {
            Event e = Event.current;
            if (e == null) return;
            EventType t = e.type;
            bool mouse = t == EventType.MouseDown || t == EventType.MouseUp || t == EventType.MouseDrag || t == EventType.ScrollWheel;
            if (mouse && window.Contains(e.mousePosition)) e.Use();
        }

        void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("НАВИГАЦИЯ · межпланетные перелёты", HudStyle.WindowTitle);
            GUILayout.FlexibleSpace();
            bool assisted = view.Assisted;
            if (Button("Режим: " + (assisted ? "С помощником" : "Ручной"), session != null, GUILayout.Width(220), GUILayout.Height(24)))
            {
                GUIUtility.keyboardControl = 0;
                Defer(() => service.SetAssisted(!assisted));
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Планеты движутся по орбитам; расчёты — уровень A (идеальные импульсы), проверка — прогноз по сферам влияния с реальным движением планет.",
                HudStyle.Small);
        }

        void DrawTabs(float innerW)
        {
            float tw = Mathf.Min(180f, (innerW - 16f) / TabNames.Length);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < TabNames.Length; i++)
            {
                var t = (Page)i;
                bool on = tab == t;
                if (GUILayout.Toggle(on, TabNames[i], HudStyle.Tab, GUILayout.Width(tw), GUILayout.Height(26)) && !on)
                {
                    GUIUtility.keyboardControl = 0;
                    Defer(() => tab = t);
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label("Отправление: " + view.OriginName + " · цель: " + view.TargetName, compact);
            GUILayout.EndHorizontal();
        }

        // ---- Tab 1: planets --------------------------------------------------------------------------------------

        void DrawPlanets(float innerW, float height)
        {
            const float listWidth = 250f;
            float detailWidth = Mathf.Max(300f, innerW - listWidth - 20f);
            GUILayout.BeginHorizontal();

            listScroll = GUILayout.BeginScrollView(listScroll, GUILayout.Width(listWidth), GUILayout.Height(height));
            foreach (ListItem item in view.Bodies)
            {
                int index = item.Index;
                bool on = index == selectedBody;
                if (GUILayout.Toggle(on, item.Label, HudStyle.Button, GUILayout.Width(listWidth - 30f), GUILayout.Height(24)) && !on)
                {
                    GUIUtility.keyboardControl = 0;
                    Defer(() => { selectedBody = index; detailScroll = Vector2.zero; });
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8);
            GUILayout.BeginVertical(GUILayout.Width(detailWidth));
            GUILayout.BeginHorizontal();
            GUILayout.Label(view.DetailsTitle, HudStyle.Title);
            GUILayout.FlexibleSpace();
            if (view.CanTarget)
            {
                int index = selectedBody;
                if (Button("Выбрать целью", true, GUILayout.Width(170), GUILayout.Height(24)))
                {
                    GUIUtility.keyboardControl = 0;
                    Defer(() => { ChooseTarget(index); tab = Page.Plan; });
                }
            }
            GUILayout.EndHorizontal();
            detailScroll = GUILayout.BeginScrollView(detailScroll, GUILayout.Height(height - 30f));
            DrawLines(view.Details, 260f, detailWidth - 30f);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        // ---- Tab 2: flight plan ----------------------------------------------------------------------------------

        void DrawPlan(float height)
        {
            float colW = Mathf.Max(300f, (contentWidth - 16f) * 0.5f);
            planScroll = GUILayout.BeginScrollView(planScroll, GUILayout.Height(height));

            // Target selector.
            GUILayout.BeginVertical(HudStyle.Panel, GUILayout.Width(contentWidth));
            GUILayout.Label("Цель: " + view.TargetName, HudStyle.Title);
            if (view.Targets.Count == 0) GUILayout.Label("Других тел нет.", HudStyle.Small);
            const int perRow = 6;
            float bw = Mathf.Min(170f, (contentWidth - 40f) / perRow);
            for (int start = 0; start < view.Targets.Count; start += perRow)
            {
                GUILayout.BeginHorizontal();
                for (int k = start; k < Math.Min(view.Targets.Count, start + perRow); k++)
                {
                    ListItem t = view.Targets[k];
                    int index = t.Index;
                    bool on = index == view.Target;
                    if (GUILayout.Toggle(on, t.Label, HudStyle.Button, GUILayout.Width(bw), GUILayout.Height(24)) && !on)
                    {
                        GUIUtility.keyboardControl = 0;
                        Defer(() => ChooseTarget(index));
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();

            // Left column: orbits, departure burn, check.
            GUILayout.BeginVertical(GUILayout.Width(colW));
            GUILayout.BeginVertical(HudStyle.Panel);
            GUILayout.Label("Орбиты и посадка", HudStyle.Title);
            FieldRow("Орбита ожидания у " + view.OriginName, FPark, "км");
            FieldRow("Орбита у цели", FCap, "км");
            bool land = service.Plan.Land;
            bool newLand = GUILayout.Toggle(land, " Посадка на цель");
            if (newLand != land)
            {
                service.Plan.Land = newLand;
                planEditedSinceCheck = true;
                dirty = true;
            }
            FieldRow("Ваш импульс захвата (0 — не задан)", FCapDv, "м/с");
            GUILayout.EndVertical();
            GUILayout.Space(4);

            GUILayout.BeginVertical(HudStyle.Panel);
            GUILayout.Label("Манёвр отлёта (импульс)", HudStyle.Title);
            FieldRow("Время импульса: через", FDt, "с от текущего момента");
            GUILayout.Label(view.NodePreview, HudStyle.Small);
            FieldRow("Прогрейд", FPro, "м/с");
            FieldRow("Нормаль", FNor, "м/с");
            FieldRow("Радиально", FRad, "м/с");
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            if (Button("Задать манёвр", true, GUILayout.Width(150), GUILayout.Height(26)))
            {
                GUIUtility.keyboardControl = 0;
                Defer(ApplyNode);
            }
            if (Button("Удалить манёвр", view.HasNode, GUILayout.Width(150), GUILayout.Height(26)))
            {
                GUIUtility.keyboardControl = 0;
                Defer(RemoveNode);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (view.Assisted)
            {
                string label = view.Suggesting ? "Подбираю…" : "Подобрать манёвр (помощник)";
                if (Button(label, !view.Suggesting, GUILayout.Width(306), GUILayout.Height(26)))
                {
                    GUIUtility.keyboardControl = 0;
                    Defer(() => service.SuggestAsync());
                }
            }
            GUILayout.Space(2);
            DrawLines(view.Node, 210f, colW - 30f);
            GUILayout.EndVertical();
            GUILayout.Space(4);

            if (Button("Проверить план", true, GUILayout.Width(colW - 10f), GUILayout.Height(32)))
            {
                GUIUtility.keyboardControl = 0;
                Defer(CheckPlan);
            }
            if (!string.IsNullOrEmpty(localStatus)) Colored(localStatus, note, localStatusColor, GUILayout.Width(colW - 10f));
            if (!string.IsNullOrEmpty(view.Status)) Colored("Навигация: " + view.Status, note, Color.white, GUILayout.Width(colW - 10f));
            GUILayout.EndVertical();

            GUILayout.Space(8);

            // Right column: Level A numbers and the check result.
            GUILayout.BeginVertical(GUILayout.Width(colW));
            GUILayout.BeginVertical(HudStyle.Panel);
            GUILayout.Label(view.CalcTitle, HudStyle.Title);
            DrawLines(view.Calc, 230f, colW - 30f);
            if (!string.IsNullOrEmpty(view.AssistHint)) Colored(view.AssistHint, note, HudStyle.Accent, GUILayout.Width(colW - 30f));
            GUILayout.EndVertical();
            GUILayout.Space(4);
            GUILayout.BeginVertical(HudStyle.Panel);
            if (view.HasResult)
            {
                Colored(view.Verdict, verdict, view.VerdictColor);
                DrawLines(view.Result, 230f, colW - 30f);
            }
            else GUILayout.Label("План ещё не проверен. Нажмите «Проверить план» — прогноз по сферам влияния покажет, попадает ли манёвр в цель.",
                note, GUILayout.Width(colW - 30f));
            GUILayout.EndVertical();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }

        void FieldRow(string label, int index, string unit)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, HudStyle.Label, GUILayout.Width(250));
            GUI.SetNextControlName(FieldNames[index]);
            string value = GUILayout.TextField(fields[index] ?? "", HudStyle.Field, GUILayout.Width(110));
            if (value != fields[index])
            {
                fields[index] = value;
                OnFieldEdited(index);
            }
            GUILayout.Label(unit, compact);
            if (badField[index]) Colored("не число ≥ 0", compact, HudStyle.Danger);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        // ---- Tab 3: map ------------------------------------------------------------------------------------------

        void DrawMap(float innerW, float height)
        {
            const float sideWidth = 300f;
            float size = Mathf.Max(200f, Mathf.Min(height - 24f, innerW - sideWidth - 30f));
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(size));
            Rect map = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            if (Event.current != null && Event.current.type == EventType.Repaint) PaintMap(map);
            GUILayout.Label(view.ScaleText, compact);
            GUILayout.EndVertical();

            GUILayout.Space(10);
            GUILayout.BeginVertical(GUILayout.Width(sideWidth));
            GUILayout.Label("Вид", HudStyle.Title);
            GUILayout.BeginHorizontal();
            if (Button("+", true, GUILayout.Width(44), GUILayout.Height(24)))
            {
                GUIUtility.keyboardControl = 0;
                Defer(() => mapZoom = Math.Min(mapZoom * 2.0, 4096.0));
            }
            if (Button("−", true, GUILayout.Width(44), GUILayout.Height(24)))
            {
                GUIUtility.keyboardControl = 0;
                Defer(() => mapZoom = Math.Max(mapZoom * 0.5, 1.0 / 64.0));
            }
            GUILayout.Label("×" + N(mapZoom, mapZoom < 1.0 ? 3 : 0), compact);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(!mapAroundVehicle, "Вся система", HudStyle.Button, GUILayout.Width(140), GUILayout.Height(24)) && mapAroundVehicle)
            {
                GUIUtility.keyboardControl = 0;
                Defer(() => { mapAroundVehicle = false; mapZoom = 1.0; });
            }
            if (GUILayout.Toggle(mapAroundVehicle, "Вокруг аппарата", HudStyle.Button, GUILayout.Width(140), GUILayout.Height(24)) && !mapAroundVehicle)
            {
                GUIUtility.keyboardControl = 0;
                Defer(() => { mapAroundVehicle = true; mapZoom = 1.0; });
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Обозначения", HudStyle.Title);
            Colored("■ звезда", compact, StarColor);
            Colored("■ планеты и луны (сейчас)", compact, BodyColor);
            Colored("■ " + view.OriginName + " — отправление", compact, HudStyle.Accent);
            Colored("■ цель: " + view.TargetName, compact, HudStyle.Warning);
            Colored("○ цель в момент встречи / сближения", compact, HudStyle.Warning);
            Colored("■ аппарат", compact, Color.white);
            Colored("· · · прогноз траектории", compact, HudStyle.Accent);
            Colored("· · · орбиты тел", compact, OrbitColor);
            if (mapAroundVehicle) Colored("· · · сфера влияния", compact, SoiColor);
            GUILayout.Label("Вид сверху на плоскость эклиптики (X, Y).", HudStyle.Small);

            GUILayout.Space(6);
            GUILayout.Label("Прогноз", HudStyle.Title);
            mapScroll = GUILayout.BeginScrollView(mapScroll, GUILayout.Height(Mathf.Max(80f, height - 330f)));
            DrawLines(view.MapSummary, 0f, sideWidth - 30f);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        /// <summary>Visible half-extent [m] and centre (star-centric, ecliptic X/Y) of the map.</summary>
        void MapFrame(PatchedConicSystem sys, out double half, out double cx, out double cy)
        {
            EnsureOrbitCache(sys);
            if (mapAroundVehicle)
            {
                Vector3d vehicle = service.VehicleStarPosition();
                cx = vehicle.x;
                cy = vehicle.y;
                int primary = service.Origin;
                double soi = primary >= 0 && primary < sys.Count ? sys[primary].SoiRadius : double.PositiveInfinity;
                double baseHalf = double.IsInfinity(soi) || double.IsNaN(soi) || soi <= 0.0 ? 0.05 * AstronomicalUnit : 3.0 * soi;
                half = baseHalf / mapZoom;
            }
            else
            {
                cx = 0.0;
                cy = 0.0;
                half = systemExtent / mapZoom;
            }
            if (!(half > 0.0)) half = AstronomicalUnit;
        }

        /// <summary>Orbit shapes relative to the parent, sampled once per navigation system (they do not change).</summary>
        void EnsureOrbitCache(PatchedConicSystem sys)
        {
            if (ReferenceEquals(sys, orbitSystem) && orbitRelative != null) return;
            orbitSystem = sys;
            orbitRelative = new List<Vector3d>[sys.Count];
            orbitScreen = new List<Vector2>[sys.Count];
            double extent = 0.0;
            for (int i = 0; i < sys.Count; i++)
            {
                NavBody b = sys[i];
                var pts = new List<Vector3d>(OrbitSamples);
                orbitRelative[i] = pts;
                orbitScreen[i] = new List<Vector2>(OrbitSamples);
                if (b.Orbit == null) continue;
                double period = b.Orbit.Period;
                for (int k = 0; k < OrbitSamples; k++) pts.Add(b.Orbit.Position(period * k / OrbitSamples));
                if (b.ParentIndex < 0) extent = Math.Max(extent, b.Orbit.SemiMajorAxis * (1.0 + b.Orbit.Eccentricity));
            }
            systemExtent = extent > 0.0 ? extent * 1.1 : AstronomicalUnit;
            orbitKey = default;
            predictionRef = null;
        }

        static Vector2 Project(Rect map, double half, double cx, double cy, Vector3d p)
        {
            float scale = (float)(0.5 * map.width / half);
            return new Vector2(map.x + 0.5f * map.width + (float)(p.x - cx) * scale, map.y + 0.5f * map.height - (float)(p.y - cy) * scale);
        }

        static bool Inside(Rect map, Vector2 p) => p.x >= map.x && p.x <= map.xMax && p.y >= map.y && p.y <= map.yMax;

        /// <summary>Repaint only: GUI primitives; projected points are cached per (scale, centre, rect) and prediction reference.</summary>
        void PaintMap(Rect map)
        {
            PatchedConicSystem sys = service.System;
            GUI.Box(map, GUIContent.none, HudStyle.Panel);
            if (sys == null) return;
            MapFrame(sys, out double half, out double cx, out double cy);
            double now = service.Now;
            var key = (half, cx, cy, map.x, map.y, map.width, map.height);
            float pxPerMetre = (float)(0.5 * map.width / half);
            int origin = service.Origin;
            int target = service.Plan.Target;

            // Orbits (shapes fixed; moons' parents move, so also refreshed twice a second).
            if (key != orbitKey || Time.unscaledTime >= nextOrbitRebuild)
            {
                orbitKey = key;
                nextOrbitRebuild = Time.unscaledTime + 0.5f;
                for (int i = 0; i < sys.Count; i++)
                {
                    List<Vector2> screen = orbitScreen[i];
                    screen.Clear();
                    NavBody b = sys[i];
                    if (b.Orbit == null || b.Orbit.SemiMajorAxis * pxPerMetre < 4f) continue; // too small to see at this scale
                    Vector3d offset = b.ParentIndex >= 0 ? sys.Position(b.ParentIndex, now) : Vector3d.Zero;
                    foreach (Vector3d rel in orbitRelative[i])
                    {
                        Vector2 s = Project(map, half, cx, cy, offset + rel);
                        if (Inside(map, s)) screen.Add(s);
                    }
                }
            }
            for (int i = 0; i < sys.Count; i++)
                foreach (Vector2 s in orbitScreen[i]) HudStyle.Dot(s, 2f, OrbitColor);

            // Sphere of influence of the current primary (vehicle view).
            if (mapAroundVehicle && origin >= 0 && origin < sys.Count && !double.IsInfinity(sys[origin].SoiRadius))
            {
                Vector3d c = sys.Position(origin, now);
                double soi = sys[origin].SoiRadius;
                for (int k = 0; k < 72; k++)
                {
                    double a = MathD.TwoPi * k / 72.0;
                    Vector2 s = Project(map, half, cx, cy, c + new Vector3d(soi * Math.Cos(a), soi * Math.Sin(a), 0.0));
                    if (Inside(map, s)) HudStyle.Dot(s, 2f, SoiColor);
                }
            }

            // Predicted trajectory (rebuilt when the prediction or the view changes).
            TrajectoryPrediction p = service.Prediction;
            if (!ReferenceEquals(p, predictionRef) || key != predictionKey)
            {
                predictionRef = p;
                predictionKey = key;
                predictionScreen.Clear();
                if (p != null)
                {
                    foreach (PredictedPatch patch in p.Patches)
                    {
                        if (patch == null) continue;
                        // Around the vehicle: the current primary's patch is drawn relative to where that body is now
                        // (star-centric samples would be smeared by the planet's own motion at this scale).
                        bool relative = mapAroundVehicle && origin >= 0 && patch.Primary == origin;
                        Vector3d offset = relative ? sys.Position(origin, now) : Vector3d.Zero;
                        List<Vector3d> pts = relative ? patch.RelativePoints : patch.StarPoints;
                        foreach (Vector3d q in pts)
                        {
                            Vector2 s = Project(map, half, cx, cy, offset + q);
                            if (Inside(map, s)) predictionScreen.Add(s);
                        }
                    }
                }
            }
            foreach (Vector2 s in predictionScreen) HudStyle.Dot(s, 2f, HudStyle.Accent);

            // Star, bodies, labels.
            Vector2 star = Project(map, half, cx, cy, Vector3d.Zero);
            if (Inside(map, star))
            {
                HudStyle.Dot(star, 8f, StarColor);
                GUI.Label(new Rect(star.x + 6f, star.y - 8f, 140f, 18f), sys.NameOf(PatchedConicSystem.Star), mapLabel);
            }
            for (int i = 0; i < sys.Count; i++)
            {
                NavBody b = sys[i];
                Vector2 s = Project(map, half, cx, cy, sys.Position(i, now));
                if (!Inside(map, s)) continue;
                // A moon whose orbit is too small to see would sit on its planet's marker.
                if (b.ParentIndex >= 0 && b.Orbit != null && b.Orbit.SemiMajorAxis * pxPerMetre < 4f && i != target && i != origin) continue;
                Color c = i == target ? HudStyle.Warning : i == origin ? HudStyle.Accent : BodyColor;
                float bodyPx = Mathf.Max(6f, (float)(2.0 * b.Radius * pxPerMetre));
                HudStyle.Dot(s, Mathf.Min(bodyPx, map.width * 0.5f), c);
                Color old = GUI.color;
                GUI.color = c;
                GUI.Label(new Rect(s.x + 6f, s.y - 8f, 140f, 18f), b.Name, mapLabel);
                GUI.color = old;
            }

            // Target at encounter / closest approach (hollow ring).
            if (p != null && target >= 0 && target < sys.Count && (p.Encounter || !double.IsInfinity(p.ClosestDistance)))
            {
                Vector2 s = Project(map, half, cx, cy, sys.Position(target, p.Encounter ? p.EncounterTime : p.ClosestTime));
                if (Inside(map, s))
                    for (int k = 0; k < 16; k++)
                    {
                        float a = 2f * Mathf.PI * k / 16f;
                        HudStyle.Dot(new Vector2(s.x + 7f * Mathf.Cos(a), s.y + 7f * Mathf.Sin(a)), 2f, HudStyle.Warning);
                    }
            }

            // Vehicle.
            Vector2 vehicle = Project(map, half, cx, cy, service.VehicleStarPosition());
            if (Inside(map, vehicle))
            {
                HudStyle.Dot(vehicle, 6f, Color.white);
                GUI.Label(new Rect(vehicle.x + 6f, vehicle.y + 2f, 100f, 18f), "аппарат", mapLabel);
            }

            // Scale bar: one division.
            double division = NiceStep(half / 4.0);
            float barPx = (float)(division * pxPerMetre);
            float bx = map.x + 12f, by = map.yMax - 14f;
            GUI.DrawTexture(new Rect(bx, by, barPx, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(bx, by - 4f, 1f, 10f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(bx + barPx - 1f, by - 4f, 1f, 10f), Texture2D.whiteTexture);
        }

        /// <summary>Round display step 1/2/5·10ⁿ not larger than <paramref name="x"/> (map drawing only).</summary>
        static double NiceStep(double x)
        {
            if (!(x > 0.0) || double.IsInfinity(x)) return 1.0;
            double p = Math.Pow(10.0, Math.Floor(Math.Log10(x)));
            double f = x / p;
            return (f >= 5.0 ? 5.0 : f >= 2.0 ? 2.0 : 1.0) * p;
        }

        // ---- Footer ----------------------------------------------------------------------------------------------

        void DrawFooter(float innerW)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(FlightInput.KeyLabel(InputKey.G) + " — открыть/закрыть · " + FlightInput.KeyLabel(InputKey.Comma) + " " + FlightInput.KeyLabel(InputKey.Period) + " — ускорение времени (выше ×1000 — только вне атмосферы, с выключенным двигателем)",
                HudStyle.Small, GUILayout.Width(Mathf.Max(100f, innerW - 190f)));
            GUILayout.FlexibleSpace();
            if (Button("Закрыть (" + FlightInput.KeyLabel(InputKey.G) + " · Esc)", true, GUILayout.Width(170), GUILayout.Height(30)))
            {
                GUIUtility.keyboardControl = 0;
                Defer(Close);
            }
            GUILayout.EndHorizontal();
        }

        // ---- IMGUI helpers ---------------------------------------------------------------------------------------

        void DrawLines(List<Line> lines, float labelWidth, float width)
        {
            foreach (Line l in lines)
            {
                switch (l.Kind)
                {
                    case LineKind.Row: HudStyle.Row(l.Label, l.Value, labelWidth); break;
                    case LineKind.Title: GUILayout.Label(l.Value, HudStyle.Title); break;
                    case LineKind.Gap: GUILayout.Space(6); break;
                    default: Colored(l.Value, note, l.Color, GUILayout.Width(width)); break;
                }
            }
        }

        static bool Button(string text, bool enabled, params GUILayoutOption[] options)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            bool clicked = GUILayout.Button(text, HudStyle.Button, options);
            GUI.enabled = previous;
            return clicked && enabled;
        }

        static void Colored(string text, GUIStyle style, Color color, params GUILayoutOption[] options)
        {
            Color c = GUI.color;
            GUI.color = color;
            GUILayout.Label(text, style, options);
            GUI.color = c;
        }

        // ---- Formatting (display only; unit conversions) ---------------------------------------------------------

        static string N(double value, int decimals) => UnitFormatter.Number(value, decimals);

        static string Dv(double metresPerSecond) => N(metresPerSecond, 0) + " м/с";

        static string Days(double seconds) => AcademyNumber.Format(seconds / Day) + " сут";

        static string Deg(double radians) => N(radians * 180.0 / Math.PI, 1) + "°";

        static string PressureText(double pascals) =>
            pascals >= 1e6 || (pascals > 0.0 && pascals < 1.0) ? UnitFormatter.Scientific(pascals, 3) + " Па" : N(pascals, 0) + " Па";

        static string Dist(double metres)
        {
            if (double.IsNaN(metres) || double.IsInfinity(metres)) return "—";
            double a = Math.Abs(metres);
            if (a < 1e9) return N(metres / 1000.0, 0) + " км";
            if (a < 0.05 * AstronomicalUnit) return N(metres / 1e9, 2) + " млн км";
            return N(metres / AstronomicalUnit, 3) + " а.е. (" + N(metres / 1e9, 1) + " млн км)";
        }
    }
}
