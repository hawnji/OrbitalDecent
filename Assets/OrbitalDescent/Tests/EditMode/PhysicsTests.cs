using System;
using NUnit.Framework;
using OrbitalDescent.Core;

namespace OrbitalDescent.Tests
{
    /// <summary>Physics validation against analytical or tabulated reference cases.
    /// Tolerances are stated per test and documented in docs/PHYSICS.md.</summary>
    public class PhysicsTests
    {
        [TestCase(0.0, 1.2250, 288.15)]
        [TestCase(11000.0, 0.36391, 216.77)]
        [TestCase(20000.0, 0.088035, 216.65)]
        [TestCase(50000.0, 0.0010269, 270.65)]
        [TestCase(80000.0, 1.846e-5, 198.64)]
        public void Atmosphere_MatchesUSStandard1976(double h, double rho, double temp)
        {
            var s = Atmosphere.Sample(h);
            // Tabulated values are for geometric altitude; tolerance 1.5% density, 1 K temperature.
            Assert.AreEqual(rho, s.Density, rho * 0.015, "density");
            Assert.AreEqual(temp, s.Temperature, 1.0, "temperature");
        }

        [Test]
        public void Atmosphere_DensityDecreasesMonotonically()
        {
            double prev = double.MaxValue;
            for (double h = 0; h <= 140000; h += 250)
            {
                double d = Atmosphere.Sample(h).Density;
                Assert.Less(d, prev, $"at {h} m");
                prev = d;
            }
            Assert.AreEqual(2.2e-8, Atmosphere.Sample(120000).Density, 0.6e-8);
        }

        [Test]
        public void Gravity_IsInverseSquare()
        {
            double g0 = Planet.Gravity(Planet.PointAbove(0, 0, 0)).Length;
            Assert.AreEqual(Constants.GM / (Planet.R * Planet.R), g0, 1e-9);
            double g1 = Planet.Gravity(Planet.PointAbove(5000, 3000, Planet.R)).Length;
            Assert.AreEqual(g0 / 4.0, g1, 1e-9);
        }

        [Test]
        public void Altitude_StableFormulaMatchesDirect()
        {
            var rnd = new Random(3);
            for (int i = 0; i < 200; i++)
            {
                double x = (rnd.NextDouble() - 0.5) * 4e5, z = (rnd.NextDouble() - 0.5) * 4e5, h = rnd.NextDouble() * 1.5e5;
                Vec3d p = Planet.PointAbove(x, z, h);
                Assert.AreEqual(h, Planet.Altitude(p), 1e-4);
            }
        }

        [Test]
        public void Vacuum_CircularOrbitConservesEnergyAndRadius()
        {
            var world = TestUtil.CalmWorld();
            var m = new FlightModel(VehicleConfig.Dart, world);
            double r = Planet.R + 400000; // above the modelled atmosphere
            double v = Math.Sqrt(Constants.GM / r);
            var s = m.CreateState(Planet.Center + new Vec3d(0, r, 0), new Vec3d(0, 0, v));
            double e0 = 0.5 * v * v - Constants.GM / r;
            double period = 2 * Math.PI * Math.Sqrt(r * r * r / Constants.GM);
            double maxDev = 0;
            var input = new ControlInput();
            while (s.Time < period)
            {
                m.Step(s, input, ControlModifiers.Nominal, FlightModel.DefaultStep);
                maxDev = Math.Max(maxDev, Math.Abs(Planet.RadiusAt(s.Position) - r));
            }
            double e1 = 0.5 * s.Velocity.SqrLength - Constants.GM / Planet.RadiusAt(s.Position);
            Assert.Less(Math.Abs((e1 - e0) / e0), 1e-6, "specific orbital energy drift");
            Assert.Less(maxDev, 50.0, "radius deviation over one orbit (m)");
        }

        [Test]
        public void Vacuum_FreeFallMatchesAnalyticEnergy()
        {
            var world = TestUtil.CalmWorld();
            var m = new FlightModel(VehicleConfig.Hammer, world);
            double h0 = 600000;
            var s = m.CreateState(Planet.PointAbove(0, 0, h0), Vec3d.Zero);
            var input = new ControlInput();
            while (Planet.Altitude(s.Position) > 200000) m.Step(s, input, ControlModifiers.Nominal, FlightModel.DefaultStep);
            double r0 = Planet.R + h0, r1 = Planet.RadiusAt(s.Position);
            double expected = Math.Sqrt(2 * Constants.GM * (1 / r1 - 1 / r0));
            Assert.AreEqual(expected, s.Velocity.Length, expected * 2e-4);
        }

        [Test]
        public void TerminalVelocity_MatchesAnalytic()
        {
            var world = TestUtil.CalmWorld();
            var v = VehicleConfig.Hammer;
            var m = new FlightModel(v, world);
            var s = m.CreateState(Planet.PointAbove(0, 0, 2500), new Vec3d(0, -112, 0));
            var input = new ControlInput { Sas = true };
            while (Planet.Altitude(s.Position) > 1000) m.Step(s, input, ControlModifiers.Nominal, FlightModel.DefaultStep);
            var atm = Atmosphere.Sample(1000);
            double g = Planet.Gravity(s.Position).Length;
            double mach = s.Velocity.Length / atm.SpeedOfSound;
            double machFactor = 1.0 + 0.35 * Math.Exp(-Math.Pow((mach - 1.1) / 0.45, 2));
            double vt = Math.Sqrt(2 * m.Mass(s) * g / (atm.Density * v.AxialCoeff * machFactor * v.RefArea));
            // Still decelerating slightly toward the local terminal speed: 2% tolerance.
            Assert.AreEqual(vt, s.Velocity.Length, vt * 0.02);
        }

        [Test]
        public void Drag_OpposesAirRelativeMotion()
        {
            var lvl = TestUtil.CalmLevel();
            lvl.SurfaceWindSpeed = 20; lvl.SurfaceWindDirDeg = 90; // blowing toward +X (east)
            var world = TestUtil.WorldFor(lvl);
            var m = new FlightModel(VehicleConfig.Dart, world);
            var s = m.CreateState(Planet.PointAbove(0, 0, 3000), new Vec3d(0, -1, 0));
            for (int i = 0; i < 480; i++) m.Step(s, new ControlInput { Sas = true }, ControlModifiers.Nominal, FlightModel.DefaultStep);
            Assert.Greater(s.Velocity.x, 2.0, "the air mass drags a still probe downwind");
            Assert.Less(s.Velocity.x, 20.0, "but never faster than the wind");
        }

        [Test]
        public void TimeStep_Independence()
        {
            var world = TestUtil.CalmWorld();
            var a = new FlightModel(VehicleConfig.Dart, world) { Deterministic = true };
            var s1 = TestUtil.RunToGround(a, TestUtil.Entry(a, 120000, 1600, -28), null, 1.0 / 60.0);
            var s2 = TestUtil.RunToGround(a, TestUtil.Entry(a, 120000, 1600, -28), null, 1.0 / 240.0);
            double d = Planet.SurfaceDistance(s1.ContactPoint, s2.ContactPoint);
            Assert.Less(d, 60.0, "landing point difference (m) between 60 Hz and 240 Hz");
            Assert.AreEqual(s1.Time, s2.Time, 1.0);
        }

        [Test]
        public void Predictor_AgreesWithSimulation()
        {
            var world = TestUtil.CalmWorld();
            var m = new FlightModel(VehicleConfig.Phantom, world);
            var s = TestUtil.Entry(m, 120000, 1600, -28);
            var p = new Predictor(VehicleConfig.Phantom, world).RunToCompletion(s, true);
            var end = TestUtil.RunToGround(m, s.Clone());
            double d = Planet.SurfaceDistance(p.ImpactPoint, end.ContactPoint);
            Assert.IsTrue(p.Valid);
            Assert.Less(d, 120.0, "prediction error over a ~120 km descent (m)");
            Assert.AreEqual(end.Time, p.ImpactTime, 2.0);
        }

        [Test]
        public void Steering_LeaningShiftsImpactTowardLean()
        {
            var world = TestUtil.CalmWorld();
            var m = new FlightModel(VehicleConfig.Dart, world);
            var baseline = TestUtil.RunToGround(m, TestUtil.Entry(m, 120000, 1600, -28));
            // Lean east (+X) whenever the air is dense enough to matter.
            var steered = TestUtil.RunToGround(m, TestUtil.Entry(m, 120000, 1600, -28),
                (s, t) => new ControlInput { Sas = true, Steer = t.DynamicPressure > 300 ? Vec3d.UnitX.PerpendicularTo(s.BodyAxis).Normalized * 0.8 : Vec3d.Zero });
            TestUtil.Horizontal(baseline.ContactPoint, out double bx, out _);
            TestUtil.Horizontal(steered.ContactPoint, out double sx, out _);
            Assert.Greater(sx - bx, 1500.0, "lateral shift from leaning (m)");
        }

        [Test]
        public void Steering_InVacuumRotatesButBarelyMovesTrajectory()
        {
            var world = TestUtil.CalmWorld();
            var m = new FlightModel(VehicleConfig.Dart, world);
            var baseline = TestUtil.RunToGround(m, TestUtil.Entry(m, 120000, 1600, -28));
            double maxRate = 0;
            var steered = TestUtil.RunToGround(m, TestUtil.Entry(m, 120000, 1600, -28), (s, t) =>
            {
                maxRate = Math.Max(maxRate, s.AngularVelocity.Length);
                return new ControlInput { Sas = true, Steer = s.Time < 8 ? Vec3d.UnitX.PerpendicularTo(s.BodyAxis).Normalized : Vec3d.Zero };
            });
            Assert.Greater(maxRate, 0.3, "RCS rotates the probe in vacuum");
            double d = Planet.SurfaceDistance(baseline.ContactPoint, steered.ContactPoint);
            Assert.Less(d, 1200.0, "vacuum steering has little trajectory authority (m)");
        }

        [Test]
        public void Crosswind_LightProbeDriftsMoreThanHeavy()
        {
            var lvl = TestUtil.CalmLevel();
            lvl.SurfaceWindSpeed = 15; lvl.JetSpeed = 30; lvl.SurfaceWindDirDeg = 90; lvl.WindVeerDegPer10km = 0;
            var windy = TestUtil.WorldFor(lvl);
            var calm = TestUtil.CalmWorld();
            double Drift(VehicleConfig v)
            {
                var a = new FlightModel(v, calm); var b = new FlightModel(v, windy);
                var sa = TestUtil.RunToGround(a, TestUtil.Entry(a, 120000, 1600, -28));
                var sb = TestUtil.RunToGround(b, TestUtil.Entry(b, 120000, 1600, -28));
                TestUtil.Horizontal(sa.ContactPoint, out double xa, out _);
                TestUtil.Horizontal(sb.ContactPoint, out double xb, out _);
                return xb - xa;
            }
            double dart = Drift(VehicleConfig.Dart), hammer = Drift(VehicleConfig.Hammer);
            Assert.Greater(dart, 0, "drifts downwind");
            Assert.Greater(dart, hammer * 1.3, "lower ballistic coefficient drifts further");
        }

        [Test]
        public void Rotation_StabilizeRecoversFromTumble()
        {
            var world = TestUtil.CalmWorld();
            foreach (var v in VehicleConfig.All)
            {
                var m = new FlightModel(v, world);
                var s = m.CreateState(Planet.PointAbove(0, 0, 12000), new Vec3d(0, -250, 60));
                s.AngularVelocity = Vec3d.UnitX * 3.0;
                double t0 = s.Time;
                while (s.AngularVelocity.Length > 0.15 && s.Time - t0 < 20)
                    m.Step(s, new ControlInput { Stabilize = true }, ControlModifiers.Nominal, FlightModel.DefaultStep);
                Assert.Less(s.Time - t0, 12.0, $"{v.Name} recovers within 12 s");
            }
        }

        [Test]
        public void Rotation_LowStabilityAirframeOscillatesLonger()
        {
            var world = TestUtil.CalmWorld();
            double Settle(VehicleConfig v)
            {
                var m = new FlightModel(v, world);
                var s = m.CreateState(Planet.PointAbove(0, 0, 15000), new Vec3d(0, -300, 0));
                s.AngularVelocity = Vec3d.UnitX * 1.5;
                double last = 0;
                while (s.Time < 30)
                {
                    m.Step(s, new ControlInput(), ControlModifiers.Nominal, FlightModel.DefaultStep);
                    if (s.AngularVelocity.Length > 0.1) last = s.Time;
                }
                return last;
            }
            Assert.Greater(Settle(VehicleConfig.Wildcard), Settle(VehicleConfig.Dart));
        }

        [Test]
        public void Safeguard_AngularRateBounded()
        {
            var v = VehicleConfig.Dart.Clone();
            v.RcsTorque = 1e7;
            var m = new FlightModel(v, TestUtil.CalmWorld());
            var s = m.CreateState(Planet.PointAbove(0, 0, 130000), new Vec3d(0, -100, 0));
            for (int i = 0; i < 600; i++)
                m.Step(s, new ControlInput { Steer = Vec3d.UnitX.PerpendicularTo(s.BodyAxis).Normalized }, ControlModifiers.Nominal, FlightModel.DefaultStep);
            Assert.LessOrEqual(s.AngularVelocity.Length, FlightModel.MaxAngularRate + 1e-9);
            Assert.IsTrue(s.Attitude.IsFinite);
        }

        [Test]
        public void Safeguard_NonFiniteStateIsCaught()
        {
            var m = new FlightModel(VehicleConfig.Dart, TestUtil.CalmWorld());
            var s = m.CreateState(Planet.PointAbove(0, 0, 50000), new Vec3d(0, -100, 0));
            s.Velocity = new Vec3d(double.NaN, 0, 0);
            m.Step(s, new ControlInput(), ControlModifiers.Nominal, FlightModel.DefaultStep);
            Assert.IsTrue(s.NumericalFault);
            Assert.IsTrue(s.Position.IsFinite && s.Velocity.IsFinite);
        }

        [Test]
        public void Contact_NoTunnelingAtHypersonicSpeed()
        {
            var lvl = TestUtil.CalmLevel();
            lvl.ZoneElevation = 400; lvl.MountainHeight = 1200; lvl.ValleyRadius = 500; lvl.MountainScale = 3000;
            var world = TestUtil.WorldFor(lvl);
            var m = new FlightModel(VehicleConfig.Hammer, world) { Deterministic = true }; // no structural break-up
            var s = m.CreateState(Planet.PointAbove(1500, 800, 2500), new Vec3d(400, -2500, 300));
            TestUtil.RunToGround(m, s, (st, t) => new ControlInput(), FlightModel.DefaultStep, 10);
            Assert.IsTrue(s.Contact);
            TestUtil.Horizontal(s.ContactPoint, out double x, out double z);
            Assert.AreEqual(world.Terrain.Height(x, z), Planet.Altitude(s.ContactPoint), 0.5);
            Assert.AreEqual(ContactResult.Crashed, s.ContactKind);
        }

        [Test]
        public void Contact_SoftTouchdownIsLanding()
        {
            var m = new FlightModel(VehicleConfig.Dart, TestUtil.CalmWorld());
            var s = m.CreateState(Planet.PointAbove(0, 0, 0.3), new Vec3d(0, -3, 0));
            TestUtil.RunToGround(m, s, null, FlightModel.DefaultStep, 10);
            Assert.AreEqual(ContactResult.Landed, s.ContactKind);
            Assert.AreEqual(Math.Sqrt(9 + 2 * Constants.G0 * 0.3), s.ContactSpeed, 0.1);
        }

        [Test]
        public void Chute_InterlockAndDescentRate()
        {
            var v = VehicleConfig.Dart;
            var m = new FlightModel(v, TestUtil.CalmWorld());
            var high = m.CreateState(Planet.PointAbove(0, 0, 20000), new Vec3d(0, -200, 0));
            Assert.IsFalse(m.DeployChute(high, m.Measure(high)), "too high");
            var fast = m.CreateState(Planet.PointAbove(0, 0, 5000), new Vec3d(0, -400, 0));
            Assert.IsFalse(m.DeployChute(fast, m.Measure(fast)), "too fast");
            var ok = m.CreateState(Planet.PointAbove(0, 0, 3000), new Vec3d(0, -60, 0));
            Assert.IsTrue(m.DeployChute(ok, m.Measure(ok)));
            while (Planet.Altitude(ok.Position) > 1000) m.Step(ok, new ControlInput { Sas = true }, ControlModifiers.Nominal, FlightModel.DefaultStep);
            var atm = Atmosphere.Sample(1000);
            double expected = Math.Sqrt(2 * m.Mass(ok) * Constants.G0 / (atm.Density * (v.ChuteCdA + v.AxialCoeff * v.RefArea)));
            Assert.AreEqual(expected, ok.Velocity.Length, expected * 0.05);
        }

        [Test]
        public void Engine_ThrustDecelerates()
        {
            var m = new FlightModel(VehicleConfig.Dart, TestUtil.CalmWorld());
            var s = m.CreateState(Planet.PointAbove(0, 0, 2000), new Vec3d(0, -60, 0));
            double fuel0 = s.Fuel;
            for (int i = 0; i < 240; i++) m.Step(s, new ControlInput { Sas = true, Throttle = 1 }, ControlModifiers.Nominal, FlightModel.DefaultStep);
            Assert.Less(s.Velocity.Length, 45.0);
            Assert.Less(s.Fuel, fuel0);
        }

        [Test]
        public void Heating_GrowsWithSpeedAndDensity()
        {
            var m = new FlightModel(VehicleConfig.Dart, TestUtil.CalmWorld());
            var slow = m.Measure(m.CreateState(Planet.PointAbove(0, 0, 50000), new Vec3d(0, -1000, 0)));
            var fast = m.Measure(m.CreateState(Planet.PointAbove(0, 0, 50000), new Vec3d(0, -2000, 0)));
            var dense = m.Measure(m.CreateState(Planet.PointAbove(0, 0, 40000), new Vec3d(0, -2000, 0)));
            Assert.AreEqual(8.0, fast.HeatFlux / slow.HeatFlux, 0.01, "q ~ V^3");
            Assert.Greater(dense.HeatFlux, fast.HeatFlux);
        }

        [Test]
        public void FixedStep_FrameRateIndependentMission()
        {
            Vec3d Run(double frame)
            {
                var lvl = Levels.All[0];
                var ms = new MissionSession(lvl, VehicleConfig.Dart, TestUtil.Shape, TestUtil.Coverage);
                ms.Release();
                int frames = (int)Math.Round(60.0 / frame);
                for (int i = 0; i < frames; i++) ms.Tick(frame, new ControlInput { Sas = true });
                return ms.State.Position;
            }
            Vec3d a = Run(1.0 / 30.0), b = Run(1.0 / 144.0);
            Assert.Less((a - b).Length, 20.0, "positions after 60 s at 30 fps vs 144 fps differ by at most one fixed step (m)");
        }
    }
}
