using System;
using System.Collections.Generic;

namespace SpaceSim.Economy
{
    /// <summary>A research achievement with a reward.</summary>
    public sealed class MilestoneDefinition
    {
        public MilestoneDefinition(string id, string name, string condition, int creditReward, double researchReward)
        {
            Id = id;
            Name = name;
            Condition = condition;
            CreditReward = creditReward;
            ResearchReward = researchReward;
        }

        public string Id { get; }
        public string Name { get; }
        public string Condition { get; }
        public int CreditReward { get; }
        public double ResearchReward { get; }
    }

    /// <summary>
    /// Research progression: physical achievements detected from the actual simulation (Level B) — never from
    /// teleports or test tools. Rewards fund upgrades before sample trade (M6) exists.
    /// </summary>
    public static class ResearchMilestones
    {
        public const string ReachSpace = "reach-space";
        public const string FirstOrbit = "first-orbit";
        public const string EscapeHome = "escape-home";
        public const string Interplanetary = "interplanetary";
        public const string OtherworldLanding = "otherworld-landing";
        public const string FirstSample = "first-sample";

        static readonly List<MilestoneDefinition> all = new List<MilestoneDefinition>
        {
            new MilestoneDefinition(ReachSpace, "Первый выход в космос", "Пересечь условную границу космоса стартовой планеты на своей ракете.", 8000, 10),
            new MilestoneDefinition(FirstOrbit, "Первая орбита", "Выйти на устойчивую орбиту (ε < 0, перицентр выше плотной атмосферы).", 20000, 30),
            new MilestoneDefinition(EscapeHome, "Уход от планеты", "Набрать скорость убегания (ε ≥ 0) у стартовой планеты.", 30000, 50),
            new MilestoneDefinition(OtherworldLanding, "Посадка на другом теле", "Мягко или жёстко сесть на другую планету или спутник на своей ракете.", 50000, 150),
            new MilestoneDefinition(FirstSample, "Первый образец", "Взять первый образец грунта, породы или жидкости.", 5000, 20),
            new MilestoneDefinition(Interplanetary, "Первый межпланетный перелёт", "Долететь до сферы влияния другой планеты на своей ракете.", 40000, 100),
        };

        public static IReadOnlyList<MilestoneDefinition> All => all;

        public static MilestoneDefinition Find(string id) => all.Find(m => m.Id == id);

        /// <summary>Grants a milestone once. Returns the definition if it was newly achieved, otherwise null.</summary>
        public static MilestoneDefinition Award(PlayerProfile profile, string id)
        {
            MilestoneDefinition def = Find(id);
            if (def == null || profile.HasMilestone(id)) return null;
            profile.achievedMilestones.Add(id);
            profile.Earn(def.CreditReward);
            profile.researchPoints += def.ResearchReward;
            return def;
        }
    }

    /// <summary>Sample trading. Price grows with rarity and with on-site analysis; research points are awarded on sale.</summary>
    public static class Market
    {
        static readonly double[] RarityFactor = { 1.0, 2.5, 7.0, 20.0 };

        public static int SellPrice(SampleRecord s)
        {
            if (s == null) return 0;
            double factor = RarityFactor[Math.Min((int)s.rarity, RarityFactor.Length - 1)];
            double analysis = s.analysed ? Math.Max(1.0, s.analysisMultiplier) : 1.0;
            return (int)Math.Round(s.basePrice * factor * analysis);
        }

        /// <summary>Sells one delivered sample: credits and research points added, sample removed. Atomic. Samples still in the rocket cannot be sold.</summary>
        public static bool Sell(PlayerProfile profile, string sampleId, out int credits)
        {
            credits = 0;
            int index = profile.samples.FindIndex(x => x.id == sampleId);
            if (index < 0 || !profile.samples[index].delivered) return false;
            SampleRecord s = profile.samples[index];
            credits = SellPrice(s);
            profile.samples.RemoveAt(index);
            profile.Earn(credits);
            profile.researchPoints += s.scientificValue * (s.analysed ? Math.Max(1.0, s.analysisMultiplier) : 1.0);
            return true;
        }
    }
}
