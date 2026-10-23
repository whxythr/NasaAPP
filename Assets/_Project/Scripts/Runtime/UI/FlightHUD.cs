using System;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Units;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Instrument panel. Shows real SI values from <see cref="FlightTelemetry"/> (rounded for display only).
    /// Derived orbital elements are hidden by default (training mode): the player is expected to compute them
    /// from r, v, v_r and ε using the Flight Manual; press O to reveal them for checking.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class FlightHUD : MonoBehaviour
    {
        [SerializeField] DisplayUnitSystem units = DisplayUnitSystem.SI;
        [SerializeField] bool showOrbitAnalysis;

        FlightStateController state;
        FlightBody body;
        PlanetRuntime planet;
        SimulationController simulation;
        SpaceCameraController cameraController;
        StarfieldController stars;
        FlightManualUI manual;
        InitialConditionsPanel initialConditions;
        bool visible = true;

        public void Initialize(FlightStateController flightState, FlightBody flightBody, PlanetRuntime planetRuntime,
            SimulationController sim, SpaceCameraController cam, StarfieldController starfield,
            FlightManualUI manualUi, InitialConditionsPanel icPanel)
        {
            state = flightState;
            body = flightBody;
            planet = planetRuntime;
            simulation = sim;
            cameraController = cam;
            stars = starfield;
            manual = manualUi;
            initialConditions = icPanel;
        }

        void Update()
        {
            if (state == null || FlightInput.IsTyping) return;
            if (FlightInput.GetKeyDown(InputKey.H)) visible = !visible;
            if (FlightInput.GetKeyDown(InputKey.U)) units = units == DisplayUnitSystem.SI ? DisplayUnitSystem.Kilometric : DisplayUnitSystem.SI;
            if (FlightInput.GetKeyDown(InputKey.O)) showOrbitAnalysis = !showOrbitAnalysis;
            if (FlightInput.GetKeyDown(InputKey.M) || FlightInput.GetKeyDown(InputKey.F1)) manual.Toggle();
            if (FlightInput.GetKeyDown(InputKey.I)) initialConditions.Toggle();
            if (FlightInput.GetKeyDown(InputKey.R))
            {
                body.ResetToLaunchSite();
                state.Notify("Аппарат возвращён на стартовую площадку.");
            }
        }

        void OnGUI()
        {
            if (state == null || planet.Model == null) return;
            HudStyle.Begin();
            if (!visible)
            {
                GUI.Label(new Rect(10, 6, 400, 20), "H — показать приборы", HudStyle.Small);
                return;
            }
            Vector2 screen = HudStyle.VirtualScreen;
            FlightTelemetry t = state.Telemetry;

            GUILayout.BeginArea(new Rect(12, 12, 360, screen.y - 80));
            DrawTelemetry(t);
            if (showOrbitAnalysis) DrawOrbit(t);
            GUILayout.EndArea();

            DrawStatus(t, screen);
            DrawProximity(t, screen);
            DrawNotifications(screen);
            GUI.Label(new Rect(0, screen.y - 22, screen.x, 20),
                "ПКМ — обзор · колесо/± — зум · C — система отсчёта камеры · F — фокус планета/аппарат · , . — ускорение времени · P — пауза · " +
                "I — начальные условия · R — на площадку · M/F1 — Flight Manual · O — анализ орбиты · U — единицы · H — скрыть",
                HudStyle.Hint);
        }

        void DrawTelemetry(FlightTelemetry t)
        {
            GUILayout.BeginVertical(HudStyle.Panel);
            GUILayout.Label("ТЕЛЕМЕТРИЯ · " + planet.Definition.displayName.ToUpperInvariant(), HudStyle.Title);
            HudStyle.Row("Время симуляции", UnitFormatter.Duration(t.Time));
            HudStyle.Row("Широта / долгота", UnitFormatter.Angle(t.Latitude) + "  " + UnitFormatter.Angle(t.Longitude));
            GUILayout.Space(4);
            HudStyle.Row("Высота над поверхн.", UnitFormatter.Distance(t.AltitudeAboveSurface, units));
            HudStyle.Row("Высота над R (h)", UnitFormatter.Distance(t.AltitudeAboveReference, units));
            HudStyle.Row("От центра (r)", UnitFormatter.Distance(t.DistanceFromCenter, units));
            GUILayout.Space(4);
            HudStyle.Row("|v| инерциальная", UnitFormatter.Speed(t.InertialSpeed, units));
            HudStyle.Row("|v| отн. поверхн.", UnitFormatter.Speed(t.SurfaceSpeed, units));
            HudStyle.Row("|v| отн. воздуха", UnitFormatter.Speed(t.AirSpeed, units));
            HudStyle.Row("v радиальная (v_r)", UnitFormatter.Speed(t.RadialVelocity, units));
            HudStyle.Row("v горизонт. (инерц.)", UnitFormatter.Speed(t.HorizontalInertialSpeed, units));
            HudStyle.Row("v⃗ отн. пов. E/N/Up",
                UnitFormatter.Number(Conv(t.VelocityEast), Dec) + " / " + UnitFormatter.Number(Conv(t.VelocityNorth), Dec) + " / " +
                UnitFormatter.Number(Conv(t.VelocityUp), Dec) + (units == DisplayUnitSystem.SI ? " m/s" : " km/s"));
            Vector3d v = t.InertialVelocity;
            HudStyle.Row("v⃗ инерц. x/y/z",
                UnitFormatter.Number(Conv(v.x), Dec) + " / " + UnitFormatter.Number(Conv(v.y), Dec) + " / " + UnitFormatter.Number(Conv(v.z), Dec));
            GUILayout.Space(4);
            HudStyle.Row("g(r) = μ/r²", UnitFormatter.Acceleration(t.GravityMagnitude));
            HudStyle.Row("|a| полное", UnitFormatter.Acceleration(t.TotalAcceleration));
            HudStyle.Row("Перегрузка (акселерометр)", UnitFormatter.Number(t.ProperAcceleration / PhysicalConstants.StandardGravity, 3) + " g₀");
            GUILayout.Space(4);
            HudStyle.Row("Плотность ρ", UnitFormatter.Density(t.Density));
            HudStyle.Row("ρ/ρ₀", UnitFormatter.Scientific(t.DensityRatio, 3));
            HudStyle.Row("Давление p", UnitFormatter.Pressure(t.Pressure));
            HudStyle.Row("Скоростной напор q", UnitFormatter.Pressure(t.DynamicPressure));
            HudStyle.Row("Сопротивление a_drag", UnitFormatter.Acceleration(t.DragAcceleration));
            GUILayout.Space(4);
            HudStyle.Row("ε = v²/2 − μ/r", UnitFormatter.SpecificEnergy(t.SpecificEnergy, units));
            GUILayout.EndVertical();
        }

        void DrawOrbit(FlightTelemetry t)
        {
            var o = t.Orbit;
            double R = planet.Model.Radius;
            GUILayout.Space(6);
            GUILayout.BeginVertical(HudStyle.Panel);
            GUILayout.Label("АНАЛИЗ ОРБИТЫ (двухтельная модель, без атмосферы)", HudStyle.Title);
            HudStyle.Row("Эксцентриситет e", UnitFormatter.Number(o.Eccentricity, 5));
            HudStyle.Row("Большая полуось a", o.IsBound ? UnitFormatter.Distance(o.SemiMajorAxis, units) : "— (ε ≥ 0)");
            HudStyle.Row("Перицентр (высота)", UnitFormatter.Distance(o.PeriapsisRadius - R, units));
            HudStyle.Row("Апоцентр (высота)", o.IsBound ? UnitFormatter.Distance(o.ApoapsisRadius - R, units) : "∞");
            HudStyle.Row("Период T", UnitFormatter.Duration(o.Period));
            HudStyle.Row("Наклонение i", UnitFormatter.Angle(o.Inclination));
            HudStyle.Row("v_круг(r) = √(μ/r)", UnitFormatter.Speed(t.CircularVelocityHere, units));
            HudStyle.Row("v_уход(r) = √(2μ/r)", UnitFormatter.Speed(t.EscapeVelocityHere, units));
            GUILayout.Label("Это проверочные значения. Для обучения рассчитайте их сами по r, v и формулам из Flight Manual.", HudStyle.Small);
            GUILayout.EndVertical();
        }

        void DrawStatus(FlightTelemetry t, Vector2 screen)
        {
            float w = 560f;
            var r = new Rect((screen.x - w) * 0.5f, 10, w, 92);
            GUI.Box(r, GUIContent.none, HudStyle.Panel);
            GUI.Label(new Rect(r.x, r.y + 6, w, 26), PhaseName(state.Phase), HudStyle.Phase);
            GUI.Label(new Rect(r.x + 10, r.y + 34, w - 20, 32), ReasonText(state.Reason), HudStyle.Hint);

            string warp = simulation.Paused ? "ПАУЗА" : "×" + UnitFormatter.Number(simulation.TimeWarp, 0);
            if (simulation.WarpLimited) warp += " (ограничено производительностью)";
            string cam = (cameraController.Focus == SpaceCameraController.FocusTarget.Planet ? "камера: планета" : "камера: аппарат") +
                         (cameraController.Frame == SpaceCameraController.ReferenceFrame.Inertial ? ", инерциальная" : ", местный горизонт");
            GUI.Label(new Rect(r.x + 10, r.y + 64, w - 20, 22),
                "Время: " + warp + "  ·  шаг " + UnitFormatter.Number(simulation.FixedStep, 3) + " s  ·  " + cam +
                "  ·  звёзды " + UnitFormatter.Number(stars.Visibility * 100.0, 0) + "%", HudStyle.Hint);
        }

        void DrawProximity(FlightTelemetry t, Vector2 screen)
        {
            var r = new Rect(screen.x - 120, 120, 100, 300);
            GUI.Box(r, GUIContent.none, HudStyle.Panel);
            GUI.Label(new Rect(r.x, r.y + 4, r.width, 18), "ДО ПОВЕРХН.", HudStyle.Hint);
            double alt = Math.Max(0.0, t.AltitudeAboveSurface);
            float frac = (float)(Math.Log10(alt + 1.0) / Math.Log10(100001.0)); // 0 m … 100 km, logarithmic
            bool danger = !double.IsNaN(t.TimeToImpactEstimate) && t.TimeToImpactEstimate < 30.0;
            bool caution = !double.IsNaN(t.TimeToImpactEstimate) && t.TimeToImpactEstimate < 120.0;
            Color c = danger ? HudStyle.Danger : (caution ? HudStyle.Warning : HudStyle.Accent);
            HudStyle.Bar(new Rect(r.x + 38, r.y + 26, 24, 200), frac, c);
            GUI.Label(new Rect(r.x, r.y + 230, r.width, 18), UnitFormatter.Distance(alt, DisplayUnitSystem.Kilometric), HudStyle.Hint);
            GUI.Label(new Rect(r.x, r.y + 250, r.width, 18), "до контакта≈", HudStyle.Hint);
            GUI.Label(new Rect(r.x, r.y + 268, r.width, 18), UnitFormatter.Duration(t.TimeToImpactEstimate), HudStyle.Hint);
        }

        void DrawNotifications(Vector2 screen)
        {
            float y = screen.y - 60;
            for (int i = state.Notifications.Count - 1; i >= 0; i--)
            {
                var n = state.Notifications[i];
                float age = Time.unscaledTime - n.RealTime;
                var c = GUI.color;
                GUI.color = new Color(1, 1, 1, Mathf.Clamp01(1.5f - age / 8f));
                GUI.Label(new Rect(screen.x * 0.5f - 400, y, 800, 22), n.Text, HudStyle.Hint);
                GUI.color = c;
                y -= 22;
            }
        }

        double Conv(double metersPerSecond) => units == DisplayUnitSystem.SI ? metersPerSecond : metersPerSecond / 1000.0;
        int Dec => units == DisplayUnitSystem.SI ? 1 : 4;

        public static string PhaseName(FlightPhase p)
        {
            switch (p)
            {
                case FlightPhase.Ground: return "НА ПОВЕРХНОСТИ";
                case FlightPhase.AtmosphericAscent: return "ПОДЪЁМ В АТМОСФЕРЕ";
                case FlightPhase.UpperAtmosphere: return "ВЕРХНИЕ СЛОИ АТМОСФЕРЫ";
                case FlightPhase.Spaceflight: return "КОСМИЧЕСКИЙ ПОЛЁТ (НЕ ОРБИТА)";
                case FlightPhase.OrbitalFlight: return "ОРБИТАЛЬНЫЙ ПОЛЁТ";
                case FlightPhase.EscapeTrajectory: return "ТРАЕКТОРИЯ УХОДА";
                case FlightPhase.AtmosphericDescent: return "СНИЖЕНИЕ В АТМОСФЕРЕ";
                case FlightPhase.Landing: return "ЗАХОД НА ПОСАДКУ";
                default: return p.ToString();
            }
        }

        public static string ReasonText(FlightPhaseReason r)
        {
            switch (r)
            {
                case FlightPhaseReason.RestingOnSurface: return "Аппарат покоится на грунте и вращается вместе с планетой.";
                case FlightPhaseReason.ClimbingInDenseAir: return "Вертикальная скорость > 0, плотный воздух.";
                case FlightPhaseReason.DescendingInDenseAir: return "Вертикальная скорость < 0, плотный воздух.";
                case FlightPhaseReason.ThinAirAboveThreshold: return "ρ/ρ₀ ниже порога верхней атмосферы; сопротивление ещё действует.";
                case FlightPhaseReason.CloseToSurfaceDescending: return "Снижение у поверхности: следите за вертикальной скоростью.";
                case FlightPhaseReason.AboveBoundarySuborbital: return "Суборбитальная траектория.";
                case FlightPhaseReason.AboveBoundaryPeriapsisBelowSurface: return "Выше условной границы, но перицентр под поверхностью — траектория пересекает планету.";
                case FlightPhaseReason.AboveBoundaryPeriapsisInAtmosphere: return "ε < 0, но перицентр в атмосфере — сопротивление разрушит орбиту.";
                case FlightPhaseReason.BoundOrbitPeriapsisClear: return "ε < 0 и перицентр выше плотной атмосферы.";
                case FlightPhaseReason.UnboundEnergy: return "ε ≥ 0 — траектория не замкнута, аппарат уходит от планеты.";
                default: return r.ToString();
            }
        }
    }
}
