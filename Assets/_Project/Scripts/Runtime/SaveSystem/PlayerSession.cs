using System;
using System.IO;
using SpaceSim.Economy;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Owns the player's persistent <see cref="PlayerProfile"/>: loads it at start, applies transactions through the
    /// Core rules (<see cref="Shop"/>, <see cref="Market"/>, <see cref="ResearchMilestones"/>) and saves after every change.
    /// Saving is atomic (temp file + replace); a corrupt file is kept as a backup; a profile written by a newer build is
    /// never overwritten (read-only session).
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public sealed class PlayerSession : MonoBehaviour
    {
        /// <summary>Tests point this at a temporary folder so they never touch the player's real save.</summary>
        public static string SaveDirectoryOverride;

        public static string SaveDirectory => SaveDirectoryOverride ?? Path.Combine(Application.persistentDataPath, "Player");
        public static string SavePath => Path.Combine(SaveDirectory, "profile.json");

        public PlayerProfile Profile { get; private set; }
        /// <summary>The save on disk is from a newer game version: play continues, nothing is written.</summary>
        public bool ReadOnly { get; private set; }
        public string LoadMessage { get; private set; } = "";

        /// <summary>Raised after any change to the profile (purchase, sale, milestone, design, settings).</summary>
        public event Action Changed;

        public void Initialize(string homeBodyId)
        {
            Profile = Load(homeBodyId, out string message, out bool readOnly);
            LoadMessage = message;
            ReadOnly = readOnly;
            if (!readOnly) Save();
        }

        static PlayerProfile Load(string homeBodyId, out string message, out bool readOnly)
        {
            readOnly = false;
            string path = SavePath;
            if (!File.Exists(path))
            {
                message = "Новый профиль: " + PlayerProfile.StartingCredits + " кр.";
                return PlayerProfile.CreateNew(homeBodyId);
            }
            try
            {
                var p = JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(path));
                if (p == null) throw new InvalidDataException("empty profile");
                switch (ProfileMigration.Migrate(p))
                {
                    case MigrationResult.TooNew:
                        readOnly = true;
                        message = "Сохранение создано более новой версией игры (v" + p.version + ") — профиль только для чтения, изменения не сохраняются.";
                        Debug.LogWarning("[SpaceSim] " + message);
                        return p;
                    case MigrationResult.Migrated:
                        message = "Профиль обновлён до версии " + PlayerProfile.CurrentVersion + ".";
                        return p;
                    default:
                        message = "Профиль загружен: " + p.credits + " кр.";
                        return p;
                }
            }
            catch (Exception e)
            {
                string backup = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json";
                try { File.Move(path, backup); } catch { /* keep going with a fresh profile */ }
                message = "Сохранение повреждено (" + e.Message + ") — сохранено как " + Path.GetFileName(backup) + ", создан новый профиль.";
                Debug.LogWarning("[SpaceSim] " + message);
                return PlayerProfile.CreateNew(homeBodyId);
            }
        }

        public void Save()
        {
            if (ReadOnly || Profile == null) return;
            Directory.CreateDirectory(SaveDirectory);
            Profile.lastSavedUtc = DateTime.UtcNow.ToString("o");
            string path = SavePath, tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(Profile, true));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        void Commit()
        {
            Save();
            Changed?.Invoke();
        }

        public bool Owns(string moduleId) => Profile != null && Profile.Owns(moduleId);

        public PurchaseResult Purchase(string moduleId, out string message)
        {
            if (ReadOnly)
            {
                message = "Профиль только для чтения (сохранение от более новой версии игры) — покупки недоступны.";
                return PurchaseResult.ReadOnlyProfile;
            }
            PurchaseResult r = Shop.TryPurchase(Profile, moduleId, out message);
            if (r == PurchaseResult.Success) Commit();
            return r;
        }

        public bool SellSample(string sampleId, out int credits)
        {
            credits = 0;
            if (ReadOnly) return false;
            bool ok = Market.Sell(Profile, sampleId, out credits);
            if (ok) Commit();
            return ok;
        }

        /// <summary>Grants a research milestone once; returns it if newly achieved.</summary>
        public MilestoneDefinition Award(string milestoneId)
        {
            MilestoneDefinition m = ResearchMilestones.Award(Profile, milestoneId);
            if (m != null) Commit();
            return m;
        }

        public void SetBlueprint(RocketBlueprint design)
        {
            if (design == null) return;
            Profile.blueprint = design.Clone();
            Commit();
        }

        public void MarkVisited(string bodyId)
        {
            if (string.IsNullOrEmpty(bodyId) || Profile.visitedBodies.Contains(bodyId)) return;
            Profile.visitedBodies.Add(bodyId);
            if (!Profile.unlockedBodies.Contains(bodyId)) Profile.unlockedBodies.Add(bodyId);
            Commit();
        }

        /// <summary>Puts a collected sample on board (saved at once). False if that site was already taken or the profile is read-only.</summary>
        public bool AddCargo(SampleRecord sample)
        {
            if (ReadOnly || !Profile.AddCargo(sample)) return false;
            Commit();
            return true;
        }

        /// <summary>The rocket is back home: on-board samples become sellable. Returns how many.</summary>
        public int DeliverCargo()
        {
            int n = Profile.DeliverCargo();
            if (n > 0) Commit();
            return n;
        }

        /// <summary>The rocket is lost or replaced: on-board samples are gone. Returns how many.</summary>
        public int LoseCargo()
        {
            int n = Profile.LoseCargo();
            if (n > 0) Commit();
            return n;
        }

        /// <summary>Records that a Flight Academy lesson was opened.</summary>
        public void MarkTopicRead(string topicId)
        {
            if (string.IsNullOrEmpty(topicId) || Profile.academy.HasRead(topicId)) return;
            Profile.academy.readTopics.Add(topicId);
            Commit();
        }

        /// <summary>Records a solved Flight Academy exercise.</summary>
        public void MarkExerciseSolved(string exerciseId)
        {
            if (string.IsNullOrEmpty(exerciseId) || Profile.academy.HasSolved(exerciseId)) return;
            Profile.academy.solvedExercises.Add(exerciseId);
            Commit();
        }

        /// <summary>Flight planning mode: assisted (suggestions, hints) or manual (the player computes everything).</summary>
        public void SetAssistedPlanning(bool assisted)
        {
            if (Profile.settings.assistedPlanning == assisted) return;
            Profile.settings.assistedPlanning = assisted;
            Commit();
        }

        public void SetUnits(SpaceSim.Units.DisplayUnitSystem units)
        {
            if (Profile.settings.units == units) return;
            Profile.settings.units = units;
            Commit();
        }

        void OnApplicationQuit() => Save();
    }
}
