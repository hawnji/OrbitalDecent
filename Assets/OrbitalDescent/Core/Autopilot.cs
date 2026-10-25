using System;

namespace OrbitalDescent.Core
{
    /// <summary>
    /// A simple scripted pilot that uses only what the player sees (prediction, telemetry) and the
    /// same controls. Used by the menu's demo flight and by automated tests to prove each level can
    /// be completed with the real flight model. It is intentionally not optimal.
    /// </summary>
    public sealed class Autopilot
    {
        public double SteerGain = 1.0 / 1500.0;
        public bool UseChute = true;
        public double ChuteAltitude = 1500;
        public double CutChuteAltitude = 180;
        bool chuteTried;

        public ControlInput Update(MissionSession m)
        {
            var s = m.State;
            var t = m.Telemetry;
            var input = new ControlInput { Sas = true };
            var p = m.Predictor.Latest;
            Vec3d up = Planet.Up(s.Position);
            Vec3d b = s.BodyAxis;

            // Lean toward the zone in proportion to the predicted miss.
            if (p != null && p.Valid && m.Nav.Available && t.AltitudeAboveGround > 150)
            {
                Vec3d miss = new Vec3d(p.ErrorEast, 0, p.ErrorNorth);
                Vec3d want = (-miss).PerpendicularTo(up);
                double mag = MathUtil.Clamp01(want.Length * SteerGain);
                if (s.Chute == ChuteState.Open || s.Chute == ChuteState.Opening) mag *= 0.3;
                if (want.Length > 1) input.Steer = want.Normalized.PerpendicularTo(b) * mag;
            }

            // Ice
            input.Heater = s.Ice > 0.5;

            // Reboot as soon as a lightning outage happens.
            if (m.Hazards.Lightning.Outage > 0) m.RebootRequested = true;

            // Parachute
            if (UseChute && !chuteTried && t.AltitudeAboveGround < ChuteAltitude && t.AltitudeAboveGround > 600)
            {
                if (m.Model.ChuteDeployAllowed(s, t, out _)) { m.RequestChute(); chuteTried = true; }
            }

            // Final approach: drop the canopy (it drags the probe along with the wind), lean against
            // horizontal drift and fly a decelerating speed profile with the engine.
            double agl = Math.Max(0, t.AltitudeAboveGround);
            if (agl < CutChuteAltitude && (s.Chute == ChuteState.Open || s.Chute == ChuteState.Opening)) m.CutChute();
            if (agl < 400)
            {
                Vec3d vh = (s.Velocity - m.World.ZoneVelocity).PerpendicularTo(up);
                // Attitude target: tilt the top (and so the engine thrust) against the drift.
                Vec3d desiredAxis = (up + (-vh * 0.04).ClampLength(0.4)).Normalized;
                input.Steer = ((desiredAxis - b) * 4.0).PerpendicularTo(b).ClampLength(1.0);
            }
            double mass = m.Model.Mass(s);
            double g = Constants.G0;
            double aMax = m.Vehicle.EngineThrust / mass - g;
            if (aMax > 0.5 && s.Fuel > 0 && agl < 1500)
            {
                double sink = -t.VerticalSpeed;
                const double profile = 0.55;
                double target = Math.Max(1.5, Math.Sqrt(2 * aMax * profile * Math.Max(0, agl - 1.5)));
                if (sink > target * 0.9)
                {
                    double upFactor = Math.Max(0.3, Vec3d.Dot(b, up));
                    double decel = g + aMax * profile + 3.0 * (sink - target);
                    input.Throttle = MathUtil.Clamp01(decel * mass / (m.Vehicle.EngineThrust * upFactor));
                }
            }
            return input;
        }
    }
}
