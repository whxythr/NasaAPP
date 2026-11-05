using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using static SpaceSim.Runtime.Menu.MenuKit;

namespace SpaceSim.Runtime.Menu
{
    /// <summary>
    /// The in-game Esc menu in the HORIZON style (UI Toolkit overlay over the 3D view): Settings (brightness, mouse,
    /// key bindings), back to the flight, to the main menu, or quit. Esc first leaves a text field, then closes the window
    /// on top (hangar, shop, Academy, navigation, manual, map…); only with nothing open does it open this menu. While the
    /// menu is open the simulation is paused (unless it already was), the HUD hides and every flight hotkey is ignored.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameMenu : MonoBehaviour
    {
        /// <summary>Scene loaded by «Главное меню» (SpaceSim ▸ Create Main Menu Scene puts it in Build Settings).</summary>
        public const string MainMenuScene = "MainMenu";

        UIDocument document;
        PanelSettings panel;
        Texture2D shade;
        VisualElement root, modal;
        Label modalEyebrow, modalTitle, modalText, modalConfirmLabel;
        Action modalConfirm;
        Button mainMenuButton;
        SettingsView settings;
        SimulationController simulation;
        Func<bool> closeTopWindow;
        bool open, pausedByMenu;

        public bool IsOpen => open;
        public bool ModalOpen => modal != null && modal.ClassListContains("modal--open");
        public SettingsView Settings => settings;
        /// <summary>True when the main menu scene is in Build Settings (the «Главное меню» button works).</summary>
        public static bool MainMenuAvailable => Application.CanStreamedLevelBeLoaded(MainMenuScene);

        public void Initialize(SimulationController sim) => simulation = sim;

        /// <summary>Esc closes the window on top through this (the HUD knows its windows); returns false if none was open.</summary>
        public void SetWindowCloser(Func<bool> closer) => closeTopWindow = closer;

        void Awake()
        {
            document = GetComponent<UIDocument>();
            panel = CreatePanel("GameMenuPanel (runtime)", opaque: false, sortingOrder: 100);
            document.panelSettings = panel;
        }

        void OnEnable()
        {
            if (document != null) Build(document.rootVisualElement);
        }

        void OnDisable()
        {
            settings?.Detach();
            if (open) Close();
        }

        void OnDestroy()
        {
            FlightInput.PauseMenuOpen = false;
            if (pausedByMenu && simulation != null) simulation.SetPaused(false);
            pausedByMenu = false;
            if (panel != null) Destroy(panel);
            if (shade != null) Destroy(shade);
        }

        void Update()
        {
            if (open) settings?.Tick();
            if (FlightInput.EscapePressed) HandleEscape();
        }

        /// <summary>
        /// What Esc does now: cancel a key prompt / close the confirmation / close this menu; otherwise leave a text field,
        /// close the top window, or — with nothing open — open this menu.
        /// </summary>
        public void HandleEscape()
        {
            if (open)
            {
                if (settings != null && settings.BlocksBack) return; // that Esc cancelled a key prompt
                if (ModalOpen) CloseModal();
                else Close();
                return;
            }
            if (GUIUtility.keyboardControl != 0)
            {
                GUIUtility.keyboardControl = 0; // Esc leaves a text field first
                return;
            }
            if (closeTopWindow != null && closeTopWindow()) return;
            Open();
        }

        public void Open()
        {
            if (open || root == null) return;
            open = true;
            FlightInput.PauseMenuOpen = true;
            GUIUtility.keyboardControl = 0;
            if (simulation != null && !simulation.Paused)
            {
                simulation.SetPaused(true);
                pausedByMenu = true;
            }
            mainMenuButton?.EnableInClassList("btn--disabled", !MainMenuAvailable);
            settings.ShowPage(settings.CurrentPage); // fresh values, no status left from last time
            root.style.display = DisplayStyle.Flex;
            root.schedule.Execute(() => { if (open) root.AddToClassList("game-menu--open"); }).StartingIn(16);
        }

        public void Close()
        {
            if (!open) return;
            settings?.CancelCapture();
            CloseModal();
            open = false;
            FlightInput.PauseMenuOpen = false;
            if (pausedByMenu && simulation != null) simulation.SetPaused(false);
            pausedByMenu = false;
            root.RemoveFromClassList("game-menu--open");
            root.schedule.Execute(() => { if (!open) root.style.display = DisplayStyle.None; }).StartingIn(200);
            root.focusController?.focusedElement?.Blur();
        }

        // ─────────────────────────────── layout ───────────────────────────────

        void Build(VisualElement documentRoot)
        {
            documentRoot.Clear();
            StretchRoot(documentRoot);
            AddStyleSheet(documentRoot, MenuStyleSheet);

            root = El("game-menu");
            root.style.display = DisplayStyle.None;
            documentRoot.Add(root);
            ApplyRootFonts(root);

            if (shade == null) shade = Gradient(256);
            Add(root, El("game-menu__shade")).style.backgroundImage = new StyleBackground(shade);

            var top = BrandBar(root, "ЦЕНТР УПРАВЛЕНИЯ ПОЛЁТОМ");
            var chip = Add(top, El("chip"));
            chip.Add(El("status-dot", "status-dot--amber"));
            chip.Add(Text("СИМУЛЯЦИЯ", "chip__label", "t-strong"));
            chip.Add(Text("НА ПАУЗЕ", "chip__value", "t-strong"));

            var head = Add(root, El("screen-head"));
            var back = new Button(Close);
            back.AddToClassList("back-btn");
            back.Add(Text("ESC", "key-cap", "t-strong"));
            back.Add(Text("К ПОЛЁТУ", "key-hint__label", "t-strong"));
            head.Add(back);
            var title = Add(head, El("grow"));
            title.Add(Text("ПАУЗА  /  НАСТРОЙКИ", "eyebrow", "t-strong"));
            title.Add(Text("НАСТРОЙКИ", "screen-title", "t-strong"));

            settings?.Detach();
            settings = new SettingsView(root, 226f, 296f, BuildActions);

            var bottom = Add(root, El("hud-bottom"));
            KeyHint(bottom, "ESC", "К ПОЛЁТУ");
            KeyHint(bottom, "ЛКМ", "ВЫБРАТЬ");
            KeyHint(bottom, "← →", "ИЗМЕНИТЬ ЗНАЧЕНИЕ");
            bottom.Add(El("grow"));
            bottom.Add(Text("HORIZON  ·  NASA SPACE APPS 2026", "build-info", "t-mono"));

            BuildModal();
        }

        void BuildActions(VisualElement side)
        {
            var actions = Add(side, El("game-menu__actions"));
            actions.Add(ButtonEl("ПРОДОЛЖИТЬ ПОЛЁТ", Close, primary: true));
            mainMenuButton = Add(actions, ButtonEl("ГЛАВНОЕ МЕНЮ", () =>
            {
                if (!MainMenuAvailable) return;
                Confirm("ГЛАВНОЕ МЕНЮ", "Выйти в главное меню?",
                    "Текущий полёт не сохраняется. Профиль — кредиты, модули, образцы и конструкция ракеты — уже сохранён.",
                    "В ГЛАВНОЕ МЕНЮ", ReturnToMainMenu);
            }));
            actions.Add(ButtonEl("ВЫЙТИ ИЗ ИГРЫ", () => Confirm("ЗАВЕРШИТЬ СЕАНС", "Выйти из игры?",
                "Текущий полёт не сохраняется. Профиль игрока уже сохранён.", "ВЫЙТИ НА РАБОЧИЙ СТОЛ", Quit)));
            if (!MainMenuAvailable)
                actions.Add(Text("Главное меню недоступно: сцены MainMenu нет в Build Settings (SpaceSim ▸ Create Main Menu Scene).",
                    "game-menu__note"));
        }

        void BuildModal()
        {
            modal = Add(root, El("modal"));
            var box = Add(modal, El("panel", "modal__box"));
            modalEyebrow = Add(box, Text("", "eyebrow", "t-strong"));
            modalTitle = Add(box, Text("", "panel__title", "t-strong"));
            modalTitle.style.fontSize = 34;
            modalText = Add(box, Text("", "panel__text"));
            var buttons = Add(box, El("modal__buttons"));
            buttons.Add(ButtonEl("ОТМЕНА", CloseModal));
            var confirm = Add(buttons, ButtonEl("", () => modalConfirm?.Invoke(), primary: true));
            modalConfirmLabel = confirm.Q<Label>();
        }

        void Confirm(string eyebrow, string title, string text, string action, Action onConfirm)
        {
            settings?.CancelCapture();
            modalEyebrow.text = eyebrow;
            modalTitle.text = title;
            modalText.text = text;
            modalConfirmLabel.text = action;
            modalConfirm = onConfirm;
            modal.AddToClassList("modal--open");
        }

        void CloseModal() => modal?.RemoveFromClassList("modal--open");

        /// <summary>Leaves the flight for the main menu scene (the profile is already saved).</summary>
        public void ReturnToMainMenu()
        {
            Close();
            FlightInput.ModalWindowOpen = false;
            SceneManager.LoadScene(MainMenuScene);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Left-to-right darkening behind the text column, like the main menu's left shade.</summary>
        static Texture2D Gradient(int width)
        {
            var t = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color[width];
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                px[x] = new Color(0.008f, 0.012f, 0.03f, 0.88f * Mathf.Pow(1f - u, 1.6f));
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }
    }
}
