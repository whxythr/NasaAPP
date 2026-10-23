using System;
using System.Collections.Generic;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Procedural;

namespace SpaceSim.Persistence
{
    /// <summary>Kind of a persistent surface change (reserved for future digging/building/impacts).</summary>
    public enum SurfaceModificationType
    {
        Crater = 0,
        Flatten = 1,
        Raise = 2
    }

    /// <summary>A persistent local change applied on top of the deterministic surface. Not applied in Phase 1.</summary>
    [Serializable]
    public struct SurfaceModificationRecord
    {
        public SurfaceModificationType type;
        public double latitudeDegrees;
        public double longitudeDegrees;
        public double radius;
        public double heightDelta;
        public double simulationTime;
    }

    /// <summary>
    /// Everything needed to reproduce a planet exactly: identity, seeds, generator versions, generation settings,
    /// physical properties (stored, never re-derived), and a surface fingerprint for verification.
    /// </summary>
    [Serializable]
    public class PlanetSaveData
    {
        public const int CurrentFormatVersion = 1;

        public int saveFormatVersion = CurrentFormatVersion;
        public string planetId = "";
        public string displayName = "";
        public string createdUtc = "";

        /// <summary>Version of PlanetSurfaceSampler used to create the surface.</summary>
        public int surfaceAlgorithmVersion;
        /// <summary>Version of PlanetPhysicalGenerator, or 0 if the physical data was authored by hand.</summary>
        public int physicalGeneratorVersion;
        public int physicalSeed;

        public PlanetGenerationSettings generation = new PlanetGenerationSettings();
        public PlanetPhysicalProperties physical = new PlanetPhysicalProperties();
        public AtmosphereOpticalProperties optics = new AtmosphereOpticalProperties();

        /// <summary>PlanetFingerprint of the surface at save time.</summary>
        public string surfaceFingerprint = "";

        public List<SurfaceModificationRecord> surfaceModifications = new List<SurfaceModificationRecord>();
    }

    public enum PlanetSaveStatus
    {
        Valid,
        UnsupportedFormatVersion,
        UnsupportedAlgorithmVersion,
        InvalidPhysicalData,
        FingerprintMismatch,
        MissingData
    }

    public readonly struct PlanetSaveValidation
    {
        public PlanetSaveValidation(PlanetSaveStatus status, string message, string actualFingerprint)
        {
            Status = status;
            Message = message;
            ActualFingerprint = actualFingerprint;
        }

        public PlanetSaveStatus Status { get; }
        public string Message { get; }
        public string ActualFingerprint { get; }
        public bool IsValid => Status == PlanetSaveStatus.Valid;
    }

    public static class PlanetSaveValidator
    {
        /// <summary>
        /// Checks that this build can reproduce the saved planet bit-for-bit (within the fingerprint's
        /// centimetre quantisation). A failure must be reported to the player — never "fixed" by regenerating.
        /// </summary>
        public static PlanetSaveValidation Validate(PlanetSaveData data)
        {
            if (data == null || data.generation == null || data.physical == null)
                return new PlanetSaveValidation(PlanetSaveStatus.MissingData, "Save data is incomplete.", null);
            if (data.saveFormatVersion > PlanetSaveData.CurrentFormatVersion)
                return new PlanetSaveValidation(PlanetSaveStatus.UnsupportedFormatVersion,
                    "Save format " + data.saveFormatVersion + " is newer than this build supports.", null);
            if (!PlanetSurfaceSampler.IsAlgorithmVersionSupported(data.surfaceAlgorithmVersion) ||
                data.generation.algorithmVersion != data.surfaceAlgorithmVersion)
                return new PlanetSaveValidation(PlanetSaveStatus.UnsupportedAlgorithmVersion,
                    "Surface algorithm v" + data.surfaceAlgorithmVersion + " is not available in this build.", null);

            var issues = data.physical.Validate();
            if (issues.Count > 0)
                return new PlanetSaveValidation(PlanetSaveStatus.InvalidPhysicalData, string.Join(" ", issues), null);

            var sampler = PlanetSurfaceSampler.Create(data.generation, data.physical.meanRadius);
            string actual = PlanetFingerprint.Compute(sampler);
            if (!string.IsNullOrEmpty(data.surfaceFingerprint) && actual != data.surfaceFingerprint)
                return new PlanetSaveValidation(PlanetSaveStatus.FingerprintMismatch,
                    "Regenerated surface differs from the saved one (" + actual + " ≠ " + data.surfaceFingerprint + ").", actual);

            return new PlanetSaveValidation(PlanetSaveStatus.Valid, "OK", actual);
        }

        /// <summary>Builds save data for a planet and stamps the current fingerprint.</summary>
        public static PlanetSaveData Capture(string planetId, string displayName, PlanetGenerationSettings generation,
            PlanetPhysicalProperties physical, AtmosphereOpticalProperties optics, int physicalGeneratorVersion, int physicalSeed,
            string createdUtc)
        {
            var data = new PlanetSaveData
            {
                planetId = planetId ?? "",
                displayName = displayName ?? "",
                createdUtc = createdUtc ?? "",
                surfaceAlgorithmVersion = generation.algorithmVersion,
                physicalGeneratorVersion = physicalGeneratorVersion,
                physicalSeed = physicalSeed,
                generation = generation.Clone(),
                physical = physical.Clone(),
                optics = optics != null ? optics.Clone() : new AtmosphereOpticalProperties()
            };
            data.surfaceFingerprint = PlanetFingerprint.Compute(PlanetSurfaceSampler.Create(data.generation, data.physical.meanRadius));
            return data;
        }
    }
}
