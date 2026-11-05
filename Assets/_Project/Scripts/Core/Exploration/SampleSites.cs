using System;
using System.Collections.Generic;
using SpaceSim.Economy;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Procedural;
using SpaceSim.Vehicles;

namespace SpaceSim.Exploration
{
    /// <summary>A collectable sample lying at a fixed place on a planet.</summary>
    public sealed class SampleSite
    {
        /// <summary>Stable id: planet + surface cell + index (the same place always has the same samples).</summary>
        public string Id = "";
        public string PlanetId = "";
        public string Name = "";
        public string Description = "";
        public SampleCategory Category;
        public SampleRarity Rarity;
        /// <summary>[kg]</summary>
        public double Mass;
        public double ScientificValue;
        public int BasePrice;
        /// <summary>Planet-fixed unit direction of the spot.</summary>
        public Vector3d FixedDirection;
        /// <summary>Collector abilities needed.</summary>
        public SampleKinds Required;
        public bool IsLiquid => Category == SampleCategory.Water || Category == SampleCategory.OtherLiquid || Category == SampleCategory.Acid;
    }

    public readonly struct CollectionCheck
    {
        public CollectionCheck(bool ok, string reason)
        {
            Ok = ok;
            Reason = reason;
        }

        public bool Ok { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// Deterministic sample sites. The surface is divided into cells of <see cref="CellSize"/> metres (latitude/longitude grid);
    /// each cell holds a few samples whose kind depends on the planet and on the ground there (water, biome). Generated on
    /// demand around the landing point; nothing is stored except which sites were already collected.
    /// </summary>
    public static class SampleSiteGenerator
    {
        public const double CellSize = 400.0;
        public const int SitesPerCell = 3;

        sealed class PlanetSamples
        {
            public double ValueFactor = 1.0;
            public (SampleCategory category, double weight, string[] names)[] Kinds;
        }

        static readonly Dictionary<string, PlanetSamples> Tables = new Dictionary<string, PlanetSamples>
        {
            [HomeSystemCatalog.HomeWorldId] = new PlanetSamples
            {
                ValueFactor = 0.3,
                Kinds = new[]
                {
                    (SampleCategory.Rock, 0.35, new[] { "Базальт", "Гранит", "Песчаник" }),
                    (SampleCategory.Mineral, 0.12, new[] { "Кварц", "Пироксен", "Полевой шпат" }),
                    (SampleCategory.RareGeology, 0.03, new[] { "Кристалл фиолетового флюорита" }),
                    (SampleCategory.Soil, 0.30, new[] { "Пустынный песок", "Почва степи", "Береговой песок" }),
                    (SampleCategory.MineralSoil, 0.12, new[] { "Железистая латеритная почва" }),
                    (SampleCategory.UnusualCompound, 0.08, new[] { "Органический ил" })
                }
            },
            [HomeSystemCatalog.TeonanId] = new PlanetSamples
            {
                ValueFactor = 1.8,
                Kinds = new[]
                {
                    (SampleCategory.Rock, 0.40, new[] { "Силикатная кора", "Серо-коричневый базальт" }),
                    (SampleCategory.Mineral, 0.20, new[] { "Железо-никелевое включение", "Оливин" }),
                    (SampleCategory.RareGeology, 0.06, new[] { "Импактное стекло" }),
                    (SampleCategory.Soil, 0.20, new[] { "Реголит" }),
                    (SampleCategory.MineralSoil, 0.10, new[] { "Железистый реголит" }),
                    (SampleCategory.UnusualCompound, 0.04, new[] { "Вулканическая сера" })
                }
            },
            [HomeSystemCatalog.QuorisId] = new PlanetSamples
            {
                ValueFactor = 1.4,
                Kinds = new[]
                {
                    (SampleCategory.Rock, 0.38, new[] { "Окисленный базальт", "Ржавая брекчия" }),
                    (SampleCategory.Mineral, 0.20, new[] { "Гематит", "Магнетит" }),
                    (SampleCategory.RareGeology, 0.06, new[] { "Ударный расплав «сердечка»" }),
                    (SampleCategory.Soil, 0.22, new[] { "Ржавый реголит" }),
                    (SampleCategory.MineralSoil, 0.10, new[] { "Оксидная пыль" }),
                    (SampleCategory.UnusualCompound, 0.04, new[] { "Перхлоратная корка" })
                }
            },
            [HomeSystemCatalog.HermeousMoraId] = new PlanetSamples
            {
                ValueFactor = 2.5,
                Kinds = new[]
                {
                    (SampleCategory.Rock, 0.28, new[] { "Вулканический андезит" }),
                    (SampleCategory.Mineral, 0.14, new[] { "Пирит", "Ангидрит" }),
                    (SampleCategory.RareGeology, 0.05, new[] { "Сульфатный кристалл" }),
                    (SampleCategory.Soil, 0.18, new[] { "Сернистый грунт" }),
                    (SampleCategory.UnusualCompound, 0.07, new[] { "Органическая плёнка из дымки" }),
                    (SampleCategory.Acid, 0.18, new[] { "Конденсат серной кислоты" }),
                    (SampleCategory.OtherLiquid, 0.10, new[] { "Расплав серы" })
                }
            },
            [HomeSystemCatalog.MoraMoonId] = new PlanetSamples
            {
                ValueFactor = 1.6,
                Kinds = new[]
                {
                    (SampleCategory.Rock, 0.45, new[] { "Обломок спутника" }),
                    (SampleCategory.Mineral, 0.20, new[] { "Оливин спутника" }),
                    (SampleCategory.RareGeology, 0.05, new[] { "Хондрит" }),
                    (SampleCategory.Soil, 0.30, new[] { "Пыль спутника" })
                }
            }
        };

        static readonly PlanetSamples Generic = new PlanetSamples
        {
            Kinds = new[]
            {
                (SampleCategory.Rock, 0.5, new[] { "Порода" }), (SampleCategory.Mineral, 0.2, new[] { "Минерал" }),
                (SampleCategory.Soil, 0.3, new[] { "Грунт" })
            }
        };

        /// <summary>Sites in the cells around a planet-fixed direction (3×3 cells ≈ 1.2 km square).</summary>
        public static List<SampleSite> Around(string planetId, double radius, Vector3d centerDirection, Func<Vector3d, bool> isWater = null)
        {
            var sites = new List<SampleSite>();
            PlanetRotation.LatLonFromDirection(centerDirection.Normalized, out double lat, out double lon);
            double cellAngle = CellSize / radius;
            long ci = (long)Math.Floor(lat / cellAngle), cj = (long)Math.Floor(lon / cellAngle);
            for (long i = ci - 1; i <= ci + 1; i++)
                for (long j = cj - 1; j <= cj + 1; j++)
                    AddCell(sites, planetId, radius, i, j, cellAngle, isWater);
            return sites;
        }

        static void AddCell(List<SampleSite> sites, string planetId, double radius, long i, long j, double cellAngle, Func<Vector3d, bool> isWater)
        {
            PlanetSamples table = Tables.TryGetValue(planetId ?? "", out PlanetSamples t) ? t : Generic;
            ulong seed = DeterministicRandom.Mix(Hash(planetId), (ulong)(i * 73856093L) ^ (ulong)(j * 19349663L));
            var rnd = new DeterministicRandom(seed);
            for (int k = 0; k < SitesPerCell; k++)
            {
                double lat = (i + rnd.NextDouble()) * cellAngle;
                double lon = (j + rnd.NextDouble()) * cellAngle;
                if (Math.Abs(lat) > Math.PI / 2.0 - 1e-6) continue;
                Vector3d dir = PlanetRotation.DirectionFromLatLon(lat, lon);
                bool water = isWater != null && isWater(dir);
                double u = rnd.NextDouble();
                SampleCategory category;
                string[] names;
                if (water)
                {
                    if (planetId != HomeSystemCatalog.HomeWorldId) continue; // nothing to pick up under other seas (none exist now)
                    category = SampleCategory.Water;
                    names = new[] { "Фиолетовая морская вода" };
                }
                else category = Pick(table, u, out names);
                var site = new SampleSite
                {
                    Id = planetId + ":" + i + ":" + j + ":" + k,
                    PlanetId = planetId,
                    Name = names[rnd.NextInt(names.Length)],
                    Category = category,
                    FixedDirection = dir
                };
                Describe(site, table, rnd);
                sites.Add(site);
            }
        }

        static SampleCategory Pick(PlanetSamples table, double u, out string[] names)
        {
            double total = 0.0;
            foreach (var k in table.Kinds) total += k.weight;
            double x = u * total;
            foreach (var k in table.Kinds)
            {
                x -= k.weight;
                if (x <= 0.0)
                {
                    names = k.names;
                    return k.category;
                }
            }
            var last = table.Kinds[table.Kinds.Length - 1];
            names = last.names;
            return last.category;
        }

        static void Describe(SampleSite s, PlanetSamples table, DeterministicRandom rnd)
        {
            switch (s.Category)
            {
                case SampleCategory.Rock: s.Rarity = SampleRarity.Common; s.Mass = rnd.Range(0.4, 3.0); s.Required = SampleKinds.Rock; break;
                case SampleCategory.Mineral: s.Rarity = SampleRarity.Uncommon; s.Mass = rnd.Range(0.2, 1.5); s.Required = SampleKinds.Rock; break;
                case SampleCategory.RareGeology:
                    s.Rarity = rnd.NextDouble() < 0.15 ? SampleRarity.Exceptional : SampleRarity.Rare;
                    s.Mass = rnd.Range(0.1, 0.6);
                    s.Required = SampleKinds.Rock | SampleKinds.Rare;
                    break;
                case SampleCategory.Soil: s.Rarity = SampleRarity.Common; s.Mass = rnd.Range(0.5, 2.0); s.Required = SampleKinds.Soil; break;
                case SampleCategory.MineralSoil: s.Rarity = SampleRarity.Uncommon; s.Mass = rnd.Range(0.5, 1.5); s.Required = SampleKinds.Soil; break;
                case SampleCategory.UnusualCompound: s.Rarity = SampleRarity.Rare; s.Mass = rnd.Range(0.1, 0.8); s.Required = SampleKinds.Soil | SampleKinds.Rare; break;
                case SampleCategory.Water: s.Rarity = SampleRarity.Common; s.Mass = rnd.Range(0.3, 1.0); s.Required = SampleKinds.Liquid; break;
                case SampleCategory.OtherLiquid: s.Rarity = SampleRarity.Uncommon; s.Mass = rnd.Range(0.3, 1.0); s.Required = SampleKinds.Liquid; break;
                default: s.Rarity = SampleRarity.Uncommon; s.Mass = rnd.Range(0.2, 0.8); s.Required = SampleKinds.Liquid | SampleKinds.Acid; break;
            }
            double[] science = { 5.0, 15.0, 50.0, 150.0 };
            s.ScientificValue = science[(int)s.Rarity] * table.ValueFactor;
            double kindBase = s.IsLiquid ? (s.Category == SampleCategory.Acid ? 120.0 : 70.0) : s.Category >= SampleCategory.Soil ? 60.0 : 80.0;
            s.BasePrice = (int)Math.Round(kindBase * table.ValueFactor * (0.6 + 0.4 * Math.Min(3.0, s.Mass)));
            s.Description = CategoryName(s.Category) + " · " + RarityName(s.Rarity) + " · " + Academy.AcademyNumber.Format(s.Mass) + " кг";
        }

        public static string CategoryName(SampleCategory c)
        {
            switch (c)
            {
                case SampleCategory.Rock: return "горная порода";
                case SampleCategory.Mineral: return "минерал";
                case SampleCategory.RareGeology: return "редкий геологический материал";
                case SampleCategory.Soil: return "поверхностный грунт";
                case SampleCategory.MineralSoil: return "минерализованный грунт";
                case SampleCategory.UnusualCompound: return "необычное соединение";
                case SampleCategory.Water: return "вода";
                case SampleCategory.OtherLiquid: return "жидкость";
                default: return "кислота";
            }
        }

        public static string RarityName(SampleRarity r) =>
            r == SampleRarity.Common ? "обычный" : r == SampleRarity.Uncommon ? "необычный" : r == SampleRarity.Rare ? "редкий" : "исключительный";

        static ulong Hash(string s)
        {
            ulong h = 1469598103934665603UL;
            foreach (char ch in s ?? "")
            {
                h ^= ch;
                h *= 1099511628211UL;
            }
            return h;
        }
    }

    /// <summary>Rules for taking samples and going outside (game rules with stated physical reasons).</summary>
    public static class SampleCollection
    {
        /// <summary>From inside the lander the collector arm reaches this far [m].</summary>
        public const double ArmReach = 25.0;
        /// <summary>On foot the astronaut picks up within this distance [m] (also liquids from the shore).</summary>
        public const double HandReach = 4.0;

        public static CollectionCheck Check(SampleSite site, VehicleCapabilities caps, double carriedMass, IList<string> collected)
        {
            if (collected != null && collected.Contains(site.Id)) return new CollectionCheck(false, "Этот образец уже взят.");
            if (caps == null || caps.SampleCapacity <= 0.0)
                return new CollectionCheck(false, "Нет пробоотборника: поставьте ПБ-1 в ангаре (камни и грунт, бесплатно).");
            if (!caps.CanCollect(site.Required))
                return new CollectionCheck(false, "Пробоотборник не умеет брать " + Missing(site.Required & ~caps.SampleKinds) + ": нужен ПБ-2 (магазин).");
            if (carriedMass + site.Mass > caps.SampleCapacity + 1e-9)
                return new CollectionCheck(false, "Нет места: в контейнере " + Academy.AcademyNumber.Format(carriedMass) + " из " +
                                                  Academy.AcademyNumber.Format(caps.SampleCapacity) + " кг, образец " + Academy.AcademyNumber.Format(site.Mass) + " кг.");
            return new CollectionCheck(true, "");
        }

        static string Missing(SampleKinds k)
        {
            var parts = new List<string>();
            if ((k & SampleKinds.Rock) != 0) parts.Add("камни");
            if ((k & SampleKinds.Soil) != 0) parts.Add("грунт");
            if ((k & SampleKinds.Liquid) != 0) parts.Add("жидкости");
            if ((k & SampleKinds.Acid) != 0) parts.Add("кислоты");
            if ((k & SampleKinds.Rare) != 0) parts.Add("редкие материалы");
            return string.Join(", ", parts);
        }

        /// <summary>The profile record of a collected sample: on board (not yet delivered), analysed on site with a spectrometer.</summary>
        public static SampleRecord ToRecord(SampleSite site, VehicleCapabilities caps)
        {
            bool analysed = caps != null && caps.Has(ModuleCapability.SampleAnalysis);
            return new SampleRecord
            {
                id = site.Id, name = site.Name, category = site.Category, planetId = site.PlanetId, mass = site.Mass, rarity = site.Rarity,
                scientificValue = site.ScientificValue, basePrice = site.BasePrice, analysed = analysed,
                analysisMultiplier = analysed ? Math.Max(1.0, caps.ResearchMultiplier) : 1.0, delivered = false
            };
        }

        /// <summary>
        /// Whether a person in a suit may go outside: pressure, temperature, acid and strong radiation (reasons with numbers).
        /// Where they may not, samples can still be taken with the lander's arm within <see cref="ArmReach"/>.
        /// </summary>
        public static CollectionCheck CanExit(PlanetPhysicalProperties planet, EnvironmentHazards hazards)
        {
            var a = planet.atmosphere;
            double p = a != null && a.hasAtmosphere ? a.surfacePressure : 0.0;
            double t = a != null ? a.surfaceTemperature : 0.0;
            if (p > 2.0e5) return new CollectionCheck(false, "Давление " + Academy.AcademyNumber.Format(p / 101325.0) + " атм — скафандр не выдержит (предел 2 атм).");
            if (t > 480.0) return new CollectionCheck(false, "Температура " + Academy.AcademyNumber.Format(t - 273.15) + " °C — скафандр перегреется (предел 207 °C).");
            if (t > 0.0 && t < 90.0) return new CollectionCheck(false, "Температура " + Academy.AcademyNumber.Format(t - 273.15) + " °C — ниже предела скафандра (−183 °C).");
            if (hazards != null && hazards.acidLevel > 0) return new CollectionCheck(false, "Кислотная среда разъест скафандр.");
            if (hazards != null && hazards.radiationLevel >= 2) return new CollectionCheck(false, "Радиация уровня " + hazards.radiationLevel + " — опасная доза за минуты.");
            return new CollectionCheck(true, "");
        }
    }
}
