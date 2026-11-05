using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using static SpaceSim.Runtime.Menu.MenuKit;

namespace SpaceSim.Runtime.Menu
{
    /// <summary>
    /// The Settings screen shared by the main menu and the in-game Esc menu, in the HORIZON style: tabs
    /// «Изображение» (brightness), «Мышь» (sensitivity, inverted Y) and «Управление» (key bindings). Every change goes
    /// straight to <see cref="GameSettings"/> (applied and saved at once). The host calls <see cref="Tick"/> from Update
    /// (the "press a key" prompt polls the keyboard) and <see cref="Detach"/> when it is destroyed.
    /// </summary>
    public sealed class SettingsView
    {
        public enum Page { Display, Mouse, Controls }

        static readonly string[] PageNames = { "ИЗОБРАЖЕНИЕ", "МЫШЬ", "УПРАВЛЕНИЕ" };
        const float BrightnessStepEv = 0.05f, SensitivityStep = 0.05f;

        readonly VisualElement host, tabsRow, list, side;
        readonly List<Button> tabs = new List<Button>();
        readonly Dictionary<InputKey, (VisualElement row, Label key)> keyRows = new Dictionary<InputKey, (VisualElement, Label)>();
        Label infoEyebrow, infoTitle, infoText, statusLabel;
        SettingSlider brightness, sensitivity;
        VisualElement invertSwitch;
        VisualElement viewport, content, scrollThumb;
        float scroll;
        Page page;
        InputKey? listening;
        int captureStartFrame = -10, captureEndFrame = -10;
        string status = "";

        public Page CurrentPage => page;
        /// <summary>Waiting for the player to press a key for <see cref="ListeningFor"/>.</summary>
        public bool IsCapturing => listening.HasValue;
        public InputKey? ListeningFor => listening;
        /// <summary>The host must not treat Esc / Back as "leave the screen" now (it cancelled a key prompt).</summary>
        public bool BlocksBack => IsCapturing || Time.frameCount <= captureEndFrame + 1;
        public string Status => status;

        /// <param name="screen">Absolutely positioned screen element (1920×1080 reference) that receives the tabs and the body.</param>
        /// <param name="buildActions">Adds the host's buttons under the info panel (e.g. «Продолжить», «Главное меню»).</param>
        public SettingsView(VisualElement screen, float tabsTop, float bodyTop, Action<VisualElement> buildActions)
        {
            host = screen;
            AddStyleSheet(screen, GameStyleSheet);

            tabsRow = Add(screen, El("tabs"));
            tabsRow.style.top = tabsTop;
            for (int i = 0; i < PageNames.Length; i++)
            {
                var p = (Page)i;
                var tab = new Button(() => ShowPage(p)) { text = PageNames[i] };
                tab.AddToClassList("tab");
                tab.AddToClassList("tab--live");
                tab.AddToClassList("t-strong");
                tabs.Add(tab);
                tabsRow.Add(tab);
            }

            var body = Add(screen, El("screen-body"));
            body.style.top = bodyTop;
            list = Add(body, El("panel", "settings-list", "settings-list--live"));
            side = Add(body, El("settings-side"));

            var info = Add(side, El("panel"));
            infoEyebrow = Add(info, Text("", "eyebrow", "eyebrow--cyan", "t-strong"));
            infoTitle = Add(info, Text("", "panel__title", "t-strong"));
            infoTitle.style.marginTop = 10;
            infoText = Add(info, Text("", "panel__text"));
            statusLabel = Add(info, Text("", "settings-status"));

            var row = Add(side, El("row"));
            row.style.marginTop = 18;
            row.Add(ButtonEl("ПО УМОЛЧАНИЮ", ResetPage));
            row.Add(El("grow"));
            buildActions?.Invoke(side);

            // While a key prompt is open, keyboard navigation must not move the focus, press buttons or leave the screen.
            screen.RegisterCallback<NavigationMoveEvent>(BlockWhileCapturing, TrickleDown.TrickleDown);
            screen.RegisterCallback<NavigationSubmitEvent>(BlockWhileCapturing, TrickleDown.TrickleDown);
            screen.RegisterCallback<NavigationCancelEvent>(BlockWhileCapturing, TrickleDown.TrickleDown);
            screen.RegisterCallback<KeyDownEvent>(BlockWhileCapturing, TrickleDown.TrickleDown);
            // A click anywhere except a binding row cancels the prompt.
            screen.RegisterCallback<PointerDownEvent>(e =>
            {
                if (IsCapturing && !IsInsideBindRow(e.target as VisualElement)) CancelCapture();
            }, TrickleDown.TrickleDown);

            GameSettings.Changed += Refresh;
            ShowPage(Page.Display);
        }

        /// <summary>Unsubscribes from <see cref="GameSettings.Changed"/>.</summary>
        public void Detach() => GameSettings.Changed -= Refresh;

        /// <summary>Call every frame while the screen is visible.</summary>
        public void Tick()
        {
            if (!listening.HasValue || Time.frameCount == captureStartFrame) return; // ignore the key that opened the prompt
            if (FlightInput.EscapePressed)
            {
                CancelCapture();
                return;
            }
            if (FlightInput.TryGetPressedKey(out KeyCode code)) Assign(listening.Value, code);
        }

        public void ShowPage(Page p)
        {
            if (IsCapturing) CancelCapture();
            page = p;
            for (int i = 0; i < tabs.Count; i++) tabs[i].EnableInClassList("tab--on", i == (int)p);
            status = "";
            BuildPage();
        }

        /// <summary>Opens the "press a key" prompt for a command.</summary>
        public void BeginCapture(InputKey key)
        {
            if (!KeyBindings.IsRebindable(key)) return;
            if (page != Page.Controls) ShowPage(Page.Controls);
            listening = key;
            captureStartFrame = Time.frameCount;
            status = "Нажмите клавишу для «" + KeyBindings.LabelOf(key) + "». Esc — отмена.";
            RefreshKeys();
            if (keyRows.TryGetValue(key, out var r)) EnsureVisible(r.row);
            RefreshInfo();
        }

        /// <summary>Closes the "press a key" prompt without changing the binding.</summary>
        public void CancelCapture()
        {
            if (!listening.HasValue) return;
            status = "Отмена: клавиша для «" + KeyBindings.LabelOf(listening.Value) + "» не изменилась.";
            listening = null;
            captureEndFrame = Time.frameCount;
            RefreshKeys();
            RefreshInfo();
        }

        /// <summary>Re-reads every value from <see cref="GameSettings"/> (after a reset or a change elsewhere).</summary>
        public void Refresh()
        {
            brightness?.SetValueWithoutNotify(GameSettings.ExposureEv);
            sensitivity?.SetValueWithoutNotify(GameSettings.MouseSensitivity);
            invertSwitch?.EnableInClassList("switch--on", GameSettings.InvertMouseY);
            RefreshKeys();
            RefreshInfo();
        }

        void Assign(InputKey key, KeyCode code)
        {
            KeyCode before = GameSettings.GetBinding(key);
            InputKey? swapped = GameSettings.SetBinding(key, code);
            listening = null;
            captureEndFrame = Time.frameCount;
            string name = KeyBindings.DisplayName(code);
            if (before == code) status = "«" + KeyBindings.LabelOf(key) + "» и так на " + name + ".";
            else if (swapped.HasValue)
                status = "«" + KeyBindings.LabelOf(key) + "» — теперь " + name + ". Клавиша была у «" + KeyBindings.LabelOf(swapped.Value) +
                         "», ей досталась " + KeyBindings.DisplayName(GameSettings.GetBinding(swapped.Value)) + ".";
            else status = "«" + KeyBindings.LabelOf(key) + "» — теперь " + name + ".";
            Refresh();
        }

        void ResetPage()
        {
            if (IsCapturing) CancelCapture();
            switch (page)
            {
                case Page.Display:
                    GameSettings.ResetDisplay();
                    status = "Яркость сброшена: 100 %.";
                    break;
                case Page.Mouse:
                    GameSettings.ResetMouse();
                    status = "Мышь: чувствительность ×1, без инверсии.";
                    break;
                default:
                    GameSettings.ResetBindings();
                    status = "Все клавиши — как по умолчанию.";
                    break;
            }
            Refresh();
        }

        // ─────────────────────────────── pages ───────────────────────────────

        void BuildPage()
        {
            list.Clear();
            keyRows.Clear();
            brightness = sensitivity = null;
            invertSwitch = null;
            viewport = content = scrollThumb = null;
            scroll = 0f;
            list.EnableInClassList("settings-list--fill", page == Page.Controls);

            switch (page)
            {
                case Page.Display:
                {
                    var row = SettingRow(list, "Яркость", "Экспозиция кадра от −1 до +1 ступени (EV)");
                    brightness = new SettingSlider(-1f, 1f, BrightnessStepEv, GameSettings.ExposureEv,
                        ev => Mathf.RoundToInt(Mathf.Pow(2f, ev) * 100f) + " %",
                        ev => GameSettings.Brightness = Mathf.Pow(2f, ev));
                    row.Add(brightness);
                    SliderKeys(row, brightness);
                    break;
                }
                case Page.Mouse:
                {
                    var row = SettingRow(list, "Чувствительность мыши", "Обзор в полёте (ПКМ / СКМ) и взгляд на поверхности");
                    sensitivity = new SettingSlider(GameSettings.MinMouseSensitivity, GameSettings.MaxMouseSensitivity, SensitivityStep,
                        GameSettings.MouseSensitivity, v => "×" + v.ToString("0.00", CultureInfo.InvariantCulture),
                        v => GameSettings.MouseSensitivity = v);
                    row.Add(sensitivity);
                    SliderKeys(row, sensitivity);

                    var inv = SettingRow(list, "Инвертировать ось Y", "Мышь вверх — взгляд вниз, как в авиасимуляторах");
                    invertSwitch = Add(inv, El("switch", GameSettings.InvertMouseY ? "switch--on" : null));
                    invertSwitch.Add(El("switch__knob"));
                    inv.RegisterCallback<ClickEvent>(_ => GameSettings.InvertMouseY = !GameSettings.InvertMouseY);
                    inv.RegisterCallback<NavigationSubmitEvent>(_ => GameSettings.InvertMouseY = !GameSettings.InvertMouseY);
                    break;
                }
                default:
                    BuildControls();
                    break;
            }
            ApplyFonts(list);
            Refresh();
        }

        void BuildControls()
        {
            viewport = Add(list, El("scroll-viewport"));
            content = Add(viewport, El("scroll-content"));
            var bar = Add(list, El("scroll-bar"));
            scrollThumb = Add(bar, El("scroll-bar__thumb"));
            viewport.RegisterCallback<WheelEvent>(e =>
            {
                float notches = Mathf.Abs(e.delta.y) >= 10f ? e.delta.y / 120f : e.delta.y;
                ScrollBy(notches * 46f);
                e.StopPropagation();
            });
            viewport.RegisterCallback<GeometryChangedEvent>(_ => ScrollBy(0f));
            content.RegisterCallback<GeometryChangedEvent>(_ => ScrollBy(0f));

            string group = null;
            foreach (KeyAction a in KeyBindings.Actions)
            {
                if (a.Group != group)
                {
                    group = a.Group;
                    content.Add(Text(group, "bind-group", "t-strong"));
                }
                InputKey key = a.Key;
                var row = SettingRow(content, a.Label, null);
                row.AddToClassList("setting--bind");
                var cap = Add(row, Text("", "bind-key", "t-strong"));
                row.RegisterCallback<ClickEvent>(_ => OnBindRow(key));
                row.RegisterCallback<NavigationSubmitEvent>(_ => OnBindRow(key));
                row.RegisterCallback<FocusInEvent>(_ => EnsureVisible(row));
                keyRows[key] = (row, cap);
            }
        }

        void OnBindRow(InputKey key)
        {
            if (Time.frameCount <= captureEndFrame + 1) return; // the key that just finished a prompt must not reopen it
            if (listening == key) CancelCapture();
            else BeginCapture(key);
        }

        void RefreshKeys()
        {
            foreach (KeyValuePair<InputKey, (VisualElement row, Label key)> p in keyRows)
            {
                bool on = listening == p.Key;
                p.Value.key.text = on ? "НАЖМИТЕ КЛАВИШУ" : KeyBindings.DisplayName(GameSettings.GetBinding(p.Key));
                p.Value.key.EnableInClassList("bind-key--listening", on);
                p.Value.key.EnableInClassList("bind-key--custom", !on && !GameSettings.IsDefaultBinding(p.Key));
                p.Value.row.EnableInClassList("setting--listening", on);
            }
        }

        void RefreshInfo()
        {
            switch (page)
            {
                case Page.Display:
                    infoEyebrow.text = "ИЗОБРАЖЕНИЕ";
                    infoTitle.text = "Яркость экрана";
                    infoText.text = "Меняет экспозицию кадра: 50 % — вдвое темнее, 200 % — вдвое светлее (±1 ступень). " +
                                    "Освещённость в симуляции, приборы и физика не меняются — только изображение.";
                    break;
                case Page.Mouse:
                    infoEyebrow.text = "МЫШЬ";
                    infoTitle.text = "Обзор мышью";
                    infoText.text = "Чувствительность умножает поворот камеры вокруг ракеты (правая или средняя кнопка) и взгляд астронавта " +
                                    "на поверхности. ×1 — как было в игре изначально. Колесо мыши по-прежнему меняет приближение.";
                    break;
                default:
                    infoEyebrow.text = "УПРАВЛЕНИЕ";
                    infoTitle.text = "Клавиши";
                    infoText.text = "Нажмите на команду, затем нужную клавишу. Esc — отмена. Если клавиша уже занята, команды " +
                                    "поменяются клавишами. Esc всегда закрывает окна и открывает это меню.";
                    break;
            }
            statusLabel.text = status;
            statusLabel.style.display = string.IsNullOrEmpty(status) ? DisplayStyle.None : DisplayStyle.Flex;
            statusLabel.EnableInClassList("settings-status--active", IsCapturing);
        }

        // ─────────────────────────────── rows ───────────────────────────────

        static VisualElement SettingRow(VisualElement parent, string label, string hint)
        {
            var row = Add(parent, El("setting", "setting--live"));
            row.focusable = true;
            row.RegisterCallback<FocusInEvent>(_ => row.AddToClassList("setting--focused"));
            row.RegisterCallback<FocusOutEvent>(_ => row.RemoveFromClassList("setting--focused"));
            var text = Add(row, El("grow"));
            text.Add(Text(label, "setting__label", "t-strong"));
            if (hint != null) text.Add(Text(hint, "setting__hint"));
            return row;
        }

        /// <summary>← / → change the value of a focused slider row (instead of moving the focus).</summary>
        static void SliderKeys(VisualElement row, SettingSlider slider)
        {
            row.RegisterCallback<NavigationMoveEvent>(e =>
            {
                int dir = e.direction == NavigationMoveEvent.Direction.Left ? -1 : e.direction == NavigationMoveEvent.Direction.Right ? 1 : 0;
                if (dir == 0) return;
                slider.StepBy(dir);
                e.StopPropagation();
                row.focusController?.IgnoreEvent(e);
            });
        }

        void BlockWhileCapturing(EventBase e)
        {
            if (!IsCapturing && Time.frameCount > captureEndFrame + 1) return;
            e.StopImmediatePropagation();
            host.focusController?.IgnoreEvent(e);
        }

        static bool IsInsideBindRow(VisualElement e)
        {
            for (; e != null; e = e.parent)
                if (e.ClassListContains("setting--bind")) return true;
            return false;
        }

        // ─────────────────────────────── scrolling ───────────────────────────────

        void ScrollBy(float dy)
        {
            if (viewport == null || content == null) return;
            float view = viewport.layout.height, total = content.layout.height;
            if (float.IsNaN(view) || float.IsNaN(total) || view <= 0f) return;
            float max = Mathf.Max(0f, total - view);
            scroll = Mathf.Clamp(scroll + dy, 0f, max);
            content.style.top = -scroll;
            if (scrollThumb != null)
            {
                bool needed = max > 0.5f;
                scrollThumb.parent.style.display = needed ? DisplayStyle.Flex : DisplayStyle.None;
                if (needed)
                {
                    float frac = view / total;
                    scrollThumb.style.height = Length.Percent(frac * 100f);
                    scrollThumb.style.top = Length.Percent((1f - frac) * (scroll / max) * 100f);
                }
            }
        }

        void EnsureVisible(VisualElement row)
        {
            if (viewport == null) return;
            float top = row.layout.y, bottom = top + row.layout.height, view = viewport.layout.height;
            if (float.IsNaN(top) || view <= 0f) return;
            if (top < scroll) ScrollBy(top - scroll - 8f);
            else if (bottom > scroll + view) ScrollBy(bottom - scroll - view + 8f);
        }

        /// <summary>The menu's slider (track, cyan fill, white knob, value) made draggable and steppable.</summary>
        sealed class SettingSlider : VisualElement
        {
            readonly VisualElement track, fill, knob;
            readonly Label valueLabel;
            readonly float min, max, step;
            readonly Func<float, string> format;
            readonly Action<float> changed;
            float value;
            int pointer = -1;

            public SettingSlider(float min, float max, float step, float value, Func<float, string> format, Action<float> changed)
            {
                this.min = min;
                this.max = max;
                this.step = step;
                this.format = format;
                this.changed = changed;
                AddToClassList("slider");
                AddToClassList("slider--live");
                track = MenuKit.Add(this, MenuKit.El("slider__track"));
                fill = MenuKit.Add(track, MenuKit.El("slider__fill"));
                knob = MenuKit.Add(track, MenuKit.El("slider__knob"));
                valueLabel = MenuKit.Add(this, MenuKit.Text("", "slider__value", "t-mono"));
                RegisterCallback<PointerDownEvent>(OnPointerDown);
                RegisterCallback<PointerMoveEvent>(OnPointerMove);
                RegisterCallback<PointerUpEvent>(OnPointerUp);
                RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
                SetValueWithoutNotify(value);
            }

            public void StepBy(int direction) => Set(value + direction * step);

            public void SetValueWithoutNotify(float v)
            {
                value = Mathf.Clamp(v, min, max);
                float t = max > min ? (value - min) / (max - min) : 0f;
                fill.style.width = Length.Percent(t * 100f);
                knob.style.left = Length.Percent(t * 100f);
                valueLabel.text = format(value);
            }

            void Set(float v)
            {
                v = Mathf.Clamp(Mathf.Round(v / step) * step, min, max);
                if (Mathf.Abs(v - value) < step * 0.01f) return;
                SetValueWithoutNotify(v);
                changed?.Invoke(value);
            }

            void OnPointerDown(PointerDownEvent e)
            {
                if (e.button != 0) return;
                pointer = e.pointerId;
                this.CapturePointer(pointer);
                AddToClassList("slider--drag");
                SetFromPanel(e.position);
                e.StopPropagation();
            }

            void OnPointerMove(PointerMoveEvent e)
            {
                if (e.pointerId != pointer || !this.HasPointerCapture(pointer)) return;
                SetFromPanel(e.position);
            }

            void OnPointerUp(PointerUpEvent e)
            {
                if (e.pointerId != pointer) return;
                this.ReleasePointer(pointer);
                EndDrag();
            }

            void EndDrag()
            {
                pointer = -1;
                RemoveFromClassList("slider--drag");
            }

            void SetFromPanel(Vector3 panelPosition)
            {
                float w = track.layout.width;
                if (float.IsNaN(w) || w <= 0f) return;
                Vector2 local = track.WorldToLocal(panelPosition);
                Set(Mathf.Lerp(min, max, Mathf.Clamp01(local.x / w)));
            }
        }
    }
}
