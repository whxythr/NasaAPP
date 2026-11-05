using System;
using System.Collections.Generic;
using SpaceSim.Academy;
using SpaceSim.Economy;
using SpaceSim.Units;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Flight Academy window (IMGUI). Three tabs — «Темы» (13 lessons: formulas with their variables, the worked example with
    /// the player's planet and rocket, use in the game, limits, an exercise), «Калькуляторы» (the Level A calculators) and
    /// «Подсказки» (contextual lessons the advisor raised during the flight).
    /// Every number comes from the Core Academy: lesson texts from <see cref="AcademyTopics"/>, results from
    /// <see cref="AcademyCalculator.Compute"/>, verdicts from <see cref="AcademyExercise.Check(string)"/>, hints from
    /// <see cref="AcademyAdvisor"/>. This class only lays out text and forwards the player's input; progress goes through
    /// <see cref="PlayerSession"/>.
    /// </summary>
    /// <remarks>
    /// IMGUI rule followed here (same as <see cref="ShopUI"/> / <see cref="HangarUI"/>): everything that changes which controls
    /// are drawn (tab, selection, reveals, recomputed results, lessons rebuilt for a new planet or rocket) is applied at the
    /// start of a Layout pass or queued with <see cref="Defer"/> and applied after the whole pass, so a Layout pass and the
    /// event pass after it always draw the same controls. Typed text goes straight into per-field string buffers.
    /// </remarks>
    [DefaultExecutionOrder(217)]
    public sealed class AcademyUI : MonoBehaviour
    {
        const float PreferredWidth = 1040f;
        const float PreferredHeight = 660f;
        const float PreferredListWidth = 260f;

        const string AnswerControl = "academy.answer";
        const string InputControlPrefix = "academy.input.";
        /// <summary>Bind(): keep every field of the calculator (the player is typing in one of them, which one is unknown).</summary>
        const int KeepAllFields = -2;

        const string HeaderText = "ЛЁТНАЯ АКАДЕМИЯ · физика полёта на практике";
        const string SubtitleText =
            "Все расчёты — те же функции, что считают полёт. Уровень A — идеальная теория; полёт (уровень B) учитывает потери.";
        const string ReadOnlyText =
            "Профиль только для чтения (сохранение от более новой версии игры): прочитанные темы и решённые задачи не сохранятся после выхода.";
        static string FooterText => "Подсказки появляются по ходу полёта · " + FlightInput.KeyLabel(InputKey.L) + " — открыть Академию";
        const string EmptyHintsText =
            "Пока подсказок нет. Академия реагирует на полёт: например, если тяга/вес меньше 1 или топливо кончилось до орбиты.";
        const string DefaultsLabel = "По умолчанию";
        const string StepsLockedText = "Решение по шагам откроется после всех подсказок.";

        enum Tab { Topics, Calculators, Hints }

        AcademyService service;
        PlayerSession session;
        FlightStateController state;
        bool open;
        /// <summary>Opened mid-frame (e.g. from a HUD button): draw nothing until a Layout pass has seen the window.</summary>
        bool awaitingLayout;
        Tab tab = Tab.Topics;

        // Navigation requested from outside (OpenTopic/OpenCalculator/OpenHint) or by an in-window link; applied on Layout.
        bool hasRequest;
        Tab requestTab;
        string requestTopic, requestCalc;
        AcademyHint requestHint;

        // Content snapshot (the service rebuilds lessons and calculators when the planet or the rocket design changes).
        int contentVersion = int.MinValue;
        IReadOnlyList<AcademyTopic> topics;
        IReadOnlyList<AcademyCalculator> calculators;
        string selectedTopicId, selectedCalcId;
        AcademyHint selectedHint;

        /// <summary>Per exercise id: typed answer, last verdict and how many hints / solution steps are revealed.</summary>
        readonly Dictionary<string, ExerciseState> exercises = new Dictionary<string, ExerciseState>();
        /// <summary>Per calculator id: one text buffer per input index, plus the last result.</summary>
        readonly Dictionary<string, CalcState> calcs = new Dictionary<string, CalcState>();

        // View snapshot (rebuilt on Layout when something changed).
        bool viewDirty = true;
        readonly List<TopicRow> topicRows = new List<TopicRow>();
        readonly TopicView topicView = new TopicView();
        readonly List<HintRow> hintRows = new List<HintRow>();
        readonly HintView hintView = new HintView();
        readonly HashSet<string> seenHintIds = new HashSet<string>();
        int hintCount = -1;
        AcademyHint hintLatest;
        string progressText = "";
        string hintsTabLabel = "Подсказки (0)";
        string calcTopicId, calcTopicLabel;
        GUILayoutOption[] calcTopicOptions;

        Vector2 topicListScroll, topicScroll, calcListScroll, calcScroll, hintListScroll, hintScroll;
        readonly List<Action> pending = new List<Action>();

        // Layout metrics and cached layout options (no per-pass allocations).
        float metricsW = -1f, metricsH = -1f, textW;
        GUILayoutOption[] optTab, optListScroll, optListItem, optDetailScroll, optText, optPanelText;
        GUILayoutOption[] optVarSymbol, optVarMeaning, optVarUnit, optInputLabel, optInputField;
        GUILayoutOption[] optOutLabel, optOutSymbol, optOutValue, optAnswerLabel, optAnswerField;
        GUILayoutOption[] optCheck, optReveal, optStep, optCalcButton, optFooterNote, optClose;

        GUIStyle text, textWhite, header, headerWhite, formula, strong, verdict, note, smallLine, cell, symbol, listButton;
        GUIContent measure;

        static readonly List<string> InputNames = new List<string>();

        sealed class ExerciseState
        {
            public string Answer = "";
            public int Hints, Steps;
            public string Message = "";
            public Color MessageColor = Color.white;
        }

        sealed class CalcState
        {
            public AcademyCalculator Calc;
            public string[] Fields = new string[0];
            public string[] RowLabels = new string[0];
            public bool[] Invalid = new bool[0];
            public double[] Values = new double[0];
            /// <summary>A field changed: recompute on the next Layout pass.</summary>
            public bool Dirty = true;
            public string Error;
            public bool HasResult;
            public readonly List<string> OutLabel = new List<string>();
            public readonly List<string> OutSymbol = new List<string>();
            public readonly List<string> OutText = new List<string>();
            public readonly List<string> Steps = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public readonly List<string> Notes = new List<string>();
            /// <summary>Preset buttons wrapped into rows for the width <see cref="ChipsFor"/>; chip 0 = defaults.</summary>
            public float ChipsFor = -1f;
            public readonly List<List<int>> ChipRows = new List<List<int>>();
            public string[] ChipLabels = new string[0];
            public GUILayoutOption[][] ChipOptions = new GUILayoutOption[0][];
        }

        sealed class TopicRow
        {
            public string Id, Label;
        }

        sealed class TopicView
        {
            public AcademyTopic Topic;
            public string Title = "";
            public readonly List<string> CalcIds = new List<string>();
            public readonly List<string> CalcLabels = new List<string>();
            public readonly List<GUILayoutOption[]> CalcOptions = new List<GUILayoutOption[]>();
            public AcademyExercise Exercise;
            public ExerciseState State;
            public bool Solved;
            public string AnswerLabel = "";
            public readonly List<string> ShownHints = new List<string>();
            public readonly List<string> ShownSteps = new List<string>();
            public bool HasHints, CanHint, HasSteps, CanStep;
            public string HintButton = "", StepButton = "";
        }

        sealed class HintRow
        {
            public AcademyHint Hint;
            public bool Active;
            public string Label;
            public Color Color;
        }

        sealed class HintView
        {
            public AcademyHint Hint;
            public Color Color = Color.white;
            public string Title = "", Meta = "", Message = "";
            public readonly List<string> ShownHints = new List<string>();
            public readonly List<string> ShownSteps = new List<string>();
            public bool HasHints, CanHint, HasSteps, CanStep;
            public string HintButton = "", StepButton = "";
            public string TopicId, TopicLabel, CalcId, CalcLabel;
            public GUILayoutOption[] TopicOptions, CalcOptions;
        }

        // ---- Public API ----------------------------------------------------------------------

        public bool IsOpen => open;

        /// <summary>
        /// Raised with the new state whenever the window opens or closes — from the HUD, the close button, Esc, or the
        /// component being disabled.
        /// </summary>
        public event Action<bool> OpenChanged;

        public void Initialize(AcademyService academyService, PlayerSession playerSession, FlightStateController flightState)
        {
            if (session != null) session.Changed -= OnProfileChanged;
            service = academyService;
            session = playerSession;
            state = flightState;
            if (session != null) session.Changed += OnProfileChanged;
            contentVersion = int.MinValue;
            topics = null;
            calculators = null;
            hintCount = -1;
            hintLatest = null;
            viewDirty = true;
        }

        /// <summary>Opens the Academy on the last used tab, or closes it.</summary>
        public void Toggle()
        {
            if (open) Close();
            else Open();
        }

        /// <summary>Opens the «Темы» tab on that lesson.</summary>
        public void OpenTopic(string topicId)
        {
            Request(Tab.Topics, topicId, null, null);
            Open();
        }

        /// <summary>Opens the «Калькуляторы» tab on that calculator.</summary>
        public void OpenCalculator(string calculatorId)
        {
            Request(Tab.Calculators, null, calculatorId, null);
            Open();
        }

        /// <summary>Opens the «Подсказки» tab with that hint selected.</summary>
        public void OpenHint(AcademyHint hint)
        {
            Request(Tab.Hints, null, null, hint);
            Open();
        }

        void OnDisable()
        {
            // A disabled window draws nothing: report it closed so the HUD resumes the flight and its hotkeys.
            if (open) Close();
        }

        void OnDestroy()
        {
            if (session != null) session.Changed -= OnProfileChanged;
        }

        void OnProfileChanged() => viewDirty = true;

        void Open()
        {
            if (open || service == null) return;
            pending.Clear();
            awaitingLayout = true;
            viewDirty = true;
            SetOpen(true);
        }

        void Close()
        {
            pending.Clear();
            hasRequest = false;
            GUIUtility.keyboardControl = 0;
            SetOpen(false);
        }

        void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            OpenChanged?.Invoke(open);
        }

        void Request(Tab t, string topicId, string calcId, AcademyHint hint)
        {
            hasRequest = true;
            requestTab = t;
            requestTopic = topicId;
            requestCalc = calcId;
            requestHint = hint;
        }

        // ---- Deferred actions (applied between IMGUI passes) ---------------------------------

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
            viewDirty = true;
        }

        void SwitchTab(Tab t)
        {
            if (tab == t) return;
            tab = t;
            GUIUtility.keyboardControl = 0;
            viewDirty = true;
        }

        void SelectTopic(string id)
        {
            if (id == selectedTopicId) return;
            selectedTopicId = id;
            topicScroll = Vector2.zero;
            GUIUtility.keyboardControl = 0;
            viewDirty = true;
        }

        void SelectCalc(string id)
        {
            if (id == selectedCalcId) return;
            selectedCalcId = id;
            calcScroll = Vector2.zero;
            GUIUtility.keyboardControl = 0;
            viewDirty = true;
        }

        void SelectHint(AcademyHint hint)
        {
            if (ReferenceEquals(hint, selectedHint)) return;
            selectedHint = hint;
            hintScroll = Vector2.zero;
            GUIUtility.keyboardControl = 0;
            viewDirty = true;
        }

        void CheckExercise(AcademyExercise ex, string topicTitle)
        {
            if (ex == null) return;
            ExerciseState es = Exercise(ex.Id);
            ExerciseCheck check = ex.Check(es.Answer ?? "");
            es.Message = check.Message ?? "";
            es.MessageColor = VerdictColor(check.Verdict);
            AcademyProgress progress = Progress();
            if (check.Solved && progress != null && !progress.HasSolved(ex.Id))
            {
                session.MarkExerciseSolved(ex.Id);
                if (state != null)
                    state.Notify("Академия: задача по теме «" + topicTitle + "» решена ★" +
                                 (session.ReadOnly ? " (профиль только для чтения — не сохранится)" : ""));
            }
        }

        static void ApplyValues(CalcState s, double[] values)
        {
            if (s == null || values == null) return;
            GUIUtility.keyboardControl = 0; // a focused field would keep editing the old text
            for (int i = 0; i < s.Fields.Length && i < values.Length; i++) s.Fields[i] = AcademyNumber.FormatEditable(values[i]);
            s.Dirty = true;
        }

        // ---- Snapshot (Layout pass only) -------------------------------------------------------

        void Prepare()
        {
            SyncContent();
            ApplyRequest();
            EnsureSelection();
            MarkSelectedTopicRead();
            SyncHints();
            CalcState calc = SelectedCalc();
            if (calc != null)
            {
                if (calc.Dirty) Recompute(calc);
                if (calc.ChipsFor != textW) LayoutChips(calc);
            }
            if (viewDirty) RebuildView();
        }

        /// <summary>
        /// Picks up lessons and calculators rebuilt for a new planet or rocket design: exercise reveals reset (the numbers in the
        /// questions changed) and calculator fields are re-seeded with the new defaults — except a field the player is typing in.
        /// </summary>
        void SyncContent()
        {
            IReadOnlyList<AcademyTopic> t = service.Topics;
            IReadOnlyList<AcademyCalculator> c = service.Calculators;
            int version = service.ContentVersion;
            if (version == contentVersion && ReferenceEquals(t, topics) && ReferenceEquals(c, calculators)) return;
            contentVersion = version;
            topics = t;
            calculators = c;
            viewDirty = true;

            bool typing = GUIUtility.keyboardControl != 0;
            string focused = typing ? GUI.GetNameOfFocusedControl() ?? "" : "";
            bool focusUnknown = typing && focused.Length == 0;

            string keepAnswerId = null;
            if (tab == Tab.Topics && (focusUnknown || focused == AnswerControl))
            {
                AcademyTopic selected = TopicById(selectedTopicId);
                if (selected != null && selected.Exercise != null) keepAnswerId = selected.Exercise.Id;
            }
            ExerciseState kept = null;
            if (keepAnswerId != null) exercises.TryGetValue(keepAnswerId, out kept);
            exercises.Clear();
            if (kept != null) exercises[keepAnswerId] = new ExerciseState { Answer = kept.Answer };

            int keep = -1;
            if (tab == Tab.Calculators && typing)
            {
                if (focusUnknown) keep = KeepAllFields;
                else
                    for (int i = 0; i < InputNames.Count; i++)
                        if (focused == InputNames[i])
                        {
                            keep = i;
                            break;
                        }
            }
            if (calculators == null) return;
            for (int i = 0; i < calculators.Count; i++)
            {
                AcademyCalculator calc = calculators[i];
                if (calc == null) continue;
                if (!calcs.TryGetValue(calc.Id, out CalcState s))
                {
                    s = new CalcState();
                    calcs[calc.Id] = s;
                }
                Bind(s, calc, calc.Id == selectedCalcId ? keep : -1);
            }
        }

        static void Bind(CalcState s, AcademyCalculator calc, int keep)
        {
            IReadOnlyList<CalculatorInput> inputs = calc.Inputs;
            int n = inputs.Count;
            if (s.Calc == null || s.Fields.Length != n)
            {
                s.Fields = new string[n];
                s.RowLabels = new string[n];
                s.Invalid = new bool[n];
                s.Values = new double[n];
                keep = -1;
            }
            s.Calc = calc;
            double[] defaults = calc.Defaults();
            for (int i = 0; i < n; i++)
            {
                CalculatorInput input = inputs[i];
                s.RowLabels[i] = input.Label + " (" + input.Symbol + ")" + (input.Unit.Length > 0 ? ", " + input.Unit : "");
                if (keep == KeepAllFields || keep == i) continue; // the player is typing there
                s.Fields[i] = AcademyNumber.FormatEditable(defaults[i]);
            }
            s.ChipsFor = -1f;
            s.Dirty = true;
        }

        void ApplyRequest()
        {
            if (!hasRequest) return;
            hasRequest = false;
            SwitchTab(requestTab);
            switch (requestTab)
            {
                case Tab.Topics:
                    if (TopicById(requestTopic) != null) SelectTopic(requestTopic);
                    break;
                case Tab.Calculators:
                    if (CalcById(requestCalc) != null) SelectCalc(requestCalc);
                    break;
                default:
                    if (requestHint != null) SelectHint(requestHint);
                    break;
            }
            requestTopic = requestCalc = null;
            requestHint = null;
            viewDirty = true;
        }

        void EnsureSelection()
        {
            if (topics != null && topics.Count > 0 && TopicById(selectedTopicId) == null)
            {
                selectedTopicId = topics[0] != null ? topics[0].Id : null;
                viewDirty = true;
            }
            if (calculators != null && calculators.Count > 0 && CalcById(selectedCalcId) == null)
            {
                selectedCalcId = calculators[0] != null ? calculators[0].Id : null;
                viewDirty = true;
            }
            IReadOnlyList<AcademyHint> history = service.Advisor.History;
            if (selectedHint == null && history.Count > 0)
            {
                selectedHint = history[0];
                viewDirty = true;
            }
        }

        /// <summary>A lesson counts as read once it has been shown (saved once through the session).</summary>
        void MarkSelectedTopicRead()
        {
            if (tab != Tab.Topics || string.IsNullOrEmpty(selectedTopicId)) return;
            AcademyProgress progress = Progress();
            if (progress == null || progress.HasRead(selectedTopicId)) return;
            session.MarkTopicRead(selectedTopicId); // raises Changed; the view is rebuilt below in this Layout pass
            viewDirty = true;
        }

        void SyncHints()
        {
            AcademyAdvisor advisor = service.Advisor;
            IReadOnlyList<AcademyHint> history = advisor.History;
            AcademyHint latest = history.Count > 0 ? history[0] : null;
            if (history.Count != hintCount || !ReferenceEquals(latest, hintLatest))
            {
                hintCount = history.Count;
                hintLatest = latest;
                hintsTabLabel = "Подсказки (" + hintCount + ")";
                hintRows.Clear();
                for (int i = 0; i < history.Count; i++)
                    if (history[i] != null)
                        hintRows.Add(new HintRow { Hint = history[i], Color = SeverityColor(history[i].Severity) });
                viewDirty = true;
            }
            // «активно» marks the newest entry of a rule whose condition still holds.
            seenHintIds.Clear();
            for (int i = 0; i < hintRows.Count; i++)
            {
                HintRow row = hintRows[i];
                bool active = seenHintIds.Add(row.Hint.Id ?? "") && advisor.IsActive(row.Hint.Id);
                if (row.Label != null && active == row.Active) continue;
                row.Active = active;
                row.Label = (row.Hint.Severity == HintSeverity.Warning ? "⚠ " : "") + row.Hint.Title + "\n" + TimeText(row.Hint.RaisedAt) +
                            (active ? " · активно" : "");
                if (ReferenceEquals(row.Hint, selectedHint)) viewDirty = true;
            }
        }

        /// <summary>Runs the calculator (Core) on the typed values; the results are kept as display strings.</summary>
        static void Recompute(CalcState s)
        {
            s.Dirty = false;
            s.Error = null;
            s.HasResult = false;
            s.OutLabel.Clear();
            s.OutSymbol.Clear();
            s.OutText.Clear();
            s.Steps.Clear();
            s.Warnings.Clear();
            s.Notes.Clear();
            if (s.Calc == null) return;

            IReadOnlyList<CalculatorInput> inputs = s.Calc.Inputs;
            string bad = null;
            int badCount = 0;
            for (int i = 0; i < s.Fields.Length; i++)
            {
                bool ok = AcademyNumber.TryParse(s.Fields[i], out double v);
                s.Invalid[i] = !ok;
                s.Values[i] = ok ? v : double.NaN;
                if (ok) continue;
                badCount++;
                string name = "«" + inputs[i].Label + "» (" + inputs[i].Symbol + ")";
                bad = bad == null ? name : bad + ", " + name;
            }
            if (badCount > 0)
            {
                s.Error = (badCount == 1 ? "Поле " + bad + " — не число." : "Не числа в полях: " + bad + ".") +
                          " Вводите, например: 12.5, 12,5 или 3.2e4.";
                return;
            }

            CalculatorResult r;
            try
            {
                r = s.Calc.Compute(s.Values);
            }
            catch (Exception e)
            {
                s.Error = "Расчёт невозможен: " + e.Message;
                return;
            }
            if (r == null || !r.Ok)
            {
                s.Error = r != null ? r.Error : "Расчёт не дал результата.";
                return;
            }
            s.HasResult = true;
            foreach (CalculatorOutput o in r.Outputs)
            {
                s.OutLabel.Add(o.Label);
                s.OutSymbol.Add(o.Symbol);
                s.OutText.Add(o.DisplayText);
            }
            for (int i = 0; i < r.Steps.Count; i++) s.Steps.Add((i + 1) + ". " + r.Steps[i]);
            foreach (string w in r.Warnings) s.Warnings.Add("⚠ " + w);
            s.Notes.AddRange(r.Notes);
        }

        /// <summary>Wraps «По умолчанию» and the preset buttons into rows that fit the panel width.</summary>
        void LayoutChips(CalcState s)
        {
            s.ChipsFor = textW;
            s.ChipRows.Clear();
            List<CalculatorPreset> presets = s.Calc != null ? s.Calc.Presets : null;
            int n = 1 + (presets != null ? presets.Count : 0);
            s.ChipLabels = new string[n];
            s.ChipOptions = new GUILayoutOption[n][];
            float max = Mathf.Max(120f, textW - 8f);
            float used = 0f;
            List<int> row = null;
            for (int i = 0; i < n; i++)
            {
                string label = i == 0 ? DefaultsLabel : presets[i - 1].Name;
                measure.text = label;
                float width = Mathf.Min(max, Mathf.Max(96f, HudStyle.Button.CalcSize(measure).x + 14f));
                if (row == null || used + width + 4f > max)
                {
                    row = new List<int>();
                    s.ChipRows.Add(row);
                    used = 0f;
                }
                row.Add(i);
                used += width + 4f;
                s.ChipLabels[i] = label;
                s.ChipOptions[i] = new[] { GUILayout.Width(width), GUILayout.Height(24) };
            }
        }

        void RebuildView()
        {
            viewDirty = false;
            AcademyProgress progress = Progress();

            topicRows.Clear();
            int read = 0, solved = 0, exerciseCount = 0;
            if (topics != null)
            {
                for (int i = 0; i < topics.Count; i++)
                {
                    AcademyTopic t = topics[i];
                    if (t == null) continue;
                    bool isRead = progress != null && progress.HasRead(t.Id);
                    bool isSolved = t.Exercise != null && progress != null && progress.HasSolved(t.Exercise.Id);
                    if (isRead) read++;
                    if (t.Exercise != null) exerciseCount++;
                    if (isSolved) solved++;
                    topicRows.Add(new TopicRow { Id = t.Id, Label = t.Number + ". " + t.Title + (isRead ? "  ✓" : "") + (isSolved ? " ★" : "") });
                }
            }
            progressText = progress == null
                ? "Профиль не загружен — прогресс не сохраняется"
                : "Темы: " + read + "/" + topicRows.Count + " · Задачи: " + solved + "/" + exerciseCount;

            BuildTopicView(progress);
            BuildCalcView();
            BuildHintView();
        }

        void BuildTopicView(AcademyProgress progress)
        {
            TopicView v = topicView;
            v.Topic = TopicById(selectedTopicId);
            v.CalcIds.Clear();
            v.CalcLabels.Clear();
            v.CalcOptions.Clear();
            v.ShownHints.Clear();
            v.ShownSteps.Clear();
            v.Exercise = null;
            v.State = null;
            AcademyTopic t = v.Topic;
            if (t == null) return;
            v.Title = t.Number + ". " + t.Title;
            foreach (string id in t.CalculatorIds)
            {
                AcademyCalculator calc = CalcById(id);
                if (calc == null) continue;
                string label = "Открыть калькулятор: " + calc.Title;
                v.CalcIds.Add(id);
                v.CalcLabels.Add(label);
                v.CalcOptions.Add(LinkOptions(label));
            }

            AcademyExercise ex = t.Exercise;
            if (ex == null) return;
            ExerciseState es = Exercise(ex.Id);
            es.Hints = Math.Max(0, Math.Min(es.Hints, ex.Hints.Count));
            es.Steps = Math.Max(0, Math.Min(es.Steps, ex.SolutionSteps.Count));
            v.Exercise = ex;
            v.State = es;
            v.Solved = progress != null && progress.HasSolved(ex.Id);
            v.AnswerLabel = string.IsNullOrEmpty(ex.Unit) ? "Ответ:" : "Ответ, " + ex.Unit + ":";
            for (int i = 0; i < es.Hints; i++) v.ShownHints.Add("Подсказка " + (i + 1) + ". " + ex.Hints[i]);
            for (int i = 0; i < es.Steps; i++) v.ShownSteps.Add("Шаг " + (i + 1) + ". " + ex.SolutionSteps[i]);
            v.HasHints = ex.Hints.Count > 0;
            v.CanHint = es.Hints < ex.Hints.Count;
            v.HasSteps = ex.SolutionSteps.Count > 0;
            v.CanStep = !v.CanHint && es.Steps < ex.SolutionSteps.Count; // the solution opens only after every hint
            v.HintButton = "Подсказка (" + es.Hints + "/" + ex.Hints.Count + ")";
            v.StepButton = "Решение: следующий шаг (" + es.Steps + "/" + ex.SolutionSteps.Count + ")";
        }

        void BuildCalcView()
        {
            calcTopicId = null;
            calcTopicLabel = null;
            calcTopicOptions = null;
            CalcState s = SelectedCalc();
            if (s == null || string.IsNullOrEmpty(s.Calc.TopicId)) return;
            AcademyTopic t = service.FindTopic(s.Calc.TopicId);
            if (t == null) return;
            calcTopicId = t.Id;
            calcTopicLabel = "Тема: " + t.Number + ". " + t.Title;
            calcTopicOptions = LinkOptions(calcTopicLabel);
        }

        void BuildHintView()
        {
            HintView v = hintView;
            AcademyHint h = selectedHint;
            v.Hint = h;
            v.ShownHints.Clear();
            v.ShownSteps.Clear();
            v.TopicId = v.TopicLabel = v.CalcId = v.CalcLabel = null;
            v.TopicOptions = v.CalcOptions = null;
            if (h == null) return;

            bool active = false;
            foreach (HintRow row in hintRows)
                if (ReferenceEquals(row.Hint, h))
                {
                    active = row.Active;
                    break;
                }
            v.Color = SeverityColor(h.Severity);
            v.Title = (h.Severity == HintSeverity.Warning ? "⚠ " : "") + h.Title;
            v.Meta = SeverityName(h.Severity) + " · " + TimeText(h.RaisedAt) + (active ? " · активно" : "");
            v.Message = h.Message ?? "";
            int hints = Math.Min(h.RevealedHints, h.Hints.Count), steps = Math.Min(h.RevealedSteps, h.SolutionSteps.Count);
            for (int i = 0; i < hints; i++) v.ShownHints.Add("Подсказка " + (i + 1) + ". " + h.Hints[i]);
            for (int i = 0; i < steps; i++) v.ShownSteps.Add("Шаг " + (i + 1) + ". " + h.SolutionSteps[i]);
            v.HasHints = h.Hints.Count > 0;
            v.CanHint = h.CanRevealHint;
            v.HasSteps = h.SolutionSteps.Count > 0;
            v.CanStep = h.CanRevealStep;
            v.HintButton = "Подсказка (" + h.RevealedHints + "/" + h.Hints.Count + ")";
            v.StepButton = "Решение: следующий шаг (" + h.RevealedSteps + "/" + h.SolutionSteps.Count + ")";

            AcademyTopic topic = string.IsNullOrEmpty(h.TopicId) ? null : service.FindTopic(h.TopicId);
            if (topic != null)
            {
                v.TopicId = topic.Id;
                v.TopicLabel = "Тема: " + topic.Number + ". " + topic.Title;
                v.TopicOptions = LinkOptions(v.TopicLabel);
            }
            AcademyCalculator calc = string.IsNullOrEmpty(h.CalculatorId) ? null : service.FindCalculator(h.CalculatorId);
            if (calc != null)
            {
                v.CalcId = calc.Id;
                v.CalcLabel = "Калькулятор: " + calc.Title;
                v.CalcOptions = LinkOptions(v.CalcLabel);
            }
        }

        /// <summary>A link button just wide enough for its label (measured in OnGUI; cached with the view).</summary>
        GUILayoutOption[] LinkOptions(string label)
        {
            measure.text = label;
            float width = Mathf.Min(Mathf.Max(120f, textW - 8f), Mathf.Max(120f, HudStyle.Button.CalcSize(measure).x + 16f));
            return new[] { GUILayout.Width(width), GUILayout.Height(24) };
        }

        // ---- Drawing -------------------------------------------------------------------------

        void OnGUI()
        {
            if (!open || service == null || FlightInput.PauseMenuOpen) return;
            Event e = Event.current;
            if (e == null) return;
            bool layout = e.type == EventType.Layout;
            if (awaitingLayout)
            {
                if (!layout) return;
                awaitingLayout = false;
            }
            // Esc (leave a text field, then close the window) is handled once for every window by the Esc menu (GameMenu),
            // so the same key press cannot close the Academy here and open the settings there.
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) e.Use();

            GUI.depth = -11; // in front of the HUD: drawn on top and gets input first
            HudStyle.Begin();
            EnsureStyles();

            Vector2 screen = HudStyle.VirtualScreen;
            float w = Mathf.Min(PreferredWidth, screen.x - 20f);
            float h = Mathf.Min(PreferredHeight, screen.y - 20f);
            var r = new Rect((screen.x - w) * 0.5f, (screen.y - h) * 0.5f, w, h);
            float innerW = w - 20f, innerH = h - 16f;
            bool readOnly = session != null && session.ReadOnly;
            UpdateMetrics(innerW, Mathf.Max(160f, innerH - (readOnly ? 158f : 136f)));

            if (layout) Prepare();

            HudStyle.WindowFrame(r);
            GUILayout.BeginArea(new Rect(r.x + 10f, r.y + 8f, innerW, innerH));
            DrawHeader(readOnly);
            GUILayout.Space(4);
            DrawTabs();
            GUILayout.Space(6);
            if (topics == null || calculators == null)
                Colored("Академия недоступна: уроки ещё не построены.", note, HudStyle.Danger);
            else
            {
                switch (tab)
                {
                    case Tab.Topics: DrawTopics(); break;
                    case Tab.Calculators: DrawCalculators(); break;
                    default: DrawHints(); break;
                }
            }
            GUILayout.FlexibleSpace();
            DrawFooter();
            GUILayout.EndArea();

            SwallowMouse(r);
            ApplyPending();
        }

        void EnsureStyles()
        {
            if (text != null && measure != null) return;
            // White base colours where GUI.color / contentColor tints must give exactly HudStyle.Accent / Warning / Danger.
            text = new GUIStyle(HudStyle.Body) { fontSize = 13, richText = false };
            textWhite = new GUIStyle(text);
            textWhite.normal.textColor = Color.white;
            header = new GUIStyle(HudStyle.Header) { richText = false };
            headerWhite = new GUIStyle(header);
            headerWhite.normal.textColor = Color.white;
            formula = new GUIStyle(HudStyle.Title) { fontSize = 17, wordWrap = true };
            strong = new GUIStyle(HudStyle.Title) { wordWrap = false };
            strong.normal.textColor = Color.white;
            verdict = new GUIStyle(strong) { wordWrap = true };
            note = new GUIStyle(HudStyle.Small) { fontSize = 12, wordWrap = true };
            note.normal.textColor = Color.white;
            smallLine = new GUIStyle(HudStyle.Small) { wordWrap = false };
            cell = new GUIStyle(HudStyle.Label) { wordWrap = true };
            symbol = new GUIStyle(HudStyle.Mono) { wordWrap = true };
            symbol.normal.textColor = Color.white;
            listButton = new GUIStyle(HudStyle.Button) { alignment = TextAnchor.MiddleLeft, wordWrap = true };
            measure = new GUIContent();
        }

        void UpdateMetrics(float innerW, float bodyH)
        {
            if (innerW == metricsW && bodyH == metricsH) return;
            metricsW = innerW;
            metricsH = bodyH;
            viewDirty = true; // link buttons are sized for the panel width

            float listW = Mathf.Min(PreferredListWidth, Mathf.Max(170f, innerW * 0.27f));
            float detailW = Mathf.Max(240f, innerW - listW - 10f);
            textW = Mathf.Max(220f, detailW - 30f); // minus the vertical scrollbar
            float panelW = Mathf.Max(180f, textW - 32f); // inside a Panel box (padding + label margins)
            float tabW = Mathf.Min(200f, (innerW - 16f) / 3f);

            optTab = new[] { GUILayout.Width(tabW), GUILayout.Height(26) };
            optListScroll = new[] { GUILayout.Width(listW), GUILayout.Height(bodyH) };
            optListItem = new[] { GUILayout.Width(listW - 28f) };
            optDetailScroll = new[] { GUILayout.Width(detailW), GUILayout.Height(bodyH) };
            optText = new[] { GUILayout.Width(textW) };
            optPanelText = new[] { GUILayout.Width(panelW) };
            optVarSymbol = new[] { GUILayout.Width(100f) };
            optVarUnit = new[] { GUILayout.Width(120f) };
            optVarMeaning = new[] { GUILayout.Width(Mathf.Max(80f, panelW - 100f - 120f - 24f)) };
            optInputLabel = new[] { GUILayout.Width(Mathf.Min(380f, Mathf.Max(140f, textW - 200f))) };
            optInputField = new[] { GUILayout.Width(170f) };
            optOutSymbol = new[] { GUILayout.Width(90f) };
            optOutValue = new[] { GUILayout.Width(200f) };
            optOutLabel = new[] { GUILayout.Width(Mathf.Max(100f, textW - 90f - 200f - 28f)) };
            optAnswerLabel = new[] { GUILayout.Width(110f) };
            optAnswerField = new[] { GUILayout.Width(200f) };
            optCheck = new[] { GUILayout.Width(120f), GUILayout.Height(24) };
            optReveal = new[] { GUILayout.Width(200f), GUILayout.Height(24) };
            optStep = new[] { GUILayout.Width(300f), GUILayout.Height(24) };
            optCalcButton = new[] { GUILayout.Width(140f), GUILayout.Height(26) };
            optFooterNote = new[] { GUILayout.Width(Mathf.Max(100f, innerW - 190f)) };
            optClose = new[] { GUILayout.Width(170f), GUILayout.Height(30) };
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

        void DrawHeader(bool readOnly)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(HeaderText, HudStyle.WindowTitle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(progressText, strong);
            GUILayout.EndHorizontal();
            GUILayout.Label(SubtitleText, HudStyle.Small);
            if (readOnly) Colored(ReadOnlyText, note, HudStyle.Danger);
        }

        void DrawTabs()
        {
            GUILayout.BeginHorizontal();
            TabButton(Tab.Topics, "Темы");
            TabButton(Tab.Calculators, "Калькуляторы");
            TabButton(Tab.Hints, hintsTabLabel);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        void TabButton(Tab t, string label)
        {
            bool on = tab == t;
            if (GUILayout.Toggle(on, label, HudStyle.Tab, optTab) && !on) Defer(() => SwitchTab(t));
        }

        void DrawFooter()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(FooterText, HudStyle.Small, optFooterNote);
            GUILayout.FlexibleSpace();
            if (Button("Закрыть (" + FlightInput.KeyLabel(InputKey.L) + " · Esc)", true, optClose)) Defer(Close);
            GUILayout.EndHorizontal();
        }

        // ---- Tab 1: topics ---------------------------------------------------------------------

        void DrawTopics()
        {
            GUILayout.BeginHorizontal();
            topicListScroll = GUILayout.BeginScrollView(topicListScroll, optListScroll);
            for (int i = 0; i < topicRows.Count; i++)
            {
                TopicRow row = topicRows[i];
                bool on = row.Id == selectedTopicId;
                if (GUILayout.Toggle(on, row.Label, listButton, optListItem) && !on)
                {
                    string id = row.Id;
                    Defer(() => SelectTopic(id));
                }
            }
            GUILayout.EndScrollView();
            GUILayout.Space(10);

            topicScroll = GUILayout.BeginScrollView(topicScroll, optDetailScroll);
            GUILayout.BeginVertical(optText);
            DrawTopicDetail();
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndHorizontal();
        }

        void DrawTopicDetail()
        {
            TopicView v = topicView;
            AcademyTopic t = v.Topic;
            if (t == null)
            {
                GUILayout.Label("Выберите тему в списке слева.", text, optText);
                return;
            }
            GUILayout.Label(v.Title, header, optText);
            GUILayout.Space(4);

            foreach (AcademyFormula f in t.Formulas)
            {
                if (f == null) continue;
                GUILayout.BeginVertical(HudStyle.Panel, optText);
                GUILayout.Label(f.Expression, formula, optPanelText);
                IReadOnlyList<FormulaVariable> vars = f.Variables;
                if (vars.Count > 0)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Символ", HudStyle.Small, optVarSymbol);
                    GUILayout.Label("Что означает", HudStyle.Small, optVarMeaning);
                    GUILayout.Label("Единица", HudStyle.Small, optVarUnit);
                    GUILayout.EndHorizontal();
                    for (int i = 0; i < vars.Count; i++)
                    {
                        FormulaVariable fv = vars[i];
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(fv.Symbol, symbol, optVarSymbol);
                        GUILayout.Label(fv.Meaning, cell, optVarMeaning);
                        GUILayout.Label(fv.Unit, cell, optVarUnit);
                        GUILayout.EndHorizontal();
                    }
                }
                GUILayout.EndVertical();
                GUILayout.Space(3);
            }

            Section("Объяснение", t.Explanation);
            if (!string.IsNullOrEmpty(t.Example))
            {
                GUILayout.Space(6);
                GUILayout.BeginVertical(HudStyle.Panel, optText);
                GUILayout.Label("Пример (числа вашей планеты и ракеты)", HudStyle.Title, optPanelText);
                GUILayout.Label(t.Example, textWhite, optPanelText);
                GUILayout.EndVertical();
            }
            Section("Применение в игре", t.Application);
            Section("Ограничения", t.Limitations);

            if (v.CalcIds.Count > 0)
            {
                GUILayout.Space(8);
                for (int i = 0; i < v.CalcIds.Count; i++)
                {
                    if (!Button(v.CalcLabels[i], true, v.CalcOptions[i])) continue;
                    string id = v.CalcIds[i];
                    Defer(() => Request(Tab.Calculators, null, id, null));
                }
            }

            if (v.Exercise != null && v.State != null) DrawExercise(v);
            GUILayout.Space(6);
        }

        void Section(string heading, string body)
        {
            if (string.IsNullOrEmpty(body)) return;
            GUILayout.Space(6);
            GUILayout.Label(heading, HudStyle.Title, optText);
            GUILayout.Label(body, text, optText);
        }

        void DrawExercise(TopicView v)
        {
            AcademyExercise ex = v.Exercise;
            ExerciseState es = v.State;
            string topicTitle = v.Topic.Title;

            GUILayout.Space(10);
            GUILayout.BeginVertical(HudStyle.Panel, optText);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Задача", HudStyle.Title);
            GUILayout.FlexibleSpace();
            if (v.Solved) Colored("★ решена", strong, HudStyle.Accent);
            GUILayout.EndHorizontal();
            GUILayout.Label(ex.Question, text, optPanelText);
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label(v.AnswerLabel, HudStyle.Label, optAnswerLabel);
            GUI.SetNextControlName(AnswerControl);
            string answer = GUILayout.TextField(es.Answer ?? "", HudStyle.Field, optAnswerField);
            if (!string.Equals(answer, es.Answer, StringComparison.Ordinal)) es.Answer = answer;
            bool enter = ReturnPressed(AnswerControl);
            bool check = Button("Проверить", true, optCheck);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (enter || check)
                Defer(() =>
                {
                    if (check) GUIUtility.keyboardControl = 0; // a clicked button releases the field (L / Esc work again)
                    CheckExercise(ex, topicTitle);
                });

            if (!string.IsNullOrEmpty(es.Message)) Colored(es.Message, verdict, es.MessageColor, optPanelText);

            for (int i = 0; i < v.ShownHints.Count; i++) GUILayout.Label(v.ShownHints[i], text, optPanelText);
            for (int i = 0; i < v.ShownSteps.Count; i++) GUILayout.Label(v.ShownSteps[i], textWhite, optPanelText);

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (v.HasHints && Button(v.HintButton, v.CanHint, optReveal))
                Defer(() => es.Hints = Math.Min(es.Hints + 1, ex.Hints.Count));
            if (v.HasSteps && Button(v.StepButton, v.CanStep, optStep))
                Defer(() =>
                {
                    if (es.Hints >= ex.Hints.Count) es.Steps = Math.Min(es.Steps + 1, ex.SolutionSteps.Count);
                });
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (v.HasSteps && v.CanHint) GUILayout.Label(StepsLockedText, HudStyle.Small, optPanelText);
            GUILayout.EndVertical();
        }

        // ---- Tab 2: calculators ------------------------------------------------------------------

        void DrawCalculators()
        {
            GUILayout.BeginHorizontal();
            calcListScroll = GUILayout.BeginScrollView(calcListScroll, optListScroll);
            for (int i = 0; i < calculators.Count; i++)
            {
                AcademyCalculator calc = calculators[i];
                if (calc == null) continue;
                bool on = calc.Id == selectedCalcId;
                if (GUILayout.Toggle(on, calc.Title, listButton, optListItem) && !on)
                {
                    string id = calc.Id;
                    Defer(() => SelectCalc(id));
                }
            }
            GUILayout.EndScrollView();
            GUILayout.Space(10);

            calcScroll = GUILayout.BeginScrollView(calcScroll, optDetailScroll);
            GUILayout.BeginVertical(optText);
            DrawCalcDetail();
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndHorizontal();
        }

        void DrawCalcDetail()
        {
            CalcState s = SelectedCalc();
            if (s == null)
            {
                GUILayout.Label("Выберите калькулятор в списке слева.", text, optText);
                return;
            }
            AcademyCalculator calc = s.Calc;
            GUILayout.Label(calc.Title, header, optText);
            GUILayout.Label(calc.Description, text, optText);
            if (calcTopicLabel != null && Button(calcTopicLabel, true, calcTopicOptions))
            {
                string topicId = calcTopicId;
                Defer(() => Request(Tab.Topics, topicId, null, null));
            }

            GUILayout.Space(6);
            GUILayout.Label("Наборы значений", HudStyle.Title, optText);
            for (int r = 0; r < s.ChipRows.Count; r++)
            {
                List<int> row = s.ChipRows[r];
                GUILayout.BeginHorizontal();
                for (int k = 0; k < row.Count; k++)
                {
                    int chip = row[k];
                    if (!Button(s.ChipLabels[chip], true, s.ChipOptions[chip])) continue;
                    double[] values = chip == 0 ? calc.Defaults() : calc.Presets[chip - 1].Values;
                    Defer(() => ApplyValues(s, values));
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.Label("Исходные данные", HudStyle.Title, optText);
            for (int i = 0; i < s.Fields.Length; i++)
            {
                GUILayout.BeginHorizontal();
                if (s.Invalid[i]) Colored(s.RowLabels[i], note, HudStyle.Danger, optInputLabel);
                else GUILayout.Label(s.RowLabels[i], cell, optInputLabel);
                GUI.SetNextControlName(InputName(i));
                string value = GUILayout.TextField(s.Fields[i] ?? "", HudStyle.Field, optInputField);
                if (!string.Equals(value, s.Fields[i], StringComparison.Ordinal))
                {
                    s.Fields[i] = value;
                    s.Dirty = true; // recomputed on the next Layout pass
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            bool enter = ReturnPressedIn(InputControlPrefix);
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            bool click = Button("Рассчитать", true, optCalcButton);
            GUILayout.Label("Результат пересчитывается сразу при вводе.", smallLine);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (enter || click)
                Defer(() =>
                {
                    s.Dirty = true;
                    if (click) GUIUtility.keyboardControl = 0;
                });

            GUILayout.Space(8);
            GUILayout.Label("Результат", HudStyle.Title, optText);
            if (s.Error != null)
            {
                Colored(s.Error, note, HudStyle.Danger, optText);
                return;
            }
            if (!s.HasResult)
            {
                GUILayout.Label("Введите значения — результат появится здесь.", HudStyle.Small, optText);
                return;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Величина", HudStyle.Small, optOutLabel);
            GUILayout.Label("Символ", HudStyle.Small, optOutSymbol);
            GUILayout.Label("Значение", HudStyle.Small, optOutValue);
            GUILayout.EndHorizontal();
            for (int i = 0; i < s.OutLabel.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(s.OutLabel[i], cell, optOutLabel);
                GUILayout.Label(s.OutSymbol[i], symbol, optOutSymbol);
                GUILayout.Label(s.OutText[i], HudStyle.Value, optOutValue);
                GUILayout.EndHorizontal();
            }
            if (s.Steps.Count > 0)
            {
                GUILayout.Space(6);
                GUILayout.Label("Расчёт по шагам", HudStyle.Title, optText);
                for (int i = 0; i < s.Steps.Count; i++) GUILayout.Label(s.Steps[i], text, optText);
            }
            if (s.Warnings.Count > 0)
            {
                GUILayout.Space(6);
                for (int i = 0; i < s.Warnings.Count; i++) Colored(s.Warnings[i], note, HudStyle.Warning, optText);
            }
            if (s.Notes.Count > 0)
            {
                GUILayout.Space(6);
                for (int i = 0; i < s.Notes.Count; i++) GUILayout.Label(s.Notes[i], HudStyle.Small, optText);
            }
            GUILayout.Space(6);
        }

        // ---- Tab 3: hints ----------------------------------------------------------------------

        void DrawHints()
        {
            if (hintRows.Count == 0 && hintView.Hint == null)
            {
                GUILayout.BeginVertical(HudStyle.Panel);
                GUILayout.Label("Подсказок пока нет", HudStyle.Title);
                GUILayout.Label(EmptyHintsText, text);
                GUILayout.EndVertical();
                return;
            }

            GUILayout.BeginHorizontal();
            hintListScroll = GUILayout.BeginScrollView(hintListScroll, optListScroll);
            for (int i = 0; i < hintRows.Count; i++)
            {
                HintRow row = hintRows[i];
                bool on = ReferenceEquals(row.Hint, selectedHint);
                Color old = GUI.contentColor;
                GUI.contentColor = row.Color;
                bool clicked = GUILayout.Toggle(on, row.Label, listButton, optListItem);
                GUI.contentColor = old;
                if (clicked && !on)
                {
                    AcademyHint hint = row.Hint;
                    Defer(() => SelectHint(hint));
                }
            }
            GUILayout.EndScrollView();
            GUILayout.Space(10);

            hintScroll = GUILayout.BeginScrollView(hintScroll, optDetailScroll);
            GUILayout.BeginVertical(optText);
            DrawHintDetail();
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndHorizontal();
        }

        void DrawHintDetail()
        {
            HintView v = hintView;
            AcademyHint h = v.Hint;
            if (h == null)
            {
                GUILayout.Label("Выберите подсказку в списке слева.", text, optText);
                return;
            }
            Colored(v.Title, headerWhite, v.Color, optText);
            GUILayout.Label(v.Meta, HudStyle.Small, optText);
            GUILayout.Space(4);
            GUILayout.Label(v.Message, textWhite, optText);

            if (v.ShownHints.Count > 0 || v.ShownSteps.Count > 0) GUILayout.Space(4);
            for (int i = 0; i < v.ShownHints.Count; i++) GUILayout.Label(v.ShownHints[i], text, optText);
            for (int i = 0; i < v.ShownSteps.Count; i++) GUILayout.Label(v.ShownSteps[i], textWhite, optText);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (v.HasHints && Button(v.HintButton, v.CanHint, optReveal)) Defer(() => h.RevealHint());
            if (v.HasSteps && Button(v.StepButton, v.CanStep, optStep)) Defer(() => h.RevealStep());
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (v.HasSteps && v.CanHint) GUILayout.Label(StepsLockedText, HudStyle.Small, optText);

            if (v.TopicLabel != null || v.CalcLabel != null)
            {
                GUILayout.Space(8);
                if (v.TopicLabel != null && Button(v.TopicLabel, true, v.TopicOptions))
                {
                    string topicId = v.TopicId;
                    Defer(() => Request(Tab.Topics, topicId, null, null));
                }
                if (v.CalcLabel != null && Button(v.CalcLabel, true, v.CalcOptions))
                {
                    string calcId = v.CalcId;
                    Defer(() => Request(Tab.Calculators, null, calcId, null));
                }
            }
            GUILayout.Space(6);
        }

        // ---- IMGUI helpers -------------------------------------------------------------------

        static bool Button(string label, bool enabled, params GUILayoutOption[] options)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            bool clicked = GUILayout.Button(label, HudStyle.Button, options);
            GUI.enabled = previous;
            return clicked && enabled;
        }

        static void Colored(string label, GUIStyle style, Color color, params GUILayoutOption[] options)
        {
            Color c = GUI.color;
            GUI.color = color;
            GUILayout.Label(label, style, options);
            GUI.color = c;
        }

        /// <summary>Enter in the named text field (checked right after drawing it, in the same pass).</summary>
        static bool ReturnPressed(string controlName)
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown || (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter)) return false;
            if (GUI.GetNameOfFocusedControl() != controlName) return false;
            e.Use();
            return true;
        }

        static bool ReturnPressedIn(string controlPrefix)
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown || (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter)) return false;
            string focused = GUI.GetNameOfFocusedControl();
            if (string.IsNullOrEmpty(focused) || !focused.StartsWith(controlPrefix, StringComparison.Ordinal)) return false;
            e.Use();
            return true;
        }

        static string InputName(int index)
        {
            while (InputNames.Count <= index) InputNames.Add(InputControlPrefix + InputNames.Count);
            return InputNames[index];
        }

        // ---- Lookups and formatting (display only) -------------------------------------------

        AcademyProgress Progress()
        {
            PlayerProfile p = session != null ? session.Profile : null;
            return p != null ? p.academy : null;
        }

        ExerciseState Exercise(string id)
        {
            if (!exercises.TryGetValue(id, out ExerciseState es))
            {
                es = new ExerciseState();
                exercises[id] = es;
            }
            return es;
        }

        CalcState SelectedCalc() =>
            selectedCalcId != null && calcs.TryGetValue(selectedCalcId, out CalcState s) && s.Calc != null ? s : null;

        AcademyTopic TopicById(string id)
        {
            if (topics == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < topics.Count; i++)
                if (topics[i] != null && topics[i].Id == id) return topics[i];
            return null;
        }

        AcademyCalculator CalcById(string id)
        {
            if (calculators == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < calculators.Count; i++)
                if (calculators[i] != null && calculators[i].Id == id) return calculators[i];
            return null;
        }

        static string TimeText(double simSeconds) => "t = " + UnitFormatter.Number(simSeconds, 0) + " с";

        static Color VerdictColor(AnswerVerdict v)
        {
            switch (v)
            {
                case AnswerVerdict.Correct: return HudStyle.Accent;
                case AnswerVerdict.Close: return HudStyle.Warning;
                default: return HudStyle.Danger;
            }
        }

        static Color SeverityColor(HintSeverity severity)
        {
            switch (severity)
            {
                case HintSeverity.Info: return HudStyle.Accent;
                case HintSeverity.Warning: return HudStyle.Warning;
                default: return Color.white;
            }
        }

        static string SeverityName(HintSeverity severity)
        {
            switch (severity)
            {
                case HintSeverity.Info: return "Проверьте себя";
                case HintSeverity.Warning: return "Предупреждение";
                default: return "Совет";
            }
        }
    }
}
