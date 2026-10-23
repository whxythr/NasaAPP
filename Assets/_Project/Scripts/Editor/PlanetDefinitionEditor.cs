using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Procedural;
using SpaceSim.Runtime;
using SpaceSim.Units;
using UnityEditor;
using UnityEngine;

namespace SpaceSim.Editor
{
    /// <summary>Inspector for PlanetDefinition: default fields plus derived physical values and seed tools.</summary>
    [CustomEditor(typeof(PlanetDefinition))]
    public sealed class PlanetDefinitionEditor : UnityEditor.Editor
    {
        string fingerprint;

        public override void OnInspectorGUI()
        {
            var def = (PlanetDefinition)target;
            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Derived values (read-only)", EditorStyles.boldLabel);
            var p = def.physical;
            var issues = p.Validate();
            if (issues.Count > 0)
            {
                EditorGUILayout.HelpBox(string.Join("\n", issues), MessageType.Error);
                return;
            }
            double mu = p.GravitationalParameter, R = p.meanRadius;
            Line("μ used by physics", UnitFormatter.Scientific(mu, 7) + " m³/s²  (" + p.gravitySource + ")");
            Line("G·M vs measured μ", UnitFormatter.Number(p.GravitationalParameterMismatch * 100.0, 4) + " %");
            Line("g at R = μ/R²", UnitFormatter.Acceleration(p.SurfaceGravity));
            Line("Mean density", UnitFormatter.Number(p.MeanDensity, 1) + " kg/m³");
            Line("v_circ at 200 km", UnitFormatter.Speed(OrbitalMechanics.CircularVelocity(mu, R + 200000), DisplayUnitSystem.SI));
            Line("v_esc at R", UnitFormatter.Speed(OrbitalMechanics.EscapeVelocity(mu, R), DisplayUnitSystem.SI));
            Line("Equatorial surface speed ωR", UnitFormatter.Speed(System.Math.Abs(MathD.TwoPi / p.siderealRotationPeriod) * R, DisplayUnitSystem.SI));
            var profile = AtmosphericModel.Create(p.atmosphere);
            Line("Visual atmosphere shell", UnitFormatter.Distance(AtmosphereOptics.VisualShellHeight(profile, def.optics), DisplayUnitSystem.Kilometric));
            Line("ρ at space boundary", UnitFormatter.Density(profile.Density(p.spaceBoundaryAltitude)));
            if (p.atmosphere.hasAtmosphere && p.SurfaceGravity > 0)
                Line("Isothermal H = RT/(Mg)", UnitFormatter.Distance(AtmosphericModel.IsothermalScaleHeight(
                    p.atmosphere.surfaceTemperature, p.atmosphere.meanMolarMass, p.SurfaceGravity), DisplayUnitSystem.SI));
            if (p.GravitationalParameterMismatch > 0.01 && p.gravitySource == GravitationalParameterSource.Measured)
                EditorGUILayout.HelpBox("Measured μ differs from G·M by more than 1 %. Check mass or μ.", MessageType.Warning);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Seeds & reproducibility", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("New random visual seed"))
            {
                Undo.RecordObject(def, "New visual seed");
                def.generation.seed = System.Environment.TickCount & 0x7FFFFFFF;
                fingerprint = null;
                EditorUtility.SetDirty(def);
                def.NotifyChanged();
            }
            if (GUILayout.Button("Generate physics from physical seed"))
            {
                Undo.RecordObject(def, "Generate physics");
                def.GeneratePhysicalProperties(def.physicalSeed);
                EditorUtility.SetDirty(def);
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Compute surface fingerprint"))
            {
                try
                {
                    fingerprint = PlanetFingerprint.Compute(PlanetSurfaceSampler.Create(def.BuildGenerationSettings(), R));
                }
                catch (System.Exception e)
                {
                    fingerprint = "error: " + e.Message;
                }
            }
            if (!string.IsNullOrEmpty(fingerprint)) Line("Fingerprint (alg. v" + def.generation.algorithmVersion + ")", fingerprint);
            EditorGUILayout.HelpBox("The visual seed changes only relief and colours; mass, radius and gravity are never derived from it.", MessageType.Info);
        }

        static void Line(string label, string value) => EditorGUILayout.LabelField(label, value);
    }

    /// <summary>Colour picker for engine-independent ColorRGB fields.</summary>
    [CustomPropertyDrawer(typeof(ColorRGB))]
    public sealed class ColorRGBDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var r = property.FindPropertyRelative("r");
            var g = property.FindPropertyRelative("g");
            var b = property.FindPropertyRelative("b");
            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            Color c = EditorGUI.ColorField(position, label, new Color(r.floatValue, g.floatValue, b.floatValue), true, false, false);
            if (EditorGUI.EndChangeCheck())
            {
                r.floatValue = c.r;
                g.floatValue = c.g;
                b.floatValue = c.b;
            }
            EditorGUI.EndProperty();
        }
    }
}
