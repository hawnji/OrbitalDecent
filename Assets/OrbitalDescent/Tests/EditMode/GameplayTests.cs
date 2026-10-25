using System;
using NUnit.Framework;
using OrbitalDescent.Core;

namespace OrbitalDescent.Tests
{
    public class GameplayTests
    {
        static HazardContext Ctx(MissionSession m) => new HazardContext
        {
            State = m.State, Telemetry = m.Model.Measure(m.State), World = m.World, Vehicle = m.Vehicle,
            Prediction = m.Predictor.Latest, Dt = FlightModel.DefaultStep,
        };

        [Test]
        public void Release_NominalLandsAtAimBias()
        {
            foreach (var lvl in Levels.All)
            {
                var m = new MissionSession(lvl, VehicleConfig.Dart, TestUtil.Shape, TestUtil.Coverage);
                var p = m.SetAim(0, 0);
                Assert.IsTrue(p.Valid, lvl.Name);
                double bias = Math.Sqrt(lvl.AimBiasCross * lvl.AimBiasCross + lvl.AimBiasDown * lvl.AimBiasDown);
                Assert.AreEqual(bias, p.ErrorToZone, 60.0, $"{lvl.Name}: un-steered miss equals the designed bias");
            }
        }

        [Test]
        public void Release_AimChangesPredictionAndIsClamped()
        {
            var m = new MissionSession(Levels.All[0], VehicleConfig.Dart, TestUtil.Shape, TestUtil.Coverage);
            var p0 = m.SetAim(0, 0); var e0 = new Vec3d(p0.ErrorEast, 0, p0.ErrorNorth);
            var p1 = m.SetAim(3, 0); var e1 = new Vec3d(p1.ErrorEast, 0, p1.ErrorNorth);
            Assert.Greater((e1 - e0).Length, 1000.0, "3 degrees of heading moves the touchdown by kilometres");
            m.SetAim(90, -90);
            Assert.AreEqual(Levels.All[0].AimHeadingRangeDeg, m.AimHeadingDeg, 1e-9);
            Assert.AreEqual(-Levels.All[0].AimFlightPathRangeDeg, m.AimFlightPathDeg, 1e-9);
        }

        [Test]
        public void NoInput_DescentFinishesWithResult()
        {
            var m = new MissionSession(Levels.All[0], VehicleConfig.Dart, TestUtil.Shape, TestUtil.Coverage);
            m.Release();
            int guard = 0;
            while (m.Phase == MissionPhase.Descent && guard++ < 400000) m.StepFixed(new ControlInput());
            Assert.AreEqual(MissionPhase.Finished, m.Phase);
            Assert.IsNotNull(m.Result);
            Assert.AreEqual(MissionOutcome.Crashed, m.Result.Outcome, "no chute, no engine: terminal-velocity impact");
            Assert.AreEqual(0, m.Result.Stars);
            Assert.IsFalse(m.State.NumericalFault);
        }

        [Test]
        public void Lightning_BuildsStrikesAndRebootRecovers()
        {
            var lvl = Levels.All[3]; // Storm Front
            var m = new MissionSession(lvl, VehicleConfig.Dart, TestUtil.Shape, TestUtil.Coverage);
            m.Release();
            var cell = lvl.Storms[0];
            // Park the probe inside the storm core at 6 km, drifting slowly.
            m.State.Position = Planet.PointAbove(cell.X, cell.Z, 6000);
            m.State.Velocity = new Vec3d(0, -60, 0);
            var lightning = m.Hazards.Lightning;
            int steps = 0;
            bool warned = false;
            while (lightning.Outage <= 0 && steps++ < 120 * 60)
            {
                m.StepFixed(new ControlInput { Sas = true });
                if (lightning.Status.Level >= HazardLevel.Caution && lightning.Status.Message.StartsWith("STATIC")) warned = true;
                m.State.Position = Planet.PointAbove(cell.X, cell.Z, 6000);
            }
            Assert.IsTrue(warned, "static buildup warning precedes the strike");
            Assert.Greater(lightning.Outage, 0, "strike happened");
            Assert.IsTrue(m.Hazards.Effects.Controls.FinsFrozen);
            Assert.IsFalse(m.Hazards.Effects.Controls.SasAvailable);
            Assert.AreEqual(1, m.Record.Strikes);
            // Reboot: outage is capped to the reboot time and controls come back.
            m.RebootRequested = true;
            m.StepFixed(new ControlInput());
            Assert.LessOrEqual(lightning.Outage, LightningHazard.RebootTime);
            for (int i = 0; i < 200; i++) { m.State.Position = Planet.PointAbove(0, -30000, 6000); m.StepFixed(new ControlInput()); }
            Assert.AreEqual(0, lightning.Outage, 1e-9);
            Assert.IsFalse(m.Hazards.Effects.Controls.FinsFrozen);
            Assert.AreEqual(1.0, m.Hazards.Effects.Controls.Authority, 1e-9);
        }

        [Test]
        public void EmInterference_DegradesNavAndDelaysInput_ThenRecovers()
        {
            var lvl = Levels.All[4]; // Highlands: 35-60 km band
            var m = new MissionSession(lvl, VehicleConfig.Dart, TestUtil.Shape, TestUtil.Coverage);
            m.Release();
            m.State.Position = Planet.PointAbove(0, -20000, 48000);
            m.State.Velocity = new Vec3d(0, -10, 0);
            for (int i = 0; i < 120 * 8; i++) { m.State.Position = Planet.PointAbove(0, -20000, 48000); m.StepFixed(new ControlInput()); }
            Assert.Greater(m.Hazards.Effects.NavNoise, 300.0);
            Assert.Greater(m.Hazards.Effects.InputDelay, 0.1);
            Assert.Greater(m.Nav.Uncertainty, 200.0);
            for (int i = 0; i < 120 * 20; i++) { m.State.Position = Planet.PointAbove(0, -20000, 20000); m.StepFixed(new ControlInput()); }
            Assert.Less(m.Hazards.Effects.NavNoise, 5.0);
            Assert.AreEqual(0, m.Hazards.Effects.InputDelay, 0.01);
        }

        [Test]
        public void InputDelay_ShiftsSteeringInTime()
        {
            var d = new InputDelayLine();
            var on = new ControlInput { Steer = Vec3d.UnitX };
            Assert.AreEqual(0, d.Process(on, 0.00, 0.3).Steer.x, 1e-9);
            Assert.AreEqual(0, d.Process(on, 0.20, 0.3).Steer.x, 1e-9);
            Assert.AreEqual(1, d.Process(on, 0.31, 0.3).Steer.x, 1e-9);
        }

        [Test]
        public void PlasmaBlackout_FromPhysicalHeatFlux()
        {
            var lvl = Levels.All[5];
            var m = new MissionSession(lvl, VehicleConfig.Phantom, TestUtil.Shape, TestUtil.Coverage);
            m.Release();
            bool blackout = false, recovered = false;
            while (m.Phase == MissionPhase.Descent && m.Telemetry.Altitude > 15000)
            {
                m.StepFixed(new ControlInput { Sas = true });
                if (!m.Hazards.Effects.NavAvailable) blackout = true;
                if (blackout && m.Hazards.Effects.NavAvailable) recovered = true;
            }
            Assert.IsTrue(blackout, "steep fast entry produces a blackout");
            Assert.IsTrue(recovered, "navigation returns once heating subsides");
        }

        [Test]
        public void WindShear_IsASpatialGradient()
        {
            var lvl = Levels.All[1];
            var w = TestUtil.WorldFor(lvl);
            var s = lvl.Shear[0];
            Vec3d above = w.Wind.MeanWind(0, 0, s.Altitude + s.Thickness * 4);
            Vec3d below = w.Wind.MeanWind(0, 0, s.Altitude - s.Thickness * 4);
            // Removes the smooth background change across the same height band.
            double bgE = Math.Abs(w.Wind.MeanWind(0, 0, s.Altitude + 6000).x - w.Wind.MeanWind(0, 0, s.Altitude + 6000 - s.Thickness * 8).x);
            Assert.AreEqual(s.DeltaEast, below.x - above.x, Math.Abs(s.DeltaEast) * 0.05 + bgE + 1.0);
        }

        [Test]
        public void Turbulence_IsBounded()
        {
            double max = 0;
            var rnd = new Random(1);
            for (int i = 0; i < 20000; i++)
            {
                var p = new Vec3d(rnd.NextDouble() * 1e5, rnd.NextDouble() * 1e4, rnd.NextDouble() * 1e5);
                max = Math.Max(max, WindField.TurbulenceVelocity(p, rnd.NextDouble() * 1000, 1.0, 3000).Length);
            }
            Assert.LessOrEqual(max, 9.0 * Math.Sqrt(1 + 0.36 + 1) + 1e-9);
            Assert.Greater(max, 5.0);
        }

        [Test]
        public void Icing_AccretesInSupercooledCloudAndHeaterClears()
        {
            var lvl = TestUtil.CalmLevel();
            lvl.Clouds = new[] { new CloudLayer { Type = CloudType.Stratus, Base = 2000, Top = 5000, Coverage = 1.0, Density = 1.0, ShapeScale = 3000 } };
            var w = TestUtil.WorldFor(lvl);
            var m = new FlightModel(VehicleConfig.Dart, w);
            var s = m.CreateState(Planet.PointAbove(0, 0, 3800), new Vec3d(0, -80, 0));
            bool sawIcing = false;
            for (int i = 0; i < 120 * 40; i++)
            {
                s.Position = Planet.PointAbove(0, 0, 3800); s.Velocity = new Vec3d(0, -80, 0);
                var t = m.Step(s, new ControlInput { Sas = true }, ControlModifiers.Nominal, FlightModel.DefaultStep);
                sawIcing |= t.IcingConditions;
            }
            Assert.IsTrue(sawIcing);
            Assert.Greater(s.Ice, 0.05, "ice accreted (kg)");
            double ice = s.Ice;
            for (int i = 0; i < 120 * 10; i++)
            {
                s.Position = Planet.PointAbove(0, 0, 3800); s.Velocity = new Vec3d(0, -80, 0);
                m.Step(s, new ControlInput { Sas = true, Heater = true }, ControlModifiers.Nominal, FlightModel.DefaultStep);
            }
            Assert.Less(s.Ice, ice * 0.5);
        }

        [Test]
        public void CloudDensity_MatchesTextureSampling()
        {
            var shape = TestUtil.Shape;
            int n = shape.Size;
            // Texel centres return the stored byte exactly; sampling wraps.
            Assert.AreEqual(shape.Data[3 + n * (5 + n * 7)] / 255.0, shape.Sample(3.5 / n, 5.5 / n, 7.5 / n), 1e-9);
            Assert.AreEqual(shape.Sample(0.2, 0.3, 0.4), shape.Sample(1.2, -0.7, 2.4), 1e-9);
        }

        [Test]
        public void Scoring_StarsFollowRules()
        {
            var lvl = Levels.All[0];
            var v = VehicleConfig.Dart;
            var zone = Planet.PointAbove(0, 0, lvl.ZoneElevation);
            ScoreResult Score(double err, double speed, double fuelFrac, ContactResult kind = ContactResult.Landed)
            {
                var s = new FlightState { Contact = true, ContactKind = kind, ContactSpeed = speed, Fuel = v.FuelMass * fuelFrac, Integrity = 1 };
                s.ContactPoint = Planet.PointAbove(err, 0, lvl.ZoneElevation);
                return ScoreCalculator.Evaluate(lvl, v, s, new FlightRecord { FlightTime = 200 }, zone);
            }
            Assert.AreEqual(3, Score(30, 2, 0.5).Stars);
            Assert.AreEqual(2, Score(30, 2, 0.1).Stars, "objective (fuel) not met");
            Assert.AreEqual(2, Score(300, 2, 0.5).Stars);
            Assert.AreEqual(1, Score(300, 7, 0.5).Stars, "too hard for 2 stars");
            Assert.AreEqual(1, Score(3000, 2, 0.5).Stars);
            Assert.AreEqual(0, Score(6000, 2, 0.5).Stars, "outside completion radius");
            Assert.AreEqual(0, Score(10, 20, 0.5, ContactResult.Crashed).Stars);
            Assert.Greater(Score(10, 2, 0.5).Total, Score(200, 2, 0.5).Total, "closer scores higher");
        }

        [Test]
        public void Progression_UnlocksAndPersists()
        {
            var store = new MemorySaveStore();
            var p = new Progression(store, Levels.All.Count);
            Assert.IsTrue(p.IsLevelUnlocked(0));
            Assert.IsFalse(p.IsLevelUnlocked(1));
            Assert.IsFalse(p.IsVehicleUnlocked("hammer"));
            p.RecordResult(0, new ScoreResult { Success = true, Stars = 2, ErrorMeters = 120 });
            p.RecordResult(0, new ScoreResult { Success = true, Stars = 1, ErrorMeters = 900 });
            Assert.AreEqual(2, p.Levels[0].BestStars, "a worse run never lowers the best");
            Assert.IsTrue(p.IsLevelUnlocked(1));
            Assert.IsTrue(p.IsVehicleUnlocked("hammer"));
            var reloaded = new Progression(store, Levels.All.Count);
            Assert.AreEqual(2, reloaded.Levels[0].BestStars);
            Assert.AreEqual(120, reloaded.Levels[0].BestError, 1e-9);
            Assert.AreEqual(2, reloaded.Levels[0].Attempts);
            store.Save("od.progress.v1", "v1|garbage");
            var corrupt = new Progression(store, Levels.All.Count);
            Assert.AreEqual(0, corrupt.TotalStars);
        }

        [Test]
        public void Retry_FreshSessionIsIdentical()
        {
            var a = new MissionSession(Levels.All[1], VehicleConfig.Dart, TestUtil.Shape, TestUtil.Coverage);
            var b = new MissionSession(Levels.All[1], VehicleConfig.Dart, TestUtil.Shape, TestUtil.Coverage);
            a.Release(); b.Release();
            for (int i = 0; i < 2000; i++) { a.StepFixed(new ControlInput { Sas = true }); b.StepFixed(new ControlInput { Sas = true }); }
            Assert.AreEqual(0, (a.State.Position - b.State.Position).Length, 1e-9, "deterministic retries");
        }

        [Test]
        public void Autopilot_CompletesEveryLevel()
        {
            foreach (var lvl in Levels.All)
            {
                var v = VehicleConfig.ById(lvl.RecommendedVehicle);
                var m = new MissionSession(lvl, v, TestUtil.Shape, TestUtil.Coverage);
                m.Release();
                var ap = new Autopilot();
                int guard = 0;
                while (m.Phase == MissionPhase.Descent && guard++ < 120 * 1500)
                    m.Tick(1.0 / 60.0, ap.Update(m), 1.0, 64);
                Assert.AreEqual(MissionPhase.Finished, m.Phase, lvl.Name);
                TestContext.WriteLine($"{lvl.Name} ({v.Name}): {m.Result.Outcome} err {m.Result.ErrorMeters:F0} m, {m.Result.TouchdownSpeed:F1} m/s, {m.Result.Stars} stars, {m.Result.Total} pts, t {m.State.Time:F0}s, fuel {m.State.Fuel:F1}, strikes {m.Record.Strikes}");
                Assert.IsTrue(m.Result.Success, $"{lvl.Name}: {m.Result.Outcome} {m.Result.FailureReason} err {m.Result.ErrorMeters:F0} speed {m.Result.TouchdownSpeed:F1}");
            }
        }
    }
}
