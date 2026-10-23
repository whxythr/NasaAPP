using System.Collections.Generic;
using SpaceSim.Flight;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>Flight Manual window: formulas with explanations, assumptions, pitfalls and live worked examples.</summary>
    [DefaultExecutionOrder(220)]
    public sealed class FlightManualUI : MonoBehaviour
    {
        PlanetRuntime planet;
        bool open;
        int selected;
        Vector2 scroll, listScroll;
        List<FlightManualContent.Section> sections;
        object sectionsBuiltFor;

        public bool IsOpen => open;

        public void Initialize(PlanetRuntime planetRuntime)
        {
            planet = planetRuntime;
        }

        public void Toggle() => open = !open;

        void OnGUI()
        {
            if (!open || planet == null || planet.Model == null) return;
            if (sections == null || !ReferenceEquals(sectionsBuiltFor, planet.Model))
            {
                sections = FlightManualContent.Build(planet.Model, planet.Definition.launchSite.latitudeDegrees);
                sectionsBuiltFor = planet.Model;
                selected = Mathf.Clamp(selected, 0, sections.Count - 1);
            }

            HudStyle.Begin();
            Vector2 screen = HudStyle.VirtualScreen;
            var r = new Rect(screen.x * 0.5f - 520, 70, 1040, screen.y - 140);
            GUI.Box(r, GUIContent.none, HudStyle.Panel);
            GUILayout.BeginArea(new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 16));
            GUILayout.BeginHorizontal();
            GUILayout.Label("FLIGHT MANUAL · " + planet.Definition.displayName, HudStyle.Title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Закрыть (M)", HudStyle.Button, GUILayout.Width(110))) open = false;
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            listScroll = GUILayout.BeginScrollView(listScroll, GUILayout.Width(260));
            for (int i = 0; i < sections.Count; i++)
            {
                bool on = GUILayout.Toggle(i == selected, sections[i].Title, HudStyle.Button);
                if (on && i != selected)
                {
                    selected = i;
                    scroll = Vector2.zero;
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(10);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(sections[selected].Title, HudStyle.Header);
            GUILayout.Space(4);
            GUILayout.Label(sections[selected].Body, HudStyle.Body);
            GUILayout.EndScrollView();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
