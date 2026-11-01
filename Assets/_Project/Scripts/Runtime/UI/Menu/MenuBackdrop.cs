using System;
using System.Threading.Tasks;
using SpaceSim.Mathematics;
using SpaceSim.Procedural;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceSim.Runtime.Menu
{
    /// <summary>
    /// Full-screen animated backdrop for the main menu: nebula, twinkling star field, the K-type home star and the
    /// homeworld (violet oceans, ochre deserts, night-side city lights) rising from the lower right.
    /// Every texture is generated procedurally on a worker thread, so the menu ships with no art assets;
    /// layers fade in when ready and drift with the pointer for a parallax effect.
    /// </summary>
    public sealed class MenuBackdrop : VisualElement
    {
        const int PlanetSize = 1536, NebulaWidth = 640, NebulaHeight = 360;
        const ulong Seed = 0x4E415341_41505021UL;

        readonly VisualElement nebula, planet, glow, streak;
        readonly StarLayer stars;
        Task<Color32[]> planetJob, nebulaJob;
        Vector2 pointer, parallax;

        public MenuBackdrop()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("backdrop");

            nebula = Layer("backdrop__nebula");
            stars = new StarLayer();
            stars.AddToClassList("backdrop__layer");
            Add(stars);
            glow = Layer("backdrop__glow");
            glow.style.backgroundImage = new StyleBackground(Radial(256, new Color(1f, 0.9f, 0.78f), 6f));
            streak = Layer("backdrop__streak");
            streak.style.backgroundImage = new StyleBackground(Streak(512, 32));
            planet = Layer("backdrop__planet");
            Layer("backdrop__shade-left").style.backgroundImage = new StyleBackground(LinearAlpha(256, 1, horizontal: true, 0.94f, 2.1f));
            Layer("backdrop__shade-bottom").style.backgroundImage = new StyleBackground(LinearAlpha(1, 128, horizontal: false, 0.85f, 1.6f));
            Layer("backdrop__vignette").style.backgroundImage = new StyleBackground(Vignette(256, 144));

            planetJob = Task.Run(() => BuildPlanet(PlanetSize));
            nebulaJob = Task.Run(() => BuildNebula(NebulaWidth, NebulaHeight));
            schedule.Execute(Tick).Every(16);
            RegisterCallback<AttachToPanelEvent>(e => panel.visualTree.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown));
        }

        /// <summary>True once the procedural textures have been uploaded (used by the screenshot harness).</summary>
        public bool IsReady => planetJob == null && nebulaJob == null;

        VisualElement Layer(string className)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("backdrop__layer");
            e.AddToClassList(className);
            Add(e);
            return e;
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (layout.width <= 0f) return;
            pointer = new Vector2(e.position.x / layout.width - 0.5f, e.position.y / layout.height - 0.5f);
        }

        void Tick(TimerState t)
        {
            if (planetJob != null && planetJob.IsCompleted)
            {
                if (planetJob.Status == TaskStatus.RanToCompletion)
                {
                    planet.style.backgroundImage = new StyleBackground(Upload(planetJob.Result, PlanetSize, PlanetSize, false));
                    planet.AddToClassList("backdrop__layer--ready");
                }
                else Debug.LogException(planetJob.Exception);
                planetJob = null;
            }
            if (nebulaJob != null && nebulaJob.IsCompleted)
            {
                if (nebulaJob.Status == TaskStatus.RanToCompletion)
                {
                    nebula.style.backgroundImage = new StyleBackground(Upload(nebulaJob.Result, NebulaWidth, NebulaHeight, true));
                    nebula.AddToClassList("backdrop__layer--ready");
                }
                else Debug.LogException(nebulaJob.Exception);
                nebulaJob = null;
            }

            parallax = Vector2.Lerp(parallax, pointer, 0.06f);
            Shift(nebula, 8f);
            Shift(stars, 14f);
            Shift(glow, 18f);
            Shift(streak, 18f);
            Shift(planet, 34f);
            stars.Time = Time.unscaledTime;
            stars.MarkDirtyRepaint();
        }

        void Shift(VisualElement e, float pixels) =>
            e.style.translate = new Translate(-parallax.x * pixels, -parallax.y * pixels);

        // ───────────────────────────────── star field ─────────────────────────────────

        sealed class StarLayer : VisualElement
        {
            struct Star { public Vector2 Position; public float Size, Brightness, Phase, Speed; public Color Tint; }
            readonly Star[] stars = new Star[900];
            public float Time;

            public StarLayer()
            {
                pickingMode = PickingMode.Ignore;
                var rng = new DeterministicRandom(Seed ^ 0x51A25UL);
                for (int i = 0; i < stars.Length; i++)
                {
                    double m = rng.NextDouble();
                    float temp = (float)rng.NextDouble();
                    stars[i] = new Star
                    {
                        Position = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble()),
                        Size = (float)(0.7 + 2.0 * m * m * m),
                        Brightness = (float)(0.25 + 0.75 * Math.Pow(rng.NextDouble(), 2.2)),
                        Phase = (float)(rng.NextDouble() * Math.PI * 2.0),
                        Speed = (float)rng.Range(0.4, 2.2),
                        Tint = temp < 0.2f ? new Color(1f, 0.82f, 0.66f) : temp < 0.55f ? new Color(0.8f, 0.88f, 1f) : Color.white,
                    };
                }
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext mgc)
            {
                float w = contentRect.width, h = contentRect.height;
                if (w <= 0f || h <= 0f) return;
                int flares = 0;
                foreach (var s in stars) if (s.Size > 2.2f) flares++;
                var mesh = mgc.Allocate((stars.Length + flares * 2) * 4, (stars.Length + flares * 2) * 6);
                ushort v = 0;
                foreach (var s in stars)
                {
                    float twinkle = 0.75f + 0.25f * Mathf.Sin(Time * s.Speed + s.Phase);
                    var c = s.Tint;
                    c.a = s.Brightness * twinkle;
                    var p = new Vector2(s.Position.x * w, s.Position.y * h);
                    Quad(mesh, ref v, p, s.Size, s.Size, c);
                    if (s.Size > 2.2f)
                    {
                        c.a *= 0.35f;
                        Quad(mesh, ref v, p, s.Size * 5f, 0.6f, c);
                        Quad(mesh, ref v, p, 0.6f, s.Size * 5f, c);
                    }
                }
            }

            static void Quad(MeshWriteData mesh, ref ushort v, Vector2 c, float w, float h, Color color)
            {
                float x0 = c.x - w * 0.5f, x1 = c.x + w * 0.5f, y0 = c.y - h * 0.5f, y1 = c.y + h * 0.5f;
                mesh.SetNextVertex(new Vertex { position = new Vector3(x0, y0, Vertex.nearZ), tint = color });
                mesh.SetNextVertex(new Vertex { position = new Vector3(x1, y0, Vertex.nearZ), tint = color });
                mesh.SetNextVertex(new Vertex { position = new Vector3(x1, y1, Vertex.nearZ), tint = color });
                mesh.SetNextVertex(new Vertex { position = new Vector3(x0, y1, Vertex.nearZ), tint = color });
                mesh.SetNextIndex(v); mesh.SetNextIndex((ushort)(v + 1)); mesh.SetNextIndex((ushort)(v + 2));
                mesh.SetNextIndex(v); mesh.SetNextIndex((ushort)(v + 2)); mesh.SetNextIndex((ushort)(v + 3));
                v += 4;
            }
        }

        // ───────────────────────────── procedural textures ─────────────────────────────

        static Texture2D Upload(Color32[] pixels, int w, int h, bool opaque)
        {
            var t = new Texture2D(w, h, opaque ? TextureFormat.RGB24 : TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.HideAndDontSave,
            };
            t.SetPixels32(pixels);
            t.Apply(true, true);
            return t;
        }

        static Color32[] BuildPlanet(int n)
        {
            var terrain = new GradientNoise3D(Seed);
            var climate = new GradientNoise3D(Seed ^ 0xC11A7EUL);
            var clouds = new GradientNoise3D(Seed ^ 0xC10D5UL);
            var cities = new GradientNoise3D(Seed ^ 0xC17EUL);
            var px = new Color32[n * n];

            float discRadius = n * 0.5f * 0.86f, centre = n * 0.5f;
            var light = new Vector3(-0.72f, 0.5f, 0.48f).normalized;
            var halfway = (light + Vector3.forward).normalized;
            var sunColor = new Color(1f, 0.9f, 0.78f);
            var sky = new Color(0.45f, 0.56f, 1f);
            // Planet frame: axial tilt 22.5° (HomeSystemCatalog) and a longitude that puts a continent on the limb.
            var spin = Quaternion.Euler(0f, 0f, -22.5f) * Quaternion.Euler(0f, 128f, 0f);

            Parallel.For(0, n, row =>
            {
                for (int col = 0; col < n; col++)
                {
                    float x = (col + 0.5f - centre) / discRadius;
                    float y = (row + 0.5f - centre) / discRadius; // Texture rows go bottom → top, so +y is up.
                    float r = Mathf.Sqrt(x * x + y * y);
                    Color result;

                    // Atmosphere halo outside the disc, brighter towards the star.
                    float rimFacing = Mathf.Clamp01(0.55f + 0.75f * ((x * light.x + y * light.y) / Mathf.Max(r, 1e-4f)));
                    float halo = Mathf.Exp(-Mathf.Max(r - 1f, 0f) * 26f) * rimFacing * rimFacing;
                    Color haloColor = sky * 1.15f;

                    if (r < 1f + 1.5f / discRadius)
                    {
                        float z = Mathf.Sqrt(Mathf.Max(0f, 1f - r * r));
                        var normal = new Vector3(x, y, z);
                        var q = spin * normal;
                        var p = new Vector3d(q.x, q.y, q.z);
                        float lat = Mathf.Abs(q.y);

                        float h = (float)terrain.Fbm(p * 1.7, 7, 2.05, 0.52);
                        float moisture = (float)climate.Fbm(p * 2.3, 4, 2.0, 0.5);
                        bool land = h > 0.035f;

                        Color surface;
                        if (land)
                        {
                            float relief = Mathf.Clamp01((h - 0.035f) * 4.5f);
                            var desert = new Color(0.80f, 0.58f, 0.36f);
                            var forest = new Color(0.17f, 0.30f, 0.27f);
                            var steppe = new Color(0.52f, 0.46f, 0.33f);
                            float aridity = Mathf.Clamp01(1.25f - lat * 3.2f + moisture * 1.6f);
                            surface = Color.Lerp(Color.Lerp(forest, steppe, Mathf.Clamp01(moisture * 2f + 0.5f)), desert, aridity);
                            surface = Color.Lerp(surface, new Color(0.47f, 0.42f, 0.40f), relief * 0.8f);
                        }
                        else
                        {
                            float depth = Mathf.Clamp01((0.035f - h) * 7f);
                            surface = Color.Lerp(new Color(0.40f, 0.28f, 0.80f), new Color(0.09f, 0.05f, 0.26f), Mathf.Sqrt(depth));
                        }

                        float ice = Mathf.Clamp01((lat - 0.82f + moisture * 0.12f) * 14f);
                        surface = Color.Lerp(surface, new Color(0.9f, 0.93f, 1f), ice);

                        var cp = new Vector3d(q.x * 2.6, q.y * 6.5, q.z * 2.6);
                        float c = (float)clouds.Fbm(cp + p * 1.5, 6, 2.1, 0.55);
                        float cloud = Mathf.Clamp01((c + 0.02f) * 3.2f);
                        cloud *= cloud;

                        float ndl = Vector3.Dot(normal, light);
                        float day = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ndl * 2.4f + 0.18f));
                        float diffuse = Mathf.Clamp01(ndl) * 0.85f + 0.15f * day;
                        float dusk = Mathf.Clamp01(1f - Mathf.Abs(ndl - 0.05f) * 6f);

                        Color lit = Color.Lerp(surface, Color.white, cloud * 0.92f) * diffuse;
                        lit = lit * Color.Lerp(sunColor, new Color(1f, 0.55f, 0.35f), dusk * 0.7f);
                        if (!land)
                        {
                            float spec = Mathf.Pow(Mathf.Clamp01(Vector3.Dot(normal, halfway)), 70f) * (1f - cloud) * 0.9f;
                            lit += new Color(1f, 0.86f, 0.7f) * spec;
                        }

                        // Night side: warm city lights on habitable land.
                        if (land && ice < 0.5f)
                        {
                            float cityNoise = (float)cities.Sample(p * 42.0) + 0.5f * (float)cities.Sample(p * 95.0);
                            float city = Mathf.Clamp01((cityNoise - 0.38f) * 4f) * (1f - day) * (1f - cloud * 0.8f);
                            lit += new Color(1f, 0.68f, 0.32f) * city * 0.9f;
                        }

                        // Rayleigh-ish rim: thin violet-blue scattering towards the limb on the day side.
                        float fresnel = Mathf.Pow(1f - z, 2.6f);
                        float scatterLight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ndl * 1.8f + 0.35f));
                        lit = Color.Lerp(lit, sky * 1.2f, fresnel * scatterLight * 0.85f);
                        lit += new Color(1f, 0.5f, 0.3f) * dusk * fresnel * 0.6f;

                        float edge = Mathf.Clamp01((1f - r) * discRadius + 0.5f);
                        result = Color.Lerp(haloColor, lit, edge);
                        result.a = Mathf.Max(edge, halo);
                    }
                    else
                    {
                        result = haloColor;
                        result.a = halo;
                    }
                    px[row * n + col] = ToneMap(result);
                }
            });
            return px;
        }

        static Color32[] BuildNebula(int w, int h)
        {
            var noise = new GradientNoise3D(Seed ^ 0xBEB01AUL);
            var warp = new GradientNoise3D(Seed ^ 0x3A2BUL);
            var px = new Color32[w * h];
            var space = new Color(0.012f, 0.018f, 0.04f);
            var violet = new Color(0.32f, 0.14f, 0.55f);
            var blue = new Color(0.06f, 0.20f, 0.46f);
            var ember = new Color(0.65f, 0.32f, 0.14f);

            Parallel.For(0, h, row =>
            {
                for (int col = 0; col < w; col++)
                {
                    float u = col / (float)w, v = row / (float)h;
                    var p = new Vector3d(u * 3.6, v * 2.0, 0.37);
                    double wx = warp.Fbm(p * 0.8, 3, 2.0, 0.5);
                    var q = new Vector3d(p.x + wx * 1.4, p.y + wx * 0.9, p.z);
                    float density = (float)noise.Fbm(q, 6, 2.0, 0.55);
                    float hue = (float)warp.Fbm(q * 0.6 + new Vector3d(5, 1, 3), 3, 2.0, 0.5);

                    // A diagonal "galactic" band from the upper left to the right edge.
                    float band = Mathf.Exp(-Mathf.Pow((v - 0.25f - u * 0.45f) * 3.1f, 2f));
                    float cloud = Mathf.Clamp01(density * 2.4f + 0.25f) * (0.25f + 0.75f * band);
                    cloud = cloud * cloud;

                    // Upper left is warmed by the home star.
                    float warmth = Mathf.Clamp01(1.1f - Mathf.Sqrt((u - 0.12f) * (u - 0.12f) * 1.4f + (v - 0.85f) * (v - 0.85f)) * 1.8f);
                    Color tint = Color.Lerp(blue, violet, Mathf.Clamp01(hue * 2.5f + 0.5f));
                    tint = Color.Lerp(tint, ember, warmth * 0.65f);
                    Color c = space + tint * cloud * 0.55f + new Color(0.9f, 0.62f, 0.4f) * warmth * warmth * 0.12f;
                    px[row * w + col] = ToneMap(c);
                }
            });
            return px;
        }

        static Color32 ToneMap(Color c)
        {
            // Soft shoulder so bright highlights roll off instead of clipping.
            float Map(float x) => 1f - Mathf.Exp(-x * 1.35f);
            return new Color(Map(c.r), Map(c.g), Map(c.b), Mathf.Clamp01(c.a));
        }

        static Texture2D Radial(int n, Color color, float falloff)
        {
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = 0.3f * Mathf.Exp(-r * falloff) + 0.7f * Mathf.Exp(-r * 30f);
                a *= Mathf.Clamp01((1f - r) * 3f);
                var c = Color.Lerp(color, Color.white, Mathf.Exp(-r * 18f));
                // Fade colour with alpha: in linear space the last 8-bit alpha step would otherwise show as a ring.
                float k = Mathf.Sqrt(Mathf.Clamp01(a));
                c = new Color(c.r * k, c.g * k, c.b * k, Mathf.Clamp01(a));
                px[y * n + x] = c;
            }
            return Upload(px, n, n, false);
        }

        static Texture2D Streak(int w, int h)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Abs((x + 0.5f) / w * 2f - 1f), dy = Mathf.Abs((y + 0.5f) / h * 2f - 1f);
                float a = Mathf.Exp(-dx * 4.5f) * Mathf.Exp(-dy * dy * 9f) * (1f - dx);
                float k = Mathf.Sqrt(a);
                px[y * w + x] = new Color(k, 0.85f * k, 0.68f * k, a * 0.55f);
            }
            return Upload(px, w, h, false);
        }

        static Texture2D LinearAlpha(int w, int h, bool horizontal, float maxAlpha, float exponent)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float t = horizontal ? (x + 0.5f) / w : (y + 0.5f) / h;
                float a = maxAlpha * Mathf.Pow(1f - t, exponent);
                px[y * w + x] = new Color(0.008f, 0.012f, 0.03f, a);
            }
            return Upload(px, w, h, false);
        }

        static Texture2D Vignette(int w, int h)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x + 0.5f) / w * 2f - 1f, dy = (y + 0.5f) / h * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx * 0.8f + dy * dy);
                px[y * w + x] = new Color(0f, 0f, 0f, Mathf.Clamp01((r - 0.75f) * 0.9f));
            }
            return Upload(px, w, h, false);
        }
    }
}
