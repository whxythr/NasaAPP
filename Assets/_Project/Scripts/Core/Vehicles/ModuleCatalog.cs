using System.Collections.Generic;

namespace SpaceSim.Vehicles
{
    /// <summary>
    /// Built-in rocket components. Numbers are game-balanced but physically plausible (Isp and thrust-to-mass of
    /// real engine classes; tank dry fractions 4–10 %). Modules marked requiresPurchase are sold in the shop (M3).
    /// </summary>
    public static class ModuleCatalog
    {
        public const string EngineSokol = "eng-sokol-1";
        public const string EngineSokolM = "eng-sokol-1m";
        public const string EngineVega = "eng-vega-v";
        public const string EngineHydra = "eng-hydra";
        public const string EngineIskra = "eng-iskra";
        public const string TankK100 = "tank-k100";
        public const string TankK140 = "tank-k140";
        public const string TankK40 = "tank-k40";
        public const string TankK10 = "tank-k10";
        public const string TankH20 = "tank-h20";
        public const string TankN2 = "tank-n2";
        public const string ProbeCore = "core-probe";
        public const string NavComputer = "nav-nk1";
        public const string StarTracker = "nav-sd2";
        public const string RadiationShield = "prot-rad1";
        public const string RadiationShieldHeavy = "prot-rad2";
        public const string AcidCoating = "prot-acid1";
        public const string Spectrometer = "sci-sp1";
        public const string MeteoKit = "sci-meteo";
        public const string CollectorBasic = "col-pb1";
        public const string CollectorAdvanced = "col-pb2";

        static List<ModuleDefinition> all;

        public static IReadOnlyList<ModuleDefinition> All => all ?? (all = Build());

        public static ModuleDefinition Find(string id)
        {
            foreach (var m in All)
                if (m.id == id) return m;
            return null;
        }

        public static List<ModuleDefinition> InCategory(ModuleCategory c)
        {
            var list = new List<ModuleDefinition>();
            foreach (var m in All)
                if (m.category == c) list.Add(m);
            return list;
        }

        static List<ModuleDefinition> Build() => new List<ModuleDefinition>
        {
            // ---- Engines ----
            new ModuleDefinition { id = EngineSokol, name = "Сокол-1", category = ModuleCategory.Engine, propellant = PropellantType.Kerolox,
                dryMass = 2500, length = 3, price = 0, vacuumThrust = 1.30e6, vacuumIsp = 330, seaLevelIsp = 295,
                description = "Керосин-кислородный двигатель первой ступени. Хорошая тяга у поверхности." },
            new ModuleDefinition { id = EngineSokolM, name = "Сокол-1М", category = ModuleCategory.Engine, propellant = PropellantType.Kerolox,
                dryMass = 2400, length = 3, price = 9000, requiresPurchase = true, vacuumThrust = 1.50e6, vacuumIsp = 338, seaLevelIsp = 305,
                description = "Модернизированный Сокол: +15 % тяги, выше Isp, легче." },
            new ModuleDefinition { id = EngineVega, name = "Вега-В", category = ModuleCategory.Engine, propellant = PropellantType.Kerolox,
                dryMass = 1600, length = 2, price = 0, vacuumThrust = 7.0e5, vacuumIsp = 350, seaLevelIsp = 240,
                description = "Вакуумный двигатель верхней ступени: большое сопло, у поверхности тяга сильно падает." },
            new ModuleDefinition { id = EngineHydra, name = "Гидра", category = ModuleCategory.Engine, propellant = PropellantType.Hydrolox,
                dryMass = 300, length = 2.5, price = 0, vacuumThrust = 1.1e5, vacuumIsp = 450, seaLevelIsp = 280,
                description = "Водородный вакуумный двигатель: лучший Isp, но малая тяга — долгие разгоны." },
            new ModuleDefinition { id = EngineIskra, name = "Искра", category = ModuleCategory.Engine, propellant = PropellantType.Hypergolic,
                dryMass = 100, length = 1, price = 0, vacuumThrust = 3.0e4, vacuumIsp = 315, seaLevelIsp = 250,
                description = "Маленький двигатель на самовоспламеняющемся топливе: манёвры и посадка." },

            // ---- Fuel tanks ----
            new ModuleDefinition { id = TankK100, name = "Бак K-100", category = ModuleCategory.FuelTank, propellant = PropellantType.Kerolox,
                dryMass = 4000, length = 11, price = 0, propellantCapacity = 100000, description = "Большой керосин-кислородный бак, 100 т." },
            new ModuleDefinition { id = TankK140, name = "Расширенный бак K-140", category = ModuleCategory.FuelTank, propellant = PropellantType.Kerolox,
                dryMass = 5200, length = 15, price = 6000, requiresPurchase = true, propellantCapacity = 140000,
                description = "Расширенный бак: +40 % топлива при +30 % сухой массы. Больше Δv — но и тяжелее старт." },
            new ModuleDefinition { id = TankK40, name = "Бак K-40", category = ModuleCategory.FuelTank, propellant = PropellantType.Kerolox,
                dryMass = 2400, length = 10, price = 0, propellantCapacity = 40000, description = "Средний керосин-кислородный бак, 40 т." },
            new ModuleDefinition { id = TankK10, name = "Бак K-10", category = ModuleCategory.FuelTank, propellant = PropellantType.Kerolox,
                dryMass = 800, length = 3, price = 0, propellantCapacity = 10000, description = "Малый керосин-кислородный бак, 10 т." },
            new ModuleDefinition { id = TankH20, name = "Бак H-20", category = ModuleCategory.FuelTank, propellant = PropellantType.Hydrolox,
                dryMass = 2000, length = 12, price = 0, propellantCapacity = 20000,
                description = "Водород-кислородный бак, 20 т. Водород лёгкий и объёмный — бак тяжёлый и длинный." },
            new ModuleDefinition { id = TankN2, name = "Бак N-2", category = ModuleCategory.FuelTank, propellant = PropellantType.Hypergolic,
                dryMass = 200, length = 1.5, price = 0, propellantCapacity = 2000, description = "Малый бак самовоспламеняющегося топлива, 2 т." },

            // ---- Payload section ----
            new ModuleDefinition { id = ProbeCore, name = "Зонд (ядро)", category = ModuleCategory.Science, dryMass = 1000, length = 2.5, mandatory = true,
                description = "Научный зонд: связь, питание, управление. Обязательная полезная нагрузка." },
            new ModuleDefinition { id = NavComputer, name = "Навигационный компьютер НК-1", category = ModuleCategory.Navigation, dryMass = 120, length = 0.3, price = 0,
                capabilities = ModuleCapability.OrbitPrediction,
                description = "Рассчитывает текущую орбиту: перицентр, апоцентр, период, наклонение (прибор «Анализ орбиты»)." },
            new ModuleDefinition { id = StarTracker, name = "Звёздный датчик СД-2", category = ModuleCategory.Navigation, dryMass = 60, length = 0.2, price = 0,
                capabilities = ModuleCapability.InterplanetaryNavigation,
                description = "Ориентация по звёздам — нужна для межпланетной навигации и режима подсказок планировщика." },
            new ModuleDefinition { id = RadiationShield, name = "Радиационная защита Р-1", category = ModuleCategory.Protection, dryMass = 450, length = 0.4,
                price = 7000, requiresPurchase = true, radiationShielding = 1,
                description = "Экранирует оборудование и экипаж: открывает исследование районов с сильным излучением." },
            new ModuleDefinition { id = RadiationShieldHeavy, name = "Радиационная защита Р-2", category = ModuleCategory.Protection, dryMass = 900, length = 0.6,
                price = 14000, requiresPurchase = true, radiationShielding = 2,
                description = "Тяжёлая защита уровня 2: работа в самых сильных полях излучения (Теонан). Вдвое тяжелее Р-1." },
            new ModuleDefinition { id = AcidCoating, name = "Кислотостойкое покрытие К-1", category = ModuleCategory.Protection, dryMass = 300, length = 0.1,
                price = 6500, requiresPurchase = true, acidResistance = 1,
                description = "Защищает от коррозии: нужно для посадки в кислотной атмосфере (Гермеус Мора)." },
            new ModuleDefinition { id = Spectrometer, name = "Спектрометр СП-1", category = ModuleCategory.Science, dryMass = 80, length = 0.3, price = 0,
                capabilities = ModuleCapability.SampleAnalysis, researchMultiplier = 1.5,
                description = "Определяет минералы на месте: научная ценность образцов ×1.5." },
            new ModuleDefinition { id = MeteoKit, name = "Метеокомплекс МЕТЕО", category = ModuleCategory.Science, dryMass = 25, length = 0.1, price = 0,
                capabilities = ModuleCapability.AtmosphereAnalysis,
                description = "Давление, температура и состав атмосферы в точке посадки." },
            new ModuleDefinition { id = CollectorBasic, name = "Пробоотборник ПБ-1", category = ModuleCategory.SampleCollector, dryMass = 150, length = 0.5, price = 0,
                sampleCapacity = 10, sampleKinds = SampleKinds.Rock | SampleKinds.Soil,
                description = "Камни и грунт, до 10 кг." },
            new ModuleDefinition { id = CollectorAdvanced, name = "Пробоотборник ПБ-2", category = ModuleCategory.SampleCollector, dryMass = 260, length = 0.7,
                price = 8000, requiresPurchase = true, sampleCapacity = 30,
                sampleKinds = SampleKinds.Rock | SampleKinds.Soil | SampleKinds.Liquid | SampleKinds.Acid | SampleKinds.Rare,
                description = "До 30 кг; жидкости, кислоты и редкие материалы." },
        };
    }
}
