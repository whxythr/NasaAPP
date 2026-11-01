using UnityEngine;

namespace SpaceSim.Runtime.Menu
{
    /// <summary>The six mission resources every scenario is constrained by.</summary>
    public enum MissionResource { Budget, Mass, Power, Data, Comms, Lifetime }

    /// <summary>
    /// Static content for the main menu: mission scenarios, difficulty levels, team roles and Academy lessons.
    /// Pure data so the mission-design screens can reuse it later.
    /// </summary>
    public static class MenuCatalog
    {
        public sealed class Scenario
        {
            public string Id, Title, Target, Type, Summary, LearningFocus;
            public string[] Goals;
            public MissionResource Critical;
            /// <summary>Starting allowance per resource in display units (see <see cref="ResourceUnits"/>).</summary>
            public float[] Allowance;
            /// <summary>How tight each resource is for this scenario, 0 = generous … 1 = very tight.</summary>
            public float[] Pressure;
            public int Stars;
            public Color Tint;
        }

        public sealed class Difficulty
        {
            public string Title, Tagline, Description;
        }

        public sealed class Role
        {
            public string Title, Initials, Owns, Description;
            public Color Tint;
        }

        public sealed class Lesson
        {
            public string Title, Topic, Minutes;
            public float Progress;
            public bool Locked;
        }

        public static readonly string[] ResourceNames = { "BUDGET", "MASS", "POWER", "DATA", "COMMS", "LIFETIME" };
        public static readonly string[] ResourceUnits = { "M$", "kg", "W", "Gb/day", "kbps", "yr" };
        public static readonly string[] ResourceHints =
        {
            "Money to build, launch and operate the mission.",
            "Everything the rocket must lift: structure, fuel, instruments.",
            "Electricity from solar panels or RTGs, stored in batteries.",
            "Science your instruments produce every day.",
            "How fast the spacecraft can send data to the ground.",
            "How long the mission must survive and keep working.",
        };
        public static readonly Color[] ResourceColors =
        {
            new Color32(0xFF, 0xC8, 0x57, 0xFF),
            new Color32(0x7F, 0xD4, 0xFF, 0xFF),
            new Color32(0xC6, 0xF2, 0x6B, 0xFF),
            new Color32(0xA9, 0x8B, 0xFF, 0xFF),
            new Color32(0x5B, 0xE4, 0xC8, 0xFF),
            new Color32(0xFF, 0x8F, 0xB1, 0xFF),
        };

        public static readonly Scenario[] Scenarios =
        {
            new Scenario
            {
                Id = "weather-watch", Title = "Homeworld Weather Watch", Target = "Home · Low orbit", Type = "EARTH OBSERVATION",
                Summary = "Build a small satellite that photographs storms on the homeworld twice a day and warns coastal cities.",
                Goals = new[] { "Image every coastline at least twice per day", "Deliver storm alerts within 3 hours", "Operate for 3 years" },
                LearningFocus = "Orbit altitude vs. coverage and revisit time",
                Critical = MissionResource.Comms,
                Allowance = new[] { 120f, 450f, 600f, 40f, 2000f, 3f },
                Pressure = new[] { 0.3f, 0.2f, 0.3f, 0.5f, 0.7f, 0.2f },
                Stars = 1, Tint = new Color32(0x6A, 0x4B, 0xD0, 0xFF),
            },
            new Scenario
            {
                Id = "relay-ring", Title = "Relay Ring", Target = "Home · Geostationary", Type = "COMMUNICATIONS",
                Summary = "Place a relay so the far side of the homeworld never loses contact with Mission Control.",
                Goals = new[] { "Continuous link to 3 ground stations", "Survive 10 years in high orbit", "Stay inside the launch window" },
                LearningFocus = "Why higher orbits see more but cost more to reach",
                Critical = MissionResource.Mass,
                Allowance = new[] { 260f, 1800f, 3500f, 5f, 50000f, 10f },
                Pressure = new[] { 0.5f, 0.8f, 0.4f, 0.1f, 0.3f, 0.6f },
                Stars = 2, Tint = new Color32(0x5B, 0xE4, 0xC8, 0xFF),
            },
            new Scenario
            {
                Id = "quoris-mapper", Title = "Quoris Polar Mapper", Target = "Quoris · Polar orbit", Type = "PLANETARY SCIENCE",
                Summary = "Map the frozen poles of Quoris and look for buried ice. Every instrument you add competes for power and data.",
                Goals = new[] { "Map 90% of the surface at 50 m resolution", "Detect subsurface ice", "Return 2 Tb of science" },
                LearningFocus = "Instrument choice vs. mass, power and data volume",
                Critical = MissionResource.Power,
                Allowance = new[] { 420f, 1200f, 900f, 120f, 600f, 4f },
                Pressure = new[] { 0.6f, 0.6f, 0.8f, 0.7f, 0.6f, 0.4f },
                Stars = 3, Tint = new Color32(0x7F, 0xD4, 0xFF, 0xFF),
            },
            new Scenario
            {
                Id = "teonan-sungrazer", Title = "Teonan Sun-Grazer", Target = "Teonan · Inner system", Type = "HELIOPHYSICS",
                Summary = "Fly closer to the star than anything before. Power is plentiful — heat is not your friend.",
                Goals = new[] { "Reach 0.1 AU from the star", "Measure the stellar wind for 1 year", "Keep electronics below 70 °C" },
                LearningFocus = "Thermal limits, solar power and distance from the star",
                Critical = MissionResource.Mass,
                Allowance = new[] { 650f, 700f, 400f, 20f, 300f, 2f },
                Pressure = new[] { 0.5f, 0.9f, 0.2f, 0.4f, 0.7f, 0.5f },
                Stars = 4, Tint = new Color32(0xFF, 0xB5, 0x47, 0xFF),
            },
            new Scenario
            {
                Id = "mora-lander", Title = "Mora Moon Lander", Target = "Hermeous Mora I · Surface", Type = "LANDER",
                Summary = "Land on the irregular moon of Hermeous Mora and sample its dust. Far from the star, every watt counts.",
                Goals = new[] { "Soft-land within 2 km of the target", "Analyse 5 regolith samples", "Relay data via an orbiter" },
                LearningFocus = "Communications distance, relays and power far from the star",
                Critical = MissionResource.Comms,
                Allowance = new[] { 900f, 2600f, 300f, 8f, 120f, 1f },
                Pressure = new[] { 0.7f, 0.8f, 0.9f, 0.3f, 0.9f, 0.5f },
                Stars = 5, Tint = new Color32(0xFF, 0x8F, 0xB1, 0xFF),
            },
            new Scenario
            {
                Id = "sandbox", Title = "Sandbox", Target = "Anywhere", Type = "FREE DESIGN",
                Summary = "No objectives, no limits. Try any spacecraft, orbit and rocket and watch how the numbers react.",
                Goals = new[] { "Unlimited budget and parts", "Simulate as often as you like", "Great for learning the systems" },
                LearningFocus = "Experiment freely with every trade-off",
                Critical = MissionResource.Budget,
                Allowance = new[] { 9999f, 99999f, 99999f, 999f, 99999f, 99f },
                Pressure = new[] { 0f, 0f, 0f, 0f, 0f, 0f },
                Stars = 0, Tint = new Color32(0xC6, 0xF2, 0x6B, 0xFF),
            },
        };

        public static readonly Difficulty[] Difficulties =
        {
            new Difficulty
            {
                Title = "CADET", Tagline = "Guided",
                Description = "Simple parts, generous margins and an advisor who explains every number before you commit.",
            },
            new Difficulty
            {
                Title = "ENGINEER", Tagline = "Full trade-offs",
                Description = "Advanced components and real margins. The advisor only speaks up when a design will fail.",
            },
            new Difficulty
            {
                Title = "FLIGHT DIRECTOR", Tagline = "No safety net",
                Description = "Tight budgets, part failures and random events during the mission. Plan reserves or lose the spacecraft.",
            },
        };

        public static readonly Role[] Roles =
        {
            new Role
            {
                Title = "MISSION PLANNER", Initials = "MP", Owns = "Objective · Orbit · Timeline",
                Description = "Sets the goal, picks the orbit and keeps the schedule.",
                Tint = new Color32(0xFF, 0xC8, 0x57, 0xFF),
            },
            new Role
            {
                Title = "SPACECRAFT ENGINEER", Initials = "SE", Owns = "Structure · Power · Mass",
                Description = "Builds the bus: frame, solar arrays, batteries, attitude control.",
                Tint = new Color32(0x7F, 0xD4, 0xFF, 0xFF),
            },
            new Role
            {
                Title = "SCIENCE LEAD", Initials = "SL", Owns = "Instruments · Data",
                Description = "Chooses the instruments and fights for science return.",
                Tint = new Color32(0xA9, 0x8B, 0xFF, 0xFF),
            },
            new Role
            {
                Title = "OPS & COMMS", Initials = "OC", Owns = "Launch · Ground stations · Budget",
                Description = "Books the rocket, the antennas and signs off on the money.",
                Tint = new Color32(0x5B, 0xE4, 0xC8, 0xFF),
            },
        };

        public static readonly Lesson[] Lessons =
        {
            new Lesson { Title = "What keeps a satellite up?", Topic = "ORBITS", Minutes = "4 min", Progress = 1f },
            new Lesson { Title = "Low, polar or geostationary?", Topic = "ORBITS", Minutes = "6 min", Progress = 1f },
            new Lesson { Title = "Payload capacity & choosing a rocket", Topic = "LAUNCH", Minutes = "5 min", Progress = 0.6f },
            new Lesson { Title = "The power budget: watts vs. sunlight", Topic = "POWER", Minutes = "5 min", Progress = 0f },
            new Lesson { Title = "Data rate, antennas & ground stations", Topic = "COMMS", Minutes = "7 min", Progress = 0f },
            new Lesson { Title = "Mass margins: why engineers add 20%", Topic = "MASS", Minutes = "4 min", Progress = 0f },
            new Lesson { Title = "Instruments: what does science cost?", Topic = "SCIENCE", Minutes = "6 min", Progress = 0f, Locked = true },
            new Lesson { Title = "Radiation, heat and mission lifetime", Topic = "LIFETIME", Minutes = "6 min", Progress = 0f, Locked = true },
        };
    }
}
