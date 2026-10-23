using NUnit.Framework;
using SpaceSim.Flight;

namespace SpaceSim.Tests
{
    public class FlightManualTests
    {
        [Test]
        public void Manual_CoversRequiredTopics_WithLiveExamples()
        {
            var sections = FlightManualContent.Build(TestPlanets.Earth(), 45.965);
            Assert.GreaterOrEqual(sections.Count, 12);
            string all = string.Join("\n", sections.ConvertAll(s => s.Title + "\n" + s.Body));
            StringAssert.Contains("√(μ / r)", all);
            StringAssert.Contains("√(2μ / r)", all);
            StringAssert.Contains("ε = v²/2 − μ/r", all);
            StringAssert.Contains("T = 2π · √(r³ / μ)", all);
            StringAssert.Contains("100 км", all);
            // Worked example for 200 km uses the planet's μ: √(3.986004418e14 / 6 571 000) = 7 788.5 m/s.
            StringAssert.Contains("7 788.5", all);
            StringAssert.Contains("3.986E+14", all);
            Assert.IsFalse(all.Contains("NaN"), "no unformatted values");
        }

        [Test]
        public void Manual_WorksForAirlessPlanet()
        {
            var sections = FlightManualContent.Build(TestPlanets.Earth(atmosphere: false), 0.0);
            string all = string.Join("\n", sections.ConvertAll(s => s.Body));
            StringAssert.Contains("нет атмосферы", all);
        }
    }
}
