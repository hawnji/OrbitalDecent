using System;
using OrbitalDescent.Core;

namespace OrbitalDescent.Tests
{
    static class TestUtil
    {
        static NoiseVolume shape;
        static NoiseMap coverage;

        public static NoiseVolume Shape => shape ?? (shape = NoiseVolume.GenerateCloudShape(32, 1));
        public static NoiseMap Coverage => coverage ?? (coverage = NoiseMap.GenerateCoverage(128, 2));

        /// <summary>Flat terrain at sea level, no wind, no clouds, no turbulence.</summary>
        public static LevelDefinition CalmLevel()
        {
            return new LevelDefinition
            {
                Id = "calm", Name = "Calm",
                SurfaceWindSpeed = 0, JetSpeed = 0, StratosphereWind = 0, TurbulenceBase = 0, ConvectiveTurbulence = 0,
                ZoneElevation = 0, MountainHeight = 0, ValleyRadius = 100,
                Clouds = new CloudLayer[0],
                AimBiasCross = 0, AimBiasDown = 0,
            };
        }

        public static World CalmWorld() => new World(CalmLevel(), Shape, Coverage);

        public static World WorldFor(LevelDefinition l) => new World(l, Shape, Coverage);

        public static FlightState Entry(FlightModel m, double altitude, double speed, double fpaDeg, double startZ = -120000)
        {
            double f = fpaDeg * Math.PI / 180.0;
            Vec3d pos = Planet.PointAbove(0, startZ, altitude);
            Vec3d up = Planet.Up(pos);
            Vec3d horiz = Vec3d.UnitZ.PerpendicularTo(up).Normalized;
            return m.CreateState(pos, (horiz * Math.Cos(f) + up * Math.Sin(f)) * speed);
        }

        public static FlightState RunToGround(FlightModel m, FlightState s, Func<FlightState, FlightTelemetry, ControlInput> pilot = null,
            double dt = FlightModel.DefaultStep, double maxTime = 2000)
        {
            FlightTelemetry t = m.Measure(s);
            while (!s.Contact && s.Time < maxTime && s.Integrity > 0)
            {
                var input = pilot != null ? pilot(s, t) : new ControlInput { Sas = true };
                t = m.Step(s, input, ControlModifiers.Nominal, dt);
            }
            return s;
        }

        public static void Horizontal(Vec3d p, out double x, out double z) => Planet.Horizontal(p, out x, out z);
    }
}
