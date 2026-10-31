using SpaceSim.Mathematics;
using SpaceSim.Physics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// One per planet: renders that planet's atmosphere shell and feeds that planet's surface material with its own
    /// star direction, centre, scale and scattering parameters. Rayleigh density follows the PHYSICAL profile via its
    /// (effective) scale height; the shell radius is the visual bound derived from it — not the "space boundary".
    /// Rendering never feeds back into physics.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public sealed class AtmosphereRenderer : MonoBehaviour
    {
        static readonly int CameraPlanetPos = Shader.PropertyToID("_CameraPlanetPos");
        static readonly int CameraHeight = Shader.PropertyToID("_CameraHeight");
        static readonly int PlanetRadius = Shader.PropertyToID("_PlanetRadius");
        static readonly int AtmosphereHeight = Shader.PropertyToID("_AtmosphereHeight");
        static readonly int BetaRayleigh = Shader.PropertyToID("_BetaRayleigh");
        static readonly int BetaMie = Shader.PropertyToID("_BetaMie");
        static readonly int MieExtinction = Shader.PropertyToID("_MieExtinction");
        static readonly int ScaleHeightR = Shader.PropertyToID("_ScaleHeightR");
        static readonly int ScaleHeightM = Shader.PropertyToID("_ScaleHeightM");
        static readonly int MieG = Shader.PropertyToID("_MieG");
        static readonly int SunDir = Shader.PropertyToID("_SunDir");
        static readonly int SunIntensity = Shader.PropertyToID("_SunIntensity");
        static readonly int Exposure = Shader.PropertyToID("_Exposure");
        static readonly int ViewSamples = Shader.PropertyToID("_ViewSamples");
        static readonly int LightSamples = Shader.PropertyToID("_LightSamples");
        static readonly int UseDepth = Shader.PropertyToID("_UseDepth");
        static readonly int RenderScale = Shader.PropertyToID("_RenderScale");
        static readonly int HazeScattering = Shader.PropertyToID("_HazeScattering");
        static readonly int HazeAltitude = Shader.PropertyToID("_HazeAltitude");
        static readonly int HazeThickness = Shader.PropertyToID("_HazeThickness");

        static readonly int SSunDir = Shader.PropertyToID("_SpaceSimSunDir");
        static readonly int SSunColor = Shader.PropertyToID("_SpaceSimSunColor");
        static readonly int SPlanetCenter = Shader.PropertyToID("_SpaceSimPlanetCenter");
        static readonly int SPlanetRadius = Shader.PropertyToID("_SpaceSimPlanetRadius");
        static readonly int SRenderScale = Shader.PropertyToID("_SpaceSimRenderScale");
        static readonly int SBetaRayleigh = Shader.PropertyToID("_SpaceSimBetaRayleigh");
        static readonly int SMieExtinction = Shader.PropertyToID("_SpaceSimMieExtinction");
        static readonly int SScaleHeightR = Shader.PropertyToID("_SpaceSimScaleHeightR");
        static readonly int SScaleHeightM = Shader.PropertyToID("_SpaceSimScaleHeightM");
        static readonly int SHasAtmosphere = Shader.PropertyToID("_SpaceSimHasAtmosphere");
        static readonly int SSkyAmbient = Shader.PropertyToID("_SpaceSimSkyAmbient");

        PlanetRuntime planet;
        FloatingOrigin origin;
        StarSystemRuntime system;
        Material surfaceMaterial;
        Material material;
        MeshRenderer meshRenderer;
        Mesh shellMesh;

        /// <summary>Mesh inflation so the polygonal shell fully covers the analytic sphere.</summary>
        const float ShellInflation = 1.03f;

        public void Initialize(PlanetRuntime planetRuntime, FloatingOrigin floatingOrigin, StarSystemRuntime starSystem,
            Shader shader, Material planetSurfaceMaterial)
        {
            planet = planetRuntime;
            origin = floatingOrigin;
            system = starSystem;
            surfaceMaterial = planetSurfaceMaterial;
            material = new Material(shader) { name = "Atmosphere · " + planet.Definition.displayName };
            shellMesh = ProceduralMeshes.UvSphere(96, 48, "AtmosphereShell");
            shellMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f * ShellInflation);
            gameObject.AddComponent<MeshFilter>().sharedMesh = shellMesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (shellMesh != null) Destroy(shellMesh);
        }

        /// <summary>
        /// Scale height that reproduces the profile's density at 5 km (exact for the exponential model, a fit
        /// for tabulated ones). The shader's exp(−h/H) is the only approximation of the physical profile.
        /// </summary>
        public static double EffectiveScaleHeight(IAtmosphereProfile profile)
        {
            if (profile is ExponentialAtmosphere e) return e.ScaleHeight;
            double rho0 = profile.ReferenceDensity;
            double rho = profile.Density(5000.0);
            if (rho0 <= 0.0 || rho <= 0.0 || rho >= rho0) return 8500.0;
            return -5000.0 / System.Math.Log(rho / rho0);
        }

        void LateUpdate()
        {
            if (planet == null || planet.Model == null) return;
            var def = planet.Definition;
            var model = planet.Model;
            bool hasAir = def.atmosphereRendering.enabled && model.Atmosphere.ReferenceDensity > 0.0 && planet.VisualShellHeight > 0.0;

            AtmosphereOpticalProperties optics = def.optics;
            var tint = def.generation.atmosphereTint;
            Vector3d br = optics.rayleighScattering;
            var rayleigh = new Vector4((float)br.x * tint.r, (float)br.y * tint.g, (float)br.z * tint.b, 0f);
            float mieExt = (float)(optics.mieScattering * optics.mieExtinctionRatio);
            float hR = (float)EffectiveScaleHeight(model.Atmosphere);
            float hM = Mathf.Max(1f, (float)optics.mieScaleHeight);
            float scale = (float)planet.RenderScale;
            Vector3 center = planet.UnityCenter(origin);
            Vector3 sunDir = CoordinateSystem.ToUnityVector(system.SunDirectionFrom(model.Position));

            // ---- this planet's surface material ----
            if (surfaceMaterial != null)
            {
                Color sun = system.StarLightColor;
                surfaceMaterial.SetVector(SSunDir, sunDir);
                surfaceMaterial.SetVector(SSunColor, new Vector4(sun.r, sun.g, sun.b, 0f));
                surfaceMaterial.SetVector(SPlanetCenter, center);
                surfaceMaterial.SetFloat(SPlanetRadius, (float)model.Radius * scale);
                surfaceMaterial.SetFloat(SRenderScale, scale);
                surfaceMaterial.SetVector(SBetaRayleigh, rayleigh);
                surfaceMaterial.SetFloat(SMieExtinction, mieExt);
                surfaceMaterial.SetFloat(SScaleHeightR, hR);
                surfaceMaterial.SetFloat(SScaleHeightM, hM);
                surfaceMaterial.SetFloat(SHasAtmosphere, hasAir ? 1f : 0f);
                surfaceMaterial.SetVector(SSkyAmbient, system.SkyAmbient);
            }

            meshRenderer.enabled = hasAir;
            if (!hasAir) return;

            double R = model.Radius;
            double shell = planet.VisualShellHeight;
            transform.SetPositionAndRotation(center, Quaternion.identity);
            transform.localScale = Vector3.one * (float)((R + shell) * ShellInflation * scale);

            Vector3d camRel = origin.ViewerPosition - model.Position;
            bool haze = optics.HasHaze;
            Vector3d bh = haze ? optics.hazeScattering : Vector3d.Zero;
            material.SetVector(CameraPlanetPos, CoordinateSystem.ToUnityVector(camRel));
            material.SetFloat(CameraHeight, (float)(camRel.Magnitude - R));
            material.SetFloat(PlanetRadius, (float)R);
            material.SetFloat(AtmosphereHeight, (float)shell);
            material.SetVector(BetaRayleigh, rayleigh);
            material.SetFloat(BetaMie, (float)optics.mieScattering);
            material.SetFloat(MieExtinction, mieExt);
            material.SetFloat(ScaleHeightR, hR);
            material.SetFloat(ScaleHeightM, hM);
            material.SetFloat(MieG, (float)optics.mieAnisotropy);
            material.SetVector(SunDir, sunDir);
            material.SetFloat(SunIntensity, def.atmosphereRendering.sunIntensity);
            material.SetFloat(Exposure, def.atmosphereRendering.exposure);
            material.SetFloat(ViewSamples, def.atmosphereRendering.viewSamples);
            material.SetFloat(LightSamples, def.atmosphereRendering.lightSamples);
            material.SetFloat(UseDepth, 1f);
            material.SetFloat(RenderScale, scale);
            material.SetVector(HazeScattering, new Vector4((float)bh.x, (float)bh.y, (float)bh.z, 0f));
            material.SetFloat(HazeAltitude, (float)optics.hazeAltitude);
            material.SetFloat(HazeThickness, Mathf.Max(1f, (float)optics.hazeThickness));
        }
    }
}
