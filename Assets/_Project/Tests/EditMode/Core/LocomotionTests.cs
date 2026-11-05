using System;
using NUnit.Framework;
using SpaceSim.Exploration;
using SpaceSim.Mathematics;
using SpaceSim.Planets;

namespace SpaceSim.Tests
{
    /// <summary>Sprint on foot in different gravity: speed limits, the anaerobic reserve, gait, and the walker using them.</summary>
    public class LocomotionTests
    {
        static CelestialBodyPreset Preset(string id) => HomeSystemCatalog.Bodies().Find(b => b.Id == id);
        static double G(string id) => Preset(id).Physical.SurfaceGravity;

        static SurfaceWalker Walker(string bodyId)
        {
            CelestialBodyPreset b = Preset(bodyId);
            var w = new SurfaceWalker(b.Physical.GravitationalParameter, b.Physical.meanRadius, ReferenceSphereSurface.Instance, new Vector3d(1, 0, 0));
            w.PlaceAt(w.Anchor);
            return w;
        }

        // ---- Level A ------------------------------------------------------------------------------------------------

        [Test]
        public void SprintSpeed_FroudeCapOnSmallWorlds_LegForceOnHeavyOnes_NoneWhenTooHeavy()
        {
            double home = G(HomeSystemCatalog.HomeWorldId), teonan = G(HomeSystemCatalog.TeonanId), mora = G(HomeSystemCatalog.HermeousMoraId);

            // Home (8.27 m/s²): the legs out-push the suited weight k = 1.63 times → Weyand v = L_c(2k − 1)/t_sw ≈ 4.5 m/s.
            double k = Locomotion.ForceRatio(home);
            Assert.AreEqual(Locomotion.LegForce * Locomotion.BodyMass * 9.80665 / (Locomotion.TotalMass * home), k, 1e-12);
            Assert.AreEqual(SprintLimit.Strength, Locomotion.SprintLimitedBy(home));
            Assert.AreEqual(Locomotion.ContactLength * (2.0 * k - 1.0) / Locomotion.SwingTime, Locomotion.SprintSpeed(home), 1e-12);
            Assert.AreEqual(4.52, Locomotion.SprintSpeed(home), 0.02);

            // Teonan (2.3 m/s²): plenty of force, but each push-off becomes a long flight — the Froude cap √(3·g·L).
            Assert.AreEqual(SprintLimit.Froude, Locomotion.SprintLimitedBy(teonan));
            Assert.AreEqual(Math.Sqrt(Locomotion.SprintFroude * teonan * Locomotion.LegLength), Locomotion.SprintSpeed(teonan), 1e-12);
            Assert.Less(Locomotion.SprintSpeed(teonan), Locomotion.SprintSpeed(home), "a small world is slower to run on");

            // Calibration: the Apollo lunar lope was ≈ 2 m/s at 1.62 m/s².
            Assert.AreEqual(2.09, Locomotion.SprintSpeed(1.62), 0.01);

            // Hermeous Mora (≈ 14.5 m/s²): k < 1 — the legs cannot lift 177 kg of weight into a flight phase: walking only.
            Assert.Less(Locomotion.ForceRatio(mora), 1.0);
            Assert.IsFalse(Locomotion.CanRun(mora));
            Assert.AreEqual(SprintLimit.Weight, Locomotion.SprintLimitedBy(mora));
            Assert.AreEqual(Locomotion.WalkSpeed(mora), Locomotion.SprintSpeed(mora), 1e-12);

            // Strongest gravity first raises the top speed (Fr cap), then lowers it (leg force): a peak in between.
            Assert.Less(Locomotion.SprintSpeed(13.0), Locomotion.SprintSpeed(home));
        }

        [Test]
        public void Effort_ScalesWithWeight_SoASmallWorldSprintNeverTires()
        {
            double home = G(HomeSystemCatalog.HomeWorldId), teonan = G(HomeSystemCatalog.TeonanId);
            Assert.AreEqual(Locomotion.TotalMass * home / 9.80665, Locomotion.EffectiveLoad(home), 1e-12);
            double pHome = Locomotion.MetabolicPower(Locomotion.SprintSpeed(home), home);
            Assert.AreEqual(Locomotion.RestPower + Locomotion.RunCost * Locomotion.EffectiveLoad(home) * Locomotion.SprintSpeed(home), pHome, 1e-9);
            Assert.Greater(pHome, Locomotion.CriticalPower);
            Assert.AreEqual(Locomotion.AnaerobicCapacity / (pHome - Locomotion.CriticalPower), Locomotion.SprintEndurance(home), 1e-9);
            Assert.AreEqual(38.8, Locomotion.SprintEndurance(home), 0.5, "≈ 40 s of sprint at home");

            Assert.Less(Locomotion.MetabolicPower(Locomotion.SprintSpeed(teonan), teonan), Locomotion.CriticalPower);
            Assert.IsTrue(double.IsPositiveInfinity(Locomotion.SprintEndurance(teonan)), "Apollo-like: loping costs less than the aerobic limit");

            // The fastest jog that does not touch the reserve: P(v) = CP.
            double vSustain = Locomotion.SustainableSpeed(home, true);
            Assert.AreEqual(Locomotion.CriticalPower, Locomotion.RestPower + Locomotion.RunCost * Locomotion.EffectiveLoad(home) * vSustain, 1e-9);
            Assert.Less(Locomotion.MetabolicPower(Locomotion.WalkSpeed(home), home), Locomotion.CriticalPower, "walking always refills the reserve here");
        }

        [Test]
        public void WorkCapacity_DrainsAboveCriticalPower_RefillsBelow_ExactlyPerStep()
        {
            var w = new WorkCapacity();
            double t = 0.0;
            while (!w.Exhausted && t < 100.0)
            {
                w.Step(Locomotion.CriticalPower + 1000.0, 0.01);
                t += 0.01;
            }
            Assert.AreEqual(Locomotion.AnaerobicCapacity / 1000.0, t, 0.011, "W′/(P − CP)");
            Assert.AreEqual(0.0, w.Balance, 1e-12);

            // Recovery below CP follows dW′/dt = (CP − P)(W′₀ − W′)/W′₀; exhaustion clears at 30 %.
            double tRecover = Locomotion.RecoveryTime(Locomotion.RecoveredFraction, Locomotion.RestPower);
            w.Step(Locomotion.RestPower, tRecover - 0.05);
            Assert.IsTrue(w.Exhausted, "not yet");
            w.Step(Locomotion.RestPower, 0.1);
            Assert.IsFalse(w.Exhausted);
            Assert.AreEqual(Locomotion.RecoveredFraction, w.Fraction, 0.002);

            // The exact exponential step does not depend on the frame rate.
            var coarse = new WorkCapacity();
            var fine = new WorkCapacity();
            coarse.Step(2000.0, 10.0);
            fine.Step(2000.0, 10.0);
            coarse.Step(300.0, 20.0);
            for (int i = 0; i < 2000; i++) fine.Step(300.0, 0.01);
            Assert.AreEqual(coarse.Balance, fine.Balance, 1e-6);
        }

        [Test]
        public void Gait_SwitchesToRunningAtFroudeHalf_AndCadenceIsDynamicallySimilar()
        {
            SurfaceWalker w = Walker(HomeSystemCatalog.HomeWorldId);
            double switchSpeed = double.NaN;
            for (int i = 0; i < 300 && double.IsNaN(switchSpeed); i++)
            {
                w.Step(0.01, 1.0, 0.0, 0.0, true, false);
                if (w.Gait == Gait.Running) switchSpeed = w.HorizontalSpeed;
            }
            Assert.AreEqual(Locomotion.TransitionSpeed(w.Gravity), switchSpeed, Locomotion.MaxAcceleration(w.Gravity) * 0.01 + 1e-9, "walk → run at Fr = 0.5");

            Assert.AreEqual(1.8, Locomotion.StepFrequency(1.5, 9.80665), 0.1, "Earth walking cadence");
            Assert.AreEqual(2.6, Locomotion.StepFrequency(3.0, 9.80665), 0.15, "Earth running cadence");
            // Same Fr on two worlds → same f·√(L/g): a moon walk is the Earth walk in slow motion.
            double g1 = 9.80665, g2 = 1.62, fr = 1.2;
            double v1 = Locomotion.FroudeSpeed(fr, g1), v2 = Locomotion.FroudeSpeed(fr, g2);
            Assert.AreEqual(Locomotion.StepFrequency(v1, g1) * Math.Sqrt(Locomotion.LegLength / g1),
                Locomotion.StepFrequency(v2, g2) * Math.Sqrt(Locomotion.LegLength / g2), 1e-12);
            Assert.Less(Locomotion.StepFrequency(v2, g2), Locomotion.StepFrequency(v1, g1));

            // Leaning into the start: tan θ = a/g — the boots' limit μ·g gives the same angle everywhere.
            Assert.AreEqual(Math.Atan(Locomotion.Friction), Locomotion.LeanAngle(Locomotion.MaxAcceleration(2.0), 2.0), 1e-12);
        }

        // ---- Level B: the walker ------------------------------------------------------------------------------------

        [Test]
        public void HomeSprint_TiresAfterTheFormulaTime_ThenWalks_ThenSprintsAgain()
        {
            SurfaceWalker w = Walker(HomeSystemCatalog.HomeWorldId);
            double g = w.Gravity, t = 0.0;
            while (!w.Stamina.Exhausted && t < 120.0)
            {
                w.Step(0.01, 1.0, 0.0, 0.0, true, false);
                t += 0.01;
            }
            double endurance = Locomotion.SprintEndurance(g), spinUp = w.SprintSpeed / Locomotion.MaxAcceleration(g);
            Assert.IsTrue(w.Stamina.Exhausted);
            Assert.That(t, Is.InRange(endurance, endurance + spinUp), "W′/(P − CP) after the start-up phase");
            Assert.AreEqual(w.SprintSpeed, w.HorizontalSpeed, 1e-6, "at top speed until the reserve ran out");

            // Still holding Shift: the walker slows to a walk and the reserve refills at P_walk < CP.
            for (int i = 0; i < 300; i++) w.Step(0.01, 1.0, 0.0, 0.0, true, false);
            Assert.IsFalse(w.Sprinting);
            StringAssert.Contains("нет сил", w.SprintBlocked);
            Assert.AreEqual(w.WalkSpeed, w.HorizontalSpeed, 1e-6);
            double pWalk = Locomotion.MetabolicPower(w.WalkSpeed, g);
            Assert.AreEqual(pWalk, w.MetabolicPower, 1e-9);
            double back = Locomotion.RecoveryTime(Locomotion.RecoveredFraction, pWalk);
            int steps = 0;
            while (w.Stamina.Exhausted && steps < 100000)
            {
                w.Step(0.01, 1.0, 0.0, 0.0, true, false);
                steps++;
            }
            Assert.AreEqual(back, 3.0 + steps * 0.01, 0.6, "recovery time −W′/(CP − P)·ln(1 − 0.3), counted from exhaustion");
            w.Step(0.01, 1.0, 0.0, 0.0, true, false);
            Assert.IsTrue(w.Sprinting, "sprints again");
        }

        [Test]
        public void SmallWorldSprint_IsSlowToStart_AndNeverTires()
        {
            SurfaceWalker home = Walker(HomeSystemCatalog.HomeWorldId), teonan = Walker(HomeSystemCatalog.TeonanId);
            double SpinUp(SurfaceWalker w)
            {
                double t = 0.0;
                while (w.HorizontalSpeed < 0.95 * w.SprintSpeed && t < 30.0)
                {
                    w.Step(0.001, 1.0, 0.0, 0.0, true, false);
                    t += 0.001;
                }
                return t;
            }
            double tHome = SpinUp(home), tTeonan = SpinUp(teonan);
            Assert.AreEqual(0.95 * home.SprintSpeed / Locomotion.MaxAcceleration(home.Gravity), tHome, 0.002, "t = v/(μg)");
            Assert.AreEqual(0.95 * teonan.SprintSpeed / Locomotion.MaxAcceleration(teonan.Gravity), tTeonan, 0.002);
            Assert.Greater(tTeonan, tHome, "weak grip: a long run-up on a small world");
            Assert.AreEqual(Locomotion.LeanAngle(Locomotion.MaxAcceleration(teonan.Gravity), teonan.Gravity),
                Locomotion.LeanAngle((teonan.Acceleration).Magnitude, teonan.Gravity), 1e-9, "full lean while speeding up");

            for (int i = 0; i < 9000; i++) teonan.Step(0.01, 1.0, 0.0, 0.0, true, false); // 90 s ≈ 225 m, inside the tether
            Assert.IsTrue(teonan.Sprinting);
            Assert.IsFalse(teonan.Stamina.Exhausted);
            Assert.AreEqual(1.0, teonan.Stamina.Fraction, 1e-9, "a lope below critical power costs no reserve");
            Assert.AreEqual(teonan.SprintSpeed, teonan.HorizontalSpeed, 1e-6);
            Assert.AreEqual(Gait.Running, teonan.Gait);
        }

        [Test]
        public void HeavyWorld_RefusesTheSprint_WithTheReason()
        {
            SurfaceWalker mora = Walker(HomeSystemCatalog.HermeousMoraId);
            for (int i = 0; i < 500; i++) mora.Step(0.01, 1.0, 0.0, 0.0, true, false);
            Assert.IsFalse(mora.CanSprint);
            Assert.IsFalse(mora.Sprinting);
            StringAssert.Contains("бег невозможен", mora.SprintBlocked);
            Assert.AreEqual(mora.WalkSpeed, mora.HorizontalSpeed, 1e-6);
            Assert.AreEqual(Gait.Walking, mora.Gait);
        }

        [Test]
        public void Jump_RestsTheMuscles_AndTouchdownIsRecorded()
        {
            SurfaceWalker w = Walker(HomeSystemCatalog.TeonanId);
            w.Step(0.001, 0, 0, 0, false, true);
            Assert.AreEqual(Gait.Airborne, w.Gait);
            Assert.AreEqual(Locomotion.RestPower, w.MetabolicPower, 1e-12);
            int before = w.Touchdowns;
            for (int i = 0; i < 5000 && !w.OnGround; i++) w.Step(0.001, 0, 0, 0, false, false);
            Assert.AreEqual(before + 1, w.Touchdowns);
            Assert.AreEqual(SurfaceWalker.JumpSpeed, w.LastTouchdownSpeed, 0.01, "lands as fast as it took off");
        }
    }
}
