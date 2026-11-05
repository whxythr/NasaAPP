using System;
using System.Collections.Generic;
using SpaceSim.Economy;
using SpaceSim.Planets;
using SpaceSim.Units;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Shop window (Milestone 3: economy). Three tabs — «Магазин» (upgrades), «Рынок» (sample trade),
    /// «Исследования» (milestones, discoveries, environment hazards).
    /// Every rule lives in the Core: prices, unlock state and purchase checks come from <see cref="Shop"/>, sale prices from
    /// <see cref="Market"/>, milestones from <see cref="ResearchMilestones"/>, hazard rules from <see cref="HazardModel"/>.
    /// All changes go through <see cref="PlayerSession"/> (which applies the Core transaction and saves). This class only
    /// formats results.
    /// </summary>
    /// <remarks>
    /// IMGUI rule followed here (same as <see cref="HangarUI"/>): the displayed data is snapshotted on the Layout event and the
    /// following event pass draws the same snapshot; anything that changes the structure (purchases, sales, tab switches)
    /// is queued with <see cref="Defer"/> and applied after the whole pass.
    /// </remarks>
    [DefaultExecutionOrder(216)]
    public sealed class ShopUI : MonoBehaviour
    {
        const float PreferredWidth = 1000f;
        const float PreferredHeight = 640f;

        enum Tab { Shop, Market, Research }

        static readonly string[] TabNames = { "Магазин", "Рынок", "Исследования" };

        PlayerSession session;
        FlightStateController state;
        StarSystemRuntime starSystem;
        bool open;
        /// <summary>Opened mid-frame (e.g. from a HUD button): draw nothing until a Layout pass has seen the window.</summary>
        bool awaitingLayout;
        Tab tab = Tab.Shop;

        View view;
        bool dirty = true;
        (int credits, double research, int owned, int samples, int milestones, int visited, bool readOnly) signature;

        string status = "";
        Color statusColor = Color.white;
        Vector2 shopScroll, marketScroll, researchScroll;
        float contentWidth;
        readonly List<Action> pending = new List<Action>();
        GUIStyle note, compact, cell;

        public bool IsOpen => open;

        public void Initialize(PlayerSession playerSession, FlightStateController flightState, StarSystemRuntime system)
        {
            if (session != null) session.Changed -= OnProfileChanged;
            session = playerSession;
            state = flightState;
            starSystem = system;
            if (session != null) session.Changed += OnProfileChanged;
            dirty = true;
        }

        void OnDestroy()
        {
            if (session != null) session.Changed -= OnProfileChanged;
        }

        void OnProfileChanged() => dirty = true;

        /// <summary>Opens the shop on the last used tab, or closes it.</summary>
        public void Toggle()
        {
            if (open)
            {
                Close();
                return;
            }
            if (session == null) return;
            pending.Clear();
            status = "";
            open = true;
            awaitingLayout = true;
            dirty = true;
            BuildView();
        }

        void Close()
        {
            open = false;
            pending.Clear();
            GUIUtility.keyboardControl = 0;
        }

        // ---- Transactions (always through PlayerSession, applied between IMGUI passes) -------

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

        void Buy(string moduleId)
        {
            if (session == null || session.Profile == null) return;
            PurchaseResult result = session.Purchase(moduleId, out string message);
            bool ok = result == PurchaseResult.Success;
            if (ok && session.ReadOnly) message += " Профиль только для чтения — покупка не сохранится после выхода из игры.";
            SetStatus(message, ok ? HudStyle.Accent : HudStyle.Warning);
            if (state != null) state.Notify("Магазин: " + message);
        }

        void Sell(string sampleId, string sampleName)
        {
            if (session == null || session.Profile == null) return;
            double researchBefore = session.Profile.researchPoints;
            if (!session.SellSample(sampleId, out int credits))
            {
                SetStatus("Образец «" + sampleName + "» не найден — возможно, он уже продан.", HudStyle.Warning);
                return;
            }
            double gained = session.Profile.researchPoints - researchBefore;
            string message = "Продан образец «" + sampleName + "» за " + Credits(credits) +
                             (gained > 0.0 ? " · +" + Points(gained) : "") + ".";
            if (session.ReadOnly) message += " Профиль только для чтения — продажа не сохранится после выхода из игры.";
            SetStatus(message, HudStyle.Accent);
            if (state != null) state.Notify("Рынок: " + message);
        }

        void SetStatus(string text, Color color)
        {
            status = text;
            statusColor = color;
        }

        // ---- Snapshot of everything shown (rebuilt on Layout when the profile changed) -------

        sealed class ShopRow
        {
            public string Id, Title, Price, StateText, Description, Effect, BuyLabel;
            public Color StateColor;
            public bool CanBuy;
            public readonly List<KeyValuePair<string, string>> Specs = new List<KeyValuePair<string, string>>();
            public readonly List<string> SpecNotes = new List<string>();
        }

        sealed class SampleRow
        {
            public string Id, Name, Category, Planet, Mass, Rarity, Analysis, Value, Price;
            /// <summary>Back on the start planet (only then it can be sold).</summary>
            public bool Delivered;
        }

        sealed class MilestoneRow
        {
            public bool Achieved;
            public string Name, Condition, Rewards, Unlocks;
        }

        sealed class HazardRow
        {
            public string Body, Radiation, Acid, Wear, Protection;
            public bool Dangerous;
        }

        sealed class View
        {
            public bool HasProfile;
            public string Balance = "";
            public bool ReadOnly;
            public string ReadOnlyText = "";
            public readonly List<ShopRow> Shop = new List<ShopRow>();
            public readonly List<SampleRow> Samples = new List<SampleRow>();
            public string SampleSummary = "";
            public string CollectorInfo = "";
            public readonly List<MilestoneRow> Milestones = new List<MilestoneRow>();
            public string Visited = "";
            public string Lifetime = "";
            public readonly List<HazardRow> Hazards = new List<HazardRow>();
            public string HazardRules = "";
        }

        bool NeedsRebuild()
        {
            if (view == null || dirty) return true;
            PlayerProfile p = session != null ? session.Profile : null;
            return p != null && CurrentSignature(p) != signature;
        }

        (int, double, int, int, int, int, bool) CurrentSignature(PlayerProfile p) =>
            (p.credits, p.researchPoints, p.ownedModules.Count, p.samples.Count, p.achievedMilestones.Count, p.visitedBodies.Count,
                session.ReadOnly);

        void BuildView()
        {
            dirty = false;
            var v = new View();
            view = v;
            PlayerProfile p = session != null ? session.Profile : null;
            if (p == null) return;
            signature = CurrentSignature(p);

            v.HasProfile = true;
            v.Balance = "Кредиты: " + N(p.credits, 0) + " кр · Исследования: " + Points(p.researchPoints);
            v.ReadOnly = session.ReadOnly;
            if (v.ReadOnly)
                v.ReadOnlyText = "Профиль только для чтения: " + (string.IsNullOrEmpty(session.LoadMessage)
                    ? "сохранение создано более новой версией игры — изменения не сохраняются."
                    : session.LoadMessage);

            foreach (ShopItem item in SpaceSim.Economy.Shop.Items) v.Shop.Add(BuildShopRow(p, item));
            BuildMarket(v, p);
            BuildResearch(v, p);
            BuildHazards(v);
        }

        ShopRow BuildShopRow(PlayerProfile p, ShopItem item)
        {
            ModuleDefinition m = item.Module;
            ShopItemState s = SpaceSim.Economy.Shop.StateOf(p, item);
            var row = new ShopRow
            {
                Id = m.id,
                Title = m.name + " · " + CategoryName(m.category),
                Price = Credits(item.Price),
                Description = m.description,
                Effect = item.Effect,
                CanBuy = s == ShopItemState.Available,
                BuyLabel = "Купить за " + Credits(item.Price)
            };
            switch (s)
            {
                case ShopItemState.Owned:
                    row.StateText = "Куплено — модуль доступен в ангаре.";
                    row.StateColor = HudStyle.Accent;
                    row.BuyLabel = "Куплено";
                    break;
                case ShopItemState.Available:
                    row.StateText = "Доступно";
                    row.StateColor = new Color(0.55f, 0.95f, 0.6f);
                    break;
                case ShopItemState.Locked:
                    MilestoneDefinition req = ResearchMilestones.Find(item.RequiredMilestone);
                    row.StateText = req != null
                        ? "Заблокировано: требуется «" + req.Name + "» — " + req.Condition
                        : "Заблокировано: требуется достижение «" + item.RequiredMilestone + "».";
                    row.StateColor = HudStyle.Warning;
                    break;
                default:
                    row.StateText = "Не хватает " + N(item.Price - p.credits, 0) + " кр";
                    row.StateColor = HudStyle.Danger;
                    break;
            }
            AddSpecs(row, m);
            return row;
        }

        static void AddSpecs(ShopRow row, ModuleDefinition m)
        {
            void Spec(string label, string value) => row.Specs.Add(new KeyValuePair<string, string>(label, value));

            Spec("Масса (сухая)", Mass(m.dryMass));
            switch (m.category)
            {
                case ModuleCategory.Engine:
                    Spec("Тяга в вакууме", Force(m.vacuumThrust));
                    Spec("Isp в вакууме", N(m.vacuumIsp, 0) + " с");
                    Spec("Isp у поверхности", N(m.seaLevelIsp, 0) + " с");
                    Spec("Топливо", BlueprintCompiler.Name(m.propellant));
                    break;
                case ModuleCategory.FuelTank:
                    Spec("Ёмкость", Mass(m.propellantCapacity));
                    Spec("Топливо", BlueprintCompiler.Name(m.propellant));
                    Spec("Масса с топливом", Mass(m.dryMass + m.propellantCapacity));
                    break;
                case ModuleCategory.Protection:
                    Spec("Радиационная защита", Level(m.radiationShielding));
                    Spec("Кислотостойкость", Level(m.acidResistance));
                    break;
                case ModuleCategory.SampleCollector:
                    Spec("Образцы, до", m.sampleCapacity > 0.0 ? Mass(m.sampleCapacity) : "—");
                    if (m.sampleKinds != SampleKinds.None) row.SpecNotes.Add("Виды образцов: " + KindList(m.sampleKinds));
                    break;
                default:
                    Spec("Возможности", m.capabilities == ModuleCapability.None ? "—" : CapabilityList(m.capabilities));
                    if (m.researchMultiplier != 1.0) Spec("Научная ценность", "×" + N(m.researchMultiplier, 1));
                    if (m.radiationShielding > 0) Spec("Радиационная защита", Level(m.radiationShielding));
                    if (m.acidResistance > 0) Spec("Кислотостойкость", Level(m.acidResistance));
                    break;
            }
            if (m.length > 0.0) Spec("Длина", N(m.length, 1) + " м");
        }

        void BuildMarket(View v, PlayerProfile p)
        {
            foreach (SampleRecord s in p.samples)
            {
                if (s == null) continue;
                v.Samples.Add(new SampleRow
                {
                    Id = s.id,
                    Name = string.IsNullOrEmpty(s.name) ? s.id : s.name,
                    Category = SampleCategoryName(s.category),
                    Planet = BodyName(s.planetId),
                    Mass = SampleMass(s.mass),
                    Rarity = RarityName(s.rarity),
                    Analysis = s.analysed ? "да, ×" + N(Math.Max(1.0, s.analysisMultiplier), 1) : "нет",
                    Value = Points(s.scientificValue),
                    Price = Credits(Market.SellPrice(s)),
                    Delivered = s.delivered
                });
            }
            v.SampleSummary = "Образцов: " + p.samples.Count + " · общая масса " + SampleMass(p.SampleMass()) +
                              (p.CargoMass() > 0.0 ? " · в ракете " + SampleMass(p.CargoMass()) + " — продажа после возвращения на Четвёртую планету" : "");

            var collectors = new List<string>();
            foreach (ModuleDefinition m in ModuleCatalog.InCategory(ModuleCategory.SampleCollector))
                collectors.Add(m.name + " — " + KindList(m.sampleKinds) + ", до " + Mass(m.sampleCapacity) +
                               (m.requiresPurchase ? " (магазин)" : " (бесплатно)"));
            v.CollectorInfo = collectors.Count > 0 ? string.Join("; ", collectors) + "." : "";
        }

        void BuildResearch(View v, PlayerProfile p)
        {
            foreach (MilestoneDefinition m in ResearchMilestones.All)
            {
                var unlocks = new List<string>();
                foreach (ShopItem item in SpaceSim.Economy.Shop.Items)
                    if (item.RequiredMilestone == m.Id) unlocks.Add(item.Module.name);
                v.Milestones.Add(new MilestoneRow
                {
                    Achieved = p.HasMilestone(m.Id),
                    Name = m.Name,
                    Condition = m.Condition,
                    Rewards = "Награда: +" + Credits(m.CreditReward) + " · +" + Points(m.ResearchReward),
                    Unlocks = unlocks.Count > 0 ? "Открывает в магазине: " + string.Join(", ", unlocks) : ""
                });
            }

            var names = new List<string>();
            foreach (string id in p.visitedBodies) names.Add(BodyName(id));
            int total = starSystem != null ? starSystem.Bodies.Count : 0;
            v.Visited = "Посещённые тела" + (total > 0 ? " (" + names.Count + " из " + total + ")" : "") + ": " +
                        (names.Count > 0 ? string.Join(", ", names) : "пока нет");
            v.Lifetime = "Всего заработано за игру: " + N(p.lifetimeEarnings, 0) + " кр";
        }

        void BuildHazards(View v)
        {
            if (starSystem != null)
            {
                foreach (StarSystemRuntime.Body b in starSystem.Bodies)
                {
                    if (b == null || b.Runtime == null || b.Runtime.Definition == null) continue;
                    EnvironmentHazards hz = b.Runtime.Definition.hazards ?? new EnvironmentHazards();
                    // Unprotected wear in the two zones of the Core rule: above dense air (vacuum) and inside it.
                    double vacuumRate = HazardModel.Evaluate(hz, 0.0, 0, 0).DegradationPerSecond;
                    double airRate = HazardModel.Evaluate(hz, 1.0, 0, 0).DegradationPerSecond;
                    var wear = new List<string>();
                    if (vacuumRate > 0.0) wear.Add("вне плотной атмосферы −" + PercentPerMinute(vacuumRate) + " (" + TimeToOffline(vacuumRate) + ")");
                    if (airRate > 0.0) wear.Add("в атмосфере −" + PercentPerMinute(airRate) + " (" + TimeToOffline(airRate) + ")");

                    var protection = new List<string>();
                    if (hz.radiationLevel > 0) protection.Add(CoverText(hz.radiationLevel, true));
                    if (hz.acidLevel > 0) protection.Add(CoverText(hz.acidLevel, false));

                    v.Hazards.Add(new HazardRow
                    {
                        Body = b.Name,
                        Radiation = Level(hz.radiationLevel),
                        Acid = Level(hz.acidLevel),
                        Wear = wear.Count > 0 ? string.Join("; ", wear) : "безопасно",
                        Protection = protection.Count > 0 ? string.Join(" + ", protection) : "не нужна",
                        Dangerous = wear.Count > 0
                    });
                }
            }

            double level1 = HazardModel.Evaluate(new EnvironmentHazards { radiationLevel = 1 }, 0.0, 0, 0).DegradationPerSecond;
            double level2 = HazardModel.Evaluate(new EnvironmentHazards { radiationLevel = 2 }, 0.0, 0, 0).DegradationPerSecond;
            var modules = new List<string>();
            foreach (ModuleDefinition m in ModuleCatalog.InCategory(ModuleCategory.Protection))
            {
                var parts = new List<string>();
                if (m.radiationShielding > 0) parts.Add("радиация уровня " + m.radiationShielding);
                if (m.acidResistance > 0) parts.Add("кислота уровня " + m.acidResistance);
                if (parts.Count > 0) modules.Add(ShortName(m) + " = " + string.Join(", ", parts));
            }
            v.HazardRules =
                "Радиация действует там, где воздух разрежен (ρ/ρ₀ < " + N(HazardModel.ThinAirRatio * 100.0, 0) +
                " %) — плотная атмосфера экранирует. Кислота действует внутри атмосферы (ρ/ρ₀ > " +
                UnitFormatter.Scientific(HazardModel.AcidAirRatio, 1) + "); в тонком верхнем слое — обе опасности сразу. " +
                "Каждый незащищённый уровень изнашивает оборудование отсека: 1 уровень ≈ " + PercentPerMinute(level1) +
                ", 2 уровня ≈ " + PercentPerMinute(level2) + " (время симуляции). При целостности ниже " +
                N(HazardModel.InstrumentThreshold * 100.0, 0) + " % приборы отсека отключаются. " +
                (modules.Count > 0 ? "Защита: " + string.Join("; ", modules) + ". " : "") +
                "Защита слабее нужного уровня не спасает полностью, но замедляет износ.";
        }

        /// <summary>Lightest protection module that fully covers the level (judged by <see cref="HazardModel"/>), and whether it is owned.</summary>
        string CoverText(int level, bool radiation)
        {
            ModuleDefinition best = null;
            foreach (ModuleDefinition m in ModuleCatalog.All)
            {
                if (radiation ? m.radiationShielding <= 0 : m.acidResistance <= 0) continue;
                HazardExposure x = radiation
                    ? HazardModel.Evaluate(new EnvironmentHazards { radiationLevel = level }, 0.0, m.radiationShielding, 0)
                    : HazardModel.Evaluate(new EnvironmentHazards { acidLevel = level }, 1.0, 0, m.acidResistance);
                bool covers = radiation ? x.RadiationExcess == 0 : x.AcidExcess == 0;
                if (covers && (best == null || m.dryMass < best.dryMass)) best = m;
            }
            if (best == null) return (radiation ? "радиация" : "кислота") + ": модуля нет";
            bool owned = session != null && session.Owns(best.id);
            return ShortName(best) + (owned ? " (есть)" : " (магазин)");
        }

        string BodyName(string planetId)
        {
            if (string.IsNullOrEmpty(planetId)) return "—";
            if (starSystem != null)
            {
                int i = starSystem.IndexOf(planetId);
                if (i >= 0) return starSystem.Bodies[i].Name;
            }
            return planetId;
        }

        // ---- Drawing -------------------------------------------------------------------------

        void OnGUI()
        {
            if (!open || session == null || FlightInput.PauseMenuOpen) return;
            Event e = Event.current;
            bool layout = e != null && e.type == EventType.Layout;
            if (awaitingLayout)
            {
                if (!layout) return;
                awaitingLayout = false;
            }
            if (view == null || (layout && NeedsRebuild())) BuildView();
            if (view == null) return;

            GUI.depth = -11; // in front of the HUD (and the hangar): drawn on top and gets input first
            HudStyle.Begin();
            EnsureStyles();

            Vector2 screen = HudStyle.VirtualScreen;
            float w = Mathf.Min(PreferredWidth, screen.x - 20f);
            float h = Mathf.Min(PreferredHeight, screen.y - 20f);
            var r = new Rect((screen.x - w) * 0.5f, (screen.y - h) * 0.5f, w, h);
            HudStyle.WindowFrame(r);

            float innerW = w - 20f, innerH = h - 16f;
            contentWidth = Mathf.Max(300f, innerW - 24f); // minus the vertical scrollbar
            float scrollHeight = Mathf.Max(140f, innerH - (view.ReadOnly ? 170f : 136f));

            GUILayout.BeginArea(new Rect(r.x + 10f, r.y + 8f, innerW, innerH));
            DrawHeader();
            GUILayout.Space(4);
            DrawTabs(innerW);
            GUILayout.Space(6);

            if (!view.HasProfile)
                Colored("Профиль игрока не загружен — магазин недоступен.", note, HudStyle.Danger);
            else
            {
                switch (tab)
                {
                    case Tab.Shop: DrawShop(scrollHeight); break;
                    case Tab.Market: DrawMarket(scrollHeight); break;
                    default: DrawResearch(scrollHeight); break;
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
            if (note != null && compact != null && cell != null) return;
            // White base colour so GUI.color tints give exactly HudStyle.Accent / Warning / Danger.
            note = new GUIStyle(HudStyle.Small) { fontSize = 12, wordWrap = true };
            note.normal.textColor = Color.white;
            compact = new GUIStyle(HudStyle.Small) { wordWrap = false };
            cell = new GUIStyle(HudStyle.Label) { wordWrap = true };
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
            GUILayout.BeginHorizontal();
            GUILayout.Label("МАГАЗИН · экономика и исследования", HudStyle.WindowTitle);
            GUILayout.FlexibleSpace();
            Colored(view.Balance, HudStyle.Title, Color.white);
            GUILayout.EndHorizontal();
            GUILayout.Label("Цены, условия открытия и сделки задаёт ядро игры; каждая покупка и продажа сразу сохраняется в профиле.",
                HudStyle.Small);
            if (view.ReadOnly) Colored(view.ReadOnlyText, note, HudStyle.Danger);
        }

        void DrawTabs(float innerW)
        {
            float tw = Mathf.Min(180f, (innerW - 16f) / TabNames.Length);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < TabNames.Length; i++)
            {
                var t = (Tab)i;
                bool on = tab == t;
                string label = TabNames[i];
                if (t == Tab.Market && view.Samples.Count > 0) label += " (" + view.Samples.Count + ")";
                if (GUILayout.Toggle(on, label, HudStyle.Tab, GUILayout.Width(tw), GUILayout.Height(26)) && !on)
                    Defer(() => tab = t);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        // ---- Tab 1: shop ---------------------------------------------------------------------

        void DrawShop(float height)
        {
            float panelInner = contentWidth - 24f;
            float specsWidth = Mathf.Min(360f, panelInner * 0.42f);
            float textWidth = Mathf.Max(120f, panelInner - specsWidth - 12f);

            shopScroll = GUILayout.BeginScrollView(shopScroll, GUILayout.Height(height));
            if (view.Shop.Count == 0) GUILayout.Label("В магазине пока ничего нет.", HudStyle.Small);
            foreach (ShopRow row in view.Shop)
            {
                string id = row.Id;
                GUILayout.BeginVertical(HudStyle.Panel, GUILayout.Width(contentWidth));
                GUILayout.BeginHorizontal();
                GUILayout.Label(row.Title, HudStyle.Title);
                GUILayout.FlexibleSpace();
                GUILayout.Label(row.Price, HudStyle.Value, GUILayout.Width(140));
                GUILayout.EndHorizontal();
                Colored(row.StateText, note, row.StateColor);
                GUILayout.Space(2);

                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.Width(specsWidth));
                foreach (KeyValuePair<string, string> spec in row.Specs) HudStyle.Row(spec.Key, spec.Value, 170f);
                foreach (string specNote in row.SpecNotes) GUILayout.Label(specNote, note);
                GUILayout.EndVertical();
                GUILayout.Space(12);
                GUILayout.BeginVertical(GUILayout.Width(textWidth));
                GUILayout.Label(row.Description, HudStyle.Small);
                GUILayout.Space(4);
                Colored("Что даёт в игре: " + row.Effect, note, HudStyle.Accent);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                if (Button(row.BuyLabel, row.CanBuy, GUILayout.Width(240), GUILayout.Height(26))) Defer(() => Buy(id));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                GUILayout.Space(4);
            }
            GUILayout.Space(4);
            GUILayout.Label("Купленные модули появляются в ангаре (кнопка АНГАР / B на стартовой площадке) — там их ставят на ракету. " +
                            "Покупка разовая: модуль остаётся в профиле. Остальные модули каталога бесплатны и уже доступны в ангаре.",
                HudStyle.Small);
            GUILayout.EndScrollView();
        }

        // ---- Tab 2: market -------------------------------------------------------------------

        static readonly float[] MarketColumns = { 0.20f, 0.15f, 0.13f, 0.08f, 0.11f, 0.10f, 0.09f, 0.14f };
        static readonly string[] MarketHeaders = { "Образец", "Категория", "Планета", "Масса", "Редкость", "Анализ", "Ценность", "Цена" };

        void DrawMarket(float height)
        {
            const float buttonWidth = 90f;
            float tableWidth = contentWidth - buttonWidth - 50f; // slack for the labels' default margins

            GUILayout.Label("Продажа образцов · " + view.SampleSummary, HudStyle.Title);
            GUILayout.Label("Цена растёт с редкостью и с анализом на месте (спектрометр). При продаже начисляются и очки исследований.",
                HudStyle.Small);
            GUILayout.Space(4);

            marketScroll = GUILayout.BeginScrollView(marketScroll, GUILayout.Height(height - 44f));
            if (view.Samples.Count == 0)
            {
                GUILayout.BeginVertical(HudStyle.Panel, GUILayout.Width(contentWidth));
                GUILayout.Label("Образцов пока нет", HudStyle.Title);
                GUILayout.Label("Образцы собирают на поверхности других планет и лун пробоотборником — это будет в этапе «Посадка и " +
                                "исследование» (следующие milestone). Вид пробоотборника ограничивает, что можно взять и сколько: " +
                                view.CollectorInfo, note);
                GUILayout.Space(4);
                GUILayout.Label("Собранные образцы появятся здесь: их можно продать за кредиты, на которые покупаются улучшения.",
                    HudStyle.Small);
                GUILayout.EndVertical();
            }
            else
            {
                GUILayout.BeginHorizontal();
                for (int c = 0; c < MarketHeaders.Length; c++)
                    GUILayout.Label(MarketHeaders[c], HudStyle.Title, GUILayout.Width(tableWidth * MarketColumns[c]));
                GUILayout.EndHorizontal();

                foreach (SampleRow s in view.Samples)
                {
                    string id = s.Id, name = s.Name;
                    GUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
                    Cell(s.Name, 0, tableWidth);
                    Cell(s.Category, 1, tableWidth);
                    Cell(s.Planet, 2, tableWidth);
                    Cell(s.Mass, 3, tableWidth);
                    Cell(s.Rarity, 4, tableWidth);
                    Cell(s.Analysis, 5, tableWidth);
                    Cell(s.Value, 6, tableWidth);
                    Cell(s.Price, 7, tableWidth);
                    GUILayout.FlexibleSpace();
                    if (Button(s.Delivered ? "Продать" : "В ракете", s.Delivered, GUILayout.Width(buttonWidth))) Defer(() => Sell(id, name));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
        }

        void Cell(string text, int column, float tableWidth) =>
            GUILayout.Label(text, cell, GUILayout.Width(tableWidth * MarketColumns[column]));

        // ---- Tab 3: research -----------------------------------------------------------------

        static readonly float[] HazardColumns = { 0.17f, 0.11f, 0.11f, 0.36f, 0.25f };
        static readonly string[] HazardHeaders = { "Тело", "Радиация", "Кислота", "Износ без защиты", "Нужна защита" };

        void DrawResearch(float height)
        {
            researchScroll = GUILayout.BeginScrollView(researchScroll, GUILayout.Height(height));

            GUILayout.Label("Исследовательские достижения", HudStyle.Title);
            GUILayout.Label("Засчитываются только по настоящему полёту (симуляция), без телепортации и тестовых инструментов.",
                HudStyle.Small);
            foreach (MilestoneRow m in view.Milestones)
            {
                GUILayout.BeginVertical(HudStyle.Panel, GUILayout.Width(contentWidth));
                GUILayout.BeginHorizontal();
                Colored((m.Achieved ? "✓ " : "○ ") + m.Name, HudStyle.Title, m.Achieved ? HudStyle.Accent : Color.white);
                GUILayout.FlexibleSpace();
                Colored(m.Achieved ? "Достигнуто" : "Не достигнуто", compact, m.Achieved ? HudStyle.Accent : HudStyle.Warning,
                    GUILayout.Width(140));
                GUILayout.EndHorizontal();
                GUILayout.Label((m.Achieved ? "Условие было: " : "Условие: ") + m.Condition, note);
                GUILayout.Label(m.Rewards, HudStyle.Label);
                if (!string.IsNullOrEmpty(m.Unlocks)) GUILayout.Label(m.Unlocks, HudStyle.Small);
                GUILayout.EndVertical();
                GUILayout.Space(3);
            }

            GUILayout.Space(6);
            GUILayout.Label("Открытия", HudStyle.Title);
            GUILayout.Label(view.Visited, note);
            GUILayout.Label(view.Lifetime, HudStyle.Small);

            GUILayout.Space(8);
            GUILayout.Label("Опасности окружения", HudStyle.Title);
            GUILayout.Label(view.HazardRules, HudStyle.Small);
            GUILayout.Space(4);
            if (view.Hazards.Count == 0)
            {
                GUILayout.Label("Звёздная система недоступна — таблица опасностей появится после загрузки системы.", HudStyle.Small);
            }
            else
            {
                float tableWidth = contentWidth - 40f; // slack for the labels' default margins
                GUILayout.BeginHorizontal();
                for (int c = 0; c < HazardHeaders.Length; c++)
                    GUILayout.Label(HazardHeaders[c], HudStyle.Title, GUILayout.Width(tableWidth * HazardColumns[c]));
                GUILayout.EndHorizontal();
                foreach (HazardRow row in view.Hazards)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(row.Body, cell, GUILayout.Width(tableWidth * HazardColumns[0]));
                    GUILayout.Label(row.Radiation, cell, GUILayout.Width(tableWidth * HazardColumns[1]));
                    GUILayout.Label(row.Acid, cell, GUILayout.Width(tableWidth * HazardColumns[2]));
                    Colored(row.Wear, note, row.Dangerous ? HudStyle.Warning : HudStyle.Accent, GUILayout.Width(tableWidth * HazardColumns[3]));
                    GUILayout.Label(row.Protection, cell, GUILayout.Width(tableWidth * HazardColumns[4]));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
        }

        // ---- Footer --------------------------------------------------------------------------

        void DrawFooter(float innerW)
        {
            GUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(status)) Colored(status, note, statusColor, GUILayout.Width(Mathf.Max(100f, innerW - 140f)));
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

        // ---- Formatting (display only; values stay SI) ---------------------------------------

        static string N(double value, int decimals) => UnitFormatter.Number(value, decimals);

        static string Credits(int credits) => N(credits, 0) + " кр.";

        /// <summary>Research points: whole numbers without decimals, fractional ones with one.</summary>
        static string Points(double points) =>
            N(points, Math.Abs(points - Math.Round(points)) < 1e-6 ? 0 : 1) + " очк.";

        static string Mass(double kg)
        {
            double a = Math.Abs(kg);
            if (a < 1000.0) return N(kg, 0) + " кг";
            if (a < 10000.0) return N(kg / 1000.0, 2) + " т";
            return N(kg / 1000.0, 1) + " т";
        }

        static string SampleMass(double kg)
        {
            double a = Math.Abs(kg);
            if (a <= 0.0) return "0 кг";
            if (a < 1.0) return N(kg * 1000.0, 0) + " г";
            return N(kg, 2) + " кг";
        }

        static string Force(double newtons) =>
            Math.Abs(newtons) >= 1.0e6 ? N(newtons / 1.0e6, 2) + " МН" : N(newtons / 1.0e3, 0) + " кН";

        static string Level(int level) => level > 0 ? "уровень " + level : "нет";

        /// <summary>Integrity loss per simulated second (fraction) shown as % per minute.</summary>
        static string PercentPerMinute(double ratePerSecond)
        {
            double pm = ratePerSecond * 60.0 * 100.0;
            return N(pm, Math.Abs(pm - Math.Round(pm)) < 1e-6 ? 0 : 1) + " %/мин";
        }

        /// <summary>Simulated time until integrity falls from 100 % to the instrument threshold at a constant rate.</summary>
        static string TimeToOffline(double ratePerSecond)
        {
            if (ratePerSecond <= 0.0) return "приборы не отключатся";
            double minutes = (1.0 - HazardModel.InstrumentThreshold) / ratePerSecond / 60.0;
            return "приборы отключатся через ≈" + N(minutes, minutes < 10.0 ? 1 : 0) + " мин";
        }

        /// <summary>"Радиационная защита Р-1" → "Р-1" (the catalog code), otherwise the full name.</summary>
        static string ShortName(ModuleDefinition m)
        {
            if (m == null || string.IsNullOrEmpty(m.name)) return "—";
            int space = m.name.LastIndexOf(' ');
            string last = space >= 0 ? m.name.Substring(space + 1) : m.name;
            return last.IndexOf('-') > 0 ? last : m.name;
        }

        static string CategoryName(ModuleCategory c)
        {
            switch (c)
            {
                case ModuleCategory.Engine: return "двигатель";
                case ModuleCategory.FuelTank: return "топливный бак";
                case ModuleCategory.Navigation: return "навигация";
                case ModuleCategory.Protection: return "защита";
                case ModuleCategory.Science: return "наука";
                case ModuleCategory.SampleCollector: return "пробоотборник";
                default: return c.ToString();
            }
        }

        static string SampleCategoryName(SampleCategory c)
        {
            switch (c)
            {
                case SampleCategory.Rock: return "камень";
                case SampleCategory.Mineral: return "минерал";
                case SampleCategory.RareGeology: return "редкая порода";
                case SampleCategory.Soil: return "грунт";
                case SampleCategory.MineralSoil: return "минеральный грунт";
                case SampleCategory.UnusualCompound: return "необычное соединение";
                case SampleCategory.Water: return "вода";
                case SampleCategory.OtherLiquid: return "другая жидкость";
                case SampleCategory.Acid: return "кислота";
                default: return c.ToString();
            }
        }

        static string RarityName(SampleRarity r)
        {
            switch (r)
            {
                case SampleRarity.Common: return "обычный";
                case SampleRarity.Uncommon: return "необычный";
                case SampleRarity.Rare: return "редкий";
                case SampleRarity.Exceptional: return "исключительный";
                default: return r.ToString();
            }
        }

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
