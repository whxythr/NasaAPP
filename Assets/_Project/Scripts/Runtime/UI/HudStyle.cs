using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>Shared minimal "instrument" look for IMGUI panels. Must be used from OnGUI.</summary>
    public static class HudStyle
    {
        static bool initialized;
        static Texture2D panelTexture, barTexture, barBackTexture;

        public static GUIStyle Panel, Title, Label, Value, Small, Mono, Phase, Hint, Button, Field, Body, Header;
        public static readonly Color Accent = new Color(0.55f, 0.85f, 1f);
        public static readonly Color Warning = new Color(1f, 0.75f, 0.3f);
        public static readonly Color Danger = new Color(1f, 0.4f, 0.35f);

        /// <summary>UI scale for the current resolution (1080p = 1).</summary>
        public static float Scale => Mathf.Clamp(Screen.height / 1080f, 0.75f, 2.5f);

        public static void Begin()
        {
            Ensure();
            float s = Scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        }

        /// <summary>Screen size in scaled GUI units.</summary>
        public static Vector2 VirtualScreen => new Vector2(Screen.width / Scale, Screen.height / Scale);

        static void Ensure()
        {
            if (initialized && panelTexture != null) return;
            initialized = true;
            panelTexture = Solid(new Color(0.02f, 0.04f, 0.07f, 0.78f));
            barTexture = Solid(Accent);
            barBackTexture = Solid(new Color(1f, 1f, 1f, 0.12f));

            Font mono = null;
            try
            {
                mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Cascadia Mono", "Menlo", "DejaVu Sans Mono", "Liberation Mono", "Courier New" }, 14);
            }
            catch { /* fall back to the default font */ }

            Panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8), alignment = TextAnchor.UpperLeft };
            Panel.normal.background = panelTexture;

            Title = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            Title.normal.textColor = Accent;

            Label = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            Label.normal.textColor = new Color(0.78f, 0.84f, 0.9f);

            Value = new GUIStyle(Label) { alignment = TextAnchor.MiddleRight };
            if (mono != null) Value.font = mono;
            Value.normal.textColor = Color.white;

            Mono = new GUIStyle(Label);
            if (mono != null) Mono.font = mono;

            Small = new GUIStyle(Label) { fontSize = 11, wordWrap = true };
            Small.normal.textColor = new Color(0.65f, 0.72f, 0.8f);

            Phase = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Phase.normal.textColor = Color.white;

            Hint = new GUIStyle(Small) { alignment = TextAnchor.MiddleCenter, wordWrap = false };

            Button = new GUIStyle(GUI.skin.button) { fontSize = 12 };
            Field = new GUIStyle(GUI.skin.textField) { fontSize = 13 };
            if (mono != null) Field.font = mono;

            Body = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, richText = true };
            Body.normal.textColor = new Color(0.85f, 0.89f, 0.94f);

            Header = new GUIStyle(Body) { fontSize = 18, fontStyle = FontStyle.Bold };
            Header.normal.textColor = Accent;
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        public static void Row(string label, string value, float labelWidth = 150f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Label, GUILayout.Width(labelWidth));
            GUILayout.Label(value, Value);
            GUILayout.EndHorizontal();
        }

        public static void Bar(Rect r, float fraction, Color color)
        {
            GUI.DrawTexture(r, barBackTexture);
            var c = GUI.color;
            GUI.color = color;
            float f = Mathf.Clamp01(fraction);
            GUI.DrawTexture(new Rect(r.x, r.yMax - r.height * f, r.width, r.height * f), barTexture);
            GUI.color = c;
        }
    }
}
