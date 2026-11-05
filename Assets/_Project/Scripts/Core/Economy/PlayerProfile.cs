using System;
using System.Collections.Generic;
using SpaceSim.Units;
using SpaceSim.Vehicles;

namespace SpaceSim.Economy
{
    public enum SampleCategory
    {
        Rock,
        Mineral,
        RareGeology,
        Soil,
        MineralSoil,
        UnusualCompound,
        Water,
        OtherLiquid,
        Acid
    }

    public enum SampleRarity
    {
        Common,
        Uncommon,
        Rare,
        Exceptional
    }

    /// <summary>A collected sample (created by the exploration systems, M6). SI mass.</summary>
    [Serializable]
    public class SampleRecord
    {
        public string id = "";
        public string name = "";
        public SampleCategory category;
        public string planetId = "";
        /// <summary>[kg]</summary>
        public double mass;
        public SampleRarity rarity;
        /// <summary>Research points the sample is worth when analysed.</summary>
        public double scientificValue;
        /// <summary>Base price in credits before analysis bonuses.</summary>
        public int basePrice;
        /// <summary>Analysed on site with a spectrometer (raises value).</summary>
        public bool analysed;
        public double analysisMultiplier = 1.0;
        /// <summary>Brought back to the start planet (only delivered samples can be sold; cargo is lost with the rocket).</summary>
        public bool delivered;
    }

    [Serializable]
    public class PlayerSettings
    {
        public DisplayUnitSystem units = DisplayUnitSystem.SI;
        /// <summary>Flight planning mode for M5: true = Assisted, false = Manual.</summary>
        public bool assistedPlanning = true;
    }

    /// <summary>Flight Academy progress (lessons opened, exercises solved). Additive: older saves simply lack it.</summary>
    [Serializable]
    public class AcademyProgress
    {
        public List<string> readTopics = new List<string>();
        public List<string> solvedExercises = new List<string>();

        public bool HasRead(string topicId) => readTopics.Contains(topicId);
        public bool HasSolved(string exerciseId) => solvedExercises.Contains(exerciseId);
    }

    /// <summary>
    /// Everything that persists between game sessions (no transient physics state). Versioned: see
    /// <see cref="ProfileMigration"/>. Plain serializable data — the Runtime layer writes it as JSON.
    /// </summary>
    [Serializable]
    public class PlayerProfile
    {
        public const int CurrentVersion = 1;
        public const int StartingCredits = 10000;

        public int version = CurrentVersion;
        public int credits = StartingCredits;
        public double researchPoints;
        /// <summary>Purchased module ids (free modules are always available).</summary>
        public List<string> ownedModules = new List<string>();
        public RocketBlueprint blueprint = RocketBlueprint.CreateDefault();
        public List<SampleRecord> samples = new List<SampleRecord>();
        /// <summary>Bodies the player has reached (discoveries).</summary>
        public List<string> visitedBodies = new List<string>();
        /// <summary>Bodies the player may select as destinations (navigation, M5).</summary>
        public List<string> unlockedBodies = new List<string>();
        public List<string> achievedMilestones = new List<string>();
        /// <summary>Sample sites already taken (sites are deterministic: the same rock cannot be collected twice).</summary>
        public List<string> collectedSites = new List<string>();
        public PlayerSettings settings = new PlayerSettings();
        public AcademyProgress academy = new AcademyProgress();
        public string lastSavedUtc = "";
        /// <summary>Total credits ever earned (statistics).</summary>
        public long lifetimeEarnings;

        public static PlayerProfile CreateNew(string homeBodyId)
        {
            var p = new PlayerProfile();
            if (!string.IsNullOrEmpty(homeBodyId))
            {
                p.visitedBodies.Add(homeBodyId);
                p.unlockedBodies.Add(homeBodyId);
            }
            return p;
        }

        /// <summary>True if the module may be used: free modules always, shop modules once bought.</summary>
        public bool Owns(string moduleId)
        {
            ModuleDefinition m = ModuleCatalog.Find(moduleId);
            if (m == null) return false;
            return !m.requiresPurchase || ownedModules.Contains(moduleId);
        }

        public bool HasMilestone(string id) => achievedMilestones.Contains(id);

        public double SampleMass()
        {
            double m = 0.0;
            foreach (var s in samples) m += s.mass;
            return m;
        }

        /// <summary>Mass of samples still on board the rocket (collected, not yet delivered) [kg].</summary>
        public double CargoMass()
        {
            double m = 0.0;
            foreach (var s in samples)
                if (!s.delivered) m += s.mass;
            return m;
        }

        /// <summary>Puts a collected sample on board. False if the site was already taken.</summary>
        public bool AddCargo(SampleRecord sample)
        {
            if (sample == null || collectedSites.Contains(sample.id)) return false;
            sample.delivered = false;
            samples.Add(sample);
            collectedSites.Add(sample.id);
            return true;
        }

        /// <summary>The rocket is back on the start planet: everything on board becomes sellable. Returns how many.</summary>
        public int DeliverCargo()
        {
            int n = 0;
            foreach (var s in samples)
                if (!s.delivered)
                {
                    s.delivered = true;
                    n++;
                }
            return n;
        }

        /// <summary>The rocket was lost or replaced: undelivered samples are gone (their sites stay taken). Returns how many.</summary>
        public int LoseCargo() => samples.RemoveAll(s => !s.delivered);

        internal void Earn(int amount)
        {
            if (amount <= 0) return;
            credits += amount;
            lifetimeEarnings += amount;
        }
    }

    public enum MigrationResult
    {
        UpToDate,
        Migrated,
        /// <summary>Saved by a newer build: must not be overwritten by this one.</summary>
        TooNew
    }

    /// <summary>Upgrades old profiles step by step; never silently downgrades a newer save.</summary>
    public static class ProfileMigration
    {
        public static MigrationResult Migrate(PlayerProfile p)
        {
            if (p.version > PlayerProfile.CurrentVersion) return MigrationResult.TooNew;
            bool changed = false;
            if (p.version < 1)
            {
                // v0 (pre-release) → v1: fill collections that older files may lack.
                p.version = 1;
                changed = true;
            }
            if (p.ownedModules == null) { p.ownedModules = new List<string>(); changed = true; }
            if (p.samples == null) { p.samples = new List<SampleRecord>(); changed = true; }
            if (p.visitedBodies == null) { p.visitedBodies = new List<string>(); changed = true; }
            if (p.unlockedBodies == null) { p.unlockedBodies = new List<string>(); changed = true; }
            if (p.achievedMilestones == null) { p.achievedMilestones = new List<string>(); changed = true; }
            if (p.collectedSites == null) { p.collectedSites = new List<string>(); changed = true; }
            if (p.settings == null) { p.settings = new PlayerSettings(); changed = true; }
            if (p.academy == null) { p.academy = new AcademyProgress(); changed = true; }
            if (p.academy.readTopics == null) { p.academy.readTopics = new List<string>(); changed = true; }
            if (p.academy.solvedExercises == null) { p.academy.solvedExercises = new List<string>(); changed = true; }
            if (p.blueprint == null || p.blueprint.stages == null || p.blueprint.stages.Count == 0)
            {
                p.blueprint = RocketBlueprint.CreateDefault();
                changed = true;
            }
            // Drop module ids this build no longer knows (keeps the design loadable).
            if (p.ownedModules.RemoveAll(id => ModuleCatalog.Find(id) == null) > 0) changed = true;
            return changed ? MigrationResult.Migrated : MigrationResult.UpToDate;
        }
    }
}
