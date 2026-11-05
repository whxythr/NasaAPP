using System;
using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Units;

namespace SpaceSim.Academy
{
    /// <summary>
    /// The Flight Academy calculators (Level A). Each one only arranges inputs and calls the central physics functions the
    /// game itself uses: <see cref="RocketEquations"/>, <see cref="PlanetaryGravity"/>, <see cref="OrbitalMechanics"/>,
    /// <see cref="AtmosphericDrag"/>, <see cref="ExponentialAtmosphere"/>, <see cref="TransferCalculator"/>,
    /// <see cref="MissionRequirements"/>. Defaults and presets come from the player's planet and rocket.
    /// </summary>
    public static class AcademyCalculators
    {
        public const string Thrust = "thrust";
        public const string Gravity = "gravity";
        public const string Acceleration = "accel";
        public const string ThrustToWeight = "twr";
        public const string Burn = "burn";
        public const string DeltaV = "dv";
        public const string Fuel = "fuel";
        public const string Orbit = "orbit";
        public const string Escape = "escape";
        public const string Drag = "drag";
        public const string Insertion = "insertion";
        public const string Hohmann = "hohmann";
        public const string Transfer = "transfer";

        const double Tonne = 1000.0, KiloNewton = 1000.0, Km = 1000.0, MillionKm = 1.0e9;
        const double Minute = 60.0, Hour = 3600.0, Day = 86400.0, Percent = 0.01, KiloPascal = 1000.0;
        const double Degree = Math.PI / 180.0;
        const double G0 = RocketEquations.G0;
        const double G = PhysicalConstants.GravitationalConstant;

        static string N(double v) => AcademyNumber.Format(v);
        static double R4(double v) => AcademyNumber.RoundSignificant(v, 4);

        public static AcademyCalculator Find(IEnumerable<AcademyCalculator> calculators, string id)
        {
            foreach (var c in calculators)
                if (c.Id == id) return c;
            return null;
        }

        public static List<AcademyCalculator> Build(AcademyContext c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            return new List<AcademyCalculator>
            {
                BuildThrust(c), BuildGravity(c), BuildAcceleration(c), BuildThrustToWeight(c), BuildBurn(c), BuildDeltaV(c),
                BuildFuel(c), BuildOrbit(c), BuildEscape(c), BuildDrag(c), BuildInsertion(c), BuildHohmann(c), BuildTransfer(c)
            };
        }

        // ---- The player's rocket (or a training rocket when there is none) ---------------------------------------

        internal static StageReport Stage(AcademyContext c, int index)
        {
            if (c.Design != null && c.Design.Stages.Count > 0) return c.Design.Stages[Math.Min(index, c.Design.Stages.Count - 1)];
            return index == 0
                ? TrainingStage("Учебная ступень 1", 15000.0, 135000.0, 42000.0, 3.0e6, 300.0, 270.0)
                : TrainingStage("Учебная ступень 2", 4000.0, 36000.0, 2000.0, 5.0e5, 340.0, 300.0);
        }

        internal static int LastStage(AcademyContext c) => c.Design != null && c.Design.Stages.Count > 0 ? c.Design.Stages.Count - 1 : 1;

        internal static bool HasRocket(AcademyContext c) => c.Design != null && c.Design.Stages.Count > 0;

        static string RocketName(AcademyContext c, StageReport s) => (HasRocket(c) ? "Моя ракета: " : "") + s.Name;

        static StageReport TrainingStage(string name, double dry, double propellant, double above, double vacuumThrust, double vacuumIsp, double surfaceIsp)
        {
            var s = new StageReport
            {
                Name = name,
                DryMass = dry,
                PropellantMass = propellant,
                MassAtIgnition = dry + propellant + above,
                MassAtBurnout = dry + above,
                VacuumThrust = vacuumThrust,
                VacuumIsp = vacuumIsp,
                SurfaceIsp = surfaceIsp
            };
            s.MassFlow = RocketEquations.MassFlow(vacuumThrust, vacuumIsp);
            s.SurfaceThrust = RocketEquations.ThrustFromMassFlow(s.MassFlow, surfaceIsp);
            s.BurnTime = RocketEquations.BurnTime(propellant, s.MassFlow);
            s.DeltaVVacuum = RocketEquations.DeltaV(vacuumIsp, s.MassAtIgnition, s.MassAtBurnout);
            return s;
        }

        // ---- 1. Thrust -------------------------------------------------------------------------------------------

        static AcademyCalculator BuildThrust(AcademyContext c)
        {
            StageReport s1 = Stage(c, 0);
            var calc = new AcademyCalculator(Thrust, "Тяга двигателя", AcademyTopics.MassThrust,
                "F = ṁ·Isp·g₀: тяга — это расход топлива, умноженный на скорость истечения.",
                new[]
                {
                    new CalculatorInput("mdot", "Расход топлива", "ṁ", "кг/с", 1.0, R4(s1.MassFlow)),
                    new CalculatorInput("isp", "Удельный импульс", "Isp", "с", 1.0, R4(s1.VacuumIsp))
                },
                v =>
                {
                    double mdot = v[0], isp = v[1];
                    double ve = RocketEquations.ExhaustVelocity(isp);
                    double f = RocketEquations.ThrustFromMassFlow(mdot, isp);
                    return new CalculatorResult()
                        .Out("Скорость истечения", "vₑ", ve, "м/с")
                        .Out("Тяга", "F", f, "кН", KiloNewton)
                        .Step("vₑ = Isp·g₀ = " + N(isp) + " с · " + N(G0) + " м/с² = " + N(ve) + " м/с")
                        .Step("F = ṁ·vₑ = " + N(mdot) + " кг/с · " + N(ve) + " м/с = " + N(f) + " Н = " + N(f / KiloNewton) + " кН")
                        .Note("g₀ = 9.80665 м/с² — постоянная пересчёта Isp в скорость, а не местная гравитация.")
                        .Note("У поверхности Isp ниже (давление мешает истечению), поэтому при том же расходе тяга меньше. В игре это учитывается.");
                });
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1) + " — вакуум", R4(s1.MassFlow), R4(s1.VacuumIsp)));
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1) + " — у поверхности", R4(s1.MassFlow), R4(s1.SurfaceIsp)));
            return calc;
        }

        // ---- 2. Gravity ------------------------------------------------------------------------------------------

        static AcademyCalculator BuildGravity(AcademyContext c)
        {
            var calc = new AcademyCalculator(Gravity, "Сила тяжести", AcademyTopics.Gravity,
                "F = G·M·m/r²: притяжение планеты на расстоянии r от её центра.",
                new[]
                {
                    new CalculatorInput("M", "Масса планеты", "M", "кг", 1.0, R4(c.Mu / G)),
                    new CalculatorInput("R", "Радиус планеты", "R", "км", Km, R4(c.Radius / Km)),
                    new CalculatorInput("h", "Высота над R", "h", "км", Km, 0.0, InputDomain.NonNegative),
                    new CalculatorInput("m", "Масса аппарата", "m", "кг", 1.0, 1000.0)
                },
                v =>
                {
                    double mass = v[0], radius = v[1], h = v[2], m = v[3];
                    double mu = PlanetaryGravity.GravitationalParameterFromMass(mass);
                    double r = radius + h;
                    double g = PlanetaryGravity.AccelerationMagnitude(mu, r);
                    double f = g * m;
                    return new CalculatorResult()
                        .Out("Гравитационный параметр", "μ", mu, "м³/с²")
                        .Out("Расстояние от центра", "r", r, "км", Km)
                        .Out("Ускорение свободного падения", "g", g, "м/с²")
                        .Out("Сила тяжести", "F", f, "Н")
                        .Step("μ = G·M = " + N(G) + " · " + N(mass) + " = " + N(mu) + " м³/с²")
                        .Step("r = R + h = " + N(radius) + " + " + N(h) + " = " + N(r) + " м")
                        .Step("g = μ/r² = " + N(mu) + " / " + N(r) + "² = " + N(g) + " м/с²")
                        .Step("F = m·g = " + N(m) + " · " + N(g) + " = " + N(f) + " Н")
                        .Note("Сферическая планета, без вращения: на экваторе вращающейся планеты вес чуть меньше.");
                });
            foreach (var b in c.Bodies)
                calc.Presets.Add(new CalculatorPreset(b.Name + " — поверхность", R4(b.Mu / G), R4(b.Radius / Km), 0.0, 1000.0));
            calc.Presets.Add(new CalculatorPreset(c.PlanetName + " — высота 400 км", R4(c.Mu / G), R4(c.Radius / Km), 400.0, 1000.0));
            return calc;
        }

        // ---- 3. Acceleration -------------------------------------------------------------------------------------

        static AcademyCalculator BuildAcceleration(AcademyContext c)
        {
            StageReport s1 = Stage(c, 0);
            double g = c.SurfaceGravity;
            var calc = new AcademyCalculator(Acceleration, "Ускорение ракеты", AcademyTopics.Acceleration,
                "a = F/m: ускорение от тяги, и сколько остаётся на подъём после вычета гравитации.",
                new[]
                {
                    new CalculatorInput("F", "Тяга", "F", "кН", KiloNewton, R4(s1.SurfaceThrust / KiloNewton)),
                    new CalculatorInput("m", "Масса", "m", "т", Tonne, R4(s1.MassAtIgnition / Tonne)),
                    new CalculatorInput("g", "Местная гравитация", "g", "м/с²", 1.0, R4(g), InputDomain.NonNegative)
                },
                v =>
                {
                    double f = v[0], m = v[1], gl = v[2];
                    double a = RocketEquations.Acceleration(f, m);
                    double net = RocketEquations.Acceleration(f - m * gl, m);
                    var r = new CalculatorResult()
                        .Out("Ускорение от тяги", "a", a, "м/с²")
                        .Out("Перегрузка от тяги", "a/g₀", a / G0, "g₀")
                        .Out("Ускорение вверх (вертикальный подъём)", "a_верт", net, "м/с²")
                        .Step("a = F/m = " + N(f) + " Н / " + N(m) + " кг = " + N(a) + " м/с²")
                        .Step("a_верт = (F − m·g)/m = (" + N(f) + " − " + N(m) + "·" + N(gl) + ") / " + N(m) + " = " + N(net) + " м/с²")
                        .Note("Без сопротивления воздуха. Масса падает по мере расхода топлива, поэтому ускорение растёт к концу работы ступени.");
                    if (net <= 0.0) r.Warn("Тяга не больше веса: вертикально вверх ракета не ускоряется (тяга/вес ≤ 1).");
                    return r;
                });
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1) + " — отрыв", R4(s1.SurfaceThrust / KiloNewton), R4(s1.MassAtIgnition / Tonne), R4(g)));
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1) + " — конец работы (вакуум)", R4(s1.VacuumThrust / KiloNewton), R4(s1.MassAtBurnout / Tonne), 0.0));
            return calc;
        }

        // ---- 4. Thrust-to-weight ---------------------------------------------------------------------------------

        static AcademyCalculator BuildThrustToWeight(AcademyContext c)
        {
            StageReport s1 = Stage(c, 0);
            double launchMass = HasRocket(c) && c.Design.LaunchMass > 0.0 ? c.Design.LaunchMass : s1.MassAtIgnition;
            var calc = new AcademyCalculator(ThrustToWeight, "Тяга/вес (TWR)", AcademyTopics.MassThrust,
                "TWR = F/(m·g): может ли ракета оторваться от поверхности.",
                new[]
                {
                    new CalculatorInput("F", "Тяга", "F", "кН", KiloNewton, R4(s1.SurfaceThrust / KiloNewton)),
                    new CalculatorInput("m", "Масса", "m", "т", Tonne, R4(launchMass / Tonne)),
                    new CalculatorInput("g", "Местная гравитация", "g", "м/с²", 1.0, R4(c.SurfaceGravity))
                },
                v =>
                {
                    double f = v[0], m = v[1], gl = v[2];
                    double twr = RocketEquations.ThrustToWeight(f, m, gl);
                    double weight = m * gl;
                    double maxMass = f / gl;
                    var r = new CalculatorResult()
                        .Out("Вес", "W", weight, "кН", KiloNewton)
                        .Out("Тяга/вес", "TWR", twr, "")
                        .Out("Наибольшая масса для отрыва", "m_max", maxMass, "т", Tonne)
                        .Step("W = m·g = " + N(m) + " кг · " + N(gl) + " м/с² = " + N(weight) + " Н")
                        .Step("TWR = F/W = " + N(f) + " / " + N(weight) + " = " + N(twr))
                        .Step("m_max = F/g = " + N(f) + " / " + N(gl) + " = " + N(maxMass) + " кг (при ней TWR = 1)");
                    if (twr < 1.0) r.Warn("TWR < 1: двигатели не могут преодолеть местную гравитацию — ракета не оторвётся. Снизьте массу на " +
                                          N((m - maxMass) / Tonne) + " т или поставьте двигатель мощнее.");
                    else if (twr < 1.2) r.Warn("TWR чуть больше 1: отрыв очень медленный, почти вся тяга уходит на удержание веса — огромные гравитационные потери.");
                    r.Note("Используйте тягу У ПОВЕРХНОСТИ (Isp там ниже) и полную стартовую массу со всеми ступенями.");
                    return r;
                });
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1) + " — старт с " + c.PlanetName, R4(s1.SurfaceThrust / KiloNewton), R4(launchMass / Tonne), R4(c.SurfaceGravity)));
            foreach (var b in c.Bodies)
            {
                if (b.Id == c.PlanetId) continue;
                double gb = PlanetaryGravity.AccelerationMagnitude(b.Mu, b.Radius);
                calc.Presets.Add(new CalculatorPreset("Та же ракета на " + b.Name, R4(s1.SurfaceThrust / KiloNewton), R4(launchMass / Tonne), R4(gb)));
            }
            return calc;
        }

        // ---- 5. Burn duration / fuel consumption -----------------------------------------------------------------

        static AcademyCalculator BuildBurn(AcademyContext c)
        {
            StageReport s1 = Stage(c, 0), s2 = Stage(c, LastStage(c));
            var calc = new AcademyCalculator(Burn, "Расход топлива и время работы", AcademyTopics.FuelConsumption,
                "ṁ = F/(Isp·g₀), t = m_топл/ṁ: сколько секунд проработает ступень и какой Δv она даст.",
                new[]
                {
                    new CalculatorInput("F", "Тяга", "F", "кН", KiloNewton, R4(s1.VacuumThrust / KiloNewton)),
                    new CalculatorInput("isp", "Удельный импульс", "Isp", "с", 1.0, R4(s1.VacuumIsp)),
                    new CalculatorInput("mp", "Топливо", "m_топл", "т", Tonne, R4(s1.PropellantMass / Tonne)),
                    new CalculatorInput("m0", "Масса при зажигании", "m₀", "т", Tonne, R4(s1.MassAtIgnition / Tonne))
                },
                v =>
                {
                    double f = v[0], isp = v[1], mp = v[2], m0 = v[3];
                    if (mp >= m0) return CalculatorResult.Fail("Топлива не может быть столько же или больше, чем вся масса при зажигании.");
                    double mdot = RocketEquations.MassFlow(f, isp);
                    double t = RocketEquations.BurnTime(mp, mdot);
                    double mf = m0 - mp;
                    double dv = RocketEquations.DeltaV(isp, m0, mf);
                    return new CalculatorResult()
                        .Out("Расход", "ṁ", mdot, "кг/с")
                        .Out("Время работы", "t", t, "с")
                        .Out("Время работы", "t_мин", t, "мин", Minute)
                        .Out("Масса после выгорания", "m_f", mf, "т", Tonne)
                        .Out("Идеальный Δv ступени", "Δv", dv, "м/с")
                        .Step("ṁ = F/(Isp·g₀) = " + N(f) + " / (" + N(isp) + " · " + N(G0) + ") = " + N(mdot) + " кг/с")
                        .Step("t = m_топл/ṁ = " + N(mp) + " / " + N(mdot) + " = " + N(t) + " с")
                        .Step("m_f = m₀ − m_топл = " + N(m0) + " − " + N(mp) + " = " + N(mf) + " кг")
                        .Step("Δv = Isp·g₀·ln(m₀/m_f) = " + N(isp * G0) + " · ln(" + N(m0 / mf) + ") = " + N(dv) + " м/с")
                        .Note("Полная тяга всё время. На половинной тяге расход и время меняются вдвое, а Δv — нет: он зависит только от масс и Isp.");
                });
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1), R4(s1.VacuumThrust / KiloNewton), R4(s1.VacuumIsp), R4(s1.PropellantMass / Tonne), R4(s1.MassAtIgnition / Tonne)));
            if (LastStage(c) > 0)
                calc.Presets.Add(new CalculatorPreset(RocketName(c, s2), R4(s2.VacuumThrust / KiloNewton), R4(s2.VacuumIsp), R4(s2.PropellantMass / Tonne), R4(s2.MassAtIgnition / Tonne)));
            return calc;
        }

        // ---- 6. Tsiolkovsky --------------------------------------------------------------------------------------

        static AcademyCalculator BuildDeltaV(AcademyContext c)
        {
            StageReport s1 = Stage(c, 0), s2 = Stage(c, LastStage(c));
            var calc = new AcademyCalculator(DeltaV, "Δv (формула Циолковского)", AcademyTopics.Tsiolkovsky,
                "Δv = Isp·g₀·ln(m₀/m_f): идеальный запас изменения скорости.",
                new[]
                {
                    new CalculatorInput("isp", "Удельный импульс", "Isp", "с", 1.0, R4(s1.VacuumIsp)),
                    new CalculatorInput("m0", "Начальная масса", "m₀", "т", Tonne, R4(s1.MassAtIgnition / Tonne)),
                    new CalculatorInput("mf", "Конечная масса", "m_f", "т", Tonne, R4(s1.MassAtBurnout / Tonne))
                },
                v =>
                {
                    double isp = v[0], m0 = v[1], mf = v[2];
                    if (mf > m0) return CalculatorResult.Fail("Конечная масса больше начальной: ракета не может потяжелеть, сжигая топливо.");
                    double ve = RocketEquations.ExhaustVelocity(isp);
                    double dv = RocketEquations.DeltaV(isp, m0, mf);
                    var r = new CalculatorResult()
                        .Out("Скорость истечения", "vₑ", ve, "м/с")
                        .Out("Отношение масс", "m₀/m_f", m0 / mf, "")
                        .Out("Доля топлива", "(m₀−m_f)/m₀", (m0 - mf) / m0, "%", Percent)
                        .Out("Идеальный Δv", "Δv", dv, "м/с")
                        .Step("vₑ = Isp·g₀ = " + N(isp) + " · " + N(G0) + " = " + N(ve) + " м/с")
                        .Step("m₀/m_f = " + N(m0) + " / " + N(mf) + " = " + N(m0 / mf))
                        .Step("Δv = vₑ·ln(m₀/m_f) = " + N(ve) + " · " + N(Math.Log(m0 / mf)) + " = " + N(dv) + " м/с")
                        .Note("Уровень A: без гравитационных и аэродинамических потерь. Реальный разгон при подъёме меньше — см. «Потери» в панели ракеты.");
                    if (HasRocket(c) && c.Design.Stages.Count > 1)
                        r.Note("Δv многоступенчатой ракеты — сумма Δv ступеней: у вашей ракеты " + N(c.Design.TotalDeltaVVacuum) + " м/с (вакуум, ангар).");
                    return r;
                });
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1) + " — вакуум", R4(s1.VacuumIsp), R4(s1.MassAtIgnition / Tonne), R4(s1.MassAtBurnout / Tonne)));
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1) + " — Isp у поверхности", R4(s1.SurfaceIsp), R4(s1.MassAtIgnition / Tonne), R4(s1.MassAtBurnout / Tonne)));
            if (LastStage(c) > 0)
                calc.Presets.Add(new CalculatorPreset(RocketName(c, s2), R4(s2.VacuumIsp), R4(s2.MassAtIgnition / Tonne), R4(s2.MassAtBurnout / Tonne)));
            return calc;
        }

        // ---- 7. Fuel for a Δv ------------------------------------------------------------------------------------

        static AcademyCalculator BuildFuel(AcademyContext c)
        {
            StageReport s1 = Stage(c, 0), s2 = Stage(c, LastStage(c));
            MissionTarget[] targets = MissionRequirements.StandardTargets(c.Mu, c.Radius, c.SiteRotationSpeed, c.SpaceBoundary, c.HasAtmosphere);
            var calc = new AcademyCalculator(Fuel, "Топливо для заданного Δv", AcademyTopics.DeltaVBudget,
                "m_топл = m_f·(e^(Δv/(Isp·g₀)) − 1): сколько топлива нужно, чтобы получить Δv.",
                new[]
                {
                    new CalculatorInput("dv", "Нужный Δv", "Δv", "м/с", 1.0, R4(targets.Length > 1 ? targets[1].IdealDeltaV : 3000.0)),
                    new CalculatorInput("isp", "Удельный импульс", "Isp", "с", 1.0, R4(s2.VacuumIsp)),
                    new CalculatorInput("mf", "Масса без топлива (сухая + груз)", "m_f", "т", Tonne, R4(s2.MassAtBurnout / Tonne))
                },
                v =>
                {
                    double dv = v[0], isp = v[1], mf = v[2];
                    double ratio = RocketEquations.MassRatio(dv, isp);
                    double mp = RocketEquations.PropellantForDeltaV(dv, isp, mf);
                    double m0 = mf + mp;
                    var r = new CalculatorResult()
                        .Out("Отношение масс", "m₀/m_f", ratio, "")
                        .Out("Нужно топлива", "m_топл", mp, "т", Tonne)
                        .Out("Стартовая масса", "m₀", m0, "т", Tonne)
                        .Out("Доля топлива", "m_топл/m₀", mp / m0, "%", Percent)
                        .Step("m₀/m_f = e^(Δv/(Isp·g₀)) = e^(" + N(dv) + " / " + N(isp * G0) + ") = " + N(ratio))
                        .Step("m_топл = m_f·(m₀/m_f − 1) = " + N(mf) + " · " + N(ratio - 1.0) + " = " + N(mp) + " кг")
                        .Step("m₀ = m_f + m_топл = " + N(m0) + " кг")
                        .Note("Уровень A: это минимум. Для подъёма через атмосферу добавьте к Δv потери (обычно 1–2 км/с) — или проверьте полётом.");
                    if (ratio > 10.0)
                        r.Warn("Отношение масс " + N(ratio) + ": топливо — больше 90 % ракеты. Баки и двигатели столько не выдержат — поэтому ракеты делают многоступенчатыми.");
                    return r;
                });
            foreach (var t in targets)
                calc.Presets.Add(new CalculatorPreset(t.Name + " (идеально, " + N(t.IdealDeltaV) + " м/с)", R4(t.IdealDeltaV), R4(s2.VacuumIsp), R4(s2.MassAtBurnout / Tonne)));
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1) + " — тот же Δv", R4(targets.Length > 1 ? targets[1].IdealDeltaV : 3000.0), R4(s1.VacuumIsp), R4(s1.MassAtBurnout / Tonne)));
            return calc;
        }

        // ---- 8. Orbital velocity ---------------------------------------------------------------------------------

        static AcademyCalculator BuildOrbit(AcademyContext c)
        {
            var calc = new AcademyCalculator(Orbit, "Круговая орбита", AcademyTopics.OrbitalVelocity,
                "v = √(μ/r), T = 2π√(r³/μ): скорость и период круговой орбиты.",
                new[]
                {
                    new CalculatorInput("mu", "Гравитационный параметр", "μ", "м³/с²", 1.0, R4(c.Mu)),
                    new CalculatorInput("R", "Радиус планеты", "R", "км", Km, R4(c.Radius / Km)),
                    new CalculatorInput("h", "Высота орбиты над R", "h", "км", Km, R4(c.LowOrbitAltitude / Km), InputDomain.NonNegative)
                },
                v =>
                {
                    double mu = v[0], radius = v[1], h = v[2];
                    double r = radius + h;
                    double vc = OrbitalMechanics.CircularVelocity(mu, r);
                    double period = OrbitalMechanics.CircularPeriod(mu, r);
                    double g = PlanetaryGravity.AccelerationMagnitude(mu, r);
                    return new CalculatorResult()
                        .Out("Радиус орбиты", "r", r, "км", Km)
                        .Out("Круговая скорость", "v_круг", vc, "м/с")
                        .Out("Период", "T", period, "мин", Minute)
                        .Out("Гравитация на орбите", "g(r)", g, "м/с²")
                        .Step("r = R + h = " + N(radius) + " + " + N(h) + " = " + N(r) + " м")
                        .Step("v = √(μ/r) = √(" + N(mu) + " / " + N(r) + ") = " + N(vc) + " м/с")
                        .Step("T = 2π·√(r³/μ) = " + N(period) + " с = " + N(period / Minute) + " мин")
                        .Note("Скорость — инерциальная. Стартуя на восток, вы уже имеете скорость вращения площадки: на " + c.PlanetName + " это " + N(c.SiteRotationSpeed) + " м/с.")
                        .Note("Нужна именно горизонтальная скорость: вертикальная скорость орбиты не даёт.");
                });
            calc.Presets.Add(new CalculatorPreset(c.PlanetName + " — " + N(c.LowOrbitAltitude / Km) + " км", R4(c.Mu), R4(c.Radius / Km), R4(c.LowOrbitAltitude / Km)));
            foreach (var b in c.Bodies)
            {
                if (b.Id == c.PlanetId) continue;
                calc.Presets.Add(new CalculatorPreset(b.Name + " — 200 км", R4(b.Mu), R4(b.Radius / Km), 200.0));
            }
            return calc;
        }

        // ---- 9. Escape velocity ----------------------------------------------------------------------------------

        static AcademyCalculator BuildEscape(AcademyContext c)
        {
            var calc = new AcademyCalculator(Escape, "Вторая космическая", AcademyTopics.EscapeVelocity,
                "v = √(2μ/r): скорость, при которой аппарат уходит от планеты навсегда.",
                new[]
                {
                    new CalculatorInput("mu", "Гравитационный параметр", "μ", "м³/с²", 1.0, R4(c.Mu)),
                    new CalculatorInput("R", "Радиус планеты", "R", "км", Km, R4(c.Radius / Km)),
                    new CalculatorInput("h", "Высота над R", "h", "км", Km, 0.0, InputDomain.NonNegative),
                    new CalculatorInput("vrot", "Скорость вращения площадки (старт на восток)", "v_вр", "м/с", 1.0, R4(c.SiteRotationSpeed), InputDomain.NonNegative)
                },
                v =>
                {
                    double mu = v[0], radius = v[1], h = v[2], vrot = v[3];
                    double r = radius + h;
                    double ve = OrbitalMechanics.EscapeVelocity(mu, r);
                    double vc = OrbitalMechanics.CircularVelocity(mu, r);
                    double fromPad = MissionRequirements.Escape(mu, r, vrot);
                    return new CalculatorResult()
                        .Out("Вторая космическая", "v_уход", ve, "м/с")
                        .Out("Круговая скорость там же", "v_круг", vc, "м/с")
                        .Out("Отношение v_уход/v_круг", "√2", ve / vc, "")
                        .Out("Нужно добавить при старте на восток", "v_уход − v_вр", fromPad, "м/с")
                        .Step("v_уход = √(2μ/r) = √(2 · " + N(mu) + " / " + N(r) + ") = " + N(ve) + " м/с")
                        .Step("v_круг = √(μ/r) = " + N(vc) + " м/с;  v_уход/v_круг = " + N(ve / vc) + " = √2")
                        .Step("с учётом вращения: " + N(ve) + " − " + N(vrot) + " = " + N(fromPad) + " м/с")
                        .Note("Уровень A: удельная энергия ε = v²/2 − μ/r = 0. Без атмосферы и гравитационных потерь; притяжение звезды не учтено.");
                });
            foreach (var b in c.Bodies)
                calc.Presets.Add(new CalculatorPreset(b.Name, R4(b.Mu), R4(b.Radius / Km), 0.0, b.Id == c.PlanetId ? R4(c.SiteRotationSpeed) : 0.0));
            return calc;
        }

        // ---- 10. Drag ----------------------------------------------------------------------------------------

        static AcademyCalculator BuildDrag(AcademyContext c)
        {
            bool exponential = c.HasAtmosphere && !double.IsNaN(c.ScaleHeight);
            double rho0 = exponential ? c.Atmosphere.ReferenceDensity : 1.225;
            double scaleHeight = exponential ? c.ScaleHeight : 8500.0;
            double cd = c.Rocket != null ? c.Rocket.dragCoefficient : 0.3;
            double area = c.Rocket != null ? c.Rocket.ReferenceArea : Math.PI * 1.85 * 1.85;
            StageReport s1 = Stage(c, 0);
            double mass = HasRocket(c) && c.Design.LaunchMass > 0.0 ? c.Design.LaunchMass : s1.MassAtIgnition;
            var calc = new AcademyCalculator(Drag, "Сопротивление атмосферы", AcademyTopics.Drag,
                "ρ(h) = ρ₀·e^(−h/H), F = ½·ρ·v²·C_d·A: сила сопротивления на высоте h при скорости v.",
                new[]
                {
                    new CalculatorInput("rho0", "Плотность у поверхности", "ρ₀", "кг/м³", 1.0, R4(rho0), InputDomain.NonNegative),
                    new CalculatorInput("H", "Шкала высот", "H", "км", Km, R4(scaleHeight / Km)),
                    new CalculatorInput("h", "Высота", "h", "км", Km, 10.0, InputDomain.NonNegative),
                    new CalculatorInput("v", "Скорость относительно воздуха", "v", "м/с", 1.0, 500.0, InputDomain.NonNegative),
                    new CalculatorInput("cd", "Коэффициент сопротивления", "C_d", "", 1.0, R4(cd)),
                    new CalculatorInput("A", "Площадь сечения", "A", "м²", 1.0, R4(area)),
                    new CalculatorInput("m", "Масса", "m", "т", Tonne, R4(mass / Tonne)),
                    new CalculatorInput("g", "Местная гравитация", "g", "м/с²", 1.0, R4(c.SurfaceGravity), InputDomain.NonNegative)
                },
                v =>
                {
                    double r0 = v[0], hScale = v[1], h = v[2], speed = v[3], dragC = v[4], a = v[5], m = v[6], g = v[7];
                    var atmosphere = new ExponentialAtmosphere(r0, hScale, 0.0, 0.0);
                    double rho = atmosphere.Density(h);
                    double q = AtmosphericDrag.DynamicPressure(rho, speed);
                    double f = AtmosphericDrag.DragForceMagnitude(rho, speed, dragC, a);
                    double ad = RocketEquations.Acceleration(f, m);
                    var r = new CalculatorResult()
                        .Out("Плотность на высоте", "ρ(h)", rho, "кг/м³")
                        .Out("Скоростной напор", "q", q, "кПа", KiloPascal)
                        .Out("Сила сопротивления", "F_d", f, "кН", KiloNewton)
                        .Out("Торможение", "a_d", ad, "м/с²")
                        .Step("ρ = ρ₀·e^(−h/H) = " + N(r0) + " · e^(−" + N(h) + "/" + N(hScale) + ") = " + N(rho) + " кг/м³")
                        .Step("q = ½·ρ·v² = 0.5 · " + N(rho) + " · " + N(speed) + "² = " + N(q) + " Па")
                        .Step("F_d = q·C_d·A = " + N(q) + " · " + N(dragC) + " · " + N(a) + " = " + N(f) + " Н")
                        .Step("a_d = F_d/m = " + N(f) + " / " + N(m) + " = " + N(ad) + " м/с²");
                    if (rho > 0.0 && g > 0.0)
                    {
                        double vt = AtmosphericDrag.TerminalVelocity(m, g, rho, dragC, a);
                        r.Out("Предельная скорость падения", "v_пред", vt, "м/с")
                            .Step("v_пред = √(2·m·g/(ρ·C_d·A)) = " + N(vt) + " м/с (сопротивление = вес)");
                    }
                    if (r0 <= 0.0) r.Note("Нет атмосферы — нет сопротивления.");
                    r.Note("Изотермическая атмосфера и постоянный C_d (в игре так же). На сверхзвуке реальный C_d выше.");
                    if (!exponential)
                        r.Note(c.HasAtmosphere ? "У " + c.PlanetName + " атмосфера задана таблицей — по умолчанию взяты земные ρ₀ и H." : "У " + c.PlanetName + " нет атмосферы — по умолчанию взяты земные ρ₀ и H.");
                    return r;
                });
            calc.Presets.Add(new CalculatorPreset("Подъём: 10 км, 500 м/с", R4(rho0), R4(scaleHeight / Km), 10.0, 500.0, R4(cd), R4(area), R4(mass / Tonne), R4(c.SurfaceGravity)));
            calc.Presets.Add(new CalculatorPreset("Плотные слои: 3 км, 300 м/с", R4(rho0), R4(scaleHeight / Km), 3.0, 300.0, R4(cd), R4(area), R4(mass / Tonne), R4(c.SurfaceGravity)));
            calc.Presets.Add(new CalculatorPreset("Граница космоса: " + N(c.SpaceBoundary / Km) + " км, 2 000 м/с", R4(rho0), R4(scaleHeight / Km), R4(c.SpaceBoundary / Km), 2000.0, R4(cd), R4(area), R4(mass / Tonne), R4(c.SurfaceGravity)));
            return calc;
        }

        // ---- 11. Orbital insertion -------------------------------------------------------------------------------

        static AcademyCalculator BuildInsertion(AcademyContext c)
        {
            double apo = c.LowOrbitAltitude / Km;
            var calc = new AcademyCalculator(Insertion, "Выход на орбиту (скругление)", AcademyTopics.Insertion,
                "Δv = √(μ/r_a) − √(μ·(2/r_a − 1/a)): разгон в апоцентре, превращающий траекторию в круговую орбиту.",
                new[]
                {
                    new CalculatorInput("mu", "Гравитационный параметр", "μ", "м³/с²", 1.0, R4(c.Mu)),
                    new CalculatorInput("R", "Радиус планеты", "R", "км", Km, R4(c.Radius / Km)),
                    new CalculatorInput("ha", "Высота апоцентра", "h_a", "км", Km, R4(apo), InputDomain.NonNegative),
                    new CalculatorInput("hp", "Высота перицентра (может быть < 0)", "h_p", "км", Km, 0.0, InputDomain.Any)
                },
                v =>
                {
                    double mu = v[0], radius = v[1], ha = v[2], hp = v[3];
                    double ra = radius + ha, rp = radius + hp;
                    if (!(rp > 0.0)) return CalculatorResult.Fail("r_p = R + h_p должно быть больше нуля: перицентр не может быть глубже центра планеты.");
                    if (rp > ra) return CalculatorResult.Fail("Перицентр выше апоцентра — поменяйте их местами.");
                    double a = 0.5 * (ra + rp);
                    double va = OrbitalMechanics.VisVivaSpeed(mu, ra, a);
                    double vc = OrbitalMechanics.CircularVelocity(mu, ra);
                    double dv = OrbitalMechanics.CircularizationDeltaV(mu, ra, rp);
                    var r = new CalculatorResult()
                        .Out("Большая полуось", "a", a, "км", Km)
                        .Out("Скорость в апоцентре", "v_a", va, "м/с")
                        .Out("Круговая скорость в апоцентре", "v_круг", vc, "м/с")
                        .Out("Нужный разгон в апоцентре", "Δv", dv, "м/с")
                        .Step("r_a = R + h_a = " + N(ra) + " м,  r_p = R + h_p = " + N(rp) + " м")
                        .Step("a = (r_a + r_p)/2 = " + N(a) + " м")
                        .Step("v_a = √(μ·(2/r_a − 1/a)) = " + N(va) + " м/с  (vis-viva)")
                        .Step("v_круг = √(μ/r_a) = " + N(vc) + " м/с")
                        .Step("Δv = v_круг − v_a = " + N(dv) + " м/с — горизонтально, по направлению движения")
                        .Note("Импульсный манёвр. Реальный двигатель работает секунды-минуты — начинайте чуть до апоцентра.");
                    if (hp < 0.0) r.Warn("Перицентр под поверхностью: без этого манёвра аппарат упадёт.");
                    else if (hp * Km < c.SpaceBoundary) r.Warn("Перицентр в атмосфере: на каждом проходе аппарат тормозится и в итоге упадёт.");
                    if (ha * Km < c.SpaceBoundary) r.Warn("Апоцентр ниже границы космоса — круговая орбита там быстро затормозится атмосферой.");
                    return r;
                });
            calc.Presets.Add(new CalculatorPreset("Апоцентр " + N(apo) + " км, перицентр 0 км", R4(c.Mu), R4(c.Radius / Km), R4(apo), 0.0));
            calc.Presets.Add(new CalculatorPreset("Почти вертикальный подъём: перицентр −" + N(c.Radius / Km * 0.5) + " км", R4(c.Mu), R4(c.Radius / Km), R4(apo), -R4(c.Radius / Km * 0.5)));
            calc.Presets.Add(new CalculatorPreset("Апоцентр " + N(apo) + " км, перицентр " + N(c.SpaceBoundary / Km) + " км", R4(c.Mu), R4(c.Radius / Km), R4(apo), R4(c.SpaceBoundary / Km)));
            return calc;
        }

        // ---- 12. Hohmann transfer (also rendezvous) --------------------------------------------------------------

        static AcademyCalculator BuildHohmann(AcademyContext c)
        {
            double low = c.LowOrbitAltitude;
            var calc = new AcademyCalculator(Hohmann, "Гомановский переход", AcademyTopics.Hohmann,
                "Два импульса между круговыми орбитами r₁ и r₂ по эллипсу a = (r₁ + r₂)/2; фазовый угол для встречи.",
                new[]
                {
                    new CalculatorInput("mu", "Гравитационный параметр центра", "μ", "м³/с²", 1.0, R4(c.Mu)),
                    new CalculatorInput("r1", "Радиус начальной орбиты (от центра)", "r₁", "км", Km, R4((c.Radius + low) / Km)),
                    new CalculatorInput("r2", "Радиус конечной орбиты (от центра)", "r₂", "км", Km, R4((c.Radius + 2.0 * low) / Km))
                },
                v =>
                {
                    double mu = v[0], r1 = v[1], r2 = v[2];
                    HohmannTransfer h = TransferCalculator.Hohmann(mu, r1, r2);
                    double phase = TransferCalculator.HohmannPhaseAngle(mu, r1, r2);
                    double synodic = TransferCalculator.SynodicPeriod(OrbitalMechanics.CircularPeriod(mu, r1), OrbitalMechanics.CircularPeriod(mu, r2));
                    var r = new CalculatorResult()
                        .Out("Большая полуось перехода", "a", h.SemiMajorAxis, "км", Km)
                        .Out("Первый импульс", "Δv₁", h.DeltaV1, "м/с")
                        .Out("Второй импульс", "Δv₂", h.DeltaV2, "м/с")
                        .Out("Сумма", "Δv", h.TotalDeltaV, "м/с")
                        .Out("Время перелёта", "t", h.TransferTime, "ч", Hour)
                        .Out("Фазовый угол цели при старте", "θ", phase, "°", Degree)
                        .Out("Синодический период (повтор окна)", "T_син", synodic, "ч", Hour)
                        .Step("a = (r₁ + r₂)/2 = (" + N(r1) + " + " + N(r2) + ")/2 = " + N(h.SemiMajorAxis) + " м")
                        .Step("Δv₁ = √(μ(2/r₁ − 1/a)) − √(μ/r₁) = " + N(h.DepartureSpeed) + " − " + N(OrbitalMechanics.CircularVelocity(mu, r1)) + " = " + N(h.DeltaV1) + " м/с")
                        .Step("Δv₂ = √(μ/r₂) − √(μ(2/r₂ − 1/a)) = " + N(OrbitalMechanics.CircularVelocity(mu, r2)) + " − " + N(h.ArrivalSpeed) + " = " + N(h.DeltaV2) + " м/с")
                        .Step("t = π·√(a³/μ) = " + N(h.TransferTime) + " с")
                        .Step("θ = π − n₂·t,  n₂ = √(μ/r₂³)  →  θ = " + N(phase / Degree) + "°")
                        .Note(phase >= 0.0 ? "θ > 0: в момент первого импульса цель должна быть ВПЕРЕДИ на θ." : "θ < 0: в момент первого импульса цель должна быть ПОЗАДИ на |θ|.")
                        .Note("Уровень A: круговые компланарные орбиты, мгновенные импульсы.");
                    if (r1 == r2) r.Note("r₁ = r₂: переход не нужен.");
                    return r;
                });
            calc.Presets.Add(new CalculatorPreset("Подъём орбиты " + N(low / Km) + " → " + N(2.0 * low / Km) + " км", R4(c.Mu), R4((c.Radius + low) / Km), R4((c.Radius + 2.0 * low) / Km)));
            calc.Presets.Add(new CalculatorPreset("Встреча: орбита 200 км → станция на 400 км", R4(c.Mu), R4((c.Radius + 200000.0) / Km), R4((c.Radius + 400000.0) / Km)));
            AcademyBody origin = c.Find(c.PlanetId);
            if (origin != null && origin.OrbitsStar && c.StarMu > 0.0)
                foreach (var target in c.Planets())
                {
                    if (target.Id == origin.Id) continue;
                    calc.Presets.Add(new CalculatorPreset(origin.Name + " → " + target.Name + " (вокруг звезды)", R4(c.StarMu), R4(origin.OrbitRadius / Km), R4(target.OrbitRadius / Km)));
                }
            return calc;
        }

        // ---- 13. Interplanetary transfer estimate ----------------------------------------------------------------

        static AcademyCalculator BuildTransfer(AcademyContext c)
        {
            AcademyBody origin = c.Find(c.PlanetId);
            if (origin == null || !origin.OrbitsStar) origin = c.Find(HomeSystemCatalog.HomeWorldId) ?? FirstPlanet(c, null);
            AcademyBody target = FirstPlanet(c, origin != null ? origin.Id : null);
            double[] defaults = TransferValues(c, origin, target);
            var calc = new AcademyCalculator(Transfer, "Оценка межпланетного перелёта", AcademyTopics.LaunchWindows,
                "Гомановский переход вокруг звезды + отлёт с орбиты ожидания и захват у цели (метод сопряжённых конических сечений).",
                new[]
                {
                    new CalculatorInput("muS", "μ звезды", "μ★", "м³/с²", 1.0, defaults[0]),
                    new CalculatorInput("r1", "Орбита планеты отлёта", "r₁", "млн км", MillionKm, defaults[1]),
                    new CalculatorInput("r2", "Орбита планеты цели", "r₂", "млн км", MillionKm, defaults[2]),
                    new CalculatorInput("mu1", "μ планеты отлёта", "μ₁", "м³/с²", 1.0, defaults[3]),
                    new CalculatorInput("R1", "Радиус планеты отлёта", "R₁", "км", Km, defaults[4]),
                    new CalculatorInput("h1", "Высота орбиты ожидания", "h₁", "км", Km, defaults[5], InputDomain.NonNegative),
                    new CalculatorInput("mu2", "μ планеты цели", "μ₂", "м³/с²", 1.0, defaults[6]),
                    new CalculatorInput("R2", "Радиус планеты цели", "R₂", "км", Km, defaults[7]),
                    new CalculatorInput("h2", "Высота орбиты у цели", "h₂", "км", Km, defaults[8], InputDomain.NonNegative),
                    new CalculatorInput("plan", "Ваш импульс отлёта (0 — не проверять)", "Δv_план", "м/с", 1.0, 0.0, InputDomain.NonNegative)
                },
                v =>
                {
                    double muS = v[0], r1 = v[1], r2 = v[2], mu1 = v[3], radius1 = v[4], h1 = v[5], mu2 = v[6], radius2 = v[7], h2 = v[8], plan = v[9];
                    HohmannTransfer h = TransferCalculator.Hohmann(muS, r1, r2);
                    TransferCalculator.HohmannExcessSpeeds(muS, r1, r2, out double vInfDep, out double vInfArr);
                    double departure = TransferCalculator.DepartureDeltaV(mu1, radius1 + h1, vInfDep);
                    double capture = TransferCalculator.CaptureDeltaV(mu2, radius2 + h2, vInfArr);
                    double phase = TransferCalculator.HohmannPhaseAngle(muS, r1, r2);
                    double synodic = TransferCalculator.SynodicPeriod(OrbitalMechanics.CircularPeriod(muS, r1), OrbitalMechanics.CircularPeriod(muS, r2));
                    var r = new CalculatorResult()
                        .Out("Большая полуось перехода", "a", h.SemiMajorAxis, "млн км", MillionKm)
                        .Out("Избыток скорости при отлёте", "v∞₁", vInfDep, "м/с")
                        .Out("Избыток скорости у цели", "v∞₂", vInfArr, "м/с")
                        .Out("Импульс отлёта с орбиты ожидания", "Δv_отл", departure, "м/с")
                        .Out("Импульс захвата у цели", "Δv_зах", capture, "м/с")
                        .Out("Итого от орбиты до орбиты", "Δv", departure + capture, "м/с")
                        .Out("Время перелёта", "t", h.TransferTime, "сут", Day)
                        .Out("Фазовый угол цели при старте", "θ", phase, "°", Degree)
                        .Out("Окна повторяются каждые", "T_син", synodic, "сут", Day)
                        .Step("a = (r₁ + r₂)/2 = " + N(h.SemiMajorAxis / MillionKm) + " млн км")
                        .Step("v∞₁ = |√(μ★(2/r₁ − 1/a)) − √(μ★/r₁)| = " + N(vInfDep) + " м/с")
                        .Step("v∞₂ = |√(μ★/r₂) − √(μ★(2/r₂ − 1/a))| = " + N(vInfArr) + " м/с")
                        .Step("Δv_отл = √(v∞₁² + 2μ₁/r_орб) − √(μ₁/r_орб) = " + N(departure) + " м/с  (r_орб = R₁ + h₁)")
                        .Step("Δv_зах = √(v∞₂² + 2μ₂/r_орб) − √(μ₂/r_орб) = " + N(capture) + " м/с  (r_орб = R₂ + h₂)")
                        .Step("t = π·√(a³/μ★) = " + N(h.TransferTime / Day) + " сут")
                        .Step("θ = π − n₂·t = " + N(phase / Degree) + "°;  T_син = 1/|1/T₁ − 1/T₂| = " + N(synodic / Day) + " сут")
                        .Note("Уровень A: круговые компланарные орбиты планет, мгновенные импульсы. Подъём с поверхности на орбиту ожидания сюда не входит.")
                        .Note(phase >= 0.0 ? "θ > 0: при отлёте цель должна опережать планету отлёта на θ." : "θ < 0: при отлёте цель должна отставать на |θ|.");
                    if (plan > 0.0)
                    {
                        if (plan < departure)
                            r.Warn("Скорость отлёта недостаточна для выхода на переходную орбиту: не хватает " + N(departure - plan) + " м/с.");
                        else
                            r.Note("Импульса отлёта хватает (запас " + N(plan - departure) + " м/с). Лишняя скорость изменит время перелёта и точку встречи.");
                    }
                    return r;
                });
            if (origin != null)
                foreach (var t in c.Planets())
                {
                    if (t.Id == origin.Id) continue;
                    calc.Presets.Add(new CalculatorPreset(origin.Name + " → " + t.Name, TransferValues(c, origin, t)));
                }
            return calc;
        }

        static AcademyBody FirstPlanet(AcademyContext c, string exceptId)
        {
            foreach (var b in c.Planets())
                if (b.Id != exceptId) return b;
            return null;
        }

        static double[] TransferValues(AcademyContext c, AcademyBody origin, AcademyBody target)
        {
            const double parking = 200.0; // km
            if (origin == null || target == null || !(c.StarMu > 0.0))
                return new[] { 1.327e20, 149.6, 227.9, 3.986e14, 6371.0, parking, 4.283e13, 3390.0, parking, 0.0 };
            return new[]
            {
                R4(c.StarMu), R4(origin.OrbitRadius / MillionKm), R4(target.OrbitRadius / MillionKm),
                R4(origin.Mu), R4(origin.Radius / Km), parking, R4(target.Mu), R4(target.Radius / Km), parking, 0.0
            };
        }
    }
}
