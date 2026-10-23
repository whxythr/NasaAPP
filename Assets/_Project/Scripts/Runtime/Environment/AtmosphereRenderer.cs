using SpaceSim.Mathematics;
using SpaceSim.Physics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Renders the atmosphere shell and publishes atmosphere parameters as shader globals for the terrain.
    /// Rayleigh density follows the PHYSICAL profile via its (effective) scale height; the shell radius is the
    /// visual bound derived from it — not the 100 km "space boundary". Rendering never feeds back into physics.
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

        static readonly int GBetaRayleigh = Shader.PropertyToID("_SpaceSimBetaRayleigh");
        static readonly int GMieExtinction = Shader.PropertyToID("_SpaceSimMieExtinction");
        static readonly int GScaleHeightR = Shader.PropertyToID("_SpaceSimScaleHeightR");
        static readonly int GScaleHeightM = Shader.PropertyToID("_SpaceSimScaleHeightM");
        static readonly int GHasAtmosphere = Shader.PropertyToID("_SpaceSimHasAtmosphere");

        PlanetRuntime planet;
        FloatingOrigin origin;
        SolarLightingController sun;
        Material material;
        MeshRenderer meshRenderer;
        Mesh shellMesh;

        /// <summary>Mesh inflation so the polygonal shell fully covers the analytic sphere.</summary>
        const float ShellInflation = 1.03f;

        public void Initialize(PlanetRuntime planetRuntime, FloatingOrigin floatingOrigin, SolarLightingController solar, Shader shader)
        {
            planet = planetRuntime;
            origin = floatingOrigin;
            sun = solar;
            material = new Material(shader) { name = "Atmosphere (runtime)" };
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
            float hM = (float)optics.mieScaleHeight;

            Shader.SetGlobalVector(GBetaRayleigh, rayleigh);
            Shader.SetGlobalFloat(GMieExtinction, mieExt);
            Shader.SetGlobalFloat(GScaleHeightR, hR);
            Shader.SetGlobalFloat(GScaleHeightM, Mathf.Max(1f, hM));
            Shader.SetGlobalFloat(GHasAtmosphere, hasAir ? 1f : 0f);

            meshRenderer.enabled = hasAir;
            if (!hasAir) return;

            double R = model.Radius;
            double shell = planet.VisualShellHeight;
            transform.SetPositionAndRotation(planet.UnityCenter(origin), Quaternion.identity);
            transform.localScale = Vector3.one * (float)((R + shell) * ShellInflation);

            Vector3d camRel = origin.ViewerPosition - model.Position;
            material.SetVector(CameraPlanetPos, CoordinateSystem.ToUnityVector(camRel));
            material.SetFloat(CameraHeight, (float)(camRel.Magnitude - R));
            material.SetFloat(PlanetRadius, (float)R);
            material.SetFloat(AtmosphereHeight, (float)shell);
            material.SetVector(BetaRayleigh, rayleigh);
            material.SetFloat(BetaMie, (float)optics.mieScattering);
            material.SetFloat(MieExtinction, mieExt);
            material.SetFloat(ScaleHeightR, hR);
            material.SetFloat(ScaleHeightM, Mathf.Max(1f, hM));
            material.SetFloat(MieG, (float)optics.mieAnisotropy);
            material.SetVector(SunDir, sun.SunDirectionUnity);
            material.SetFloat(SunIntensity, def.atmosphereRendering.sunIntensity);
            material.SetFloat(Exposure, def.atmosphereRendering.exposure);
            material.SetFloat(ViewSamples, def.atmosphereRendering.viewSamples);
            material.SetFloat(LightSamples, def.atmosphereRendering.lightSamples);
            material.SetFloat(UseDepth, 1f);
        }
    }
}
