using System;
using System.Collections.Generic;
using SpaceSim.Flight;

namespace SpaceSim.Vehicles
{
    public enum BlueprintIssueCode
    {
        FuelCapacityExceeded,
        IncompatiblePropellant,
        TooManyEngines,
        VacuumEngineOnFirstStage,
        UnknownModule,
        MissingCore,
        LockedModule,
        NoStages
    }

    /// <summary>A construction problem and its physical consequence (designs are never refused outright).</summary>
    public readonly struct BlueprintIssue
    {
        public BlueprintIssue(BlueprintIssueCode code, int stageIndex, string message)
        {
            Code = code;
            StageIndex = stageIndex;
            Message = message;
        }

        public BlueprintIssueCode Code { get; }
        /// <summary>Stage index, or −1 for the payload section.</summary>
        public int StageIndex { get; }
        public string Message { get; }
    }

    /// <summary>Aggregated non-propulsive abilities of a vehicle (navigation, protection, science, sampling).</summary>
    public sealed class VehicleCapabilities
    {
        public ModuleCapability Flags;
        public int RadiationShielding;
        public int AcidResistance;
        public double SampleCapacity;
        public SampleKinds SampleKinds;
        public double ResearchMultiplier = 1.0;

        public bool Has(ModuleCapability c) => (Flags & c) == c;
        public bool CanCollect(SampleKinds k) => (SampleKinds & k) == k;
    }

    /// <summary>Result of compiling a blueprint: the physics configuration plus capabilities and issues.</summary>
    public sealed class CompiledRocket
    {
        public RocketConfiguration Configuration;
        public VehicleCapabilities Capabilities = new VehicleCapabilities();
        public readonly List<BlueprintIssue> Issues = new List<BlueprintIssue>();
        /// <summary>Sum of the tank capacities of all stages [kg].</summary>
        public double MaxFuelCapacity;
        /// <summary>Propellant the engines can actually burn [kg].</summary>
        public double UsableFuel;
        /// <summary>Propellant loaded into tanks no engine of that stage can burn — carried as dead weight [kg].</summary>
        public double UnusableFuel;
        public double PayloadMass;
    }

    /// <summary>
    /// Turns a <see cref="RocketBlueprint"/> into the <see cref="RocketConfiguration"/> the flight physics uses.
    /// Mass, thrust and Isp come straight from module data; performance figures are then computed by
    /// <see cref="RocketDesignAnalysis"/> (the same Level A code the flight uses).
    /// </summary>
    public static class BlueprintCompiler
    {
        public const int MaxEnginesPerStage = 9;
        /// <summary>Isp_sea/Isp_vac below this marks a vacuum-optimised engine.</summary>
        const double VacuumEngineRatio = 0.75;

        /// <param name="isUnlocked">Ownership check for purchasable modules (M3 shop). Null = everything available.</param>
        public static CompiledRocket Compile(RocketBlueprint blueprint, Func<string, bool> isUnlocked = null)
        {
            if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));
            var result = new CompiledRocket();
            var config = new RocketConfiguration { stages = new RocketStage[blueprint.stages.Count] };

            if (blueprint.stages.Count == 0)
                result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.NoStages, -1, "Нет ни одной ступени: аппарат не сможет взлететь."));

            for (int i = 0; i < blueprint.stages.Count; i++)
                config.stages[i] = CompileStage(blueprint.stages[i], i, result, isUnlocked);

            // ---- payload section ----
            bool hasCore = false;
            double payload = 0.0, payloadLength = 0.0;
            var caps = result.Capabilities;
            foreach (string id in blueprint.payloadModules)
            {
                ModuleDefinition m = Resolve(id, -1, result, isUnlocked);
                if (m == null) continue;
                if (!m.IsPayload)
                {
                    result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.UnknownModule, -1, m.name + " нельзя ставить в отсек полезной нагрузки."));
                    continue;
                }
                hasCore |= m.mandatory;
                payload += m.dryMass;
                payloadLength += m.length;
                caps.Flags |= m.capabilities;
                caps.RadiationShielding = Math.Max(caps.RadiationShielding, m.radiationShielding);
                caps.AcidResistance = Math.Max(caps.AcidResistance, m.acidResistance);
                caps.SampleCapacity += m.sampleCapacity;
                caps.SampleKinds |= m.sampleKinds;
                caps.ResearchMultiplier = Math.Max(caps.ResearchMultiplier, m.researchMultiplier);
            }
            if (!hasCore)
            {
                ModuleDefinition core = ModuleCatalog.Find(ModuleCatalog.ProbeCore);
                payload += core.dryMass;
                result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.MissingCore, -1, "Зонд (ядро) обязателен и добавлен автоматически."));
            }
            config.payloadMass = payload;
            result.PayloadMass = payload;
            result.Configuration = config;
            return result;
        }

        static RocketStage CompileStage(StageBlueprint sb, int index, CompiledRocket result, Func<string, bool> isUnlocked)
        {
            var stage = new RocketStage { name = string.IsNullOrEmpty(sb.name) ? "Ступень " + (index + 1) : sb.name, dryMass = 0, propellantMass = 0,
                vacuumThrust = 0, vacuumIsp = 0, seaLevelIsp = 0, length = 0 };

            ModuleDefinition engine = string.IsNullOrEmpty(sb.engineId) ? null : Resolve(sb.engineId, index, result, isUnlocked);
            if (engine != null && !engine.IsEngine)
            {
                result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.UnknownModule, index, engine.name + " — не двигатель."));
                engine = null;
            }
            if (engine != null)
            {
                int count = Math.Max(1, sb.engineCount);
                if (count > MaxEnginesPerStage)
                {
                    result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.TooManyEngines, index,
                        stage.name + ": помещается не более " + MaxEnginesPerStage + " двигателей — установлено " + MaxEnginesPerStage + "."));
                    count = MaxEnginesPerStage;
                }
                stage.dryMass += engine.dryMass * count;
                stage.vacuumThrust = engine.vacuumThrust * count;
                stage.vacuumIsp = engine.vacuumIsp;
                stage.seaLevelIsp = engine.seaLevelIsp;
                stage.length += engine.length;
                if (index == 0 && engine.vacuumIsp > 0.0 && engine.seaLevelIsp / engine.vacuumIsp < VacuumEngineRatio)
                    result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.VacuumEngineOnFirstStage, index,
                        engine.name + " — вакуумный двигатель: у поверхности его Isp " + engine.seaLevelIsp.ToString("0") + " с вместо " +
                        engine.vacuumIsp.ToString("0") + " с, тяга на старте заметно ниже."));
            }

            foreach (TankLoad t in sb.tanks)
            {
                ModuleDefinition tank = Resolve(t.moduleId, index, result, isUnlocked);
                if (tank == null) continue;
                if (!tank.IsTank)
                {
                    result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.UnknownModule, index, tank.name + " — не топливный бак."));
                    continue;
                }
                double loaded = Math.Max(0.0, t.propellantLoaded);
                if (loaded > tank.propellantCapacity)
                {
                    result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.FuelCapacityExceeded, index,
                        tank.name + ": запрошено " + (loaded / 1000.0).ToString("0.0") + " т, вмещается " + (tank.propellantCapacity / 1000.0).ToString("0.0") +
                        " т — залито только то, что помещается."));
                    loaded = tank.propellantCapacity;
                }
                result.MaxFuelCapacity += tank.propellantCapacity;
                stage.dryMass += tank.dryMass;
                stage.length += tank.length;
                if (engine != null && tank.propellant != engine.propellant)
                {
                    stage.dryMass += loaded; // carried, but nothing can burn it
                    result.UnusableFuel += loaded;
                    result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.IncompatiblePropellant, index,
                        tank.name + " (" + Name(tank.propellant) + ") не подходит двигателю " + engine.name + " (" + Name(engine.propellant) +
                        "): " + (loaded / 1000.0).ToString("0.0") + " т топлива — мёртвый груз."));
                }
                else
                {
                    stage.propellantMass += loaded;
                    result.UsableFuel += loaded;
                }
            }
            stage.length = Math.Max(stage.length, 1.0);
            return stage;
        }

        static ModuleDefinition Resolve(string id, int stageIndex, CompiledRocket result, Func<string, bool> isUnlocked)
        {
            ModuleDefinition m = ModuleCatalog.Find(id);
            if (m == null)
            {
                result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.UnknownModule, stageIndex, "Неизвестный модуль «" + id + "» пропущен."));
                return null;
            }
            if (m.requiresPurchase && isUnlocked != null && !isUnlocked(m.id))
            {
                result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.LockedModule, stageIndex, m.name + " не куплен — не установлен."));
                return null;
            }
            return m;
        }

        public static string Name(PropellantType p)
        {
            switch (p)
            {
                case PropellantType.Kerolox: return "керосин + O₂";
                case PropellantType.Hydrolox: return "водород + O₂";
                case PropellantType.Hypergolic: return "гиперголь";
                default: return "—";
            }
        }
    }
}
