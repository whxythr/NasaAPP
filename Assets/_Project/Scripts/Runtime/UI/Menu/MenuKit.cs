using System;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace SpaceSim.Runtime.Menu
{
    /// <summary>
    /// Shared building blocks of the HORIZON UI Toolkit screens (main menu, in-game Esc menu): element helpers, the
    /// runtime panel, and the DIN-style fonts. Classes come from Resources/UI/MainMenu.uss (+ GameMenu.uss).
    /// </summary>
    public static class MenuKit
    {
        public const string MenuStyleSheet = "UI/MainMenu", GameStyleSheet = "UI/GameMenu", PanelTemplate = "UI/HorizonPanel";

        static bool fontsLoaded;
        static FontDefinition? regular, strong, mono;

        // ─────────────────────────────── elements ───────────────────────────────

        public static T Add<T>(VisualElement parent, T child) where T : VisualElement
        {
            parent.Add(child);
            return child;
        }

        public static VisualElement El(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) e.AddToClassList(c);
            return e;
        }

        public static Label Text(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) l.AddToClassList(c);
            return l;
        }

        /// <summary>Menu button (".btn"); primary = amber.</summary>
        public static Button ButtonEl(string label, Action onClick, bool primary = false, bool disabled = false)
        {
            var b = new Button(onClick);
            b.AddToClassList("btn");
            if (primary) b.AddToClassList("btn--primary");
            if (disabled) b.AddToClassList("btn--disabled");
            b.Add(Text(label, "btn__label", "t-strong"));
            return b;
        }

        /// <summary>Key cap plus caption, as in the menu's bottom bar ("ESC  BACK").</summary>
        public static VisualElement KeyHint(VisualElement parent, string key, string label)
        {
            var hint = Add(parent, El("key-hint"));
            hint.Add(Text(key, "key-cap", "t-strong"));
            hint.Add(Text(label, "key-hint__label", "t-strong"));
            return hint;
        }

        /// <summary>The amber diamond + HORIZON word mark of the top bar.</summary>
        public static VisualElement BrandBar(VisualElement parent, string subtitle)
        {
            var top = Add(parent, El("hud-top"));
            var mark = Add(top, El("brand-mark"));
            mark.Add(El("brand-mark__core"));
            top.Add(Text("HORIZON", "brand-name", "t-strong"));
            top.Add(Text(subtitle, "brand-sub"));
            top.Add(El("grow"));
            return top;
        }

        /// <summary>Loads a style sheet from Resources and attaches it (warns once if missing).</summary>
        public static void AddStyleSheet(VisualElement element, string resource)
        {
            var sheet = Resources.Load<StyleSheet>(resource);
            if (sheet != null) element.styleSheets.Add(sheet);
            else Debug.LogWarning("[HORIZON UI] Resources/" + resource + ".uss not found — the screen will be unstyled.");
        }

        // ─────────────────────────────── panel ───────────────────────────────

        /// <summary>
        /// Runtime panel at the menu's 1920×1080 reference resolution. <paramref name="opaque"/> clears the screen (main menu);
        /// an overlay over the 3D view must not.
        /// </summary>
        public static PanelSettings CreatePanel(string name, bool opaque, int sortingOrder)
        {
            // A copy of Resources/UI/HorizonPanel (it carries the empty HORIZON theme): a panel created from scratch has no
            // theme and Unity warns about it on creation. Created by SpaceSim ▸ Create Main Menu Scene / Setup.
            var template = Resources.Load<PanelSettings>(PanelTemplate);
            var panel = template != null ? UnityEngine.Object.Instantiate(template) : ScriptableObject.CreateInstance<PanelSettings>();
            panel.name = name;
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.clearColor = opaque;
            panel.colorClearValue = new Color(0.016f, 0.024f, 0.047f, 1f);
            panel.sortingOrder = sortingOrder;
            return panel;
        }

        /// <summary>What the default runtime theme would do (the runtime-created panel has none): fill the screen.</summary>
        public static void StretchRoot(VisualElement documentRoot)
        {
            documentRoot.style.position = Position.Absolute;
            documentRoot.style.left = documentRoot.style.top = documentRoot.style.right = documentRoot.style.bottom = 0;
        }

        // ─────────────────────────────── fonts ───────────────────────────────

        /// <summary>
        /// Bahnschrift (ships with Windows) for the DIN-style look and Consolas for numbers; the built-in font elsewhere.
        /// Sets the root font now and the ".t-strong" / ".t-mono" fonts on the next frame (after the screens are built).
        /// </summary>
        public static void ApplyRootFonts(VisualElement root)
        {
            EnsureFonts();
            root.style.unityFontDefinition = regular ?? FontDefinition.FromFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            root.schedule.Execute(() => ApplyFonts(root.parent ?? root));
        }

        /// <summary>Applies the strong / mono fonts to every ".t-strong" / ".t-mono" element under <paramref name="scope"/>.</summary>
        public static void ApplyFonts(VisualElement scope)
        {
            if (scope == null) return;
            EnsureFonts();
            scope.Query(className: "t-strong").ForEach(e =>
            {
                if (strong.HasValue) e.style.unityFontDefinition = strong.Value;
                else e.style.unityFontStyleAndWeight = FontStyle.Bold;
            });
            if (mono.HasValue) scope.Query(className: "t-mono").ForEach(e => e.style.unityFontDefinition = mono.Value);
        }

        static void EnsureFonts()
        {
            // Runtime font assets survive scene loads (DontUnloadUnusedAsset); rebuild only if something destroyed them.
            if (fontsLoaded && (!regular.HasValue || regular.Value.fontAsset != null)) return;
            fontsLoaded = true;
            regular = LoadFont(("Bahnschrift", "Regular"), ("Segoe UI", "Regular"));
            // Bahnschrift is a variable font: TextCore loads only its default (Regular) instance, so "SemiBold" / "Bold"
            // would just log "Unable to find a font file"; the strong weight comes from Segoe UI.
            strong = LoadFont(("Segoe UI", "Semibold"), ("Segoe UI", "Bold"));
            mono = LoadFont(("Consolas", "Regular"), ("Cascadia Mono", "Regular"));
        }

        static FontDefinition? LoadFont(params (string family, string style)[] candidates)
        {
            foreach (var (family, style) in candidates)
            {
                try
                {
                    var asset = FontAsset.CreateFontAsset(family, style);
                    if (asset == null) continue;
                    asset.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    return FontDefinition.FromSDFFont(asset);
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
