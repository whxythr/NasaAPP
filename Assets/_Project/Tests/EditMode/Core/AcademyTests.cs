using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceSim.Academy;
using SpaceSim.Economy;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Vehicles;

namespace SpaceSim.Tests
{
    /// <summary>
    /// Flight Academy (M4): lessons are complete, calculators reproduce analytic cases through the central functions,
    /// exercises are self-consistent, and the advisor reacts to the real simulation.
    /// </summary>
    public class AcademyTests
    {
        static AcademyContext HomeContext(bool withRocket = true)
        {
            var c = AcademyContext.FromHomeSystem(HomeSystemCatalog.HomeWorldId);
            if (withRocket)
            {
                RocketConfiguration config = BlueprintCompiler.Compile(RocketBlueprint.CreateDefault()).Configuration;
                c.Rocket = config;
                c.Design = RocketDesignAnalysis.Analyze(config, c.SurfaceGravity, MissionRequirements.LowOrbit(c.Mu, c.Radius, c.LowOrbitAltitude, c.SiteRotationSpeed));
            }
            return c;
        }

        static AcademyCalculator Calc(AcademyContext c, string id)
        {
            AcademyCalculator calc = AcademyCalculators.Find(AcademyCalculators.Build(c), id);
            Assert.IsNotNull(calc, id);
            return calc;
        }

        static CalculatorResult Run(AcademyCalculator calc, params double[] values)
        {
            CalculatorResult r = calc.Compute(values);
            Assert.IsTrue(r.Ok, calc.Id + ": " + r.Error);
            return r;
        }

        static double Out(CalculatorResult r, string symbol)
        {
            CalculatorOutput o = r.Find(symbol);
            Assert.IsNotNull(o, "output " + symbol);
            return o.Value;
        }

        // ---- Content ---------------------------------------------------------------------------------------------

        [Test]
        public void Context_UsesTheStartPlanetsData()
        {
            var c = HomeContext(false);
            CelestialBodyPreset home = HomeSystemCatalog.Bodies().Find(b => b.Id == HomeSystemCatalog.HomeWorldId);
            Assert.AreEqual(home.Physical.GravitationalParameter, c.Mu);
            Assert.AreEqual(8.27, c.SurfaceGravity, 0.01, "g of the fourth planet");
            Assert.AreEqual(5, c.Bodies.Count);
            Assert.AreEqual(4, new List<AcademyBody>(c.Planets()).Count, "the moon is not a heliocentric target");
            Assert.Greater(c.StarMu, 0.0);
            Assert.Greater(c.SiteRotationSpeed, 0.0);
        }

        [Test]
        public void Topics_CoverAllThirteenSubjects_WithCompleteFormulas()
        {
            var c = HomeContext();
            List<AcademyTopic> topics = AcademyTopics.Build(c);
            List<AcademyCalculator> calculators = AcademyCalculators.Build(c);
            Assert.AreEqual(13, topics.Count);
            var ids = new HashSet<string>();
            for (int i = 0; i < topics.Count; i++)
            {
                AcademyTopic t = topics[i];
                Assert.AreEqual(i + 1, t.Number);
                Assert.IsTrue(ids.Add(t.Id), "unique id " + t.Id);
                Assert.IsNotEmpty(t.Title);
                Assert.IsNotEmpty(t.Explanation, t.Id);
                Assert.IsNotEmpty(t.Example, t.Id);
                Assert.IsNotEmpty(t.Application, t.Id);
                Assert.IsNotEmpty(t.Limitations, t.Id);
                Assert.IsFalse(t.Example.Contains("NaN") || t.Example.Contains("∞"), t.Id + " example is finite: " + t.Example);
                Assert.Greater(t.Formulas.Count, 0, t.Id);
                foreach (AcademyFormula f in t.Formulas)
                {
                    Assert.IsNotEmpty(f.Expression);
                    Assert.Greater(f.Variables.Count, 0, t.Id + ": " + f.Expression);
                    foreach (FormulaVariable v in f.Variables)
                    {
                        Assert.IsNotEmpty(v.Symbol);
                        Assert.IsNotEmpty(v.Meaning);
                        Assert.IsNotEmpty(v.Unit, t.Id + " " + v.Symbol + " has a unit");
                    }
                }
                Assert.Greater(t.CalculatorIds.Count, 0, t.Id + " links a calculator");
                foreach (string id in t.CalculatorIds) Assert.IsNotNull(AcademyCalculators.Find(calculators, id), t.Id + " → " + id);
                Assert.IsNotNull(t.Exercise, t.Id + " has an exercise");
            }
            foreach (AcademyCalculator calc in calculators) Assert.IsNotNull(AcademyTopics.Find(topics, calc.TopicId), calc.Id + " explained by a topic");
            foreach (string required in new[]
                     {
                         AcademyTopics.MassThrust, AcademyTopics.Gravity, AcademyTopics.Acceleration, AcademyTopics.FuelConsumption, AcademyTopics.Tsiolkovsky,
                         AcademyTopics.DeltaVBudget, AcademyTopics.OrbitalVelocity, AcademyTopics.EscapeVelocity, AcademyTopics.Drag, AcademyTopics.Insertion,
                         AcademyTopics.Hohmann, AcademyTopics.LaunchWindows, AcademyTopics.Rendezvous
                     })
                Assert.IsTrue(ids.Contains(required), required);
        }

        [Test]
        public void Topics_BuildForEveryBody_AndWithoutARocket()
        {
            foreach (CelestialBodyPreset p in HomeSystemCatalog.Bodies())
            {
                var c = AcademyContext.FromHomeSystem(p.Id);
                Assert.AreEqual(13, AcademyTopics.Build(c).Count, p.Id);
                foreach (AcademyCalculator calc in AcademyCalculators.Build(c))
                    Assert.IsTrue(calc.Compute(calc.Defaults()).Ok, p.Id + " / " + calc.Id + " defaults compute");
            }
        }

        // ---- Exercises -------------------------------------------------------------------------------------------

        [Test]
        public void Exercises_AreSelfConsistent_AndDoNotRevealTheAnswerInHints()
        {
            foreach (AcademyTopic t in AcademyTopics.Build(HomeContext()))
            {
                AcademyExercise e = t.Exercise;
                Assert.IsTrue(e.Answer > 0.0 && !double.IsInfinity(e.Answer), e.Id + " answer " + e.Answer);
                Assert.IsTrue(e.Check(e.Answer).Solved, e.Id);
                Assert.IsTrue(e.Check(AcademyNumber.Format(e.Answer)).Solved, e.Id + " accepts the formatted answer " + AcademyNumber.Format(e.Answer));
                Assert.Greater(e.Hints.Count, 0, e.Id);
                Assert.Greater(e.SolutionSteps.Count, 0, e.Id);
                StringAssert.Contains(AcademyNumber.Format(e.Answer), e.SolutionSteps[e.SolutionSteps.Count - 1], e.Id + ": the last step gives the answer");
                foreach (string h in e.Hints) StringAssert.DoesNotContain(AcademyNumber.Format(e.Answer), h, e.Id + " hint must not give the answer");
                StringAssert.DoesNotContain(AcademyNumber.Format(e.Answer), e.Question, e.Id + " question must not give the answer");
            }
        }

        [Test]
        public void ExerciseCheck_DiagnosesClassicMistakes()
        {
            var e = new AcademyExercise { Id = "x", Answer = 7672.6, Unit = "м/с" };
            Assert.AreEqual(AnswerVerdict.Correct, e.Check(7672.0).Verdict);
            Assert.AreEqual(AnswerVerdict.Close, e.Check(7672.6 * 1.03).Verdict);
            StringAssert.Contains("1000 раз", e.Check(7.6726).Message, "km vs m");
            StringAssert.Contains("√2", e.Check(7672.6 * Math.Sqrt(2.0)).Message, "circular vs escape");
            StringAssert.Contains("g₀", e.Check(7672.6 / RocketEquations.G0).Message);
            Assert.AreEqual(AnswerVerdict.Invalid, e.Check("семь тысяч").Verdict);
            Assert.AreEqual(AnswerVerdict.Wrong, e.Check(-7672.6).Verdict);
        }

        [Test]
        public void Exercise_And_Calculator_GiveTheSameNumber()
        {
            var c = HomeContext();
            AcademyTopic orbit = AcademyTopics.Find(AcademyTopics.Build(c), AcademyTopics.OrbitalVelocity);
            double h = AcademyNumber.RoundSignificant(c.LowOrbitAltitude / 1000.0, 3);
            CalculatorResult r = Run(Calc(c, AcademyCalculators.Orbit), c.Mu, c.Radius / 1000.0, h);
            Assert.AreEqual(orbit.Exercise.Answer, Out(r, "v_круг"), 1e-9, "one implementation: lesson answer = calculator = OrbitalMechanics");
        }

        // ---- Calculators against analytic cases ----------------------------------------------------------------

        [Test]
        public void Calculators_ReproduceTextbookValues()
        {
            var c = HomeContext();
            Assert.AreEqual(294.1995, Out(Run(Calc(c, AcademyCalculators.Thrust), 100.0, 300.0), "F") / 1000.0, 1e-3, "F = ṁ·Isp·g₀");
            Assert.AreEqual(2039.23, Out(Run(Calc(c, AcademyCalculators.DeltaV), 300.0, 100.0, 50.0), "Δv"), 0.01, "Δv = Isp·g₀·ln 2");
            Assert.AreEqual(50000.0, Out(Run(Calc(c, AcademyCalculators.Fuel), 2039.226, 300.0, 50.0), "m_топл"), 5.0, "inverse Tsiolkovsky");

            CalculatorResult burn = Run(Calc(c, AcademyCalculators.Burn), 2941.995, 300.0, 100.0, 200.0);
            Assert.AreEqual(1000.0, Out(burn, "ṁ"), 1e-3);
            Assert.AreEqual(100.0, Out(burn, "t"), 1e-3);

            CalculatorResult orbit = Run(Calc(c, AcademyCalculators.Orbit), TestPlanets.EarthMu, 6371.0, 400.0);
            Assert.AreEqual(7672.6, Out(orbit, "v_круг"), 0.5, "ISS-like orbit");
            Assert.AreEqual(92.41, Out(orbit, "T") / 60.0, 0.05);

            CalculatorResult esc = Run(Calc(c, AcademyCalculators.Escape), TestPlanets.EarthMu, 6371.0, 0.0, 0.0);
            Assert.AreEqual(11186.0, Out(esc, "v_уход"), 2.0);
            Assert.AreEqual(Math.Sqrt(2.0), Out(esc, "√2"), 1e-12);

            CalculatorResult geo = Run(Calc(c, AcademyCalculators.Hohmann), TestPlanets.EarthMu, 6678.0, 42164.0);
            Assert.AreEqual(2425.7, Out(geo, "Δv₁"), 5.0, "LEO → GEO first burn");
            Assert.AreEqual(1466.8, Out(geo, "Δv₂"), 5.0);
            Assert.AreEqual(5.26, Out(geo, "t") / 3600.0, 0.02, "transfer ≈ 5.3 h");

            CalculatorResult drag = Run(Calc(c, AcademyCalculators.Drag), 1.225, 8.5, 0.0, 100.0, 1.0, 1.0, 1.0, 9.81);
            Assert.AreEqual(6125.0, Out(drag, "q"), 1e-6, "q = ½ρv²");
            Assert.AreEqual(6125.0, Out(drag, "F_d"), 1e-6);

            CalculatorResult twr = Run(Calc(c, AcademyCalculators.ThrustToWeight), 1000.0, 120.0, 9.81);
            Assert.AreEqual(1e6 / (120000.0 * 9.81), Out(twr, "TWR"), 1e-12);
            Assert.IsTrue(twr.Warnings.Exists(w => w.Contains("TWR < 1")), "heavy rocket is explained, not forbidden");
        }

        [Test]
        public void Insertion_And_Transfer_UseTheCentralFunctions()
        {
            var c = HomeContext();
            CalculatorResult circular = Run(Calc(c, AcademyCalculators.Insertion), c.Mu, c.Radius / 1000.0, 200.0, 200.0);
            Assert.AreEqual(0.0, Out(circular, "Δv"), 1e-9, "already circular");
            CalculatorResult ins = Run(Calc(c, AcademyCalculators.Insertion), c.Mu, c.Radius / 1000.0, 200.0, -1000.0);
            Assert.AreEqual(OrbitalMechanics.CircularizationDeltaV(c.Mu, c.Radius + 200000.0, c.Radius - 1000000.0), Out(ins, "Δv"), 1e-9);
            Assert.IsTrue(ins.Warnings.Count > 0, "periapsis below the surface is pointed out");
            Assert.IsFalse(Calc(c, AcademyCalculators.Insertion).Compute(new[] { c.Mu, c.Radius / 1000.0, 100.0, 200.0 }).Ok, "periapsis above apoapsis");

            AcademyCalculator transfer = Calc(c, AcademyCalculators.Transfer);
            CalculatorPreset toQuoris = transfer.Presets.Find(p => p.Name.Contains("Кварис"));
            Assert.IsNotNull(toQuoris, "presets from the real system");
            CalculatorResult tr = Run(transfer, toQuoris.Values);
            double[] v = toQuoris.Values;
            TransferCalculator.HohmannExcessSpeeds(v[0], v[1] * 1e9, v[2] * 1e9, out double vInf, out _);
            Assert.AreEqual(TransferCalculator.DepartureDeltaV(v[3], (v[4] + v[5]) * 1000.0, vInf), Out(tr, "Δv_отл"), 1e-9);

            var plan = (double[])v.Clone();
            plan[9] = Out(tr, "Δv_отл") - 10.0;
            Assert.IsTrue(Run(transfer, plan).Warnings.Exists(w => w.Contains("недостаточна")), "insufficient departure velocity");
            plan[9] = Out(tr, "Δv_отл") + 10.0;
            Assert.AreEqual(0, Run(transfer, plan).Warnings.Count);
        }

        [Test]
        public void Calculators_ValidateInputs_AndAllPresetsCompute()
        {
            var c = HomeContext();
            AcademyCalculator dv = Calc(c, AcademyCalculators.DeltaV);
            Assert.IsFalse(dv.Compute(new[] { 300.0, 50.0, 100.0 }).Ok, "final mass above initial");
            StringAssert.Contains("больше нуля", dv.Compute(new[] { 300.0, -1.0, 1.0 }).Error);
            Assert.IsFalse(dv.Compute(new[] { double.NaN, 1.0, 1.0 }).Ok);
            Assert.IsFalse(dv.Compute(new[] { 300.0, 1.0 }).Ok, "wrong number of values");
            foreach (AcademyCalculator calc in AcademyCalculators.Build(c))
            {
                Assert.IsTrue(calc.Compute(calc.Defaults()).Ok, calc.Id + " defaults");
                Assert.Greater(calc.Presets.Count, 0, calc.Id);
                foreach (CalculatorPreset p in calc.Presets)
                {
                    Assert.AreEqual(calc.Inputs.Count, p.Values.Length, calc.Id + " / " + p.Name);
                    CalculatorResult r = calc.Compute(p.Values);
                    Assert.IsTrue(r.Ok, calc.Id + " / " + p.Name + ": " + r.Error);
                    Assert.Greater(r.Steps.Count, 0, calc.Id + " shows its working");
                }
            }
        }

        [Test]
        public void Numbers_ParseWhatPlayersType()
        {
            Assert.IsTrue(AcademyNumber.TryParse("12,5", out double a));
            Assert.AreEqual(12.5, a);
            Assert.IsTrue(AcademyNumber.TryParse(" 5.03e24 ", out double b));
            Assert.AreEqual(5.03e24, b);
            Assert.IsTrue(AcademyNumber.TryParse("−3", out double m));
            Assert.AreEqual(-3.0, m);
            Assert.IsTrue(AcademyNumber.TryParse("1 000", out double k));
            Assert.AreEqual(1000.0, k);
            Assert.IsFalse(AcademyNumber.TryParse("abc", out _));
            Assert.IsFalse(AcademyNumber.TryParse("", out _));
            Assert.AreEqual(1230.0, AcademyNumber.RoundSignificant(1234.5, 3));
            Assert.AreEqual(0.0418, AcademyNumber.RoundSignificant(0.04183, 3), 1e-15);
            Assert.IsTrue(AcademyNumber.TryParse(AcademyNumber.FormatEditable(3.986004418e14), out double mu));
            Assert.AreEqual(3.986004418e14, mu, 1e9, "editable format round-trips");
        }

        [Test]
        public void Circularization_IsZeroForCircularOrbits_AndMatchesVisViva()
        {
            double mu = TestPlanets.EarthMu, ra = 6571000.0, rp = 6371000.0;
            Assert.AreEqual(0.0, OrbitalMechanics.CircularizationDeltaV(mu, ra, ra), 1e-9);
            double a = 0.5 * (ra + rp);
            Assert.AreEqual(Math.Sqrt(mu / ra) - Math.Sqrt(mu * (2.0 / ra - 1.0 / a)), OrbitalMechanics.CircularizationDeltaV(mu, ra, rp), 1e-9);
            Assert.Throws<ArgumentException>(() => OrbitalMechanics.CircularizationDeltaV(mu, rp, ra));
        }

        [Test]
        public void Profile_AcademyProgress_IsMigratedForOlderSaves()
        {
            var p = PlayerProfile.CreateNew(HomeSystemCatalog.HomeWorldId);
            p.academy = null;
            Assert.AreEqual(MigrationResult.Migrated, ProfileMigration.Migrate(p));
            Assert.IsNotNull(p.academy);
            Assert.IsFalse(p.academy.HasSolved("ex-thrust"));
        }

        // ---- Advisor -------------------------------------------------------------------------------------------

        static AcademySituation OnPad(double thrust, double mass)
        {
            return new AcademySituation
            {
                Time = 1.0, HasRocket = true, Landed = true, OnLaunchPad = true, CurrentStageHasFuel = true, AnyPropellantLeft = true,
                StageCount = 2, Mass = mass, AvailableThrust = thrust, LocalGravity = 8.27, StageVacuumIsp = 320.0,
                Mu = 3.4e14, Radius = 6.37e6, SpaceBoundary = 100000.0, LowOrbitAltitude = 200000.0, OrbitRequirement = 7500.0, DesignDeltaV = 9000.0,
                Phase = FlightPhase.Ground
            };
        }

        [Test]
        public void Advisor_TwrBelowOne_RaisedOnce_WithProgressiveSolution()
        {
            var advisor = new AcademyAdvisor();
            AcademySituation s = OnPad(900000.0, 120000.0); // TWR ≈ 0.91
            List<AcademyHint> raised = advisor.Evaluate(s);
            AcademyHint h = raised.Find(x => x.Id == AcademyAdvisor.TwrBelowOne);
            Assert.IsNotNull(h);
            StringAssert.Contains("ниже 1", h.Message);
            Assert.AreEqual(AcademyTopics.MassThrust, h.TopicId);
            Assert.AreEqual(0, advisor.Evaluate(s).Count, "not repeated while the condition holds");
            Assert.IsTrue(advisor.IsActive(AcademyAdvisor.TwrBelowOne));

            Assert.IsFalse(h.CanRevealStep, "solution only after the hints");
            while (h.RevealHint()) { }
            Assert.IsTrue(h.CanRevealStep);
            Assert.IsTrue(h.RevealStep());
            Assert.AreEqual(1, h.RevealedSteps);

            Assert.AreEqual(0, advisor.Evaluate(OnPad(1.2e6, 120000.0)).Count, "TWR 1.2: no lesson");
            Assert.IsFalse(advisor.IsActive(AcademyAdvisor.TwrBelowOne));
            s.Time = 5.0;
            Assert.AreEqual(0, advisor.Evaluate(s).Count, "cooldown");
            s.Time = 40.0;
            Assert.AreEqual(1, advisor.Evaluate(s).FindAll(x => x.Id == AcademyAdvisor.TwrBelowOne).Count, "again after the cooldown");
        }

        [Test]
        public void Advisor_ExplainsMissingOrbit_AndCelebratesOncePerFlight()
        {
            var advisor = new AcademyAdvisor();
            double R = 6.37e6, mu = 3.4e14;
            var coast = OnPad(0.0, 20000.0);
            coast.Landed = coast.OnLaunchPad = false;
            coast.CurrentStageHasFuel = true;
            coast.Bound = true;
            coast.ApoapsisRadius = R + 200000.0;
            coast.PeriapsisRadius = R - 500000.0;
            coast.Mu = mu;
            coast.Radius = R;
            coast.AvailableThrust = 4.0e5;
            coast.Mass = 20000.0;
            coast.Phase = FlightPhase.Spaceflight;
            AcademyHint ins = advisor.Evaluate(coast).Find(x => x.Id == AcademyAdvisor.NeedsInsertion);
            Assert.IsNotNull(ins, "suborbital arc above space → orbital insertion lesson");
            string dv = AcademyNumber.Format(OrbitalMechanics.CircularizationDeltaV(mu, R + 200000.0, R - 500000.0));
            Assert.IsTrue(ins.SolutionSteps.Exists(x => x.Contains(dv)), "solution uses the central insertion formula");
            Assert.IsFalse(ins.Message.Contains(dv), "the message does not give the answer away");

            // The same flight continues: the snapshot object is updated in place, as the runtime does.
            AcademySituation empty = coast;
            empty.CurrentStageHasFuel = false;
            empty.AnyPropellantLeft = false;
            empty.StageIndex = 1;
            empty.DeltaVExpended = 8000.0;
            empty.GravityLoss = 1200.0;
            empty.DragLoss = 300.0;
            AcademyHint outOfDv = advisor.Evaluate(empty).Find(x => x.Id == AcademyAdvisor.OutOfDeltaV);
            Assert.IsNotNull(outOfDv);
            StringAssert.Contains("не даёт достаточного Δv", outOfDv.Message);

            AcademySituation orbit = coast;
            orbit.PeriapsisRadius = R + 180000.0;
            orbit.Phase = FlightPhase.OrbitalFlight;
            Assert.IsNotNull(advisor.Evaluate(orbit).Find(x => x.Id == AcademyAdvisor.OrbitAchieved));
            orbit.Phase = FlightPhase.Spaceflight;
            advisor.Evaluate(orbit);
            orbit.Phase = FlightPhase.OrbitalFlight;
            orbit.Time = 1000.0;
            Assert.IsNull(advisor.Evaluate(orbit).Find(x => x.Id == AcademyAdvisor.OrbitAchieved), "once per flight");
            advisor.ResetFlight();
            orbit.Time = 2000.0;
            Assert.IsNotNull(advisor.Evaluate(orbit).Find(x => x.Id == AcademyAdvisor.OrbitAchieved), "new flight");
        }

        [Test]
        public void Advisor_AgreesWithTheSimulation_HeavyRocketStaysOnThePad()
        {
            CelestialBodyPreset home = HomeSystemCatalog.Bodies().Find(b => b.Id == HomeSystemCatalog.HomeWorldId);
            var context = AcademyContext.FromHomeSystem(home.Id);
            foreach (bool heavy in new[] { true, false })
            {
                var planet = new PlanetModel(home.DisplayName, home.Physical, ReferenceSphereSurface.Instance, Vector3d.Zero);
                var field = new GravityField();
                field.Add(planet.Gravity);
                var config = new RocketConfiguration();
                if (heavy) config.payloadMass += 0.6 * new RocketState(config).Mass;
                var sim = new FreeFlightSimulation(planet, field) { ContactRadius = 1.0 };
                sim.Rocket = new RocketState(config) { Throttle = 1.0, EngineOn = true };
                sim.PlaceOnSurface(PlanetRotation.DirectionFromLatLon(home.LaunchLatitude * MathD.DegToRad, home.LaunchLongitude * MathD.DegToRad), 0.0);
                for (int i = 0; i < 250; i++) sim.Step(0.02);

                var telemetry = new FlightTelemetry();
                FlightTelemetryCalculator.Compute(sim, telemetry);
                var s = new AcademySituation();
                s.Capture(sim, telemetry, sim.IsLanded ? FlightPhase.Ground : FlightPhase.AtmosphericAscent, sim.IsLanded, context);
                bool lesson = new AcademyAdvisor().Evaluate(s).Exists(h => h.Id == AcademyAdvisor.TwrBelowOne);
                Assert.AreEqual(heavy, sim.IsLanded, heavy ? "physics: TWR < 1 does not lift off" : "physics: the normal rocket flies");
                Assert.AreEqual(heavy, lesson, "the Academy explains exactly the case the physics produced");
            }
        }
    }
}
