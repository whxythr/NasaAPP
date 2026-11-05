using System;
using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Units;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Hangar window (Milestone 2): the player assembles a rocket from catalog modules. It edits a copy of the current
    /// design; every change is recompiled by <see cref="BlueprintCompiler"/> and analysed by <see cref="RocketDesignAnalysis"/>
    /// (Level A, ideal). This class only formats those results and adds simple sums — no physics formulas here.
    /// Unusual designs are never refused: construction issues and design warnings explain the consequences instead.
    /// </summary>
    /// <remarks>
    /// IMGUI rule followed here: anything that changes the number of controls (adding/removing stages, tanks, modules,
    /// switching tabs) is queued with <see cref="Defer"/> and applied after the whole pass, so the Layout pass and the
    /// following event pass always see the same structure. Recompilation also happens only between passes.
    /// </remarks>
    [DefaultExecutionOrder(215)]
    public sealed class HangarUI : MonoBehaviour
    {
        const float PreferredWidth = 1100f;
        const float PreferredHeight = 680f;
        const float ColumnGap = 8f;
        /// <summary>Tank slider snaps to this fraction of the capacity (0.5 %).</summary>
        const double SliderStep = 0.005;

        static readonly ModuleCategory[] Categories =
        {
            ModuleCategory.Engine, ModuleCategory.FuelTank, ModuleCategory.Navigation,
            ModuleCategory.Protection, ModuleCategory.Science, ModuleCategory.SampleCollector
        };
        static readonly string[] CategoryNames = { "Двигатели", "Топливные баки", "Навигация", "Защита", "Наука", "Пробоотборники" };

        FlightBody body;
        FlightStateController state;
        bool open;

        RocketBlueprint design;
        CompiledRocket compiled;
        RocketDesignReport report;
        bool dirty;
        bool modified;

        MissionTarget[] targets;
        object targetsBuiltFor;
        /// <summary>Index into <see cref="targets"/> used as the required Δv. StandardTargets order: hop, orbit, escape.</summary>
        int requiredTarget = 1;

        ModuleCategory category = ModuleCategory.Engine;
        List<ModuleDefinition> categoryModules;
        string selectedModule = "";
        int selectedStage;
        string status = "";
        Color statusColor = Color.white;

        Vector2 catalogScroll, designScroll, statsScroll, warningsScroll;
        float leftWidth, middleWidth, rightWidth;
        readonly List<Action> pending = new List<Action>();
        GUIStyle note, compact;

        public bool IsOpen => open;

        public void Initialize(FlightBody flightBody, FlightStateController flightState)
        {
            body = flightBody;
            state = flightState;
        }

        /// <summary>Opens the hangar with a fresh copy of the current design, or closes it (changes not applied are dropped).</summary>
        public void Toggle()
        {
            if (open)
            {
                Close();
                return;
            }
            if (body == null) return;
            design = (body.Blueprint ?? RocketBlueprint.CreateDefault()).Clone();
            compiled = null;
            report = null;
            selectedStage = 0;
            modified = false;
            status = "";
            pending.Clear();
            if (categoryModules == null || string.IsNullOrEmpty(selectedModule)) SelectCategory(category);
            open = true;
            dirty = true;
            Refresh();
        }

        void Close()
        {
            open = false;
            pending.Clear();
            GUIUtility.keyboardControl = 0;
        }

        // ---- Model ---------------------------------------------------------------------------

        /// <summary>Recompiles the edited design (if it changed) and re-runs the Level A analysis.</summary>
        void Refresh()
        {
            if (body == null || design == null) return;
            PlanetRuntime planet = body.Planet;
            if (planet == null || planet.Model == null) return;
            var model = planet.Model;
            if (targets == null || !ReferenceEquals(targetsBuiltFor, model))
            {
                targets = BuildTargets(planet);
                targetsBuiltFor = model;
                dirty = true;
            }
            if (!dirty && compiled != null && report != null) return;
            dirty = false;

            if (requiredTarget >= targets.Length) requiredTarget = targets.Length - 1;
            if (requiredTarget < 0) requiredTarget = 0;
            double required = targets.Length > 0 ? targets[requiredTarget].IdealDeltaV : 0.0;

            compiled = BlueprintCompiler.Compile(design, body.OwnsModule);
            report = RocketDesignAnalysis.Analyze(compiled.Configuration, model.Properties.SurfaceGravity, required);

            if (selectedStage >= design.stages.Count) selectedStage = design.stages.Count - 1;
            if (selectedStage < 0) selectedStage = 0;
        }

        static MissionTarget[] BuildTargets(PlanetRuntime planet)
        {
            var model = planet.Model;
            // Ground speed at the pad comes from the planet model (ω × r), not from a formula in the UI.
            Vector3d pad = planet.FixedToSimulation(planet.LaunchSiteDirection() * model.Radius, 0.0);
            double groundSpeed = model.SurfaceVelocityAt(pad).Magnitude;
            bool hasAtmosphere = model.Atmosphere != null && model.Atmosphere.ReferenceDensity > 0.0;
            return MissionRequirements.StandardTargets(model.GravitationalParameter, model.Radius, groundSpeed,
                model.SpaceBoundaryAltitude, hasAtmosphere);
        }

        /// <summary>Shop modules stay locked until the player owns them (profile ownership via FlightBody).</summary>
        bool IsLocked(ModuleDefinition m) => body != null && !body.OwnsModule(m.id);

        List<ModuleDefinition> Modules()
        {
            if (categoryModules == null) categoryModules = ModuleCatalog.InCategory(category);
            return categoryModules;
        }

        void SelectCategory(ModuleCategory c)
        {
            category = c;
            categoryModules = ModuleCatalog.InCategory(c);
            selectedModule = categoryModules.Count > 0 ? categoryModules[0].id : "";
            catalogScroll = Vector2.zero;
        }

        // ---- Edits (always applied between IMGUI passes) -------------------------------------

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
            if (open) Refresh();
        }

        void MarkChanged()
        {
            dirty = true;
            modified = true;
            status = "";
        }

        bool ValidStage(int index) => index >= 0 && index < design.stages.Count;

        void SetEngine(int stage, string engineId)
        {
            if (!ValidStage(stage)) return;
            StageBlueprint s = design.stages[stage];
            s.engineId = engineId;
            s.engineCount = Math.Max(1, Math.Min(BlueprintCompiler.MaxEnginesPerStage, s.engineCount));
            MarkChanged();
        }

        void ChangeEngineCount(int stage, int delta)
        {
            if (!ValidStage(stage)) return;
            StageBlueprint s = design.stages[stage];
            s.engineCount = Math.Max(1, Math.Min(BlueprintCompiler.MaxEnginesPerStage, s.engineCount + delta));
            MarkChanged();
        }

        void RemoveEngine(int stage)
        {
            if (!ValidStage(stage)) return;
            design.stages[stage].engineId = "";
            MarkChanged();
        }

        void AddTank(int stage, string tankId)
        {
            ModuleDefinition tank = ModuleCatalog.Find(tankId);
            if (!ValidStage(stage) || tank == null) return;
            design.stages[stage].tanks.Add(new TankLoad { moduleId = tankId, propellantLoaded = tank.propellantCapacity });
            MarkChanged();
        }

        void RemoveTank(int stage, int tank)
        {
            if (!ValidStage(stage)) return;
            List<TankLoad> tanks = design.stages[stage].tanks;
            if (tank < 0 || tank >= tanks.Count) return;
            tanks.RemoveAt(tank);
            MarkChanged();
        }

        void AddPayload(string moduleId)
        {
            ModuleDefinition m = ModuleCatalog.Find(moduleId);
            if (m == null || !m.IsPayload) return;
            if (m.mandatory && design.payloadModules.Contains(moduleId)) return;
            design.payloadModules.Add(moduleId);
            MarkChanged();
        }

        void RemovePayload(int index)
        {
            if (index < 0 || index >= design.payloadModules.Count) return;
            ModuleDefinition m = ModuleCatalog.Find(design.payloadModules[index]);
            if (m != null && m.mandatory) return;
            design.payloadModules.RemoveAt(index);
            MarkChanged();
        }

        void AddStageOnTop()
        {
            design.stages.Add(new StageBlueprint { name = "Ступень " + (design.stages.Count + 1), engineId = "", engineCount = 1 });
            selectedStage = design.stages.Count - 1;
            RenumberStages();
            MarkChanged();
        }

        void RemoveStage(int stage)
        {
            if (!ValidStage(stage)) return;
            design.stages.RemoveAt(stage);
            if (selectedStage >= design.stages.Count) selectedStage = design.stages.Count - 1;
            if (selectedStage < 0) selectedStage = 0;
            RenumberStages();
            MarkChanged();
        }

        /// <summary>Keeps default stage names in flight order ("Ступень 1" burns first) after stages are added or removed.</summary>
        void RenumberStages()
        {
            for (int i = 0; i < design.stages.Count; i++)
            {
                StageBlueprint s = design.stages[i];
                if (string.IsNullOrEmpty(s.name) || s.name.StartsWith("Ступень", StringComparison.Ordinal))
                    s.name = "Ступень " + (i + 1);
            }
        }

        void ResetToDefault()
        {
            design = RocketBlueprint.CreateDefault();
            selectedStage = 0;
            MarkChanged();
            SetStatus("Проект сброшен к стандартной ракете — нажмите «Применить», чтобы поставить её на площадку.", HudStyle.Accent);
        }

        void Apply()
        {
            Refresh();
            if (!body.IsOnLaunchPad)
            {
                SetStatus("Перестройка возможна только на стартовой площадке.", HudStyle.Warning);
                return;
            }
            if (!body.ApplyBlueprint(design))
            {
                SetStatus("Не удалось применить проект: ракета не на стартовой площадке.", HudStyle.Danger);
                return;
            }
            if (state != null && report != null)
            {
                int remarks = compiled.Issues.Count + report.Warnings.Count;
                state.Notify("Ангар: на площадке новая ракета «" + design.name + "» — ступеней: " + design.stages.Count +
                             ", стартовая масса " + Mass(report.LaunchMass) + ", идеальный Δv " + Speed(report.TotalDeltaVVacuum) +
                             (remarks > 0 ? ". Замечаний: " + remarks + " (см. ангар)." : "."));
            }
            Close();
        }

        void SetStatus(string text, Color color)
        {
            status = text;
            statusColor = color;
        }

        // ---- Drawing -------------------------------------------------------------------------

        void OnGUI()
        {
            if (!open || body == null || design == null || FlightInput.PauseMenuOpen) return;
            Event e = Event.current;
            if (compiled == null || report == null || (e != null && e.type == EventType.Layout)) Refresh();
            if (compiled == null || report == null) return;

            GUI.depth = -10; // in front of the HUD: drawn on top and gets input first
            HudStyle.Begin();
            EnsureStyles();

            Vector2 screen = HudStyle.VirtualScreen;
            float w = Mathf.Min(PreferredWidth, screen.x - 20f);
            float h = Mathf.Min(PreferredHeight, screen.y - 20f);
            var r = new Rect((screen.x - w) * 0.5f, (screen.y - h) * 0.5f, w, h);
            HudStyle.WindowFrame(r);

            float innerW = w - 20f, innerH = h - 16f;
            leftWidth = innerW * 0.30f;
            middleWidth = innerW * 0.38f;
            rightWidth = innerW - leftWidth - middleWidth - 2f * ColumnGap;
            float columnHeight = Mathf.Max(200f, innerH - 262f);

            GUILayout.BeginArea(new Rect(r.x + 10f, r.y + 8f, innerW, innerH));
            DrawHeader();
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(leftWidth));
            DrawCatalog(columnHeight);
            GUILayout.EndVertical();
            GUILayout.Space(ColumnGap);
            GUILayout.BeginVertical(GUILayout.Width(middleWidth));
            DrawDesign(columnHeight);
            GUILayout.EndVertical();
            GUILayout.Space(ColumnGap);
            GUILayout.BeginVertical(GUILayout.Width(rightWidth));
            DrawStatistics(columnHeight);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            DrawWarnings();
            GUILayout.FlexibleSpace();
            DrawButtons();
            GUILayout.EndArea();

            SwallowMouse(r);
            ApplyPending();
        }

        void EnsureStyles()
        {
            if (note != null && compact != null) return;
            // White base colour so GUI.color tints give exactly HudStyle.Accent / Warning / Danger.
            note = new GUIStyle(HudStyle.Small) { fontSize = 12, wordWrap = true };
            note.normal.textColor = Color.white;
            compact = new GUIStyle(HudStyle.Small) { wordWrap = false };
        }

        /// <summary>Clicks and scrolling inside the window must not reach HUD controls underneath.</summary>
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
            PlanetRuntime planet = body.Planet;
            string where = planet != null && planet.Definition != null ? " · " + planet.Definition.displayName : "";
            GUILayout.BeginHorizontal();
            GUILayout.Label("АНГАР · сборка ракеты" + where, HudStyle.WindowTitle);
            GUILayout.FlexibleSpace();
            if (modified) Colored("Есть неприменённые изменения", compact, HudStyle.Warning, GUILayout.Width(240));
            else GUILayout.Label("Изменений нет", compact, GUILayout.Width(240));
            GUILayout.EndHorizontal();
            GUILayout.Label("Все цифры здесь — идеально (уровень A): уравнение Циолковского без потерь на гравитацию, сопротивление воздуха " +
                            "и управление. В настоящем полёте (уровень B) Δv понадобится больше.", HudStyle.Small);
        }

        // ---- Left column: catalog ------------------------------------------------------------

        void DrawCatalog(float height)
        {
            GUILayout.Label("Каталог модулей", HudStyle.Title);
            float cell = (leftWidth - 8f) * 0.5f;
            for (int row = 0; row < 3; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < 2; col++)
                {
                    int index = row * 2 + col;
                    ModuleCategory c = Categories[index];
                    bool on = category == c;
                    if (GUILayout.Toggle(on, CategoryNames[index], HudStyle.Button, GUILayout.Width(cell)) && !on)
                        Defer(() => SelectCategory(c));
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(4);

            catalogScroll = GUILayout.BeginScrollView(catalogScroll, GUILayout.Height(height - 100f));
            foreach (ModuleDefinition m in Modules())
            {
                bool selected = m.id == selectedModule;
                bool locked = IsLocked(m);
                string id = m.id;
                Color c = GUI.color;
                if (locked) GUI.color = new Color(0.7f, 0.7f, 0.7f);
                if (GUILayout.Toggle(selected, m.name + (locked ? "  · магазин" : ""), HudStyle.Button) && !selected)
                    Defer(() => selectedModule = id);
                GUI.color = c;
            }
            GUILayout.Space(6);
            ModuleDefinition sel = ModuleCatalog.Find(selectedModule);
            if (sel == null || sel.category != category) GUILayout.Label("Выберите модуль в списке.", HudStyle.Small);
            else DrawSpecs(sel);
            GUILayout.EndScrollView();
        }

        void DrawSpecs(ModuleDefinition m)
        {
            const float lw = 150f;
            GUILayout.BeginVertical(HudStyle.Panel);
            GUILayout.Label(m.name, HudStyle.Title);
            if (IsLocked(m))
                Colored("Заблокировано — доступно в магазине (" + Price(m.price) + "). Купите его в магазине (кнопка МАГАЗИН / K).",
                    note, HudStyle.Warning);
            HudStyle.Row("Масса (сухая)", Mass(m.dryMass), lw);
            HudStyle.Row("Цена", Price(m.price), lw);
            HudStyle.Row("Длина", N(m.length, 1) + " м", lw);

            switch (m.category)
            {
                case ModuleCategory.Engine:
                    HudStyle.Row("Тяга в вакууме", Force(m.vacuumThrust), lw);
                    HudStyle.Row("Isp в вакууме", Isp(m.vacuumIsp), lw);
                    HudStyle.Row("Isp у поверхности", Isp(m.seaLevelIsp), lw);
                    HudStyle.Row("Топливо", BlueprintCompiler.Name(m.propellant), lw);
                    break;
                case ModuleCategory.FuelTank:
                    HudStyle.Row("Ёмкость", Mass(m.propellantCapacity), lw);
                    HudStyle.Row("Топливо", BlueprintCompiler.Name(m.propellant), lw);
                    HudStyle.Row("Масса с топливом", Mass(m.dryMass + m.propellantCapacity), lw);
                    break;
                default:
                    HudStyle.Row("Возможности", m.capabilities == ModuleCapability.None ? "—" : "есть", lw);
                    if (m.capabilities != ModuleCapability.None) GUILayout.Label(CapabilityList(m.capabilities), note);
                    HudStyle.Row("Радиационная защита", Level(m.radiationShielding), lw);
                    HudStyle.Row("Кислотостойкость", Level(m.acidResistance), lw);
                    HudStyle.Row("Образцы, до", m.sampleCapacity > 0.0 ? Mass(m.sampleCapacity) : "—", lw);
                    if (m.sampleKinds != SampleKinds.None) GUILayout.Label("Виды образцов: " + KindList(m.sampleKinds), note);
                    if (m.researchMultiplier != 1.0) HudStyle.Row("Научная ценность", "×" + N(m.researchMultiplier, 1), lw);
                    if (m.mandatory) Colored("Обязательный модуль: всегда в отсеке, убрать нельзя.", note, HudStyle.Accent);
                    int count = CountInPayload(m.id);
                    if (count > 0) GUILayout.Label("Сейчас в отсеке: " + count + " шт.", note);
                    break;
            }

            GUILayout.Space(4);
            GUILayout.Label(m.description, HudStyle.Small);
            GUILayout.Space(6);
            DrawAddButton(m);
            GUILayout.EndVertical();
        }

        void DrawAddButton(ModuleDefinition m)
        {
            string id = m.id;
            int stageCount = design.stages.Count;
            int stage = Math.Min(selectedStage, stageCount - 1);
            if (IsLocked(m))
            {
                Button("Доступно в магазине", false);
                return;
            }
            if (m.IsEngine || m.IsTank)
            {
                if (stageCount == 0)
                {
                    Button("Сначала добавьте ступень", false);
                    return;
                }
                if (m.IsEngine)
                {
                    if (Button("Поставить на ступень " + (stage + 1), true)) Defer(() => SetEngine(stage, id));
                    GUILayout.Label("Заменяет двигатель выбранной ступени (сейчас: " + EngineSummary(design.stages[stage]) +
                                    "). Ступень выбирается в столбце «Проект».", HudStyle.Small);
                }
                else
                {
                    if (Button("Добавить в ступень " + (stage + 1), true)) Defer(() => AddTank(stage, id));
                    GUILayout.Label("Бак ставится заправленным полностью; заправку можно уменьшить ползунком в проекте. " +
                                    "Ступень выбирается в столбце «Проект».", HudStyle.Small);
                }
                return;
            }
            bool already = m.mandatory && design.payloadModules.Contains(id);
            if (Button(already ? "Уже в отсеке (обязательный)" : "Добавить в отсек", !already)) Defer(() => AddPayload(id));
        }

        // ---- Middle column: the design -------------------------------------------------------

        void DrawDesign(float height)
        {
            GUILayout.Label("Проект: " + design.name, HudStyle.Title);
            GUILayout.Label("Сверху вниз — как ракета стоит на площадке. Ступень 1 работает первой, затем отделяется.", HudStyle.Small);
            designScroll = GUILayout.BeginScrollView(designScroll, GUILayout.Height(height - 46f));

            DrawPayloadSection();
            GUILayout.Space(4);
            if (Button("Добавить ступень сверху", true)) Defer(AddStageOnTop);
            GUILayout.Space(4);
            if (design.stages.Count == 0)
                Colored("Ступеней нет — ракете нечем взлетать. Добавьте ступень, двигатель и баки.", note, HudStyle.Danger);
            for (int i = design.stages.Count - 1; i >= 0; i--)
            {
                DrawStage(i);
                GUILayout.Space(4);
            }
            GUILayout.Label("— стартовая площадка —", compact);
            GUILayout.EndScrollView();
        }

        void DrawPayloadSection()
        {
            GUILayout.BeginVertical(HudStyle.Panel);
            GUILayout.Label("Головной отсек (полезная нагрузка) · " + Mass(compiled.PayloadMass), HudStyle.Title);
            for (int p = 0; p < design.payloadModules.Count; p++)
            {
                int index = p;
                string id = design.payloadModules[p];
                ModuleDefinition m = ModuleCatalog.Find(id);
                GUILayout.BeginHorizontal();
                GUILayout.Label(m != null ? m.name + " · " + Mass(m.dryMass) : "неизвестный модуль «" + id + "»", HudStyle.Label);
                GUILayout.FlexibleSpace();
                if (m != null && m.mandatory) GUILayout.Label("обязательный", compact, GUILayout.Width(80));
                else if (GUILayout.Button("Убрать", HudStyle.Button, GUILayout.Width(70))) Defer(() => RemovePayload(index));
                GUILayout.EndHorizontal();
            }
            if (design.payloadModules.Count == 0)
                GUILayout.Label("Пусто — ядро зонда будет добавлено автоматически.", HudStyle.Small);
            GUILayout.Label("Модули добавляются из каталога: Навигация, Защита, Наука, Пробоотборники.", HudStyle.Small);
            GUILayout.EndVertical();
        }

        void DrawStage(int i)
        {
            StageBlueprint s = design.stages[i];
            StageReport sr = i < report.Stages.Count ? report.Stages[i] : null;
            int index = i;
            bool selected = i == selectedStage;
            float inner = middleWidth - 46f;

            GUILayout.BeginVertical(HudStyle.Panel);
            GUILayout.BeginHorizontal();
            Color c = GUI.color;
            if (selected) GUI.color = HudStyle.Accent;
            if (GUILayout.Toggle(selected, StageTitle(i), HudStyle.Button, GUILayout.Width(inner - 130f)) && !selected)
                Defer(() => selectedStage = index);
            GUI.color = c;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Удалить ступень", HudStyle.Button, GUILayout.Width(120))) Defer(() => RemoveStage(index));
            GUILayout.EndHorizontal();

            // Engine
            ModuleDefinition engine = string.IsNullOrEmpty(s.engineId) ? null : ModuleCatalog.Find(s.engineId);
            if (string.IsNullOrEmpty(s.engineId))
            {
                Colored("Двигатель: нет — выберите в каталоге «Двигатели» и нажмите «Поставить на ступень " + (i + 1) + "».",
                    note, HudStyle.Warning);
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Двигатель: " + (engine != null ? engine.name : s.engineId) + " × " + s.engineCount, HudStyle.Label);
                GUILayout.FlexibleSpace();
                if (Button("−", s.engineCount > 1, GUILayout.Width(28))) Defer(() => ChangeEngineCount(index, -1));
                if (Button("+", s.engineCount < BlueprintCompiler.MaxEnginesPerStage, GUILayout.Width(28)))
                    Defer(() => ChangeEngineCount(index, +1));
                if (GUILayout.Button("Снять", HudStyle.Button, GUILayout.Width(60))) Defer(() => RemoveEngine(index));
                GUILayout.EndHorizontal();
            }

            // Tanks
            if (s.tanks.Count == 0) GUILayout.Label("Баков нет — добавьте из каталога «Топливные баки».", HudStyle.Small);
            for (int t = 0; t < s.tanks.Count; t++)
            {
                int tankIndex = t;
                TankLoad load = s.tanks[t];
                ModuleDefinition tank = ModuleCatalog.Find(load.moduleId);
                GUILayout.BeginHorizontal();
                GUILayout.Label(tank != null ? tank.name + " · " + BlueprintCompiler.Name(tank.propellant) : "неизвестный бак «" + load.moduleId + "»",
                    HudStyle.Label);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Убрать", HudStyle.Button, GUILayout.Width(70))) Defer(() => RemoveTank(index, tankIndex));
                GUILayout.EndHorizontal();

                if (tank == null || !tank.IsTank || tank.propellantCapacity <= 0.0) continue;
                double capacity = tank.propellantCapacity;
                double shown = Math.Max(0.0, Math.Min(capacity, load.propellantLoaded));
                GUILayout.BeginHorizontal();
                float slider = GUILayout.HorizontalSlider((float)shown, 0f, (float)capacity, GUILayout.Width(Mathf.Max(80f, inner - 165f)));
                GUILayout.Label(Tonnes(shown) + " / " + Tonnes(capacity) + " т (" + N(shown / capacity * 100.0, 0) + " %)", compact,
                    GUILayout.Width(160));
                GUILayout.EndHorizontal();
                if (Math.Abs(slider - shown) > capacity * 1e-4)
                {
                    // The value itself can change mid-pass (same controls); flags/status/recompile wait until after the pass.
                    double step = capacity * SliderStep;
                    load.propellantLoaded = Math.Max(0.0, Math.Min(capacity, Math.Round(slider / step) * step));
                    Defer(MarkChanged);
                }
                if (engine != null && engine.IsEngine && tank.propellant != engine.propellant)
                    Colored("Топливо не подходит двигателю " + engine.name + " (" + BlueprintCompiler.Name(engine.propellant) +
                            ") — его нечем сжечь, это мёртвый груз.", note, HudStyle.Danger);
            }

            if (sr != null)
            {
                GUILayout.Label("Ступень: сухая " + Mass(sr.DryMass) + " · топливо " + Mass(sr.PropellantMass) +
                                " · Δv " + Speed(sr.DeltaVVacuum) + " (идеально)", HudStyle.Small);
            }
            GUILayout.EndVertical();
        }

        string StageTitle(int i)
        {
            if (i == 0) return "Ступень 1 (первой работает)";
            if (i == design.stages.Count - 1) return "Ступень " + (i + 1) + " (верхняя)";
            return "Ступень " + (i + 1);
        }

        static string EngineSummary(StageBlueprint s)
        {
            if (string.IsNullOrEmpty(s.engineId)) return "нет";
            ModuleDefinition e = ModuleCatalog.Find(s.engineId);
            return (e != null ? e.name : s.engineId) + " × " + s.engineCount;
        }

        int CountInPayload(string id)
        {
            int n = 0;
            foreach (string p in design.payloadModules)
                if (p == id) n++;
            return n;
        }

        // ---- Right column: live statistics ---------------------------------------------------

        void DrawStatistics(float height)
        {
            GUILayout.Label("Характеристики · идеально (уровень A)", HudStyle.Title);
            statsScroll = GUILayout.BeginScrollView(statsScroll, GUILayout.Height(height - 24f));
            const float lw = 175f;

            HudStyle.Row("Стартовая масса", Mass(report.LaunchMass), lw);
            HudStyle.Row("Сухая масса (с отсеком)", Mass(report.DryMass), lw);
            HudStyle.Row("  в т.ч. головной отсек", Mass(compiled.PayloadMass), lw);
            HudStyle.Row("Топливо рабочее / ёмкость", Tonnes(compiled.UsableFuel) + " / " + Tonnes(compiled.MaxFuelCapacity) + " т", lw);
            if (compiled.UnusableFuel > 0.0)
                ColoredRow("Неиспользуемое топливо", Mass(compiled.UnusableFuel), lw, HudStyle.Danger);
            if (compiled.UnusableFuel > 0.0)
                Colored("Это топливо не подходит двигателю своей ступени — его везут как мёртвый груз.", note, HudStyle.Danger);
            HudStyle.Row("Макс. тяга у поверхности", Force(report.MaxSurfaceThrust), lw);
            HudStyle.Row("Макс. тяга в вакууме", Force(report.MaxVacuumThrust), lw);

            StageReport first = report.Stages.Count > 0 ? report.Stages[0] : null;
            bool firstHasEngine = first != null && first.VacuumIsp > 0.0;
            HudStyle.Row("Isp ст. 1 (пов. / вак.)", firstHasEngine ? N(first.SurfaceIsp, 0) + " / " + N(first.VacuumIsp, 0) + " с" : "—", lw);
            HudStyle.Row("Расход ṁ ступени 1", firstHasEngine ? N(first.MassFlow, 1) + " кг/с" : "—", lw);
            ColoredRow("Тяга/вес на старте (TWR)", N(report.LiftoffTwr, 2) + (report.CanLiftOff ? "" : "  < 1"), lw,
                report.CanLiftOff ? Color.white : HudStyle.Danger);
            HudStyle.Row("g на площадке", N(report.SurfaceGravity, 2) + " м/с²", lw);
            ColoredRow("Идеальный Δv (вакуум)", Speed(report.TotalDeltaVVacuum), lw, HudStyle.Accent);

            GUILayout.Space(6);
            GUILayout.Label("По ступеням (в порядке работы)", HudStyle.Title);
            if (report.Stages.Count == 0) GUILayout.Label("Ступеней нет.", HudStyle.Small);
            for (int i = 0; i < report.Stages.Count; i++)
            {
                StageReport sr = report.Stages[i];
                bool hasEngine = sr.VacuumThrust > 0.0 && sr.VacuumIsp > 0.0;
                HudStyle.Row("Ступень " + (i + 1) + " · Δv", Speed(sr.DeltaVVacuum), lw);
                GUILayout.Label(hasEngine
                        ? "   работает " + BurnTime(sr.BurnTime) + " · TWR при включении " + N(sr.IgnitionTwr, 2) +
                          (i == 0 ? " (тяга у поверхности)" : " (тяга в вакууме, g площадки)")
                        : "   нет двигателя — Δv не даёт",
                    HudStyle.Small);
            }

            GUILayout.Space(6);
            DrawFlightCapability(lw);
            GUILayout.Space(6);
            DrawCapabilities(lw);
            GUILayout.EndScrollView();
        }

        void DrawFlightCapability(float lw)
        {
            GUILayout.Label("Возможности полёта", HudStyle.Title);
            GUILayout.Label("Сравнение идеального запаса Δv с идеальной потребностью. Потери на гравитацию и сопротивление НЕ учтены — " +
                            "реальному полёту нужно больше. Отмеченная цель проверяется в предупреждениях.", HudStyle.Small);
            if (targets == null || targets.Length == 0)
            {
                GUILayout.Label("Цели не рассчитаны.", HudStyle.Small);
                return;
            }
            var notes = new List<string>();
            for (int t = 0; t < targets.Length; t++)
            {
                MissionTarget target = targets[t];
                int index = t;
                bool selected = t == requiredTarget;
                bool enough = report.TotalDeltaVVacuum >= target.IdealDeltaV;
                if (GUILayout.Toggle(selected, " " + target.Name, GUILayout.Width(rightWidth - 30f)) && !selected)
                    Defer(() =>
                    {
                        requiredTarget = index;
                        dirty = true;
                    });
                Color verdictColor = enough ? HudStyle.Accent : HudStyle.Danger;
                ColoredRow("   нужно ≥ " + Speed(target.IdealDeltaV), enough ? "✓ хватает" : "✗ не хватает", lw - 20f, verdictColor);
                Colored(enough
                        ? "      запас " + Speed(report.TotalDeltaVVacuum - target.IdealDeltaV) + " (идеально)"
                        : "      не хватает " + Speed(target.IdealDeltaV - report.TotalDeltaVVacuum) + " даже без потерь",
                    compact, verdictColor);
                if (!string.IsNullOrEmpty(target.Notes) && !notes.Contains(target.Notes)) notes.Add(target.Notes);
            }
            foreach (string n in notes) GUILayout.Label(n, HudStyle.Small);
        }

        void DrawCapabilities(float lw)
        {
            VehicleCapabilities caps = compiled.Capabilities;
            GUILayout.Label("Оборудование", HudStyle.Title);
            HudStyle.Row("Прогноз орбиты", YesNo(caps.Has(ModuleCapability.OrbitPrediction)), lw);
            HudStyle.Row("Межпланетная навигация", YesNo(caps.Has(ModuleCapability.InterplanetaryNavigation)), lw);
            HudStyle.Row("Анализ атмосферы", YesNo(caps.Has(ModuleCapability.AtmosphereAnalysis)), lw);
            HudStyle.Row("Анализ образцов", caps.Has(ModuleCapability.SampleAnalysis) ? "да, ×" + N(caps.ResearchMultiplier, 1) : "нет", lw);
            HudStyle.Row("Радиационная защита", Level(caps.RadiationShielding), lw);
            HudStyle.Row("Кислотостойкость", Level(caps.AcidResistance), lw);
            HudStyle.Row("Образцы, до", caps.SampleCapacity > 0.0 ? Mass(caps.SampleCapacity) : "нет пробоотборника", lw);
            if (caps.SampleKinds != SampleKinds.None) GUILayout.Label("Виды образцов: " + KindList(caps.SampleKinds), note);
        }

        // ---- Warnings and buttons ------------------------------------------------------------

        void DrawWarnings()
        {
            GUILayout.Label("Предупреждения — это последствия, а не запреты: применить можно любой проект", HudStyle.Title);
            warningsScroll = GUILayout.BeginScrollView(warningsScroll, GUILayout.Height(96));
            int shown = 0;
            foreach (BlueprintIssue issue in compiled.Issues)
            {
                Colored("• " + issue.Message, note, HudStyle.Warning);
                shown++;
            }
            foreach (DesignWarning warning in report.Warnings)
            {
                bool severe = warning.Code == DesignWarningCode.CannotLiftOff || warning.Code == DesignWarningCode.InsufficientDeltaV;
                Colored("• " + warning.Message, note, severe ? HudStyle.Danger : HudStyle.Warning);
                shown++;
            }
            if (shown == 0)
                Colored("Замечаний нет: ракета оторвётся от площадки, идеального Δv хватает на отмеченную цель (потери в полёте не учтены).",
                    note, HudStyle.Accent);
            GUILayout.EndScrollView();
        }

        void DrawButtons()
        {
            bool onPad = body.IsOnLaunchPad;
            GUILayout.BeginHorizontal();
            if (Button(onPad ? "Применить — новая ракета на площадке" : "Перестройка возможна только на стартовой площадке", onPad,
                    GUILayout.Width(360), GUILayout.Height(30)))
                Defer(Apply);
            if (Button("Сбросить к стандартной", true, GUILayout.Width(200), GUILayout.Height(30))) Defer(ResetToDefault);
            GUILayout.Space(8);
            if (!string.IsNullOrEmpty(status)) Colored(status, note, statusColor, GUILayout.Width(Mathf.Max(100f, leftWidth + middleWidth + rightWidth - 720f)));
            GUILayout.FlexibleSpace();
            if (Button("Закрыть (Esc)", true, GUILayout.Width(120), GUILayout.Height(30))) Defer(Close);
            GUILayout.EndHorizontal();
        }

        // ---- IMGUI helpers -------------------------------------------------------------------

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

        static void ColoredRow(string label, string value, float labelWidth, Color color)
        {
            Color c = GUI.color;
            GUI.color = color;
            HudStyle.Row(label, value, labelWidth);
            GUI.color = c;
        }

        // ---- Formatting (display only; values stay SI) ---------------------------------------

        static string N(double value, int decimals) => UnitFormatter.Number(value, decimals);

        static string Mass(double kg)
        {
            double a = Math.Abs(kg);
            if (a < 1000.0) return N(kg, 0) + " кг";
            if (a < 10000.0) return N(kg / 1000.0, 2) + " т";
            return N(kg / 1000.0, 1) + " т";
        }

        /// <summary>Kilograms shown as tonnes with one decimal (no unit suffix).</summary>
        static string Tonnes(double kg) => N(kg / 1000.0, 1);

        static string Force(double newtons) =>
            Math.Abs(newtons) >= 1.0e6 ? N(newtons / 1.0e6, 2) + " МН" : N(newtons / 1.0e3, 0) + " кН";

        static string Speed(double metresPerSecond) => N(metresPerSecond, 0) + " м/с";

        static string Isp(double seconds) => N(seconds, 0) + " с";

        static string BurnTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "—";
            if (seconds < 60.0) return N(seconds, 1) + " с";
            long total = (long)Math.Round(seconds);
            return (total / 60) + " мин " + (total % 60).ToString("00") + " с";
        }

        static string Price(int credits) => credits > 0 ? N(credits, 0) + " кр." : "бесплатно";

        static string Level(int level) => level > 0 ? "уровень " + level : "нет";

        static string YesNo(bool value) => value ? "да" : "нет";

        static string CapabilityList(ModuleCapability caps)
        {
            var parts = new List<string>();
            if ((caps & ModuleCapability.OrbitPrediction) != 0) parts.Add("прогноз орбиты");
            if ((caps & ModuleCapability.InterplanetaryNavigation) != 0) parts.Add("межпланетная навигация");
            if ((caps & ModuleCapability.AtmosphereAnalysis) != 0) parts.Add("анализ атмосферы");
            if ((caps & ModuleCapability.SampleAnalysis) != 0) parts.Add("анализ образцов");
            return parts.Count > 0 ? string.Join(", ", parts) : "—";
        }

        static string KindList(SampleKinds kinds)
        {
            var parts = new List<string>();
            if ((kinds & SampleKinds.Rock) != 0) parts.Add("камни");
            if ((kinds & SampleKinds.Soil) != 0) parts.Add("грунт");
            if ((kinds & SampleKinds.Liquid) != 0) parts.Add("жидкости");
            if ((kinds & SampleKinds.Acid) != 0) parts.Add("кислоты");
            if ((kinds & SampleKinds.Rare) != 0) parts.Add("редкие материалы");
            return parts.Count > 0 ? string.Join(", ", parts) : "—";
        }
    }
}
