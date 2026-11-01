using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace SpaceSim.Runtime.Menu
{
    /// <summary>
    /// HORIZON main menu (UI Toolkit). Builds every screen in code from <see cref="MenuCatalog"/> and styles it with
    /// Resources/UI/MainMenu.uss: Main, New Mission, Team Mission lobby, Academy, Mission Archive, Settings and the quit
    /// dialog. Keyboard / gamepad navigation comes from UI Toolkit focus; Esc (navigation cancel) goes back.
    /// Drop on an empty GameObject (SpaceSim ▸ Create Main Menu Scene).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuController : MonoBehaviour
    {
        public const string MainScreen = "main", NewMissionScreen = "new-mission", TeamScreen = "team",
            AcademyScreen = "academy", ArchiveScreen = "archive", SettingsScreen = "settings";

        [Tooltip("Optional. If empty a 1920×1080 scale-with-screen panel is created at runtime.")]
        [SerializeField] PanelSettings panelSettings;
        [Tooltip("Optional. If empty Resources/UI/MainMenu is used.")]
        [SerializeField] StyleSheet styleSheet;
        [Tooltip("Scene loaded by Begin Mission (until the mission-design scene exists).")]
        [SerializeField] string gameplayScene = "Phase1_Planet";

        UIDocument document;
        VisualElement root, modal;
        MenuBackdrop backdrop;
        readonly Dictionary<string, VisualElement> screens = new Dictionary<string, VisualElement>();
        readonly Stack<string> history = new Stack<string>();
        readonly List<VisualElement> menuItems = new List<VisualElement>();
        readonly List<VisualElement> scenarioCards = new List<VisualElement>();
        readonly List<VisualElement> difficultyTiles = new List<VisualElement>();
        string current;
        int scenarioIndex = 2, difficultyIndex = 1;
        FontDefinition fontStrong, fontMono;
        bool hasStrong, hasMono;

        // New Mission detail widgets, refreshed when the scenario changes.
        Label detailType, detailTitle, detailTarget, detailSummary, detailFocus;
        VisualElement detailGoals;
        readonly List<(VisualElement fill, Label value, Label critical)> resourceRows = new List<(VisualElement, Label, Label)>();

        public string CurrentScreen => current;
        public bool IsReady => backdrop != null && backdrop.IsReady;
        public PanelSettings Panel => document != null ? document.panelSettings : panelSettings;

        void Awake()
        {
            document = GetComponent<UIDocument>();
            if (panelSettings == null) panelSettings = document.panelSettings;
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                panelSettings.name = "MainMenuPanel (runtime)";
                panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panelSettings.referenceResolution = new Vector2Int(1920, 1080);
                panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                panelSettings.match = 0.5f;
                panelSettings.clearColor = true;
                panelSettings.colorClearValue = new Color(0.016f, 0.024f, 0.047f, 1f);
            }
            document.panelSettings = panelSettings;
            if (styleSheet == null) styleSheet = Resources.Load<StyleSheet>("UI/MainMenu");
        }

        void OnEnable()
        {
            if (document == null) return;
            Build(document.rootVisualElement);
        }

        void Build(VisualElement documentRoot)
        {
            documentRoot.Clear();
            screens.Clear();
            menuItems.Clear();
            scenarioCards.Clear();
            difficultyTiles.Clear();
            resourceRows.Clear();
            history.Clear();
            current = null;

            // Normally done by the default runtime theme, which the runtime-created panel does not have.
            documentRoot.style.position = Position.Absolute;
            documentRoot.style.left = documentRoot.style.top = documentRoot.style.right = documentRoot.style.bottom = 0;

            if (styleSheet != null) documentRoot.styleSheets.Add(styleSheet);
            else Debug.LogWarning("[MainMenu] Resources/UI/MainMenu.uss not found — the menu will be unstyled.");

            root = new VisualElement();
            root.AddToClassList("menu-root");
            documentRoot.Add(root);
            ApplyFonts(root);

            backdrop = new MenuBackdrop();
            root.Add(backdrop);

            BuildMain();
            BuildNewMission();
            BuildTeam();
            BuildAcademy();
            BuildArchive();
            BuildSettings();
            BuildHudTop();
            BuildHudBottom();
            BuildQuitModal();

            root.RegisterCallback<NavigationCancelEvent>(_ => Back());
            Show(MainScreen, remember: false);
            root.schedule.Execute(() => menuItems[0].Focus());
        }

        // ─────────────────────────────── navigation ───────────────────────────────

        /// <summary>Switches to a screen with a fade/slide transition.</summary>
        public void Show(string id, bool remember = true)
        {
            if (!screens.TryGetValue(id, out var next) || id == current) return;
            if (current != null)
            {
                if (remember) history.Push(current);
                var previous = screens[current];
                previous.RemoveFromClassList("screen--active");
                previous.schedule.Execute(() =>
                {
                    if (!previous.ClassListContains("screen--active")) previous.RemoveFromClassList("screen--visible");
                }).StartingIn(450);
            }
            current = id;
            next.AddToClassList("screen--visible");
            // One frame later so the transition starts from the hidden state.
            next.schedule.Execute(() => next.AddToClassList("screen--active")).StartingIn(16);
            RefreshHints();
        }

        public void Back()
        {
            if (modal.ClassListContains("modal--open")) { modal.RemoveFromClassList("modal--open"); return; }
            if (history.Count > 0) Show(history.Pop(), remember: false);
        }

        public void SelectScenario(int index)
        {
            scenarioIndex = Mathf.Clamp(index, 0, MenuCatalog.Scenarios.Length - 1);
            for (int i = 0; i < scenarioCards.Count; i++)
                scenarioCards[i].EnableInClassList("scenario-card--selected", i == scenarioIndex);
            RefreshScenarioDetail();
        }

        public void SelectDifficulty(int index)
        {
            difficultyIndex = Mathf.Clamp(index, 0, MenuCatalog.Difficulties.Length - 1);
            for (int i = 0; i < difficultyTiles.Count; i++)
                difficultyTiles[i].EnableInClassList("diff-tile--selected", i == difficultyIndex);
        }

        public void SelectMenuItem(int index)
        {
            for (int i = 0; i < menuItems.Count; i++)
                menuItems[i].EnableInClassList("menu-item--selected", i == index);
        }

        public void OpenQuitDialog() => modal.AddToClassList("modal--open");

        void BeginMission()
        {
            if (Application.CanStreamedLevelBeLoaded(gameplayScene)) SceneManager.LoadScene(gameplayScene);
            else Debug.LogWarning($"[MainMenu] Scene '{gameplayScene}' is not in Build Settings.");
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ─────────────────────────────── main screen ───────────────────────────────

        void BuildMain()
        {
            var screen = AddScreen(MainScreen);

            var column = Add(screen, El("main-column"));
            column.Add(Text("SPACE MISSION DESIGN PROGRAM", "eyebrow", "t-strong"));
            column.Add(Text("HORIZON", "title", "t-strong"));
            var rule = Add(column, El("title-rule"));
            rule.Add(El("title-rule__line"));
            rule.Add(Text("Design it. Launch it. Live with every trade-off.", "tagline"));

            var list = Add(column, El("menu-list"));
            AddMenuItem(list, "01", "CONTINUE", "Quoris Polar Mapper  ·  Spacecraft design  ·  64% complete", () => Show(NewMissionScreen), tag: "AUTOSAVE");
            AddMenuItem(list, "02", "NEW MISSION", "Pick an objective, then design, launch and fly it", () => Show(NewMissionScreen));
            AddMenuItem(list, "03", "TEAM MISSION", "1–4 crew  ·  co-op or versus  ·  every player owns a subsystem", () => Show(TeamScreen));
            AddMenuItem(list, "04", "SANDBOX", "No budget, no limits — experiment with every trade-off", () => { Show(NewMissionScreen); SelectScenario(MenuCatalog.Scenarios.Length - 1); });
            AddMenuItem(list, "05", "MISSION ARCHIVE", "Saved designs, debriefs, rankings and side-by-side comparison", () => Show(ArchiveScreen));
            AddMenuItem(list, "06", "ACADEMY", "Five-minute lessons on orbits, power, data and launch", () => Show(AcademyScreen));
            AddMenuItem(list, "", "SETTINGS", "Explanation depth, units, accessibility, audio", () => Show(SettingsScreen), quiet: true);
            AddMenuItem(list, "", "QUIT", "Leave Mission Control", OpenQuitDialog, quiet: true);
            SelectMenuItem(0);

            var side = Add(screen, El("side-stack"));

            var challenge = Add(side, El("panel", "panel--glass"));
            var head = Add(challenge, El("panel__header"));
            head.Add(Text("DAILY CHALLENGE", "eyebrow", "t-strong"));
            head.Add(Text("ENDS IN 14:22:07", "timer", "t-mono"));
            challenge.Add(Text("Map both poles of Quoris on a 300 M$ budget", "panel__title", "t-strong"));
            challenge.Add(Text("Smallest launch mass wins. Polar orbits cover everything — but need more energy to reach.", "panel__text"));
            var chips = Add(challenge, El("resource-chips"));
            ResourceChip(chips, MissionResource.Budget, "300 M$");
            ResourceChip(chips, MissionResource.Mass, "≤ 900 kg");
            ResourceChip(chips, MissionResource.Power, "650 W");
            ResourceChip(chips, MissionResource.Lifetime, "2 yr");

            var board = Add(side, El("panel", "panel--glass", "panel--spaced"));
            var boardHead = Add(board, El("panel__header"));
            boardHead.Add(Text("TOP CREWS · THIS WEEK", "eyebrow", "eyebrow--cyan", "t-strong"));
            boardHead.Add(Text("SCORE", "eyebrow", "eyebrow--muted"));
            LeaderRow(board, "1", "Perihelion Club", "4 crew", "9 420");
            LeaderRow(board, "2", "Lagrange Point", "3 crew", "9 115");
            LeaderRow(board, "3", "Delta-V Dreamers", "4 crew", "8 870");
            LeaderRow(board, "27", "You", "solo", "6 340", you: true);

            var news = Add(side, El("panel", "panel--glass", "panel--spaced", "news"));
            news.Add(Text("NEW", "news__tag", "t-strong"));
            news.Add(Text("Academy: \"Mass margins\" lesson unlocks the lightweight carbon bus.", "news__text"));
        }

        void AddMenuItem(VisualElement list, string index, string label, string caption, Action onClick, bool quiet = false, string tag = null)
        {
            var item = new Button(onClick) { focusable = true };
            item.AddToClassList("menu-item");
            if (quiet) item.AddToClassList("menu-item--quiet");
            item.Add(El("menu-item__bar"));
            item.Add(Text(index, "menu-item__index", "t-mono"));
            item.Add(Text(label, "menu-item__label", "t-strong"));
            if (tag != null) item.Add(Text(tag, "menu-item__tag", "t-strong"));
            item.Add(Text(caption, "menu-item__caption"));
            int i = menuItems.Count;
            item.RegisterCallback<FocusInEvent>(_ => SelectMenuItem(i));
            item.RegisterCallback<PointerEnterEvent>(_ => item.Focus());
            menuItems.Add(item);
            list.Add(item);
        }

        void ResourceChip(VisualElement parent, MissionResource r, string value)
        {
            var chip = Add(parent, El("res-chip"));
            var swatch = Add(chip, El("res-chip__swatch"));
            swatch.style.backgroundColor = MenuCatalog.ResourceColors[(int)r];
            chip.Add(Text(MenuCatalog.ResourceNames[(int)r], "res-chip__label"));
            chip.Add(Text(value, "res-chip__value", "t-mono"));
        }

        void LeaderRow(VisualElement parent, string rank, string name, string crew, string score, bool you = false)
        {
            var row = Add(parent, El("leader-row"));
            if (you) row.AddToClassList("leader-row--you");
            row.Add(Text(rank, "leader-row__rank", "t-mono"));
            row.Add(Text(name, "leader-row__name"));
            row.Add(Text(crew, "leader-row__crew"));
            row.Add(Text(score, "leader-row__score", "t-mono"));
        }

        // ─────────────────────────────── new mission ───────────────────────────────

        void BuildNewMission()
        {
            var screen = AddScreen(NewMissionScreen);
            ScreenHeader(screen, "NEW MISSION", "CHOOSE YOUR OBJECTIVE",
                "Every mission starts with a goal. The goal decides which resource will hurt the most.");

            var flow = Add(screen, El("flow"));
            string[] steps = { "OBJECTIVE", "REQUIREMENTS", "SPACECRAFT", "LAUNCH & ORBIT", "SIMULATE", "DEBRIEF" };
            for (int i = 0; i < steps.Length; i++)
            {
                var step = Add(flow, El("flow__step"));
                if (i == 0) step.AddToClassList("flow__step--active");
                step.Add(Text((i + 1).ToString(), "flow__num", "t-mono"));
                step.Add(Text(steps[i], "flow__label", "t-strong"));
                if (i < steps.Length - 1) flow.Add(El("flow__line"));
            }

            var body = Add(screen, El("screen-body"));
            body.style.top = 318;

            var list = Add(body, El("scenario-list"));
            for (int i = 0; i < MenuCatalog.Scenarios.Length; i++)
            {
                var s = MenuCatalog.Scenarios[i];
                int index = i;
                var card = new Button(() => SelectScenario(index));
                card.AddToClassList("scenario-card");
                var stripe = Add(card, El("scenario-card__stripe"));
                stripe.style.backgroundColor = s.Tint;
                var thumb = Add(card, El("scenario-card__thumb"));
                thumb.style.backgroundImage = new StyleBackground(PlanetThumb(s.Tint, i));
                var text = Add(card, El("scenario-card__body"));
                text.Add(Text(s.Type, "scenario-card__type", "t-strong"));
                text.Add(Text(s.Title, "scenario-card__title", "t-strong"));
                text.Add(Text(s.Target, "scenario-card__target"));
                var pips = Add(card, El("scenario-card__pips"));
                for (int p = 0; p < 5; p++) pips.Add(El("pip", p < s.Stars ? "pip--on" : null));
                card.RegisterCallback<FocusInEvent>(_ => SelectScenario(index));
                scenarioCards.Add(card);
                list.Add(card);
            }

            var detail = Add(body, El("panel", "detail"));
            detailType = Add(detail, Text("", "eyebrow", "t-strong"));
            detailTitle = Add(detail, Text("", "detail__title", "t-strong"));
            detailTarget = Add(detail, Text("", "detail__target", "t-strong"));
            detailSummary = Add(detail, Text("", "detail__summary"));

            var columns = Add(detail, El("detail__columns"));
            detailGoals = Add(columns, El("grow"));
            var focus = Add(columns, El("focus-box"));
            Add(focus, Text("YOU WILL LEARN", "eyebrow", "t-strong")).style.color = (Color)new Color32(0xA9, 0x8B, 0xFF, 0xFF);
            detailFocus = Add(focus, Text("", "focus-box__text"));

            detail.Add(Text("STARTING RESOURCES  ·  HOW TIGHT EACH ONE IS", "section-label", "t-strong"));
            for (int r = 0; r < MenuCatalog.ResourceNames.Length; r++)
            {
                var row = Add(detail, El("res-row"));
                var swatch = Add(row, El("res-row__swatch"));
                swatch.style.backgroundColor = MenuCatalog.ResourceColors[r];
                row.Add(Text(MenuCatalog.ResourceNames[r], "res-row__name", "t-strong"));
                var track = Add(row, El("res-row__track"));
                var fill = Add(track, El("res-row__fill"));
                fill.style.backgroundColor = MenuCatalog.ResourceColors[r];
                var value = Add(row, Text("", "res-row__value", "t-mono"));
                var critical = Add(row, Text("CRITICAL", "res-row__critical", "t-strong"));
                row.tooltip = MenuCatalog.ResourceHints[r];
                resourceRows.Add((fill, value, critical));
            }
            var legend = Add(detail, El("res-legend"));
            legend.Add(Text("GENEROUS", "res-legend__label"));
            legend.Add(Text("TIGHT", "res-legend__label"));

            var config = Add(body, El("config"));
            Add(config, Text("DIFFICULTY", "section-label", "t-strong")).style.marginTop = 0;
            for (int i = 0; i < MenuCatalog.Difficulties.Length; i++)
            {
                var d = MenuCatalog.Difficulties[i];
                int index = i;
                var tile = new Button(() => SelectDifficulty(index));
                tile.AddToClassList("diff-tile");
                var head = Add(tile, El("diff-tile__head"));
                head.Add(Text(d.Title, "diff-tile__title", "t-strong"));
                head.Add(Text(d.Tagline.ToUpperInvariant(), "diff-tile__tag", "t-strong"));
                tile.Add(Text(d.Description, "diff-tile__text"));
                difficultyTiles.Add(tile);
                config.Add(tile);
            }

            Add(config, Text("OPTIONS", "section-label", "t-strong")).style.marginTop = 14;
            ToggleRow(config, "Random mission events", true);
            ToggleRow(config, "Advisor explains each decision", true);
            ToggleRow(config, "Show real-world equivalents", false);

            var cta = Add(config, El("cta-stack"));
            cta.Add(ButtonEl("BEGIN MISSION DESIGN", BeginMission, primary: true));
            cta.Add(ButtonEl("PLAY AS A TEAM", () => Show(TeamScreen)));

            SelectScenario(scenarioIndex);
            SelectDifficulty(difficultyIndex);
        }

        void RefreshScenarioDetail()
        {
            if (detailTitle == null) return;
            var s = MenuCatalog.Scenarios[scenarioIndex];
            detailType.text = s.Type + "   ·   " + DifficultyStars(s.Stars);
            detailTitle.text = s.Title;
            detailTarget.text = s.Target.ToUpperInvariant();
            detailSummary.text = s.Summary;
            detailFocus.text = s.LearningFocus;

            detailGoals.Clear();
            detailGoals.Add(Text("MISSION GOALS", "eyebrow", "eyebrow--cyan", "t-strong"));
            foreach (var g in s.Goals)
            {
                var goal = Add(detailGoals, El("goal"));
                goal.Add(El("goal__mark"));
                goal.Add(Text(g, "goal__text"));
            }
            ApplyFonts(detailGoals);

            for (int r = 0; r < resourceRows.Count; r++)
            {
                var (fill, value, critical) = resourceRows[r];
                fill.style.width = Length.Percent(Mathf.Lerp(6f, 100f, s.Pressure[r]));
                value.text = FormatAllowance(s.Allowance[r], MenuCatalog.ResourceUnits[r]);
                critical.EnableInClassList("res-row__critical--hidden", (int)s.Critical != r || s.Stars == 0);
            }
        }

        static string DifficultyStars(int stars) => stars == 0 ? "NO LIMITS" : "DIFFICULTY " + stars + "/5";

        static string FormatAllowance(float v, string unit)
        {
            string number = v >= 1000f ? v.ToString("#,0").Replace(',', ' ') : v.ToString("0.##");
            return number + " " + unit;
        }

        void ToggleRow(VisualElement parent, string label, bool on)
        {
            var row = new Button();
            row.AddToClassList("toggle-row");
            row.Add(Text(label, "toggle-row__label"));
            var sw = Add(row, El("switch", on ? "switch--on" : null));
            sw.Add(El("switch__knob"));
            row.clicked += () => sw.ToggleInClassList("switch--on");
            parent.Add(row);
        }

        // ─────────────────────────────── team lobby ───────────────────────────────

        void BuildTeam()
        {
            var screen = AddScreen(TeamScreen);
            ScreenHeader(screen, "TEAM MISSION", "ASSEMBLE YOUR CREW",
                "Up to four players, one spacecraft. Each role owns a subsystem — and has to negotiate with the others.");

            var body = Add(screen, El("screen-body", "team-body"));
            body.style.top = 262;

            var bar = Add(body, El("team-bar"));
            var mode = Add(bar, El("team-bar__group"));
            mode.Add(Text("MODE", "team-bar__label", "t-strong"));
            Segmented(mode, new[] { "CO-OP", "VERSUS" }, 0);
            var link = Add(bar, El("team-bar__group"));
            link.Add(Text("PLAY", "team-bar__label", "t-strong"));
            Segmented(link, new[] { "SAME SCREEN", "ONLINE" }, 1);
            var code = Add(bar, El("team-bar__group"));
            code.Add(Text("ROOM CODE", "team-bar__label", "t-strong"));
            code.Add(Text("HZN-7Q4K", "room-code", "t-mono"));
            bar.Add(El("grow"));
            var mission = Add(bar, El("chip"));
            mission.Add(Text("MISSION", "chip__label"));
            mission.Add(Text("QUORIS POLAR MAPPER · ENGINEER", "chip__value", "t-strong"));

            var roles = Add(body, El("roles"));
            string[] players = { "Nova_7", "Kepler", "LunaByte", null };
            string[] meta = { "Host · Level 12", "Level 8", "Level 15", "" };
            bool[] ready = { true, true, false, false };
            string[,] stats = { { "3", "VETO" }, { "38%", "MASS MARGIN" }, { "4", "INSTRUMENTS" }, { "2", "STATIONS" } };
            for (int i = 0; i < MenuCatalog.Roles.Length; i++)
            {
                var role = MenuCatalog.Roles[i];
                bool open = players[i] == null;
                var card = Add(roles, El("role-card", open ? "role-card--open" : null, i == MenuCatalog.Roles.Length - 1 ? "role-card--last" : null));
                var band = Add(card, El("role-card__band"));
                band.style.backgroundColor = role.Tint;
                var inner = Add(card, El("role-card__inner"));
                var avatar = Add(inner, Text(open ? "+" : role.Initials, "role-card__avatar", "t-strong"));
                if (!open)
                {
                    avatar.style.borderTopColor = avatar.style.borderBottomColor = avatar.style.borderLeftColor = avatar.style.borderRightColor = role.Tint;
                    avatar.style.color = role.Tint;
                    avatar.style.backgroundColor = new Color(role.Tint.r, role.Tint.g, role.Tint.b, 0.1f);
                }
                inner.Add(Text(role.Title, "role-card__title", "t-strong"));
                Add(inner, Text(role.Owns, "role-card__owns", "t-strong")).style.color = role.Tint;
                inner.Add(Text(role.Description, "role-card__desc"));
                inner.Add(El("role-card__divider"));
                inner.Add(Text(open ? "Open seat" : players[i], "role-card__player", "t-strong"));
                inner.Add(Text(open ? "Share the room code or press ENTER on a second controller" : meta[i], "role-card__meta", "wrap"));
                if (!open)
                {
                    var st = Add(inner, El("role-card__stats"));
                    var stat = Add(st, El("stat"));
                    stat.Add(Text(stats[i, 0], "stat__value", "t-mono"));
                    stat.Add(Text(stats[i, 1], "stat__label", "t-strong"));
                    var vote = Add(st, El("stat"));
                    vote.Add(Text("1", "stat__value", "t-mono"));
                    vote.Add(Text("VOTE", "stat__label", "t-strong"));
                }
                card.Add(Text(open ? "WAITING FOR CREW" : ready[i] ? "READY" : "DESIGNING…", "ready",
                    open ? "ready--open" : ready[i] ? "ready--yes" : "ready--no", "t-strong"));
            }

            var foot = Add(body, El("team-foot"));
            Add(foot, Text("HOW IT WORKS", "eyebrow", "eyebrow--cyan", "t-strong")).style.marginRight = 8;
            foot.Add(Text("Each role signs off its own subsystem. Changes that cross roles — a heavier camera, a bigger antenna — go to a crew vote. Arguing costs schedule days, and the clock is a resource too.", "team-foot__text"));
            foot.Add(El("grow"));
            foot.Add(ButtonEl("INVITE CREW", () => { }));
            var start = Add(foot, ButtonEl("START MISSION", BeginMission, primary: false, disabled: true));
            start.style.marginLeft = 14;
            start.Add(Text("2/4 READY", "btn__count", "t-mono"));
        }

        void Segmented(VisualElement parent, string[] options, int selected)
        {
            var seg = Add(parent, El("segmented"));
            var items = new List<Button>();
            for (int i = 0; i < options.Length; i++)
            {
                var b = new Button { text = options[i] };
                b.AddToClassList("segmented__item");
                b.AddToClassList("t-strong");
                if (i == selected) b.AddToClassList("segmented__item--on");
                items.Add(b);
                seg.Add(b);
            }
            foreach (var b in items)
                b.clicked += () => { foreach (var o in items) o.EnableInClassList("segmented__item--on", o == b); };
        }

        // ─────────────────────────────── academy ───────────────────────────────

        void BuildAcademy()
        {
            var screen = AddScreen(AcademyScreen);
            ScreenHeader(screen, "ACADEMY", "FLIGHT SCHOOL",
                "Short interactive lessons. Each one explains why a decision matters — and unlocks a part in the hangar.");

            var body = Add(screen, El("screen-body"));
            body.style.top = 268;

            var side = Add(body, El("academy-side"));
            var progress = Add(side, El("panel"));
            progress.Add(Text("YOUR PROGRESS", "eyebrow", "t-strong"));
            int done = 0;
            foreach (var l in MenuCatalog.Lessons) if (l.Progress >= 1f) done++;
            var big = Add(progress, El("row"));
            big.style.alignItems = Align.FlexEnd;
            big.Add(Text(done + "/" + MenuCatalog.Lessons.Length, "big-number", "t-strong"));
            big.Add(Text("lessons complete", "big-number__unit"));
            var bar = Add(progress, El("progress"));
            Add(bar, El("progress__fill")).style.width = Length.Percent(100f * done / MenuCatalog.Lessons.Length);
            progress.Add(Text("Finish \"Payload capacity\" to unlock the heavy-lift launcher.", "panel__text"));

            var next = Add(side, El("panel", "panel--spaced"));
            next.Add(Text("UP NEXT", "eyebrow", "eyebrow--cyan", "t-strong"));
            Add(next, Text("Payload capacity & choosing a rocket", "panel__title", "t-strong")).style.marginTop = 8;
            next.Add(Text("Why a rocket that can lift 20 t to low orbit can only carry 8 t to geostationary orbit.", "panel__text"));
            var resume = Add(next, ButtonEl("RESUME LESSON", () => { }, primary: true));
            resume.style.marginTop = 20;

            var gl = Add(side, El("panel", "panel--spaced"));
            gl.Add(Text("GLOSSARY", "eyebrow", "eyebrow--muted", "t-strong"));
            var terms = Add(gl, El("glossary"));
            foreach (var t in new[] { "Orbit", "Delta-v", "Payload", "Apoapsis", "Data rate", "Margin", "RTG", "Ground station" })
                terms.Add(Text(t, "term"));

            var grid = Add(body, El("lesson-grid"));
            foreach (var l in MenuCatalog.Lessons)
            {
                var card = Add(grid, El("lesson", l.Locked ? "lesson--locked" : null));
                var topic = Add(card, El("lesson__topic"));
                var color = TopicColor(l.Topic);
                var sw = Add(topic, El("res-chip__swatch"));
                sw.style.backgroundColor = color;
                Add(topic, Text(l.Topic, "eyebrow", "t-strong")).style.color = color;
                card.Add(Text(l.Title, "lesson__title", "t-strong"));
                var p = Add(card, El("progress"));
                p.style.marginBottom = 14;
                Add(p, El("progress__fill")).style.width = Length.Percent(l.Progress * 100f);
                var foot = Add(card, El("lesson__foot"));
                string status = l.Locked ? "LOCKED" : l.Progress >= 1f ? "COMPLETE" : l.Progress > 0f ? "CONTINUE" : "START";
                var st = Add(foot, Text(status, "lesson__status", "t-strong"));
                st.style.color = (Color)(l.Locked ? new Color32(0x6E, 0x7C, 0x92, 0xFF)
                    : l.Progress >= 1f ? new Color32(0x5B, 0xE4, 0x9B, 0xFF) : new Color32(0xFF, 0xB5, 0x47, 0xFF));
                foot.Add(Text(l.Minutes, "lesson__time", "t-mono"));
            }
        }

        static Color TopicColor(string topic)
        {
            switch (topic)
            {
                case "POWER": return MenuCatalog.ResourceColors[(int)MissionResource.Power];
                case "MASS": return MenuCatalog.ResourceColors[(int)MissionResource.Mass];
                case "COMMS": return MenuCatalog.ResourceColors[(int)MissionResource.Comms];
                case "SCIENCE": return MenuCatalog.ResourceColors[(int)MissionResource.Data];
                case "LIFETIME": return MenuCatalog.ResourceColors[(int)MissionResource.Lifetime];
                case "LAUNCH": return MenuCatalog.ResourceColors[(int)MissionResource.Budget];
                default: return new Color32(0x7F, 0xD4, 0xFF, 0xFF);
            }
        }

        // ─────────────────────────────── archive ───────────────────────────────

        void BuildArchive()
        {
            var screen = AddScreen(ArchiveScreen);
            ScreenHeader(screen, "MISSION ARCHIVE", "PAST MISSIONS",
                "Every design you simulate is kept with its debrief. Pick two to compare what changed — and what it cost.");

            var body = Add(screen, El("screen-body"));
            body.style.top = 268;

            var table = Add(body, El("panel", "table"));
            var head = Add(table, El("table__row", "table__row--head"));
            Add(head, El("table__check")).style.opacity = 0;
            head.Add(Text("DESIGN", "th", "col-name", "t-strong"));
            head.Add(Text("SCENARIO", "th", "col-scn", "t-strong"));
            head.Add(Text("CREW", "th", "col-crew", "t-strong"));
            head.Add(Text("SCORE", "th", "col-score", "t-strong"));
            head.Add(Text("GRADE", "th", "col-grade", "t-strong"));
            head.Add(Text("RESULT", "th", "col-result", "t-strong"));
            head.Add(Text("DATE", "th", "col-date", "t-strong"));

            ArchiveRow(table, "Ice Hunter III", "Quoris Polar Mapper", "4", "9 120", "A", "SUCCESS", "Oct 31", true);
            ArchiveRow(table, "Ice Hunter II", "Quoris Polar Mapper", "4", "6 410", "C", "PARTIAL", "Oct 29", true);
            ArchiveRow(table, "Featherweight", "Homeworld Weather Watch", "solo", "8 750", "A", "SUCCESS", "Oct 27", false);
            ArchiveRow(table, "Big Ears", "Relay Ring", "2", "7 980", "B", "SUCCESS", "Oct 24", false);
            ArchiveRow(table, "Icarus", "Teonan Sun-Grazer", "solo", "1 205", "F", "LOST", "Oct 22", false);
            ArchiveRow(table, "Dust Devil", "Mora Moon Lander", "3", "5 560", "C", "PARTIAL", "Oct 20", false);
            ArchiveRow(table, "Everything Bagel", "Sandbox", "solo", "—", "—", "SANDBOX", "Oct 18", false);

            var side = Add(body, El("archive-side"));
            var compare = Add(side, El("panel"));
            compare.Add(Text("COMPARE  ·  ICE HUNTER II → III", "eyebrow", "eyebrow--cyan", "t-strong"));
            compare.Add(Text("Swapping the radar for a lighter spectrometer saved 140 kg — enough for a cheaper rocket.", "panel__text"));
            CompareRow(compare, MissionResource.Mass, "1 340 → 1 200 kg", 0.92f, 0.82f);
            CompareRow(compare, MissionResource.Power, "860 → 640 W", 0.95f, 0.71f);
            CompareRow(compare, MissionResource.Budget, "455 → 398 M$", 1.0f, 0.88f);
            CompareRow(compare, MissionResource.Data, "1.6 → 2.3 Tb", 0.55f, 0.8f);
            var cmp = Add(compare, ButtonEl("OPEN FULL DEBRIEF", () => { }, primary: true));
            cmp.style.marginTop = 22;

            var share = Add(side, El("panel", "panel--spaced"));
            share.Add(Text("SHARE A DESIGN", "eyebrow", "t-strong"));
            share.Add(Text("Send a design code to another team so they can fly your spacecraft against the same objective.", "panel__text"));
            var code = Add(share, El("row"));
            code.style.marginTop = 16;
            code.Add(Text("ICE3-QPM-8F2A", "room-code", "t-mono"));
            code.Add(El("grow"));
            code.Add(ButtonEl("COPY", () => { }));
        }

        void ArchiveRow(VisualElement table, string name, string scenario, string crew, string score, string grade, string result, string date, bool selected)
        {
            var row = new Button();
            row.AddToClassList("table__row");
            if (selected) row.AddToClassList("table__row--selected");
            var check = Add(row, El("table__check", selected ? "table__check--on" : null));
            row.clicked += () =>
            {
                row.ToggleInClassList("table__row--selected");
                check.ToggleInClassList("table__check--on");
            };
            row.Add(Text(name, "td", "col-name", "t-strong"));
            row.Add(Text(scenario, "td--muted", "col-scn"));
            row.Add(Text(crew, "td--muted", "col-crew"));
            row.Add(Text(score, "td", "col-score", "t-mono"));
            var g = Add(row, Text(grade, "grade", "col-grade", "t-strong"));
            g.style.color = (Color)(grade == "A" ? new Color32(0x5B, 0xE4, 0x9B, 0xFF) : grade == "F" ? new Color32(0xFF, 0x6B, 0x5E, 0xFF)
                : grade == "—" ? new Color32(0x56, 0x63, 0x78, 0xFF) : new Color32(0xFF, 0xC8, 0x57, 0xFF));
            var cell = Add(row, El("col-result"));
            string kind = result == "SUCCESS" ? "result--success" : result == "LOST" ? "result--lost" : result == "PARTIAL" ? "result--partial" : null;
            var chip = Add(cell, Text(result, "result", kind, "t-strong"));
            if (kind == null) chip.style.color = (Color)new Color32(0xC6, 0xF2, 0x6B, 0xFF);
            row.Add(Text(date, "td--muted", "col-date"));
            table.Add(row);
        }

        void CompareRow(VisualElement parent, MissionResource r, string value, float before, float after)
        {
            var label = Add(parent, El("row"));
            label.style.marginTop = 16;
            var sw = Add(label, El("res-row__swatch"));
            sw.style.backgroundColor = MenuCatalog.ResourceColors[(int)r];
            label.Add(Text(MenuCatalog.ResourceNames[(int)r], "res-row__name", "t-strong"));
            label.Add(El("grow"));
            label.Add(Text(value, "res-chip__value", "t-mono"));
            var a = Add(parent, El("progress"));
            a.style.marginTop = 8;
            var fa = Add(a, El("progress__fill"));
            fa.style.width = Length.Percent(before * 100f);
            fa.style.backgroundColor = new Color(1f, 1f, 1f, 0.22f);
            var b = Add(parent, El("progress"));
            b.style.marginTop = 4;
            var fb = Add(b, El("progress__fill"));
            fb.style.width = Length.Percent(after * 100f);
            fb.style.backgroundColor = MenuCatalog.ResourceColors[(int)r];
        }

        // ─────────────────────────────── settings ───────────────────────────────

        void BuildSettings()
        {
            var screen = AddScreen(SettingsScreen);
            ScreenHeader(screen, "SETTINGS", "SETTINGS", null);

            var tabs = Add(screen, El("tabs"));
            tabs.style.top = 220;
            string[] names = { "GAMEPLAY", "ACCESSIBILITY", "DISPLAY", "AUDIO", "CONTROLS" };
            for (int i = 0; i < names.Length; i++) tabs.Add(Text(names[i], "tab", i == 0 ? "tab--on" : null, "t-strong"));

            var body = Add(screen, El("screen-body"));
            body.style.top = 290;

            var list = Add(body, El("panel", "settings-list"));
            SettingRow(list, "Explanation depth", "How much the advisor explains", r => Segmented(r, new[] { "SIMPLE", "DETAILED", "EXPERT" }, 0), focused: true);
            SettingRow(list, "Units", "Shown on every gauge and report", r => Segmented(r, new[] { "SI (kg, W)", "IMPERIAL" }, 0));
            SettingRow(list, "Language", "Interface and lessons", r => Stepper(r, "ENGLISH"));
            SettingRow(list, "Default difficulty", "Can be changed per mission", r => Stepper(r, "ENGINEER"));
            SettingRow(list, "Colour-blind palette", "Resource colours and warnings", r => Stepper(r, "OFF"));
            SettingRow(list, "Interface scale", "Panels and text", r => SliderEl(r, 0.5f, "100%"));
            SettingRow(list, "Simulation speed", "Default time warp during mission playback", r => SliderEl(r, 0.35f, "×20"));
            SettingRow(list, "Master volume", "Music, voice and effects", r => SliderEl(r, 0.8f, "80"));

            var side = Add(body, El("settings-side"));
            var info = Add(side, El("panel"));
            info.Add(Text("EXPLANATION DEPTH", "eyebrow", "eyebrow--cyan", "t-strong"));
            Add(info, Text("Simple", "panel__title", "t-strong")).style.marginTop = 10;
            info.Add(Text("Plain-language explanations with everyday comparisons: \"This camera needs as much power as two light bulbs.\" Recommended for first missions and classrooms.", "panel__text"));
            info.Add(Text("Detailed adds the formulas; Expert shows raw engineering values and margins only.", "panel__text"));

            var preview = Add(side, El("panel", "panel--spaced"));
            preview.Add(Text("PREVIEW", "eyebrow", "eyebrow--muted", "t-strong"));
            var row = Add(preview, El("row"));
            row.style.marginTop = 12;
            var sw = Add(row, El("res-row__swatch"));
            sw.style.backgroundColor = MenuCatalog.ResourceColors[(int)MissionResource.Power];
            row.Add(Text("POWER", "res-row__name", "t-strong"));
            row.Add(El("grow"));
            row.Add(Text("540 / 650 W", "res-chip__value", "t-mono"));
            var bar = Add(preview, El("progress"));
            var fill = Add(bar, El("progress__fill"));
            fill.style.width = Length.Percent(83f);
            fill.style.backgroundColor = MenuCatalog.ResourceColors[(int)MissionResource.Power];
            preview.Add(Text("Your instruments use 83% of the power your solar panels make. In the planet's shadow the batteries must cover the rest.", "panel__text"));

            var actions = Add(side, El("row"));
            actions.style.marginTop = 18;
            actions.Add(ButtonEl("RESET DEFAULTS", () => { }));
            actions.Add(El("grow"));
            actions.Add(ButtonEl("APPLY", Back, primary: true));
        }

        void SettingRow(VisualElement parent, string label, string hint, Action<VisualElement> control, bool focused = false)
        {
            var row = Add(parent, El("setting", focused ? "setting--focused" : null));
            var text = Add(row, El("grow"));
            text.Add(Text(label, "setting__label", "t-strong"));
            text.Add(Text(hint, "setting__hint"));
            control(row);
        }

        void Stepper(VisualElement parent, string value)
        {
            var s = Add(parent, El("stepper"));
            s.Add(Text("<", "stepper__arrow", "t-strong"));
            s.Add(Text(value, "stepper__value", "t-strong"));
            s.Add(Text(">", "stepper__arrow", "t-strong"));
        }

        void SliderEl(VisualElement parent, float value, string label)
        {
            var s = Add(parent, El("slider"));
            var track = Add(s, El("slider__track"));
            Add(track, El("slider__fill")).style.width = Length.Percent(value * 100f);
            Add(track, El("slider__knob")).style.left = Length.Percent(value * 100f);
            s.Add(Text(label, "slider__value", "t-mono"));
        }

        // ─────────────────────────────── chrome ───────────────────────────────

        void BuildHudTop()
        {
            var top = Add(root, El("hud-top"));
            var mark = Add(top, El("brand-mark"));
            mark.Add(El("brand-mark__core"));
            top.Add(Text("HORIZON", "brand-name", "t-strong"));
            top.Add(Text("MISSION CONTROL  ·  HOME SYSTEM", "brand-sub"));
            top.Add(El("grow"));

            var link = Add(top, El("chip"));
            link.Add(El("status-dot"));
            link.Add(Text("UPLINK", "chip__label", "t-strong"));
            link.Add(Text("ONLINE", "chip__value", "t-strong"));

            var crew = Add(top, El("chip"));
            crew.Add(Text("CREW", "chip__label", "t-strong"));
            crew.Add(Text("3 / 4", "chip__value", "t-mono"));

            var profile = Add(top, El("chip"));
            profile.Add(Text("CM", "avatar-small", "t-strong"));
            Add(profile, Text("COMMANDER", "chip__value", "t-strong")).style.marginLeft = 0;
            Add(profile, Text("LV 7", "chip__label", "t-mono")).style.marginLeft = 10;
            var xp = Add(profile, El("xp-track"));
            xp.Add(El("xp-fill"));
        }

        Label hintBack, hintNavigate, hintSelect;

        void BuildHudBottom()
        {
            var bottom = Add(root, El("hud-bottom"));
            hintNavigate = KeyHint(bottom, "ARROWS", "NAVIGATE");
            hintSelect = KeyHint(bottom, "ENTER", "SELECT");
            hintBack = KeyHint(bottom, "ESC", "BACK");
            bottom.Add(El("grow"));
            bottom.Add(Text("BUILD 0.2.0  ·  PHASE 1 FLIGHT CORE  ·  NASA SPACE APPS 2026", "build-info", "t-mono"));
        }

        Label KeyHint(VisualElement parent, string key, string label)
        {
            var hint = Add(parent, El("key-hint"));
            hint.Add(Text(key, "key-cap", "t-strong"));
            return Add(hint, Text(label, "key-hint__label", "t-strong"));
        }

        void RefreshHints()
        {
            if (hintBack == null) return;
            hintBack.parent.style.display = current == MainScreen ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void BuildQuitModal()
        {
            modal = Add(root, El("modal"));
            var box = Add(modal, El("panel", "modal__box"));
            box.Add(Text("END SESSION", "eyebrow", "t-strong"));
            Add(box, Text("Leave Mission Control?", "panel__title", "t-strong")).style.fontSize = 34;
            box.Add(Text("Your current design is autosaved. Team members will keep the lobby open for 10 minutes.", "panel__text"));
            var buttons = Add(box, El("modal__buttons"));
            buttons.Add(ButtonEl("STAY", Back));
            buttons.Add(ButtonEl("QUIT TO DESKTOP", Quit, primary: true));
        }

        void ScreenHeader(VisualElement screen, string crumb, string title, string subtitle)
        {
            var head = Add(screen, El("screen-head"));
            var back = new Button(Back);
            back.AddToClassList("back-btn");
            back.Add(Text("ESC", "key-cap", "t-strong"));
            back.Add(Text("BACK", "key-hint__label", "t-strong"));
            head.Add(back);
            var text = Add(head, El("grow"));
            text.Add(Text("MAIN MENU  /  " + crumb, "eyebrow", "t-strong"));
            text.Add(Text(title, "screen-title", "t-strong"));
            if (subtitle != null) text.Add(Text(subtitle, "screen-sub"));
        }

        VisualElement AddScreen(string id)
        {
            var screen = Add(root, El("screen"));
            screen.name = id;
            screens[id] = screen;
            return screen;
        }

        // ─────────────────────────────── helpers ───────────────────────────────

        static T Add<T>(VisualElement parent, T child) where T : VisualElement
        {
            parent.Add(child);
            return child;
        }

        static VisualElement El(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) e.AddToClassList(c);
            return e;
        }

        static Label Text(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) l.AddToClassList(c);
            return l;
        }

        Button ButtonEl(string label, Action onClick, bool primary = false, bool disabled = false)
        {
            var b = new Button(onClick);
            b.AddToClassList("btn");
            if (primary) b.AddToClassList("btn--primary");
            if (disabled) b.AddToClassList("btn--disabled");
            b.Add(Text(label, "btn__label", "t-strong"));
            return b;
        }

        /// <summary>Small shaded sphere for scenario cards.</summary>
        static Texture2D PlanetThumb(Color tint, int seed)
        {
            const int n = 96;
            var px = new Color32[n * n];
            var light = new Vector3(-0.6f, 0.55f, 0.58f).normalized;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r2 = u * u + v * v;
                if (r2 > 1f) { px[y * n + x] = new Color32(0, 0, 0, 0); continue; }
                var normal = new Vector3(u, v, Mathf.Sqrt(1f - r2));
                float band = 0.5f + 0.5f * Mathf.Sin(v * (5f + seed) + Mathf.Sin(u * 3f + seed) * 1.5f);
                float d = Mathf.Clamp01(Vector3.Dot(normal, light)) * 0.9f + 0.06f;
                var c = Color.Lerp(tint * 0.55f, tint, band) * d;
                c += new Color(0.5f, 0.6f, 1f) * Mathf.Pow(1f - normal.z, 3f) * 0.5f;
                c.a = Mathf.Clamp01((1f - Mathf.Sqrt(r2)) * n * 0.5f);
                px[y * n + x] = c;
            }
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        // ─────────────────────────────── fonts ───────────────────────────────

        /// <summary>
        /// Uses Bahnschrift (ships with Windows) for the DIN-style look and Consolas for numbers; falls back to the
        /// built-in font elsewhere. TODO: bundle an OFL font asset so every platform looks the same.
        /// </summary>
        void ApplyFonts(VisualElement scope)
        {
            if (scope == root)
            {
                var regular = LoadFont(("Bahnschrift", "Regular"), ("Segoe UI", "Regular"));
                if (regular.HasValue) root.style.unityFontDefinition = regular.Value;
                else root.style.unityFontDefinition = FontDefinition.FromFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
                var strong = LoadFont(("Bahnschrift", "SemiBold"), ("Bahnschrift", "Bold"), ("Segoe UI", "Semibold"));
                hasStrong = strong.HasValue;
                if (hasStrong) fontStrong = strong.Value;
                var mono = LoadFont(("Consolas", "Regular"), ("Cascadia Mono", "Regular"));
                hasMono = mono.HasValue;
                if (hasMono) fontMono = mono.Value;
                // Classes are applied after the screens are built.
                root.schedule.Execute(() => ApplyFonts(root.parent));
                return;
            }
            scope.Query(className: "t-strong").ForEach(e =>
            {
                if (hasStrong) e.style.unityFontDefinition = fontStrong;
                else e.style.unityFontStyleAndWeight = FontStyle.Bold;
            });
            if (hasMono) scope.Query(className: "t-mono").ForEach(e => e.style.unityFontDefinition = fontMono);
        }

        static FontDefinition? LoadFont(params (string family, string style)[] candidates)
        {
            foreach (var (family, style) in candidates)
            {
                try
                {
                    var asset = FontAsset.CreateFontAsset(family, style);
                    if (asset != null) return FontDefinition.FromSDFFont(asset);
                }
                catch (Exception)
                {
                    // Font not installed on this machine — try the next one.
                }
            }
            return null;
        }
    }
}
