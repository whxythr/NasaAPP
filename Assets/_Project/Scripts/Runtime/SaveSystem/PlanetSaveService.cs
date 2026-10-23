using System;
using System.IO;
using SpaceSim.Persistence;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Writes/reads <see cref="PlanetSaveData"/> as JSON under Application.persistentDataPath/Planets.
    /// Loading validates algorithm version and surface fingerprint before applying anything, so a build with a
    /// different generator refuses the save instead of silently producing another planet.
    /// </summary>
    public static class PlanetSaveService
    {
        public static string Directory => Path.Combine(Application.persistentDataPath, "Planets");

        public static string PathFor(string planetId)
        {
            string safe = string.IsNullOrEmpty(planetId) ? "planet" : planetId;
            foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            return Path.Combine(Directory, safe + ".json");
        }

        public static PlanetSaveData Capture(PlanetRuntime planet)
        {
            PlanetDefinition d = planet.Definition;
            return PlanetSaveValidator.Capture(d.planetId, d.displayName, d.generation, d.physical, d.optics,
                d.physicalGeneratorVersion, d.physicalSeed, DateTime.UtcNow.ToString("o"));
        }

        public static string ToJson(PlanetSaveData data) => JsonUtility.ToJson(data, true);

        public static PlanetSaveData FromJson(string json) => JsonUtility.FromJson<PlanetSaveData>(json);

        public static string Save(PlanetRuntime planet)
        {
            var data = Capture(planet);
            System.IO.Directory.CreateDirectory(Directory);
            string path = PathFor(data.planetId);
            File.WriteAllText(path, ToJson(data));
            return path;
        }

        /// <summary>Loads the save for this planet's id and applies it if (and only if) it validates.</summary>
        public static bool TryLoadInto(PlanetRuntime planet, out string message)
        {
            string path = PathFor(planet.Definition.planetId);
            if (!File.Exists(path))
            {
                message = "No save found at " + path;
                return false;
            }
            PlanetSaveData data;
            try
            {
                data = FromJson(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                message = "Save file is unreadable: " + e.Message;
                return false;
            }
            return TryApply(planet, data, out message);
        }

        public static bool TryApply(PlanetRuntime planet, PlanetSaveData data, out string message)
        {
            var validation = PlanetSaveValidator.Validate(data);
            if (!validation.IsValid)
            {
                message = "Save rejected (" + validation.Status + "): " + validation.Message;
                return false;
            }
            PlanetDefinition d = planet.Definition;
            d.planetId = data.planetId;
            d.displayName = data.displayName;
            d.generation = data.generation.Clone();
            d.physical = data.physical.Clone();
            d.optics = data.optics != null ? data.optics.Clone() : d.optics;
            d.physicalGeneratorVersion = data.physicalGeneratorVersion;
            d.physicalSeed = data.physicalSeed;
            planet.ApplyDefinitionData();
            message = "Planet '" + data.displayName + "' restored (fingerprint " + validation.ActualFingerprint + ").";
            return true;
        }
    }
}
