using System;
using SpaceSim.Mathematics;
using SpaceSim.Procedural;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Deterministic star field fixed in the inertial frame. One static mesh; the shader projects each star with
    /// the camera rotation only, so stars do not move with the camera and do not rotate with the planet.
    /// Visibility models eye/camera adaptation: in daylight air the bright sky hides stars; at night or above the
    /// atmosphere they appear. (This is purely visual — the atmosphere pass additionally dims them by transmittance.)
    /// </summary>
    [DefaultExecutionOrder(130)]
    public sealed class StarfieldController : MonoBehaviour
    {
        [SerializeField] int starSeed = 1977;
        [SerializeField] int starCount = 9000;
        [Tooltip("Faintest magnitude generated.")]
        [SerializeField] float limitingMagnitude = 6.8f;
        [SerializeField] float brightness = 2.5f;
        [SerializeField] float pixelSize = 1.6f;

        PlanetRuntime planet;
        FloatingOrigin origin;
        SolarLightingController sun;
        Material material;
        Mesh mesh;

        /// <summary>Current visibility 0..1 (exposed for HUD/tests).</summary>
        public float Visibility { get; private set; }

        public void Initialize(PlanetRuntime planetRuntime, FloatingOrigin floatingOrigin, SolarLightingController solar, Shader shader)
        {
            planet = planetRuntime;
            origin = floatingOrigin;
            sun = solar;
            mesh = BuildMesh();
            material = new Material(shader) { name = "Starfield (runtime)" };
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (mesh != null) Destroy(mesh);
        }

        Mesh BuildMesh()
        {
            var rng = new DeterministicRandom(DeterministicRandom.Mix((ulong)(uint)starSeed, 0x57A75UL));
            int n = Mathf.Max(1, starCount);
            var vertices = new Vector3[n * 4];
            var colors = new Color[n * 4];
            var uv = new Vector2[n * 4];
            var size = new Vector2[n * 4];
            var tris = new int[n * 6];
            var corners = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            for (int s = 0; s < n; s++)
            {
                Vector3 dir = CoordinateSystem.ToUnityVector(rng.UnitVector());
                // Mostly faint stars, few bright ones (magnitudes −1.5 … limit).
                double u = rng.NextDouble();
                double mag = limitingMagnitude - (limitingMagnitude + 1.5) * u * u * u * u * u * u;
                float flux = (float)Math.Pow(10.0, -0.4 * (mag - 1.0));
                float temperature = (float)(3000.0 + 9000.0 * rng.NextDouble() * rng.NextDouble());
                Color c = TemperatureToColor(temperature) * Mathf.Min(flux, 6f) * 0.25f;
                float px = Mathf.Lerp(0.8f, 2.2f, Mathf.Clamp01((float)((limitingMagnitude - mag) / 7.0)));
                for (int k = 0; k < 4; k++)
                {
                    int v = s * 4 + k;
                    vertices[v] = dir;
                    colors[v] = c;
                    uv[v] = corners[k];
                    size[v] = new Vector2(px, 0f);
                }
                int t = s * 6, b = s * 4;
                tris[t] = b; tris[t + 1] = b + 2; tris[t + 2] = b + 1;
                tris[t + 3] = b; tris[t + 4] = b + 3; tris[t + 5] = b + 2;
            }
            var m = new Mesh { name = "Starfield", indexFormat = n * 4 > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.vertices = vertices;
            m.colors = colors;
            m.uv = uv;
            m.uv2 = size;
            m.triangles = tris;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2e10f);
            return m;
        }

        /// <summary>Approximate blackbody colour (Tanner Helland fit), normalised to max component 1.</summary>
        static Color TemperatureToColor(float kelvin)
        {
            float t = kelvin / 100f;
            float r = t <= 66f ? 1f : Mathf.Clamp01(1.292936f * Mathf.Pow(t - 60f, -0.1332047f));
            float g = t <= 66f ? Mathf.Clamp01(0.3900816f * Mathf.Log(t) - 0.6318414f) : Mathf.Clamp01(1.129891f * Mathf.Pow(t - 60f, -0.0755148f));
            float b = t >= 66f ? 1f : (t <= 19f ? 0f : Mathf.Clamp01(0.5432068f * Mathf.Log(t - 10f) - 1.1962541f));
            float max = Mathf.Max(r, Mathf.Max(g, b));
            return new Color(r / max, g / max, b / max, 1f);
        }

        void LateUpdate()
        {
            if (planet == null || planet.Model == null) return;
            var model = planet.Model;
            Vector3d rel = origin.ViewerPosition - model.Position;
            double rho0 = model.Atmosphere.ReferenceDensity;
            // Fraction of the air column above the viewer (exact for an exponential atmosphere).
            double column = rho0 > 0.0 ? model.Atmosphere.Density(rel.Magnitude - model.Radius) / rho0 : 0.0;
            double sunUp = sun.SunElevationSineAtViewer;
            double daylight = MathD.SmoothStep(-0.20, 0.05, sunUp);
            double skyBrightness = Math.Min(1.0, column) * daylight;
            Visibility = 1f - (float)MathD.SmoothStep(0.0003, 0.03, skyBrightness);

            material.SetFloat("_Brightness", brightness * Visibility);
            material.SetFloat("_PixelSize", pixelSize);
        }
    }
}
