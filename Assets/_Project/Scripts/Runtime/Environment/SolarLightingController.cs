using System;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Units;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The Sun: direction (fixed in the inertial frame for Phase 1), the directional light, the visible disk,
    /// and global lighting parameters. The light that reaches objects near the camera is attenuated by the
    /// atmosphere along the actual sun ray (CPU integration of the same optical model), and is zero inside the
    /// planet's geometric shadow. The terrain shader applies its own per-pixel transmittance instead.
    /// </summary>
    [DefaultExecutionOrder(90)]
    public sealed class SolarLightingController : MonoBehaviour
    {
        static readonly int GSunDir = Shader.PropertyToID("_SpaceSimSunDir");
        static readonly int GSunColor = Shader.PropertyToID("_SpaceSimSunColor");
        static readonly int GPlanetCenter = Shader.PropertyToID("_SpaceSimPlanetCenter");
        static readonly int GPlanetRadius = Shader.PropertyToID("_SpaceSimPlanetRadius");
        static readonly int GSkyAmbient = Shader.PropertyToID("_SpaceSimSkyAmbient");

        [Tooltip("Direction of the Sun in the ecliptic (inertial) plane [deg]. 0 = +X.")]
        [SerializeField] double eclipticLongitudeDegrees;
        [Tooltip("Distance to the Sun [m] (sets the disk's angular size).")]
        [SerializeField] double sunDistance = PhysicalConstants.AstronomicalUnit;
        [SerializeField] double sunRadius = 6.957e8;
        [Tooltip("Directional light intensity outside the atmosphere.")]
        [SerializeField] float lightIntensity = 1.4f;
        [SerializeField] Color sunColor = new Color(1f, 0.97f, 0.93f);
        [Tooltip("Sky light colour on the ground at full daylight (visual approximation).")]
        [SerializeField] Color skyAmbient = new Color(0.10f, 0.15f, 0.24f);
        [Tooltip("Ambient on the night side / in space (starlight, scattered light).")]
        [SerializeField] Color ambientFloor = new Color(0.005f, 0.006f, 0.008f);

        PlanetRuntime planet;
        FloatingOrigin origin;
        Light sunLight;
        Material diskMaterial;
        Mesh diskMesh;

        public Light Light => sunLight;
        public double EclipticLongitudeDegrees
        {
            get => eclipticLongitudeDegrees;
            set => eclipticLongitudeDegrees = value;
        }

        /// <summary>Unit vector towards the Sun in the simulation (inertial) frame.</summary>
        public Vector3d SunDirection
        {
            get
            {
                double l = eclipticLongitudeDegrees * MathD.DegToRad;
                return new Vector3d(Math.Cos(l), Math.Sin(l), 0.0);
            }
        }

        public Vector3 SunDirectionUnity => CoordinateSystem.ToUnityVector(SunDirection);
        /// <summary>Spectral transmittance of sunlight at the camera (0 in the planet's shadow).</summary>
        public Vector3d TransmittanceAtViewer { get; private set; } = new Vector3d(1, 1, 1);
        /// <summary>Sine of the Sun's elevation above the local horizontal plane at the camera.</summary>
        public double SunElevationSineAtViewer { get; private set; }

        public void Initialize(PlanetRuntime planetRuntime, FloatingOrigin floatingOrigin, Shader sunDiskShader)
        {
            planet = planetRuntime;
            origin = floatingOrigin;

            sunLight = gameObject.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.shadows = LightShadows.Soft;
            sunLight.shadowStrength = 1f;
            sunLight.shadowBias = 0.05f;
            sunLight.shadowNormalBias = 0.4f;
            RenderSettings.sun = sunLight;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;

            if (sunDiskShader != null)
            {
                var disk = new GameObject("SunDisk");
                disk.transform.SetParent(transform.parent, false);
                diskMesh = new Mesh
                {
                    name = "SunDiskQuad",
                    vertices = new[] { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero },
                    uv = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) },
                    triangles = new[] { 0, 2, 1, 0, 3, 2 },
                    bounds = new Bounds(Vector3.zero, Vector3.one * 2e10f)
                };
                disk.AddComponent<MeshFilter>().sharedMesh = diskMesh;
                diskMaterial = new Material(sunDiskShader) { name = "SunDisk (runtime)" };
                var mr = disk.AddComponent<MeshRenderer>();
                mr.sharedMaterial = diskMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        void OnDestroy()
        {
            if (diskMaterial != null) Destroy(diskMaterial);
            if (diskMesh != null) Destroy(diskMesh);
        }

        void LateUpdate()
        {
            if (planet == null || planet.Model == null) return;
            var model = planet.Model;
            Vector3d dir = SunDirection;
            Vector3 dirUnity = CoordinateSystem.ToUnityVector(dir);
            transform.rotation = Quaternion.LookRotation(-dirUnity);

            Vector3d viewerRel = origin.ViewerPosition - model.Position;
            SunElevationSineAtViewer = Vector3d.Dot(viewerRel.Normalized, dir);
            double shellRadius = model.Radius + planet.VisualShellHeight;
            TransmittanceAtViewer = AtmosphereOptics.Transmittance(model.Atmosphere, planet.Definition.optics,
                model.Radius, shellRadius, viewerRel, dir, 32);

            Vector3d t = TransmittanceAtViewer;
            float peak = (float)Math.Max(t.x, Math.Max(t.y, t.z));
            sunLight.intensity = lightIntensity * peak;
            sunLight.color = peak > 1e-4f ? new Color(sunColor.r * (float)t.x / peak, sunColor.g * (float)t.y / peak, sunColor.b * (float)t.z / peak) : sunColor;
            sunLight.enabled = peak > 1e-4f;

            Color sun = sunColor.linear * lightIntensity;
            Shader.SetGlobalVector(GSunDir, dirUnity);
            Shader.SetGlobalVector(GSunColor, new Vector4(sun.r, sun.g, sun.b, 0f));
            Shader.SetGlobalVector(GPlanetCenter, planet.UnityCenter(origin));
            Shader.SetGlobalFloat(GPlanetRadius, (float)model.Radius);
            Shader.SetGlobalVector(GSkyAmbient, skyAmbient.linear);

            // Flat ambient for standard-lit objects near the camera: sky light while inside sunlit air.
            double rho0 = model.Atmosphere.ReferenceDensity;
            double column = rho0 > 0.0 ? model.Atmosphere.Density(viewerRel.Magnitude - model.Radius) / rho0 : 0.0;
            float day = Mathf.Clamp01((float)SunElevationSineAtViewer * 2.5f + 0.25f) * (float)Math.Min(1.0, column);
            RenderSettings.ambientLight = ambientFloor + skyAmbient * day;

            if (diskMaterial != null)
            {
                diskMaterial.SetVector("_SunDir", dirUnity);
                diskMaterial.SetFloat("_AngularRadius", (float)Math.Asin(Math.Min(1.0, sunRadius / sunDistance)));
            }
        }
    }
}
