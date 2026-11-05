using System;
using System.Collections.Generic;

namespace SpaceSim.Vehicles
{
    /// <summary>A fuel tank placed in a stage and how much propellant is loaded into it.</summary>
    [Serializable]
    public class TankLoad
    {
        public string moduleId = "";
        /// <summary>Loaded propellant [kg]. May be set above capacity — the compiler then loads only what fits and warns.</summary>
        public double propellantLoaded;

        public TankLoad Clone() => (TankLoad)MemberwiseClone();
    }

    /// <summary>One stage: N identical engines plus any number of tanks.</summary>
    [Serializable]
    public class StageBlueprint
    {
        public string name = "Ступень";
        /// <summary>Engine module id ("" = no engine).</summary>
        public string engineId = "";
        public int engineCount = 1;
        public List<TankLoad> tanks = new List<TankLoad>();

        public StageBlueprint Clone()
        {
            var c = (StageBlueprint)MemberwiseClone();
            c.tanks = new List<TankLoad>();
            foreach (var t in tanks) c.tanks.Add(t.Clone());
            return c;
        }
    }

    /// <summary>
    /// The player's rocket design: stages from the bottom (stage 0 burns first) and a payload section on top
    /// (the probe core plus navigation, protection, science and sampling modules). Serializable for saves (M3).
    /// </summary>
    [Serializable]
    public class RocketBlueprint
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string name = "Ракета";
        public List<StageBlueprint> stages = new List<StageBlueprint>();
        public List<string> payloadModules = new List<string>();

        public RocketBlueprint Clone()
        {
            var c = (RocketBlueprint)MemberwiseClone();
            c.stages = new List<StageBlueprint>();
            foreach (var s in stages) c.stages.Add(s.Clone());
            c.payloadModules = new List<string>(payloadModules);
            return c;
        }

        /// <summary>
        /// The starting rocket — compiles to exactly the Milestone 1 default configuration:
        /// stage 1 = 4 × Сокол-1 + 3 × K-100 (22 t dry, 300 t propellant); stage 2 = Вега-В + K-40 (4 t, 40 t).
        /// </summary>
        public static RocketBlueprint CreateDefault()
        {
            var b = new RocketBlueprint { name = "Стартовая ракета" };
            var s1 = new StageBlueprint { name = "Ступень 1", engineId = ModuleCatalog.EngineSokol, engineCount = 4 };
            for (int i = 0; i < 3; i++) s1.tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankK100, propellantLoaded = 100000 });
            var s2 = new StageBlueprint { name = "Ступень 2", engineId = ModuleCatalog.EngineVega, engineCount = 1 };
            s2.tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankK40, propellantLoaded = 40000 });
            b.stages.Add(s1);
            b.stages.Add(s2);
            b.payloadModules.Add(ModuleCatalog.ProbeCore);
            return b;
        }
    }
}
