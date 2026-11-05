using System;

namespace SpaceSim.Vehicles
{
    public enum ModuleCategory
    {
        Engine,
        FuelTank,
        Navigation,
        Protection,
        Science,
        SampleCollector
    }

    /// <summary>Propellant combination. An engine can only burn propellant of its own type.</summary>
    public enum PropellantType
    {
        None,
        /// <summary>Kerosene + liquid oxygen: dense, moderate Isp.</summary>
        Kerolox,
        /// <summary>Liquid hydrogen + oxygen: highest Isp, bulky heavy tanks.</summary>
        Hydrolox,
        /// <summary>Storable hypergolic: lower Isp, reliable restarts — landers and manoeuvring.</summary>
        Hypergolic
    }

    /// <summary>What a module lets the vehicle do (beyond adding mass).</summary>
    [Flags]
    public enum ModuleCapability
    {
        None = 0,
        /// <summary>Predicts the current orbit (periapsis, apoapsis, period) — enables the orbit-analysis instrument.</summary>
        OrbitPrediction = 1,
        /// <summary>Star tracker: required for interplanetary navigation / assisted flight planning.</summary>
        InterplanetaryNavigation = 2,
        /// <summary>Measures and records atmosphere composition, pressure and temperature on site.</summary>
        AtmosphereAnalysis = 4,
        /// <summary>Spectrometer: identifies minerals, raises the research value of samples.</summary>
        SampleAnalysis = 8
    }

    /// <summary>Which kinds of samples a collector can take.</summary>
    [Flags]
    public enum SampleKinds
    {
        None = 0,
        Rock = 1,
        Soil = 2,
        Liquid = 4,
        Acid = 8,
        Rare = 16
    }

    /// <summary>
    /// One rocket component. All values SI (kg, N, s, m). Only the fields relevant to its category are used.
    /// Data, not behaviour: the physics comes from <see cref="SpaceSim.Flight.RocketEquations"/> via the compiler.
    /// </summary>
    [Serializable]
    public class ModuleDefinition
    {
        public string id = "";
        public string name = "";
        public ModuleCategory category;
        public string description = "";
        /// <summary>Mass without propellant [kg].</summary>
        public double dryMass;
        /// <summary>Visual/structural length contribution [m].</summary>
        public double length;
        /// <summary>Price in credits (used by the shop, M3).</summary>
        public int price;
        /// <summary>Not available until bought in the shop.</summary>
        public bool requiresPurchase;
        /// <summary>Always present in the payload section and cannot be removed (the probe core).</summary>
        public bool mandatory;

        // Engine
        public PropellantType propellant;
        public double vacuumThrust;
        public double vacuumIsp;
        public double seaLevelIsp;

        // Fuel tank
        /// <summary>Maximum propellant mass the tank holds [kg].</summary>
        public double propellantCapacity;

        // Payload-section modules
        public ModuleCapability capabilities;
        /// <summary>0 = none; higher = safer in stronger radiation (see M6 environment hazards).</summary>
        public int radiationShielding;
        /// <summary>0 = none; higher = resists stronger corrosive (acidic) atmospheres and surfaces.</summary>
        public int acidResistance;
        /// <summary>Sample storage [kg].</summary>
        public double sampleCapacity;
        public SampleKinds sampleKinds;
        /// <summary>Multiplier on the research value of samples analysed with this instrument (1 = none).</summary>
        public double researchMultiplier = 1.0;

        public bool IsEngine => category == ModuleCategory.Engine;
        public bool IsTank => category == ModuleCategory.FuelTank;
        public bool IsPayload => !IsEngine && !IsTank;

        public ModuleDefinition Clone() => (ModuleDefinition)MemberwiseClone();
    }
}
