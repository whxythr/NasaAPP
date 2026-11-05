using System;
using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Physics;
using SpaceSim.Units;

namespace SpaceSim.Academy
{
    /// <summary>
    /// The 13 Flight Academy lessons. Worked examples and exercise answers are computed with the central physics functions
    /// from the current planet and the player's rocket, so the lessons describe exactly the game being played.
    /// </summary>
    public static class AcademyTopics
    {
        public const string MassThrust = "mass-thrust";
        public const string Gravity = "gravity";
        public const string Acceleration = "acceleration";
        public const string FuelConsumption = "fuel-consumption";
        public const string Tsiolkovsky = "tsiolkovsky";
        public const string DeltaVBudget = "delta-v";
        public const string OrbitalVelocity = "orbital-velocity";
        public const string EscapeVelocity = "escape-velocity";
        public const string Drag = "drag";
        public const string Insertion = "orbital-insertion";
        public const string Hohmann = "hohmann";
        public const string LaunchWindows = "launch-windows";
        public const string Rendezvous = "rendezvous";

        const double Tonne = 1000.0, KiloNewton = 1000.0, Km = 1000.0, Day = 86400.0, Minute = 60.0;
        const double G0 = RocketEquations.G0;

        static string N(double v) => AcademyNumber.Format(v);
        static double S3(double v) => AcademyNumber.RoundSignificant(v, 3);
        static FormulaVariable V(string symbol, string meaning, string unit) => new FormulaVariable(symbol, meaning, unit);

        public static AcademyTopic Find(IEnumerable<AcademyTopic> topics, string id)
        {
            foreach (var t in topics)
                if (t.Id == id) return t;
            return null;
        }

        public static List<AcademyTopic> Build(AcademyContext c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            var list = new List<AcademyTopic>
            {
                MassAndThrust(c), GravityTopic(c), AccelerationTopic(c), FuelConsumptionTopic(c), TsiolkovskyTopic(c), DeltaVTopic(c),
                OrbitalVelocityTopic(c), EscapeVelocityTopic(c), DragTopic(c), InsertionTopic(c), HohmannTopic(c), LaunchWindowsTopic(c),
                RendezvousTopic(c)
            };
            for (int i = 0; i < list.Count; i++) list[i].Number = i + 1;
            return list;
        }

        static AcademyExercise Exercise(string id, string question, double answer, string unit, string[] hints, string[] steps)
        {
            var e = new AcademyExercise { Id = id, Question = question, Answer = answer, Unit = unit };
            e.Hints.AddRange(hints);
            e.SolutionSteps.AddRange(steps);
            return e;
        }

        static string RocketLabel(AcademyContext c) => AcademyCalculators.HasRocket(c) ? "вашей ракеты" : "учебной ракеты";

        // ---- 1 ---------------------------------------------------------------------------------------------------

        static AcademyTopic MassAndThrust(AcademyContext c)
        {
            StageReport s1 = AcademyCalculators.Stage(c, 0);
            double launchMass = AcademyCalculators.HasRocket(c) && c.Design.LaunchMass > 0.0 ? c.Design.LaunchMass : s1.MassAtIgnition;
            double g = c.SurfaceGravity;
            double twr = RocketEquations.ThrustToWeight(s1.SurfaceThrust, launchMass, g);
            var t = new AcademyTopic
            {
                Id = MassThrust,
                Title = "Масса ракеты и тяга",
                Explanation =
                    "Ракетный двигатель отбрасывает назад продукты сгорания со скоростью vₑ. По третьему закону Ньютона газ толкает ракету вперёд " +
                    "с силой F = ṁ·vₑ — это тяга. Удельный импульс Isp — та же скорость истечения, выраженная в секундах: vₑ = Isp·g₀. " +
                    "Чем выше Isp, тем меньше топлива уходит на ту же тягу.\n\n" +
                    "Почти вся масса ракеты на старте — топливо. Чтобы ракета вообще оторвалась, тяга должна быть больше её веса: " +
                    "отношение тяга/вес (TWR) > 1. Когда топливо выгорает, масса падает и TWR растёт.",
                Example =
                    "Первая ступень " + RocketLabel(c) + " («" + s1.Name + "»): ṁ = " + N(s1.MassFlow) + " кг/с, Isp = " + N(s1.VacuumIsp) + " с в вакууме и " +
                    N(s1.SurfaceIsp) + " с у поверхности.\n" +
                    "F_вак = " + N(s1.MassFlow) + " · " + N(s1.VacuumIsp) + " · 9.80665 = " + N(s1.VacuumThrust / KiloNewton) + " кН;  F_пов = " + N(s1.SurfaceThrust / KiloNewton) + " кН.\n" +
                    "Стартовая масса " + N(launchMass / Tonne) + " т, g = " + N(g) + " м/с² → TWR = " + N(s1.SurfaceThrust) + " / (" + N(launchMass) + " · " + N(g) + ") = " + N(twr) + ".",
                Application =
                    "Ангар считает массу, тягу и TWR у старта по этим формулам. Кнопка ЗАПУСК не запрещает старт при TWR < 1 — двигатель будет работать, " +
                    "а ракета стоять на площадке и жечь топливо. В полёте панель РАКЕТА показывает «Тяга/вес (макс.)» для текущей массы и давления.",
                Limitations =
                    "Тяга зависит от давления снаружи: у поверхности Isp и тяга меньше, чем в вакууме (в игре это учтено). " +
                    "TWR — статическая оценка: она не учитывает сопротивление воздуха и наклон ракеты."
            };
            t.Formulas.Add(new AcademyFormula("m₀ = m_сух + m_топл + m_груз",
                V("m₀", "стартовая масса", "кг"), V("m_сух", "сухая масса ступеней: баки, двигатели", "кг"),
                V("m_топл", "масса топлива", "кг"), V("m_груз", "полезная нагрузка и приборы", "кг")));
            t.Formulas.Add(new AcademyFormula("F = ṁ·Isp·g₀ = ṁ·vₑ",
                V("F", "тяга", "Н"), V("ṁ", "массовый расход топлива", "кг/с"), V("Isp", "удельный импульс", "с"),
                V("g₀", "стандартное ускорение 9.80665 (постоянная пересчёта, не местная гравитация)", "м/с²"), V("vₑ", "скорость истечения", "м/с")));
            t.Formulas.Add(new AcademyFormula("TWR = F / (m·g)",
                V("TWR", "отношение тяги к весу", "безразмерно"), V("m", "текущая масса", "кг"), V("g", "местное ускорение свободного падения", "м/с²")));
            t.CalculatorIds.Add(AcademyCalculators.Thrust);
            t.CalculatorIds.Add(AcademyCalculators.ThrustToWeight);

            double mdot = S3(s1.MassFlow), isp = S3(s1.VacuumIsp);
            double ve = RocketEquations.ExhaustVelocity(isp);
            double f = RocketEquations.ThrustFromMassFlow(mdot, isp);
            t.Exercise = Exercise("ex-thrust",
                "Двигатель расходует ṁ = " + N(mdot) + " кг/с топлива при Isp = " + N(isp) + " с. Какова его тяга? Ответ в килоньютонах (кН).",
                f / KiloNewton, "кН",
                new[] { "Тяга — это расход, умноженный на скорость истечения.", "Скорость истечения vₑ = Isp·g₀, где g₀ = 9.80665 м/с².", "Получится ответ в ньютонах — переведите в кН." },
                new[] { "vₑ = Isp·g₀ = " + N(isp) + " · 9.80665 = " + N(ve) + " м/с", "F = ṁ·vₑ = " + N(mdot) + " · " + N(ve) + " = " + N(f) + " Н", "F = " + N(f / KiloNewton) + " кН" });
            return t;
        }

        // ---- 2 ---------------------------------------------------------------------------------------------------

        static AcademyTopic GravityTopic(AcademyContext c)
        {
            double g0 = c.SurfaceGravity;
            double g400 = PlanetaryGravity.AccelerationMagnitude(c.Mu, c.Radius + 400000.0);
            var t = new AcademyTopic
            {
                Id = Gravity,
                Title = "Гравитация",
                Explanation =
                    "Любые две массы притягиваются с силой, обратно пропорциональной квадрату расстояния между их центрами. " +
                    "Расстояние r отсчитывается от ЦЕНТРА планеты, а не от поверхности, поэтому с высотой гравитация слабеет медленно.\n\n" +
                    "На орбите гравитация никуда не исчезает: невесомость — это свободное падение, при котором аппарат всё время «промахивается» мимо планеты.",
                Example =
                    c.PlanetName + ": μ = " + N(c.Mu) + " м³/с², R = " + N(c.Radius / Km) + " км.\n" +
                    "У поверхности g = μ/R² = " + N(g0) + " м/с².\n" +
                    "На высоте 400 км: r = " + N((c.Radius + 400000.0) / Km) + " км, g = " + N(g400) + " м/с² — это " + N(g400 / g0 * 100.0) + " % от поверхностной.",
                Application =
                    "Телеметрия показывает g(r) = μ/r². На подъёме гравитация — главный источник потерь: каждую секунду, пока ракета летит вверх, " +
                    "она отнимает около g м/с скорости («Потери: гравит.» в панели РАКЕТА).",
                Limitations =
                    "Модель — сферическая однородная планета: горы и сжатие на g не влияют. В симуляции действует притяжение только того тела, " +
                    "в чьей сфере влияния находится аппарат (метод сопряжённых конических сечений)."
            };
            t.Formulas.Add(new AcademyFormula("F = G·M·m / r²",
                V("F", "сила притяжения", "Н"), V("G", "гравитационная постоянная 6.6743·10⁻¹¹", "м³/(кг·с²)"),
                V("M", "масса планеты", "кг"), V("m", "масса аппарата", "кг"), V("r", "расстояние от центра планеты", "м")));
            t.Formulas.Add(new AcademyFormula("g(r) = μ / r²,  μ = G·M",
                V("g", "ускорение свободного падения", "м/с²"), V("μ", "гравитационный параметр", "м³/с²")));
            t.CalculatorIds.Add(AcademyCalculators.Gravity);

            double r = c.Radius + 400000.0;
            t.Exercise = Exercise("ex-gravity",
                "Чему равно ускорение свободного падения на высоте 400 км над планетой «" + c.PlanetName + "»? μ = " + N(c.Mu) + " м³/с², R = " + N(c.Radius / Km) + " км. Ответ в м/с².",
                g400, "м/с²",
                new[] { "В формулу подставляется расстояние от центра, а не высота.", "r = R + h — и всё в метрах.", "g = μ/r²." },
                new[] { "r = " + N(c.Radius) + " + 400 000 = " + N(r) + " м", "r² = " + N(r * r) + " м²", "g = " + N(c.Mu) + " / " + N(r * r) + " = " + N(g400) + " м/с²" });
            return t;
        }

        // ---- 3 ---------------------------------------------------------------------------------------------------

        static AcademyTopic AccelerationTopic(AcademyContext c)
        {
            StageReport s1 = AcademyCalculators.Stage(c, 0);
            double g = c.SurfaceGravity;
            double a0 = RocketEquations.Acceleration(s1.SurfaceThrust, s1.MassAtIgnition);
            double net0 = RocketEquations.Acceleration(s1.SurfaceThrust - s1.MassAtIgnition * g, s1.MassAtIgnition);
            double a1 = RocketEquations.Acceleration(s1.VacuumThrust, s1.MassAtBurnout);
            var t = new AcademyTopic
            {
                Id = Acceleration,
                Title = "Ускорение",
                Explanation =
                    "Второй закон Ньютона: ускорение равно сумме сил, делённой на массу. На ракету при вертикальном подъёме действуют тяга вверх и вес вниз, " +
                    "в атмосфере — ещё сопротивление.\n\n" +
                    "Масса ракеты всё время уменьшается, поэтому при постоянной тяге ускорение растёт и максимально перед выключением ступени. " +
                    "Акселерометр («перегрузка») показывает только негравитационные силы — тягу и сопротивление; в свободном падении он показывает ноль.",
                Example =
                    "Первая ступень " + RocketLabel(c) + " у старта: a = F/m = " + N(s1.SurfaceThrust / KiloNewton) + " кН / " + N(s1.MassAtIgnition / Tonne) + " т = " + N(a0) + " м/с²; " +
                    "вверх остаётся a − g = " + N(net0) + " м/с².\n" +
                    "Перед выгоранием (масса " + N(s1.MassAtBurnout / Tonne) + " т, вакуум): a = " + N(a1) + " м/с² ≈ " + N(a1 / G0) + " g₀.",
                Application =
                    "Телеметрия: «|a| полное», «Перегрузка (акселерометр)», в панели РАКЕТА — ускорение от тяги. Сильная перегрузка в конце ступени — " +
                    "повод уменьшить тягу (Ctrl) для хрупкой нагрузки.",
                Limitations = "Формула a_верт — для строго вертикального полёта без сопротивления. При наклоне на подъём идёт только часть тяги."
            };
            t.Formulas.Add(new AcademyFormula("a = F / m", V("a", "ускорение", "м/с²"), V("F", "сила (тяга)", "Н"), V("m", "масса", "кг")));
            t.Formulas.Add(new AcademyFormula("a_верт = (F − m·g) / m", V("a_верт", "ускорение вверх при вертикальном подъёме", "м/с²"), V("g", "местная гравитация", "м/с²")));
            t.CalculatorIds.Add(AcademyCalculators.Acceleration);

            double fx = S3(s1.SurfaceThrust / KiloNewton), mx = S3(s1.MassAtIgnition / Tonne), gx = S3(g);
            if (fx * KiloNewton <= mx * Tonne * gx * 1.05)
            {
                fx = 3000.0;
                mx = 200.0;
            }
            double weight = mx * Tonne * gx;
            double net = RocketEquations.Acceleration(fx * KiloNewton - weight, mx * Tonne);
            t.Exercise = Exercise("ex-acceleration",
                "Ракета массой " + N(mx) + " т стартует вертикально с тягой " + N(fx) + " кН, g = " + N(gx) + " м/с². С каким ускорением она поднимается сразу после отрыва (без сопротивления)? Ответ в м/с².",
                net, "м/с²",
                new[] { "Действуют две силы: тяга вверх и вес вниз.", "Вес W = m·g; результирующая сила — F − W.", "Ускорение = результирующая сила / масса. Массу — в килограммах, силу — в ньютонах." },
                new[] { "W = " + N(mx * Tonne) + " · " + N(gx) + " = " + N(weight) + " Н", "F − W = " + N(fx * KiloNewton) + " − " + N(weight) + " = " + N(fx * KiloNewton - weight) + " Н", "a = " + N(fx * KiloNewton - weight) + " / " + N(mx * Tonne) + " = " + N(net) + " м/с²" });
            return t;
        }

        // ---- 4 ---------------------------------------------------------------------------------------------------

        static AcademyTopic FuelConsumptionTopic(AcademyContext c)
        {
            StageReport s1 = AcademyCalculators.Stage(c, 0), s2 = AcademyCalculators.Stage(c, AcademyCalculators.LastStage(c));
            double t1 = RocketEquations.BurnTime(s1.PropellantMass, RocketEquations.MassFlow(s1.VacuumThrust, s1.VacuumIsp));
            double t2 = RocketEquations.BurnTime(s2.PropellantMass, RocketEquations.MassFlow(s2.VacuumThrust, s2.VacuumIsp));
            var t = new AcademyTopic
            {
                Id = FuelConsumption,
                Title = "Расход топлива",
                Explanation =
                    "Для заданной тяги двигатель должен выбрасывать ṁ килограммов в секунду; чем выше скорость истечения (Isp), тем меньше нужно. " +
                    "Время работы ступени — запас топлива, делённый на расход.\n\n" +
                    "Дроссель уменьшает расход и тягу в одной пропорции: ступень работает дольше, но Δv — тот же, потому что он зависит только от масс и Isp.",
                Example =
                    "«" + s1.Name + "»: ṁ = " + N(s1.MassFlow) + " кг/с, топлива " + N(s1.PropellantMass / Tonne) + " т → t = " + N(t1) + " с (" + N(t1 / Minute) + " мин).\n" +
                    "«" + s2.Name + "»: ṁ = " + N(RocketEquations.MassFlow(s2.VacuumThrust, s2.VacuumIsp)) + " кг/с, топлива " + N(s2.PropellantMass / Tonne) + " т → t = " + N(t2) + " с.",
                Application =
                    "Панель РАКЕТА: «Расход ṁ» и «Топливо ступени». Shift/Ctrl меняют дроссель — следите, как меняется ṁ. Пустая ступень — лишний груз: отделите её (Пробел).",
                Limitations =
                    "Постоянные Isp и расход; без переходных режимов запуска и остатков топлива. У поверхности Isp ниже, поэтому при том же расходе тяга меньше."
            };
            t.Formulas.Add(new AcademyFormula("ṁ = F / (Isp·g₀)", V("ṁ", "массовый расход", "кг/с"), V("F", "тяга", "Н"), V("Isp", "удельный импульс", "с"), V("g₀", "9.80665", "м/с²")));
            t.Formulas.Add(new AcademyFormula("t = m_топл / ṁ", V("t", "время работы на полной тяге", "с"), V("m_топл", "запас топлива ступени", "кг")));
            t.Formulas.Add(new AcademyFormula("m(t) = m₀ − ṁ·t", V("m(t)", "масса через t секунд", "кг"), V("m₀", "масса при зажигании", "кг")));
            t.CalculatorIds.Add(AcademyCalculators.Burn);

            double fx = S3(s2.VacuumThrust / KiloNewton), ispx = S3(s2.VacuumIsp), mpx = S3(s2.PropellantMass / Tonne);
            double mdot = RocketEquations.MassFlow(fx * KiloNewton, ispx);
            double burn = RocketEquations.BurnTime(mpx * Tonne, mdot);
            t.Exercise = Exercise("ex-burn",
                "Ступень с тягой " + N(fx) + " кН и Isp " + N(ispx) + " с несёт " + N(mpx) + " т топлива. Сколько секунд она проработает на полной тяге?",
                burn, "с",
                new[] { "Сначала найдите расход ṁ.", "ṁ = F/(Isp·g₀) — тягу подставьте в ньютонах.", "t = m_топл/ṁ — массу в килограммах." },
                new[] { "ṁ = " + N(fx * KiloNewton) + " / (" + N(ispx) + " · 9.80665) = " + N(mdot) + " кг/с", "t = " + N(mpx * Tonne) + " / " + N(mdot) + " = " + N(burn) + " с" });
            return t;
        }

        // ---- 5 ---------------------------------------------------------------------------------------------------

        static AcademyTopic TsiolkovskyTopic(AcademyContext c)
        {
            StageReport s2 = AcademyCalculators.Stage(c, AcademyCalculators.LastStage(c));
            double dv = RocketEquations.DeltaV(s2.VacuumIsp, s2.MassAtIgnition, s2.MassAtBurnout);
            double extra = 0.4 * s2.PropellantMass;
            double dvMore = RocketEquations.DeltaV(s2.VacuumIsp, s2.MassAtIgnition + extra, s2.MassAtBurnout);
            string total = AcademyCalculators.HasRocket(c) ? "Δv всей ракеты (сумма ступеней) — " + N(c.Design.TotalDeltaVVacuum) + " м/с." : "";
            var t = new AcademyTopic
            {
                Id = Tsiolkovsky,
                Title = "Формула Циолковского",
                Explanation =
                    "Сколько скорости может набрать ракета, сжигая топливо? Каждая порция газа уносит импульс, а ракете приходится разгонять ещё не сожжённое " +
                    "топливо. В итоге Δv растёт как ЛОГАРИФМ отношения масс: удвоение топлива не удваивает Δv.\n\n" +
                    "Отсюда главная идея ракетостроения — ступени: сбрасывая пустые баки, ракета перестаёт разгонять мёртвый груз.",
                Example =
                    "«" + s2.Name + "»: m₀ = " + N(s2.MassAtIgnition / Tonne) + " т, m_f = " + N(s2.MassAtBurnout / Tonne) + " т, Isp = " + N(s2.VacuumIsp) + " с → " +
                    "Δv = " + N(s2.VacuumIsp * G0) + " · ln(" + N(s2.MassAtIgnition / s2.MassAtBurnout) + ") = " + N(dv) + " м/с.\n" +
                    "Добавим 40 % топлива (+" + N(extra / Tonne) + " т): Δv = " + N(dvMore) + " м/с — прирост лишь " + N((dvMore / dv - 1.0) * 100.0) + " %. " + total,
                Application =
                    "«Δv идеальн. остаток» в панели РАКЕТА и Δv ступеней в ангаре считаются этой формулой — одной и той же функцией ядра игры.",
                Limitations =
                    "Это уровень A — идеальный расчёт: без гравитации, сопротивления и потерь на управление. Реальный разгон при подъёме меньше " +
                    "на сумму потерь, которую панель РАКЕТА показывает отдельно."
            };
            t.Formulas.Add(new AcademyFormula("Δv = Isp·g₀·ln(m₀ / m_f)",
                V("Δv", "идеальное изменение скорости", "м/с"), V("Isp", "удельный импульс", "с"), V("g₀", "9.80665", "м/с²"),
                V("m₀", "масса в начале работы", "кг"), V("m_f", "масса в конце (без сожжённого топлива)", "кг"), V("ln", "натуральный логарифм", "—")));
            t.CalculatorIds.Add(AcademyCalculators.DeltaV);

            double ispx = S3(s2.VacuumIsp), m0x = S3(s2.MassAtIgnition / Tonne), mfx = S3(s2.MassAtBurnout / Tonne);
            if (mfx >= m0x) mfx = S3(m0x * 0.4);
            double answer = RocketEquations.DeltaV(ispx, m0x, mfx);
            t.Exercise = Exercise("ex-tsiolkovsky",
                "Ступень: Isp = " + N(ispx) + " с, масса при зажигании " + N(m0x) + " т, после выгорания " + N(mfx) + " т. Каков её идеальный Δv? Ответ в м/с.",
                answer, "м/с",
                new[] { "Отношение масс безразмерно — тонны можно не переводить в килограммы.", "ln — натуральный логарифм (по основанию e).", "Скорость истечения vₑ = Isp·g₀." },
                new[] { "vₑ = " + N(ispx) + " · 9.80665 = " + N(ispx * G0) + " м/с", "m₀/m_f = " + N(m0x / mfx) + ",  ln = " + N(Math.Log(m0x / mfx)), "Δv = " + N(answer) + " м/с" });
            return t;
        }

        // ---- 6 ---------------------------------------------------------------------------------------------------

        static AcademyTopic DeltaVTopic(AcademyContext c)
        {
            MissionTarget[] targets = MissionRequirements.StandardTargets(c.Mu, c.Radius, c.SiteRotationSpeed, c.SpaceBoundary, c.HasAtmosphere);
            string table = "";
            foreach (var m in targets) table += "• " + m.Name + ": " + N(m.IdealDeltaV) + " м/с\n";
            string verdict = "";
            if (AcademyCalculators.HasRocket(c) && targets.Length > 1)
            {
                double margin = c.Design.TotalDeltaVVacuum - targets[1].IdealDeltaV;
                verdict = "У " + RocketLabel(c) + " идеальный Δv " + N(c.Design.TotalDeltaVVacuum) + " м/с: запас над идеальным требованием орбиты " + N(margin) + " м/с" +
                          (margin < 1000.0 ? " — на потери подъёма этого, скорее всего, не хватит." : ". Хватит ли его на потери — проверит полёт.");
            }
            StageReport s2 = AcademyCalculators.Stage(c, AcademyCalculators.LastStage(c));
            var t = new AcademyTopic
            {
                Id = DeltaVBudget,
                Title = "Δv — бюджет миссии",
                Explanation =
                    "Δv — «валюта» космического полёта: каждая задача (подняться, выйти на орбиту, улететь) требует определённого изменения скорости. " +
                    "Ракета несёт запас Δv — сумму Δv своих ступеней. Миссия возможна, если запас больше требования С УЧЁТОМ потерь.\n\n" +
                    "Идеальные требования (уровень A) — нижняя граница. Реально подъём через атмосферу стоит больше: часть тяги тратится на борьбу " +
                    "с гравитацией, сопротивлением и на повороты (уровень B — это и есть полёт).",
                Example = "Идеальные требования для планеты «" + c.PlanetName + "» (старт на восток, вращение даёт " + N(c.SiteRotationSpeed) + " м/с):\n" + table + verdict,
                Application =
                    "Ангар: «Оценка возможностей» сравнивает Δv ракеты с этими требованиями. После полёта сравните «Δv израсходовано» и набранную скорость: " +
                    "разница — потери, и панель РАКЕТА разбивает их на гравитационные, аэродинамические и на управление.",
                Limitations =
                    "Требования посчитаны для идеальных импульсов. Потери зависят от траектории и тяги/веса: медленная ракета теряет больше на гравитацию, " +
                    "быстрая в плотном воздухе — на сопротивление."
            };
            t.Formulas.Add(new AcademyFormula("Δv_ракеты = Σ Isp_i·g₀·ln(m₀ᵢ / m_fᵢ)", V("i", "номер ступени", "—"), V("m₀ᵢ, m_fᵢ", "масса всей ракеты при зажигании и выгорании ступени i", "кг")));
            t.Formulas.Add(new AcademyFormula("m_топл = m_f·(e^(Δv/(Isp·g₀)) − 1)", V("m_топл", "топливо для получения Δv", "кг"), V("m_f", "масса без этого топлива", "кг")));
            t.Formulas.Add(new AcademyFormula("Δv_израсх = Δv_набрано + потери_грав + потери_сопр + потери_упр", V("потери", "скорость, потраченная не на разгон (уровень B)", "м/с")));
            t.CalculatorIds.Add(AcademyCalculators.Fuel);
            t.CalculatorIds.Add(AcademyCalculators.DeltaV);

            double dvx = 2500.0, ispx = S3(s2.VacuumIsp), mfx = S3(s2.MassAtBurnout / Tonne);
            double ratio = RocketEquations.MassRatio(dvx, ispx);
            double mp = RocketEquations.PropellantForDeltaV(dvx, ispx, mfx * Tonne);
            t.Exercise = Exercise("ex-fuel",
                "Ступень вместе с грузом весит без топлива " + N(mfx) + " т, Isp = " + N(ispx) + " с. Сколько тонн топлива нужно, чтобы она дала Δv = " + N(dvx) + " м/с?",
                mp / Tonne, "т",
                new[] { "Переверните формулу Циолковского: найдите отношение масс m₀/m_f = e^(Δv/vₑ).", "Топливо — это m₀ − m_f = m_f·(m₀/m_f − 1)." },
                new[] { "vₑ = " + N(ispx * G0) + " м/с", "m₀/m_f = e^(" + N(dvx) + "/" + N(ispx * G0) + ") = " + N(ratio), "m_топл = " + N(mfx) + " · " + N(ratio - 1.0) + " = " + N(mp / Tonne) + " т" });
            return t;
        }

        // ---- 7 ---------------------------------------------------------------------------------------------------

        static AcademyTopic OrbitalVelocityTopic(AcademyContext c)
        {
            double low = c.LowOrbitAltitude;
            double rLow = c.Radius + low;
            double vLow = OrbitalMechanics.CircularVelocity(c.Mu, rLow);
            double tLow = OrbitalMechanics.CircularPeriod(c.Mu, rLow);
            double v400 = OrbitalMechanics.CircularVelocity(c.Mu, c.Radius + 400000.0);
            var t = new AcademyTopic
            {
                Id = OrbitalVelocity,
                Title = "Орбитальная скорость",
                Explanation =
                    "Орбита — это падение, при котором аппарат движется вбок так быстро, что поверхность «уходит» из-под него с той же скоростью, с какой он падает. " +
                    "Для круговой орбиты гравитация как раз обеспечивает нужное центростремительное ускорение: v²/r = μ/r².\n\n" +
                    "Нужна ГОРИЗОНТАЛЬНАЯ скорость. Ракета, летящая только вверх, поднимется и упадёт обратно, сколько бы топлива ни сожгла. " +
                    "Чем выше орбита, тем меньше на ней скорость — но тем больше энергии нужно, чтобы на неё подняться.",
                Example =
                    "Орбита " + N(low / Km) + " км над «" + c.PlanetName + "»: r = " + N(rLow / Km) + " км, v = √(μ/r) = " + N(vLow) + " м/с, период T = " + N(tLow / Minute) + " мин.\n" +
                    "Орбита 400 км: v = " + N(v400) + " м/с — медленнее.\n" +
                    "Старт на восток экономит скорость вращения площадки: " + N(c.SiteRotationSpeed) + " м/с.",
                Application =
                    "Телеметрия показывает «v горизонт.» и ε = v²/2 − μ/r. Орбита достигнута, когда перицентр выше атмосферы — фаза полёта станет «орбита», " +
                    "за первую орбиту — +20 000 кр. С навигационным компьютером НК-1 элементы орбиты видны по O; без него — считайте сами.",
                Limitations =
                    "Круговая орбита вокруг сферической планеты без атмосферы. Низкие реальные орбиты медленно тормозятся разреженным воздухом (в игре тоже: плотность нигде не обнуляется)."
            };
            t.Formulas.Add(new AcademyFormula("v_круг = √(μ / r)", V("v_круг", "скорость круговой орбиты (инерциальная)", "м/с"), V("μ", "гравитационный параметр", "м³/с²"), V("r", "радиус орбиты от центра", "м")));
            t.Formulas.Add(new AcademyFormula("T = 2π·√(r³ / μ)", V("T", "период обращения", "с")));
            t.Formulas.Add(new AcademyFormula("ε = v²/2 − μ/r", V("ε", "удельная орбитальная энергия; < 0 — орбита замкнута", "Дж/кг")));
            t.CalculatorIds.Add(AcademyCalculators.Orbit);

            double hx = S3(low / Km);
            double r = c.Radius + hx * Km;
            double answer = OrbitalMechanics.CircularVelocity(c.Mu, r);
            t.Exercise = Exercise("ex-orbit",
                "Какая скорость нужна для круговой орбиты на высоте " + N(hx) + " км над планетой «" + c.PlanetName + "»? μ = " + N(c.Mu) + " м³/с², R = " + N(c.Radius / Km) + " км. Ответ в м/с.",
                answer, "м/с",
                new[] { "Радиус орбиты отсчитывается от центра планеты.", "r = R + h, в метрах; затем v = √(μ/r)." },
                new[] { "r = " + N(c.Radius) + " + " + N(hx * Km) + " = " + N(r) + " м", "v = √(" + N(c.Mu) + " / " + N(r) + ") = " + N(answer) + " м/с" });
            return t;
        }

        // ---- 8 ---------------------------------------------------------------------------------------------------

        static AcademyTopic EscapeVelocityTopic(AcademyContext c)
        {
            double ve = OrbitalMechanics.EscapeVelocity(c.Mu, c.Radius);
            double rLow = c.Radius + c.LowOrbitAltitude;
            double veLow = OrbitalMechanics.EscapeVelocity(c.Mu, rLow);
            double vcLow = OrbitalMechanics.CircularVelocity(c.Mu, rLow);
            var t = new AcademyTopic
            {
                Id = EscapeVelocity,
                Title = "Вторая космическая скорость",
                Explanation =
                    "Чтобы улететь навсегда, кинетической энергии должно хватить на подъём «до бесконечности»: v²/2 ≥ μ/r, то есть ε ≥ 0. " +
                    "Скорость ухода в √2 ≈ 1.414 раза больше круговой на той же высоте.\n\n" +
                    "Уходить выгоднее с орбиты, а не прямо с поверхности: там ракета уже имеет круговую скорость, и добавить нужно лишь разницу.",
                Example =
                    "У поверхности «" + c.PlanetName + "»: v_уход = √(2μ/R) = " + N(ve) + " м/с.\n" +
                    "С орбиты " + N(c.LowOrbitAltitude / Km) + " км: v_уход = " + N(veLow) + " м/с, круговая там " + N(vcLow) + " м/с → добавить " + N(veLow - vcLow) + " м/с.",
                Application = "Фаза полёта «уход» включается, когда ε ≥ 0. Достижение «Уход с планеты» даёт +30 000 кр и открывает тяжёлую защиту в магазине.",
                Limitations =
                    "Только притяжение планеты: притяжение звезды и других тел не учтено. С поверхности с атмосферой разогнаться до v_уход внизу невозможно — " +
                    "сопротивление огромно; разгоняются выше атмосферы."
            };
            t.Formulas.Add(new AcademyFormula("v_уход = √(2μ / r) = √2 · v_круг", V("v_уход", "скорость ухода (вторая космическая)", "м/с"), V("r", "расстояние от центра", "м")));
            t.Formulas.Add(new AcademyFormula("ε = v²/2 − μ/r ≥ 0", V("ε", "удельная орбитальная энергия", "Дж/кг")));
            t.CalculatorIds.Add(AcademyCalculators.Escape);

            t.Exercise = Exercise("ex-escape",
                "Найдите вторую космическую скорость у поверхности планеты «" + c.PlanetName + "» (r = R = " + N(c.Radius / Km) + " км, μ = " + N(c.Mu) + " м³/с²). Ответ в м/с.",
                ve, "м/с",
                new[] { "Аппарат улетает, когда удельная энергия ε = v²/2 − μ/r становится нулевой.", "Из v²/2 = μ/R: v = √(2μ/R)." },
                new[] { "2μ/R = 2 · " + N(c.Mu) + " / " + N(c.Radius) + " = " + N(2.0 * c.Mu / c.Radius) + " м²/с²", "v = √(" + N(2.0 * c.Mu / c.Radius) + ") = " + N(ve) + " м/с" });
            return t;
        }

        // ---- 9 ---------------------------------------------------------------------------------------------------

        static AcademyTopic DragTopic(AcademyContext c)
        {
            bool exponential = c.HasAtmosphere && !double.IsNaN(c.ScaleHeight);
            double rho0 = exponential ? c.Atmosphere.ReferenceDensity : 1.225;
            double scaleHeight = exponential ? c.ScaleHeight : 8500.0;
            var air = new ExponentialAtmosphere(rho0, scaleHeight, 0.0, 0.0);
            double cd = c.Rocket != null ? c.Rocket.dragCoefficient : 0.3;
            double area = c.Rocket != null ? c.Rocket.ReferenceArea : Math.PI * 1.85 * 1.85;
            double q10 = AtmosphericDrag.DynamicPressure(air.Density(10000.0), 500.0);
            string source = exponential ? "атмосфера «" + c.PlanetName + "»" : "земные значения (у «" + c.PlanetName + "» " + (c.HasAtmosphere ? "табличная атмосфера" : "нет атмосферы") + ")";
            var t = new AcademyTopic
            {
                Id = Drag,
                Title = "Сопротивление атмосферы",
                Explanation =
                    "Плотность воздуха падает с высотой экспоненциально: каждые H километров (шкала высот) — в e ≈ 2.72 раза. " +
                    "Сила сопротивления пропорциональна плотности и КВАДРАТУ скорости, поэтому быстро лететь низко — дорого.\n\n" +
                    "Скоростной напор q = ½ρv² сначала растёт (ракета разгоняется), потом падает (воздух редеет) — максимум называют Max Q. " +
                    "Поэтому ракеты сначала поднимаются почти вертикально и набирают горизонтальную скорость уже в разреженном воздухе.",
                Example =
                    "Источник: " + source + ": ρ₀ = " + N(rho0) + " кг/м³, H = " + N(scaleHeight / Km) + " км.\n" +
                    "ρ(10 км) = " + N(air.Density(10000.0)) + ", ρ(30 км) = " + N(air.Density(30000.0)) + ", ρ(" + N(c.SpaceBoundary / Km) + " км) = " + N(air.Density(c.SpaceBoundary)) + " кг/м³.\n" +
                    "На 10 км при 500 м/с: q = " + N(q10 / 1000.0) + " кПа, сила на ракету (C_d = " + N(cd) + ", A = " + N(area) + " м²) — " + N(q10 * cd * area / KiloNewton) + " кН.",
                Application =
                    "Телеметрия: «Плотность ρ», «Скоростной напор q», «Сопротивление a_drag»; панель РАКЕТА — «Потери: сопрот.». " +
                    "Воздух вращается вместе с планетой, поэтому сопротивление считается от скорости относительно воздуха.",
                Limitations = "Изотермическая модель, постоянный C_d (на сверхзвуке он выше), без ветра и нагрева. На границе космоса плотность мала, но не ноль."
            };
            t.Formulas.Add(new AcademyFormula("ρ(h) = ρ₀·e^(−h/H)", V("ρ", "плотность воздуха", "кг/м³"), V("ρ₀", "плотность у поверхности", "кг/м³"), V("h", "высота", "м"), V("H", "шкала высот", "м")));
            t.Formulas.Add(new AcademyFormula("q = ½·ρ·v²", V("q", "скоростной напор", "Па"), V("v", "скорость относительно воздуха", "м/с")));
            t.Formulas.Add(new AcademyFormula("F_d = q·C_d·A", V("F_d", "сила сопротивления", "Н"), V("C_d", "коэффициент сопротивления", "безразмерно"), V("A", "площадь поперечного сечения", "м²")));
            t.Formulas.Add(new AcademyFormula("v_пред = √(2·m·g / (ρ·C_d·A))", V("v_пред", "предельная скорость падения: сопротивление = вес", "м/с")));
            t.CalculatorIds.Add(AcademyCalculators.Drag);

            double rx = S3(rho0), hx = S3(scaleHeight / Km);
            var exAir = new ExponentialAtmosphere(rx, hx * Km, 0.0, 0.0);
            double rho = exAir.Density(10000.0);
            double q = AtmosphericDrag.DynamicPressure(rho, 600.0);
            t.Exercise = Exercise("ex-drag",
                "Ракета летит на высоте 10 км со скоростью 600 м/с. ρ₀ = " + N(rx) + " кг/м³, H = " + N(hx) + " км. Каков скоростной напор q? Ответ в кПа.",
                q / 1000.0, "кПа",
                new[] { "Сначала найдите плотность на высоте 10 км: ρ = ρ₀·e^(−h/H), h и H — в одних единицах.", "q = ½·ρ·v².", "Паскали → килопаскали: делите на 1000." },
                new[] { "ρ = " + N(rx) + " · e^(−10/" + N(hx) + ") = " + N(rho) + " кг/м³", "q = 0.5 · " + N(rho) + " · 600² = " + N(q) + " Па", "q = " + N(q / 1000.0) + " кПа" });
            return t;
        }

        // ---- 10 --------------------------------------------------------------------------------------------------

        static AcademyTopic InsertionTopic(AcademyContext c)
        {
            double apo = c.LowOrbitAltitude;
            double ra = c.Radius + apo;
            double dv0 = OrbitalMechanics.CircularizationDeltaV(c.Mu, ra, c.Radius);
            double dvDeep = OrbitalMechanics.CircularizationDeltaV(c.Mu, ra, c.Radius * 0.5);
            var t = new AcademyTopic
            {
                Id = Insertion,
                Title = "Выход на орбиту",
                Explanation =
                    "Пока двигатель выключен, аппарат летит по эллипсу (или его куску). Если перицентр этого эллипса под поверхностью или в атмосфере — " +
                    "это ещё не орбита, аппарат упадёт. Выход на орбиту — разгон В АПОЦЕНТРЕ по направлению движения: он поднимает противоположную точку — перицентр.\n\n" +
                    "Чем более пологой (горизонтальной) была траектория подъёма, тем выше перицентр и тем дешевле этот последний манёвр. " +
                    "Поэтому ракету постепенно наклоняют на восток — «гравитационный разворот».",
                Example =
                    "Апоцентр " + N(apo / Km) + " км над «" + c.PlanetName + "», перицентр на уровне поверхности: Δv = " + N(dv0) + " м/с в апоцентре.\n" +
                    "Если подъём был почти вертикальным (перицентр на половине радиуса от центра) — уже " + N(dvDeep) + " м/с.",
                Application =
                    "Без НК-1 апоцентр и перицентр вычисляйте сами по r, v и v_r (темы 7 и 8); с НК-1 нажмите O. У апоцентра v_r ≈ 0. " +
                    "Разгон — тангаж 90° (горизонтально), курс 90° (на восток).",
                Limitations =
                    "Импульсный манёвр. Реальный двигатель работает конечное время — начинайте разгон чуть раньше апоцентра. Сопротивление при низком перицентре не учтено."
            };
            t.Formulas.Add(new AcademyFormula("a = (r_a + r_p) / 2", V("a", "большая полуось эллипса", "м"), V("r_a, r_p", "радиусы апоцентра и перицентра от центра", "м")));
            t.Formulas.Add(new AcademyFormula("v(r) = √(μ·(2/r − 1/a))", V("v(r)", "скорость на эллипсе на расстоянии r (формула vis-viva)", "м/с")));
            t.Formulas.Add(new AcademyFormula("Δv = √(μ/r_a) − √(μ·(2/r_a − 1/a))", V("Δv", "разгон в апоцентре до круговой скорости", "м/с")));
            t.CalculatorIds.Add(AcademyCalculators.Insertion);

            double hax = S3(apo / Km), hpx = S3(c.SpaceBoundary / Km * 0.3);
            double rax = c.Radius + hax * Km, rpx = c.Radius + hpx * Km;
            double a = 0.5 * (rax + rpx);
            double va = OrbitalMechanics.VisVivaSpeed(c.Mu, rax, a);
            double vc = OrbitalMechanics.CircularVelocity(c.Mu, rax);
            double answer = OrbitalMechanics.CircularizationDeltaV(c.Mu, rax, rpx);
            t.Exercise = Exercise("ex-insertion",
                "Аппарат над «" + c.PlanetName + "» летит по траектории с апоцентром " + N(hax) + " км и перицентром " + N(hpx) + " км (высоты над R = " + N(c.Radius / Km) + " км, μ = " + N(c.Mu) + "). " +
                "Какой разгон нужен в апоцентре, чтобы орбита стала круговой? Ответ в м/с.",
                answer, "м/с",
                new[] { "Переведите высоты в радиусы от центра и найдите полуось a = (r_a + r_p)/2.", "Скорость в апоцентре — по формуле vis-viva.", "Нужный разгон — разница круговой скорости на r_a и скорости в апоцентре." },
                new[] { "r_a = " + N(rax) + " м, r_p = " + N(rpx) + " м, a = " + N(a) + " м", "v_a = √(μ(2/r_a − 1/a)) = " + N(va) + " м/с", "v_круг = √(μ/r_a) = " + N(vc) + " м/с", "Δv = " + N(answer) + " м/с" });
            return t;
        }

        // ---- 11 --------------------------------------------------------------------------------------------------

        static AcademyTopic HohmannTopic(AcademyContext c)
        {
            double low = c.LowOrbitAltitude;
            HohmannTransfer local = TransferCalculator.Hohmann(c.Mu, c.Radius + low, c.Radius + 2.0 * low);
            string helio = "";
            AcademyBody origin = c.Find(c.PlanetId);
            if (origin != null && origin.OrbitsStar && c.StarMu > 0.0)
                foreach (var target in c.Planets())
                {
                    if (target.Id == origin.Id) continue;
                    HohmannTransfer h = TransferCalculator.Hohmann(c.StarMu, origin.OrbitRadius, target.OrbitRadius);
                    helio = "Вокруг звезды: " + origin.Name + " → " + target.Name + ": Δv₁ = " + N(h.DeltaV1) + " м/с, Δv₂ = " + N(h.DeltaV2) + " м/с, перелёт " + N(h.TransferTime / Day) + " сут.";
                    break;
                }
            var t = new AcademyTopic
            {
                Id = Hohmann,
                Title = "Гомановский переход",
                Explanation =
                    "Самый экономный способ перейти между двумя круговыми орбитами в одной плоскости — по половине эллипса, касающегося обеих: " +
                    "первый импульс на r₁ вытягивает орбиту до r₂, второй на r₂ скругляет её.\n\n" +
                    "Переход медленный: ровно полпериода переходного эллипса. Тот же принцип работает и вокруг планеты, и вокруг звезды — меняется только μ.",
                Example =
                    "Над «" + c.PlanetName + "»: " + N(low / Km) + " → " + N(2.0 * low / Km) + " км: Δv₁ = " + N(local.DeltaV1) + ", Δv₂ = " + N(local.DeltaV2) + ", всего " + N(local.TotalDeltaV) +
                    " м/с за " + N(local.TransferTime / Minute) + " мин.\n" + helio,
                Application = "Подъём и смена орбиты; основа межпланетных перелётов (навигация, M5) — она будет использовать эти же функции.",
                Limitations =
                    "Круговые компланарные орбиты и мгновенные импульсы. При r₂/r₁ > 11.94 выгоднее трёхимпульсный (биэллиптический) переход."
            };
            t.Formulas.Add(new AcademyFormula("a = (r₁ + r₂) / 2", V("r₁, r₂", "радиусы начальной и конечной орбит от центра", "м")));
            t.Formulas.Add(new AcademyFormula("Δv₁ = √(μ(2/r₁ − 1/a)) − √(μ/r₁)", V("Δv₁", "первый импульс (на r₁)", "м/с")));
            t.Formulas.Add(new AcademyFormula("Δv₂ = √(μ/r₂) − √(μ(2/r₂ − 1/a))", V("Δv₂", "второй импульс (на r₂)", "м/с")));
            t.Formulas.Add(new AcademyFormula("t = π·√(a³ / μ)", V("t", "время перехода", "с")));
            t.CalculatorIds.Add(AcademyCalculators.Hohmann);
            t.CalculatorIds.Add(AcademyCalculators.Transfer);

            double r1 = c.Radius + 200000.0, r2 = c.Radius + 2000000.0;
            HohmannTransfer ex = TransferCalculator.Hohmann(c.Mu, r1, r2);
            t.Exercise = Exercise("ex-hohmann",
                "Перелёт с круговой орбиты 200 км на круговую 2000 км над «" + c.PlanetName + "» (R = " + N(c.Radius / Km) + " км, μ = " + N(c.Mu) + "). Каков суммарный Δv двух импульсов? Ответ в м/с.",
                ex.TotalDeltaV, "м/с",
                new[] { "Начните с полуоси переходного эллипса — радиусы считаются от центра.", "Первый импульс: скорость на эллипсе в перицентре минус круговая на r₁.", "Второй: круговая на r₂ минус скорость эллипса в апоцентре. Сложите." },
                new[] { "r₁ = " + N(r1) + " м, r₂ = " + N(r2) + " м, a = " + N(ex.SemiMajorAxis) + " м", "Δv₁ = " + N(ex.DepartureSpeed) + " − " + N(OrbitalMechanics.CircularVelocity(c.Mu, r1)) + " = " + N(ex.DeltaV1) + " м/с",
                        "Δv₂ = " + N(OrbitalMechanics.CircularVelocity(c.Mu, r2)) + " − " + N(ex.ArrivalSpeed) + " = " + N(ex.DeltaV2) + " м/с", "Δv = " + N(ex.TotalDeltaV) + " м/с" });
            return t;
        }

        // ---- 12 --------------------------------------------------------------------------------------------------

        static AcademyTopic LaunchWindowsTopic(AcademyContext c)
        {
            AcademyBody origin = c.Find(c.PlanetId);
            if (origin == null || !origin.OrbitsStar) origin = c.Find(SpaceSim.Planets.HomeSystemCatalog.HomeWorldId);
            string table = "";
            AcademyBody exTarget = null;
            if (origin != null && c.StarMu > 0.0)
                foreach (var target in c.Planets())
                {
                    if (target.Id == origin.Id) continue;
                    if (exTarget == null) exTarget = target;
                    double phase = TransferCalculator.HohmannPhaseAngle(c.StarMu, origin.OrbitRadius, target.OrbitRadius);
                    double synodic = TransferCalculator.SynodicPeriod(Period(c, origin), Period(c, target));
                    double time = TransferCalculator.Hohmann(c.StarMu, origin.OrbitRadius, target.OrbitRadius).TransferTime;
                    table += "• → " + target.Name + ": θ = " + N(phase * 180.0 / Math.PI) + "°, перелёт " + N(time / Day) + " сут, окно каждые " + N(synodic / Day) + " сут\n";
                }
            var t = new AcademyTopic
            {
                Id = LaunchWindows,
                Title = "Окна запуска",
                Explanation =
                    "Цель не стоит на месте: пока аппарат летит по переходному эллипсу, планета-цель проходит часть своей орбиты. " +
                    "Улетать нужно, когда цель опережает (или отстаёт) на фазовый угол θ — тогда к концу перелёта вы окажетесь в одной точке.\n\n" +
                    "Такая расстановка планет повторяется раз в синодический период — это и есть окна запуска. Пропустили окно — ждите следующего " +
                    "или платите за перелёт гораздо большим Δv.",
                Example = origin != null ? "Перелёты с «" + origin.Name + "» по Гоману:\n" + table : "Нет данных о планетах системы.",
                Application = "Навигация (M5) будет проверять окно запуска по положениям планет. Уже сейчас оцените окна калькулятором «Оценка межпланетного перелёта».",
                Limitations =
                    "Круговые компланарные орбиты: реальные эллиптичность и наклоны орбит сдвигают окна и меняют Δv. Отлёт и захват — по сопряжённым коническим сечениям."
            };
            t.Formulas.Add(new AcademyFormula("θ = π − n₂·t_перелёта,  n₂ = √(μ★ / r₂³)", V("θ", "фазовый угол цели при отлёте", "рад"), V("n₂", "угловая скорость цели", "рад/с"), V("μ★", "гравитационный параметр звезды", "м³/с²")));
            t.Formulas.Add(new AcademyFormula("T_син = 1 / |1/T₁ − 1/T₂|", V("T_син", "синодический период — интервал между окнами", "с"), V("T₁, T₂", "периоды обращения планет вокруг звезды", "с")));
            t.Formulas.Add(new AcademyFormula("Δv_отл = √(v∞² + 2μ/r) − √(μ/r)", V("v∞", "нужный избыток скорости относительно планеты", "м/с"), V("r", "радиус орбиты ожидания", "м")));
            t.CalculatorIds.Add(AcademyCalculators.Transfer);

            if (origin != null && exTarget != null)
            {
                double t1 = S3(Period(c, origin) / Day), t2 = S3(Period(c, exTarget) / Day);
                double syn = TransferCalculator.SynodicPeriod(t1, t2);
                t.Exercise = Exercise("ex-window",
                    "Период обращения «" + origin.Name + "» вокруг звезды — " + N(t1) + " сут, «" + exTarget.Name + "» — " + N(t2) + " сут. Как часто повторяются окна запуска между ними? Ответ в сутках.",
                    syn, "сут",
                    new[] { "Важна разность угловых скоростей: за сутки одна планета «обгоняет» другую на (1/T₁ − 1/T₂) оборота.", "Окно повторяется, когда разница набегает на целый оборот." },
                    new[] { "1/T₁ − 1/T₂ = " + N(1.0 / t1) + " − " + N(1.0 / t2) + " = " + N(1.0 / t1 - 1.0 / t2) + " об/сут", "T_син = 1/|…| = " + N(syn) + " сут" });
            }
            return t;
        }

        static double Period(AcademyContext c, AcademyBody b) => b.OrbitPeriod > 0.0 ? b.OrbitPeriod : OrbitalMechanics.CircularPeriod(c.StarMu, b.OrbitRadius);

        // ---- 13 --------------------------------------------------------------------------------------------------

        static AcademyTopic RendezvousTopic(AcademyContext c)
        {
            double r1 = c.Radius + 200000.0, r2 = c.Radius + 400000.0;
            HohmannTransfer h = TransferCalculator.Hohmann(c.Mu, r1, r2);
            double phase = TransferCalculator.HohmannPhaseAngle(c.Mu, r1, r2);
            double synodic = TransferCalculator.SynodicPeriod(OrbitalMechanics.CircularPeriod(c.Mu, r1), OrbitalMechanics.CircularPeriod(c.Mu, r2));
            var t = new AcademyTopic
            {
                Id = Rendezvous,
                Title = "Орбитальная встреча",
                Explanation =
                    "Встреча (стыковка) — та же задача окна запуска, только вокруг планеты: нужно прибыть на орбиту цели туда, где в этот момент окажется цель.\n\n" +
                    "Парадокс орбитальной механики: чтобы догнать цель впереди на той же орбите, нужно ПРИТОРМОЗИТЬ — более низкая орбита короче и быстрее по угловой скорости. " +
                    "Чтобы пропустить цель вперёд — разогнаться и подняться.",
                Example =
                    "Вы на 200 км, станция на 400 км над «" + c.PlanetName + "»: переход " + N(h.TransferTime / Minute) + " мин, Δv = " + N(h.TotalDeltaV) + " м/с. " +
                    "Станция должна опережать вас на " + N(phase * 180.0 / Math.PI) + "°; такая расстановка повторяется каждые " + N(synodic / 3600.0) + " ч.",
                Application = "Пригодится для встреч на орбите в следующих этапах. Сейчас — тренируйте расчёт фазового угла и ожидания калькулятором «Гомановский переход».",
                Limitations = "Круговые компланарные орбиты. Сближение на последних сотнях метров описывается уравнениями относительного движения — их здесь нет."
            };
            t.Formulas.Add(new AcademyFormula("θ = π − n_ц·t_перехода,  n_ц = √(μ / r_ц³)", V("θ", "на сколько цель должна опережать вас", "рад"), V("n_ц", "угловая скорость цели", "рад/с"), V("r_ц", "радиус орбиты цели", "м")));
            t.Formulas.Add(new AcademyFormula("t_ожид = Δθ / |n₁ − n₂|", V("t_ожид", "сколько ждать нужного угла", "с"), V("Δθ", "сколько угла осталось набрать", "рад"), V("n₁, n₂", "угловые скорости орбит", "рад/с")));
            t.CalculatorIds.Add(AcademyCalculators.Hohmann);

            t.Exercise = Exercise("ex-rendezvous",
                "Вы на круговой орбите 200 км над «" + c.PlanetName + "», станция — на 400 км (R = " + N(c.Radius / Km) + " км, μ = " + N(c.Mu) + "). На какой угол станция должна опережать вас в момент первого импульса? Ответ в градусах.",
                phase * 180.0 / Math.PI, "°",
                new[] { "Найдите время гомановского перехода t = π√(a³/μ).", "За это время станция пройдёт угол n₂·t, где n₂ = √(μ/r₂³) — в радианах.", "Вы за переход проходите ровно 180°: θ = 180° − n₂·t." },
                new[] { "a = " + N(h.SemiMajorAxis) + " м, t = " + N(h.TransferTime) + " с", "n₂ = " + UnitFormatter.Scientific(Math.Sqrt(c.Mu / (r2 * r2 * r2)), 4) + " рад/с, n₂·t = " + N(Math.Sqrt(c.Mu / (r2 * r2 * r2)) * h.TransferTime * 180.0 / Math.PI) + "°",
                        "θ = 180° − " + N(Math.Sqrt(c.Mu / (r2 * r2 * r2)) * h.TransferTime * 180.0 / Math.PI) + "° = " + N(phase * 180.0 / Math.PI) + "°" });
            return t;
        }
    }
}
