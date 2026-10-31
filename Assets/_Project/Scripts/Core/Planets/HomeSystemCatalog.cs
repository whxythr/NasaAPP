using System;
using System.Collections.Generic;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Procedural;
using SpaceSim.Units;

namespace SpaceSim.Planets
{
    /// <summary>Engine-independent description of one body for building a system.</summary>
    public sealed class CelestialBodyPreset
    {
        public string Id;
        public string DisplayName;
        /// <summary>Id of the body this one orbits; null = the star.</summary>
        public string ParentId;
        public string Description;
        public PlanetPhysicalProperties Physical;
        public PlanetGenerationSettings Generation;
        public AtmosphereOpticalProperties Optics;
        public OrbitProperties Orbit;
        public double LaunchLatitude;
        public double LaunchLongitude;
    }

    /// <summary>
    /// The game's home system (data supplied by the designer): Teonan, Quoris, Hermeous Mora and its irregular moon.
    ///
    /// Notes on interpretation:
    /// • "Prot" is used as the sidereal rotation period.
    /// • Orbits are circular and coplanar (no e, i given).
    /// • The star's mass is not given; it is derived from Kepler's third law, M = 4π²a³/(G·T²), using the
    ///   given a and T of Hermeous Mora (≈ 5.19·10³⁰ kg ≈ 2.6 M☉). All three planets agree within 1 %.
    /// </summary>
    public static class HomeSystemCatalog
    {
        const double Day = 86400.0;
        const double AU = PhysicalConstants.AstronomicalUnit;
        /// <summary>Local solar time at the launch pads when the game starts [h].</summary>
        const double StartHour = 9.5;

        public const string TeonanId = "teonan";
        public const string QuorisId = "quoris";
        public const string HermeousMoraId = "hermeous-mora";
        public const string MoraMoonId = "hermeous-mora-i";
        /// <summary>The habitable fourth planet where the game starts.</summary>
        public const string HomeWorldId = "home";

        public static double StarMassFromKepler(double semiMajorAxis, double period) =>
            4.0 * Math.PI * Math.PI * Math.Pow(semiMajorAxis, 3) / (PhysicalConstants.GravitationalConstant * period * period);

        public static StarProperties Star() => new StarProperties
        {
            name = "Звезда",
            mass = StarMassFromKepler(0.427 * AU, 63.1 * Day),
            radius = 1.2e9,
            surfaceTemperature = 4900.0,
            color = new ColorRGB(1.0f, 0.88f, 0.70f)
        };

        public static List<CelestialBodyPreset> Bodies()
        {
            var list = new List<CelestialBodyPreset> { Teonan(), Quoris(), HermeousMora() };
            list.Add(MoraMoon(list[2].Physical.GravitationalParameter));
            list.Add(HomeWorld());
            return list;
        }

        /// <summary>
        /// Fourth planet — the start. An Earth copy with lower mass and gravity; violet water (K-type star light),
        /// equatorial deserts, coastal mountains near the equator, poles ≈ −49 °C.
        /// Interpretation: axial tilt 22.5°; the second "11.5°" is taken as the magnetic-pole tilt (informational,
        /// together with B = 0.254 G and the magnetopause at 13 627 603 m). Oblateness 0.286 % is stored but not
        /// yet used by gravity (no J2) nor by the shape.
        /// </summary>
        static CelestialBodyPreset HomeWorld()
        {
            const double orbitAngle = 40.0, padLat = 28.0, padLon = -52.0;
            var a = new AtmosphereProperties
            {
                hasAtmosphere = true,
                surfacePressure = 101325.0,        // Earth-like (not given)
                surfaceTemperature = 295.45,       // 22.3 °C
                meanMolarMass = 0.0289644,
                coRotationFactor = 1.0
            };
            var p = new PlanetPhysicalProperties
            {
                meanRadius = 6370332.0,
                mass = 5.03e24,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = 0.844 * Day,
                axialTiltDegrees = 22.5,
                rotationPhaseAtEpochDegrees = PhaseForLocalTime(orbitAngle, padLon, StartHour),
                spaceBoundaryAltitude = 100000.0,
                atmosphere = a
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            a.scaleHeight = AtmosphericModel.IsothermalScaleHeight(a.surfaceTemperature, a.meanMolarMass, p.SurfaceGravity);
            a.surfaceDensity = AtmosphericModel.IdealGasDensity(a.surfacePressure, a.meanMolarMass, a.surfaceTemperature);
            var g = new PlanetGenerationSettings
            {
                seed = 40417,
                surfaceStyle = SurfaceStyle.Terrestrial,
                hasOcean = true,
                continentBias = -0.03,
                equatorialAridity = 0.45,
                coastalMountainBoost = 0.9,
                elevationCooling = 0.00008,
                deepOcean = new ColorRGB(0.07f, 0.02f, 0.16f),      // violet seas
                shallowOcean = new ColorRGB(0.24f, 0.10f, 0.36f),
                seaIce = new ColorRGB(0.82f, 0.82f, 0.90f),
                grassland = new ColorRGB(0.26f, 0.42f, 0.14f),
                forest = new ColorRGB(0.10f, 0.26f, 0.08f),
                desert = new ColorRGB(0.80f, 0.64f, 0.40f),
                craterCount = 6
            };
            return new CelestialBodyPreset
            {
                Id = HomeWorldId, DisplayName = "Четвёртая планета (старт)",
                Description = "Обитаемая стартовая планета: копия Земли с меньшей массой (0.842 M⊕) и g = 8.27 м/с². Фиолетовая вода (свет звезды K-класса), экваториальные пустыни, горы у экваториальных побережий, на полюсах до −49 °C. Сплюснутость 0.286 %, наклон оси 22.5°, магнитный полюс 11.5°, B = 0.254 Гс, магнитопауза 13 627 603 м. Сутки 0.844 земных, год 124 дня.",
                Physical = p, Generation = g, Optics = new AtmosphereOpticalProperties(),
                Orbit = new OrbitProperties { semiMajorAxis = 0.67 * AU, period = 124.0 * Day, meanAnomalyAtEpochDegrees = orbitAngle },
                LaunchLatitude = padLat, LaunchLongitude = padLon
            };
        }

        /// <summary>
        /// Rotation phase at t = 0 that puts a pad at <paramref name="padLongitude"/> at local solar time
        /// <paramref name="hour"/> (circular coplanar orbit, small tilt). The Sun is at orbit angle + 180°.
        /// </summary>
        static double PhaseForLocalTime(double orbitAngleDeg, double padLongitude, double hour) =>
            (orbitAngleDeg + 180.0) - (12.0 - hour) * 15.0 - padLongitude;

        static CelestialBodyPreset Teonan()
        {
            const double orbitAngle = 0.0, padLat = 12.0, padLon = 25.0;
            var p = new PlanetPhysicalProperties
            {
                meanRadius = 1896325.0,
                mass = 1.24e23,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = 54.1 * Day,
                axialTiltDegrees = 0.6,
                rotationPhaseAtEpochDegrees = PhaseForLocalTime(orbitAngle, padLon, StartHour),
                spaceBoundaryAltitude = 50000.0,
                atmosphere = new AtmosphereProperties { hasAtmosphere = false, surfaceDensity = 0.0, surfacePressure = 0.0, surfaceTemperature = 451.15 }
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            var g = new PlanetGenerationSettings
            {
                seed = 51031,
                surfaceStyle = SurfaceStyle.Rocky,
                hasOcean = false,
                continentFrequency = 1.4, continentBias = 0.05, continentAmplitude = 2200, oceanDepthAmplitude = 4500,
                warpStrength = 0.15,
                mountainFrequency = 3.0, mountainAmplitude = 2600, mountainOctaves = 7,
                detailFrequency = 120, detailAmplitude = 120, detailOctaves = 8,
                craterCount = 320, craterMinRadius = 1500, craterMaxRadius = 70000, craterDepthRatio = 0.09,
                lowland = new ColorRGB(0.30f, 0.28f, 0.26f),
                highland = new ColorRGB(0.50f, 0.47f, 0.43f),
                stain = new ColorRGB(0.38f, 0.33f, 0.29f), stainCoverage = 0.25, stainFrequency = 3.0,
                craterFloorDarkening = 0.3, moistureFrequency = 3.0
            };
            return new CelestialBodyPreset
            {
                Id = TeonanId, DisplayName = "Теонан (Teonan)",
                Description = "Ближайшая к звезде. Без атмосферы, силикатная серо-коричневая кора, железное ядро (ρ = 4350 кг/м³), кратеры и небольшие «моря», тектонически активна. Сутки = 54.1 земных.",
                Physical = p, Generation = g, Optics = new AtmosphereOpticalProperties(),
                Orbit = new OrbitProperties { semiMajorAxis = 0.0883 * AU, period = 5.94 * Day, meanAnomalyAtEpochDegrees = orbitAngle },
                LaunchLatitude = padLat, LaunchLongitude = padLon
            };
        }

        static CelestialBodyPreset Quoris()
        {
            const double orbitAngle = 130.0, padLat = 4.0;
            // Tidally locked: rotation period = orbital period, prime meridian always faces the star.
            // A pad 37.5° west of the sub-stellar point sees permanent 9:30 morning.
            double padLon = -(12.0 - StartHour) * 15.0;
            var a = new AtmosphereProperties
            {
                hasAtmosphere = true,
                surfacePressure = 100.0,           // very thin O₂ layer
                surfaceTemperature = 452.15,       // mean of −3 °C … 361 °C
                meanMolarMass = 0.032,
                coRotationFactor = 1.0
            };
            var p = new PlanetPhysicalProperties
            {
                meanRadius = 1175730.0,
                mass = 2.82e22,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = 10.4 * Day,
                axialTiltDegrees = 0.0,
                rotationPhaseAtEpochDegrees = orbitAngle + 180.0,
                spaceBoundaryAltitude = 100000.0,
                atmosphere = a
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            a.scaleHeight = AtmosphericModel.IsothermalScaleHeight(a.surfaceTemperature, a.meanMolarMass, p.SurfaceGravity);
            a.surfaceDensity = AtmosphericModel.IdealGasDensity(a.surfacePressure, a.meanMolarMass, a.surfaceTemperature);
            double ratio = a.surfaceDensity / 1.225;
            var optics = new AtmosphereOpticalProperties
            {
                rayleighScattering = new Vector3d(5.802e-6, 13.558e-6, 33.1e-6) * ratio,
                mieScattering = 0.0,
                autoShellHeight = false,
                shellHeight = 4.0 * a.scaleHeight
            };
            var g = new PlanetGenerationSettings
            {
                seed = 72244,
                surfaceStyle = SurfaceStyle.Rocky,
                hasOcean = false,
                continentFrequency = 1.2, continentAmplitude = 2000, oceanDepthAmplitude = 3000, warpStrength = 0.2,
                mountainFrequency = 4.5, mountainAmplitude = 3500,
                detailFrequency = 120, detailAmplitude = 100,
                craterCount = 140, craterMinRadius = 4000, craterMaxRadius = 110000, craterDepthRatio = 0.14,
                lowland = new ColorRGB(0.56f, 0.50f, 0.48f),
                highland = new ColorRGB(0.66f, 0.60f, 0.56f),
                stain = new ColorRGB(0.55f, 0.16f, 0.09f), stainCoverage = 0.32, stainFrequency = 5.0,
                craterFloorDarkening = 0.2,
                // "The heart": two deep overlapping craters with a smaller one below, near the morning terminator side.
                featureCraters = new[]
                {
                    new FeatureCrater { latitudeDegrees = -16.0, longitudeDegrees = -27.0, radius = 65000, depthRatio = 0.35 },
                    new FeatureCrater { latitudeDegrees = -16.0, longitudeDegrees = -21.0, radius = 65000, depthRatio = 0.35 },
                    new FeatureCrater { latitudeDegrees = -21.0, longitudeDegrees = -24.0, radius = 42000, depthRatio = 0.35 }
                }
            };
            return new CelestialBodyPreset
            {
                Id = QuorisId, DisplayName = "Кварис (Quoris)",
                Description = "Самая малая планета. Приливный захват: всегда повёрнута одной стороной к звезде (−3 °C … 361 °C). Очень тонкий кислородный слой, ржавые красные пятна, массивные глубокие кратеры и «сердечко».",
                Physical = p, Generation = g, Optics = optics,
                Orbit = new OrbitProperties { semiMajorAxis = 0.128 * AU, period = 10.4 * Day, meanAnomalyAtEpochDegrees = orbitAngle, tidallyLocked = true },
                LaunchLatitude = padLat, LaunchLongitude = padLon
            };
        }

        static CelestialBodyPreset HermeousMora()
        {
            const double orbitAngle = 250.0, padLat = 18.0, padLon = -40.0;
            var a = new AtmosphereProperties
            {
                hasAtmosphere = true,
                surfacePressure = 7.5e6,           // lethal, Venus-like
                surfaceTemperature = 573.15,       // 300 °C
                meanMolarMass = 0.052,             // SO₂ + CO₂ with some O₂ and Ar
                coRotationFactor = 1.0
            };
            var p = new PlanetPhysicalProperties
            {
                meanRadius = 8209166.0,
                mass = 1.46e25,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = 4.65 * Day,
                axialTiltDegrees = 2.6,
                rotationPhaseAtEpochDegrees = PhaseForLocalTime(orbitAngle, padLon, StartHour),
                spaceBoundaryAltitude = 120000.0,
                atmosphere = a
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            a.scaleHeight = AtmosphericModel.IsothermalScaleHeight(a.surfaceTemperature, a.meanMolarMass, p.SurfaceGravity);
            a.surfaceDensity = AtmosphericModel.IdealGasDensity(a.surfacePressure, a.meanMolarMass, a.surfaceTemperature);
            var optics = new AtmosphereOpticalProperties
            {
                // Sulphur aerosols scatter red/yellow more than blue → cream-yellow disk, and the light that is left
                // after a long path is bluish → whitish-blue sunsets. Blue organic haze high above (blue limb).
                rayleighScattering = new Vector3d(1.8e-4, 1.5e-4, 0.9e-4),
                mieScattering = 3.0e-5, mieExtinctionRatio = 1.3, mieScaleHeight = 3500.0, mieAnisotropy = 0.7,
                hazeScattering = new Vector3d(0.6e-6, 1.6e-6, 4.5e-6), hazeAltitude = 75000.0, hazeThickness = 7000.0
            };
            var g = new PlanetGenerationSettings
            {
                seed = 33157,
                surfaceStyle = SurfaceStyle.Rocky,
                hasOcean = false,
                continentFrequency = 0.9, continentAmplitude = 3500, oceanDepthAmplitude = 4000,
                warpFrequency = 1.2, warpStrength = 0.38,
                mountainFrequency = 3.5, mountainAmplitude = 3800,
                detailFrequency = 160, detailAmplitude = 150,
                craterCount = 12, craterMinRadius = 5000, craterMaxRadius = 60000, craterDepthRatio = 0.05,
                lowland = new ColorRGB(0.66f, 0.45f, 0.22f),
                highland = new ColorRGB(0.86f, 0.77f, 0.58f),
                stain = new ColorRGB(0.40f, 0.22f, 0.12f), stainCoverage = 0.12, stainFrequency = 9.0,
                craterFloorDarkening = 0.15, moistureFrequency = 2.0
            };
            return new CelestialBodyPreset
            {
                Id = HermeousMoraId, DisplayName = "Гермеус Мора (Hermeous Mora)",
                Description = "300 °C, смертельное давление, сернокислотные осадки, атмосфера из SO₂ и CO₂ с примесью O₂ и Ar. Голубая органическая дымка над атмосферой, бело-голубые закаты. g ≈ 14.5 м/с². Сутки = 4.65 земных. Один несферический спутник на 30 000 км.",
                Physical = p, Generation = g, Optics = optics,
                Orbit = new OrbitProperties { semiMajorAxis = 0.427 * AU, period = 63.1 * Day, meanAnomalyAtEpochDegrees = orbitAngle },
                LaunchLatitude = padLat, LaunchLongitude = padLon
            };
        }

        static CelestialBodyPreset MoraMoon(double parentMu)
        {
            const double orbitAngle = 40.0, radius = 120000.0, density = 2100.0;
            var orbit = new OrbitProperties { semiMajorAxis = 3.0e7, meanAnomalyAtEpochDegrees = orbitAngle, tidallyLocked = true };
            double period = OrbitalMechanics.CircularPeriod(parentMu, orbit.semiMajorAxis);
            var p = new PlanetPhysicalProperties
            {
                meanRadius = radius,
                mass = density * 4.0 / 3.0 * Math.PI * radius * radius * radius,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = period,
                rotationPhaseAtEpochDegrees = orbitAngle + 180.0,
                axialTiltDegrees = 0.0,
                spaceBoundaryAltitude = 5000.0,
                atmosphere = new AtmosphereProperties { hasAtmosphere = false, surfaceDensity = 0.0, surfacePressure = 0.0 }
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            // Non-spherical: relief amplitudes of ~15 % of the radius at low frequency make a potato-shaped body.
            var g = new PlanetGenerationSettings
            {
                seed = 90417,
                surfaceStyle = SurfaceStyle.Rocky,
                hasOcean = false,
                continentFrequency = 0.7, continentOctaves = 4, continentAmplitude = 24000, oceanDepthAmplitude = 24000,
                warpStrength = 0.35,
                mountainFrequency = 3.0, mountainAmplitude = 2500,
                detailFrequency = 40, detailAmplitude = 200,
                craterCount = 70, craterMinRadius = 1500, craterMaxRadius = 30000, craterDepthRatio = 0.16,
                lowland = new ColorRGB(0.55f, 0.48f, 0.32f),
                highland = new ColorRGB(0.70f, 0.63f, 0.45f),
                craterFloorDarkening = 0.3
            };
            return new CelestialBodyPreset
            {
                Id = MoraMoonId, DisplayName = "Мора I (спутник)", ParentId = HermeousMoraId,
                Description = "Несферический спутник Гермеус Моры на расстоянии 30 000 км, приливно захвачен. Радиус ≈ 120 км (параметры не заданы — оценка).",
                Physical = p, Generation = g, Optics = new AtmosphereOpticalProperties(),
                Orbit = orbit,
                LaunchLatitude = 0.0, LaunchLongitude = 90.0
            };
        }
    }
}
