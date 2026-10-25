using System;

namespace OrbitalDescent.Core
{
    public enum ChuteState { Stowed, Opening, Open, Torn, Released }

    public enum ContactResult { None, Landed, Splashdown, DeckLanding, Crashed }

    /// <summary>Complete simulated state of the probe. A plain value container so the predictor can
    /// copy it and run the identical model forward.</summary>
    public sealed class FlightState
    {
        public double Time;
        public Vec3d Position;
        public Vec3d Velocity;
        public Quatd Attitude = Quatd.Identity;  // rotates body +Y (the B axis) into world
        public Vec3d AngularVelocity;            // world frame, rad/s
        public Vec3d AppliedTorque;              // actuator torque after lag, N m
        public double Fuel;
        public double SkinTemperature = 250;     // K
        public double Integrity = 1.0;           // 0 = destroyed
        public double Ice;                       // kg accreted
        public double Battery = 1.0;             // 0..1, heater power
        public ChuteState Chute = ChuteState.Stowed;
        public double ChuteOpen;                 // 0..1 opening fraction
        public double PrecisionLeft;             // s of boost reserve
        public bool Contact;
        public ContactResult ContactKind;
        public double ContactSpeed;
        public double ContactVerticalSpeed, ContactHorizontalSpeed;
        public Vec3d ContactPoint;
        public bool NumericalFault;
        public string FailureReason = "";

        public Vec3d BodyAxis => Attitude.Rotate(Vec3d.UnitY);

        public FlightState Clone()
        {
            var c = (FlightState)MemberwiseClone();
            return c;
        }
    }

    public struct ControlInput
    {
        /// <summary>World-space direction to lean the probe's top toward, perpendicular to the body
        /// axis, magnitude 0..1.</summary>
        public Vec3d Steer;
        public double Throttle;     // 0..1 descent engine
        public bool Sas;            // rate-damping stability assist (explicit gameplay assistance)
        public bool Stabilize;      // strong assist: null rates and align with the airflow
        public bool Precision;      // spend precision reserve (Phantom)
        public bool Heater;         // shield heater (de-icing)
    }

    /// <summary>Hazard-driven changes to the control system. Physical hazards (wind, turbulence)
    /// act through the world instead.</summary>
    public struct ControlModifiers
    {
        public double Authority;    // multiplier on actuator authority
        public bool FinsFrozen;
        public bool SasAvailable;
        public static ControlModifiers Nominal => new ControlModifiers { Authority = 1, SasAvailable = true };
    }

    /// <summary>Per-step derived quantities for HUD, hazards, audio and scoring.</summary>
    public struct FlightTelemetry
    {
        public double Altitude, AltitudeAboveGround, GroundHeight;
        public double Speed, AirSpeed, VerticalSpeed, HorizontalSpeed, Acceleration;
        public double Mach, DynamicPressure, Density, Temperature;
        public double HeatFlux, GLoad, AngleOfAttack, AngularRate;
        public double ControlAuthority;        // available actuator torque / inertia, rad/s^2
        public double TrajectoryAuthority;     // achievable lateral acceleration, m/s^2
        public double CommandMagnitude;        // 0..1 actuator saturation level
        public double TurbulenceIntensity, CloudDensity, Visibility;
        public Vec3d Wind, AirVelocity;
        public bool IcingConditions;
    }

    /// <summary>
    /// Force-based 6-DOF-lite flight model (3 translational DOF + 2 rotational DOF about transverse
    /// axes; roll is damped out because the probes are axisymmetric).
    /// Forces: inverse-square gravity, axial + normal aerodynamic force on air-relative velocity,
    /// parachute drag, descent engine thrust. Torques: static aerodynamic stability, aerodynamic
    /// pitch damping, fins (scale with dynamic pressure, saturate), RCS (fixed), turbulence gusts.
    /// Integration: semi-implicit Euler at a fixed step (default 1/120 s).
    /// </summary>
    public sealed class FlightModel
    {
        public const double DefaultStep = 1.0 / 120.0;
        public const double MaxAngularRate = 8.0;   // rad/s numerical/structural safeguard
        const double SuttonGravesK = 1.7415e-4;     // Earth air, SI

        public readonly VehicleConfig V;
        public readonly World World;
        public bool Deterministic;  // predictor mode: no turbulence, no ice, no damage

        public FlightModel(VehicleConfig vehicle, World world) { V = vehicle; World = world; }

        public FlightState CreateState(Vec3d position, Vec3d velocity)
        {
            var s = new FlightState
            {
                Position = position,
                Velocity = velocity,
                Fuel = V.FuelMass,
                PrecisionLeft = V.PrecisionReserve,
                SkinTemperature = 240,
            };
            // Start shield-first along the velocity (B opposite to motion).
            s.Attitude = Quatd.FromTo(Vec3d.UnitY, -velocity.Normalized);
            return s;
        }

        public double Mass(FlightState s) => V.DryMass + s.Fuel + s.Ice;

        static double MachDragFactor(double mach)
        {
            // Transonic drag rise for blunt bodies, ~+35% peak near M 1.1, settling to +15% hypersonic.
            double peak = 0.35 * Math.Exp(-Math.Pow((mach - 1.1) / 0.45, 2));
            double super = 0.15 * MathUtil.SmoothStep(0.8, 2.0, mach);
            return 1.0 + peak + super;
        }

        public bool ChuteDeployAllowed(FlightState s, FlightTelemetry t, out string reason)
        {
            reason = "";
            if (s.Chute != ChuteState.Stowed) { reason = "CHUTE USED"; return false; }
            if (t.AltitudeAboveGround > V.ChuteMaxDeployAltitude) { reason = "CHUTE: TOO HIGH"; return false; }
            if (t.DynamicPressure > V.ChuteMaxDeployQ) { reason = "CHUTE: TOO FAST"; return false; }
            return true;
        }

        public bool DeployChute(FlightState s, FlightTelemetry t)
        {
            if (!ChuteDeployAllowed(s, t, out _)) return false;
            s.Chute = ChuteState.Opening;
            s.ChuteOpen = 0;
            return true;
        }

        public void ReleaseChute(FlightState s)
        {
            if (s.Chute == ChuteState.Open || s.Chute == ChuteState.Opening) { s.Chute = ChuteState.Released; s.ChuteOpen = 0; }
        }

        /// <summary>Samples the environment and derives telemetry without advancing the state.</summary>
        public FlightTelemetry Measure(FlightState s)
        {
            FlightTelemetry t = default;
            double alt = Planet.Altitude(s.Position);
            Planet.Horizontal(s.Position, out double hx, out double hz);
            double ground = World.SurfaceHeight(hx, hz, s.Time, out _);
            Vec3d up = Planet.Up(s.Position);
            var atm = Atmosphere.Sample(alt);

            Vec3d wind = World.Wind.MeanWind(hx, hz, alt);
            double turb = 0, cloud = 0;
            if (!Deterministic)
            {
                cloud = World.Clouds.Density(hx, hz, alt, s.Time);
                turb = World.Wind.TurbulenceIntensity(hx, hz, alt, s.Time);
                wind = wind + WindField.TurbulenceVelocity(s.Position, s.Time, turb, alt);
            }
            Vec3d air = s.Velocity - wind;
            double vAir = air.Length;

            t.Altitude = alt;
            t.GroundHeight = ground;
            t.AltitudeAboveGround = alt - ground - V.GearHeight; // landing feet above the surface
            t.Speed = s.Velocity.Length;
            t.AirSpeed = vAir;
            t.VerticalSpeed = Vec3d.Dot(s.Velocity, up);
            t.HorizontalSpeed = s.Velocity.PerpendicularTo(up).Length;
            t.Density = atm.Density;
            t.Temperature = atm.Temperature;
            t.Mach = vAir / atm.SpeedOfSound;
            t.DynamicPressure = 0.5 * atm.Density * vAir * vAir;
            t.HeatFlux = SuttonGravesK * Math.Sqrt(atm.Density / V.NoseRadius) * vAir * vAir * vAir;
            t.Wind = wind;
            t.AirVelocity = air;
            t.TurbulenceIntensity = turb;
            t.CloudDensity = cloud;
            t.Visibility = CloudField.Visibility(cloud);
            t.IcingConditions = cloud > 0.25 && atm.Temperature > 253 && atm.Temperature < 273.5;
            Vec3d b = s.BodyAxis;
            t.AngleOfAttack = vAir > 1e-3 ? Math.Acos(MathUtil.Clamp(Vec3d.Dot(b, -air / vAir), -1, 1)) : 0;
            t.AngularRate = s.AngularVelocity.Length;
            return t;
        }

        /// <summary>Advances the state by dt. Returns telemetry measured at the start of the step.</summary>
        public FlightTelemetry Step(FlightState s, ControlInput input, ControlModifiers mods, double dt)
        {
            if (s.Contact) return Measure(s);
            FlightTelemetry t = Measure(s);
            double m = Mass(s);
            Vec3d b = s.BodyAxis;
            Vec3d air = t.AirVelocity;
            double vAir = t.AirSpeed;
            Vec3d flowDir = vAir > 1e-6 ? -air / vAir : -b;   // direction the air moves past the body
            double q = t.DynamicPressure;
            double A = V.RefArea, Lr = V.RefLength;
            double alpha = t.AngleOfAttack;
            double sa = Math.Sin(alpha), ca = Math.Cos(alpha);

            // ---------- Forces ----------
            Vec3d force = Planet.Gravity(s.Position) * m;
            Vec3d aero = Vec3d.Zero;
            if (q > 0)
            {
                double iceDrag = 1.0 + 2.0 * s.Ice / Math.Max(1.0, m);
                double ca0 = V.AxialCoeff * MachDragFactor(t.Mach) * iceDrag;
                // Axial force pushes along +B (on the shield) proportional to cos(alpha).
                aero = aero + b * (q * A * ca0 * ca);
                // Normal force in the (B, flow) plane, pointing with the crossflow.
                Vec3d nDir = flowDir.PerpendicularTo(b);
                double nLen = nDir.Length;
                if (nLen > 1e-9)
                {
                    nDir = nDir / nLen;
                    double cn = V.NormalSlope * sa * ca + V.CrossflowCoeff * sa * sa;
                    aero = aero + nDir * (q * A * cn);
                }
            }
            // Parachute
            if (s.Chute == ChuteState.Opening || s.Chute == ChuteState.Open)
            {
                s.ChuteOpen = Math.Min(1.0, s.ChuteOpen + dt / 2.5);
                if (s.ChuteOpen >= 1.0) s.Chute = ChuteState.Open;
                double area = V.ChuteCdA * s.ChuteOpen * s.ChuteOpen;
                aero = aero + flowDir * (q * area);
                if (!Deterministic && q > V.ChuteMaxDeployQ * 2.2) { s.Chute = ChuteState.Torn; s.ChuteOpen = 0; }
            }
            force = force + aero;
            // Engine
            double thrust = 0;
            if (input.Throttle > 0 && s.Fuel > 0)
            {
                thrust = V.EngineThrust * MathUtil.Clamp01(input.Throttle);
                force = force + b * thrust;
            }
            Vec3d nonGrav = aero + b * thrust;
            t.GLoad = nonGrav.Length / (m * Constants.G0);

            // ---------- Torques ----------
            Vec3d omegaPerp = s.AngularVelocity.PerpendicularTo(b);
            Vec3d torque = Vec3d.Zero;
            if (q > 0)
            {
                // Static stability rotates B toward the flow direction (shield into the wind).
                Vec3d restoreAxis = Vec3d.Cross(b, flowDir);
                double stiff = V.StabilityCoeff;
                if (s.Chute == ChuteState.Opening || s.Chute == ChuteState.Open)
                    stiff += 0.5 * V.ChuteCdA * s.ChuteOpen / A;  // canopy riser pulls the top downwind
                torque = torque + restoreAxis * (q * A * Lr * stiff);
                // Aerodynamic pitch damping: -q A L^2 / (2V) * Cmq * omega
                double damp = q * A * Lr * Lr / (2.0 * Math.Max(vAir, 1.0)) * V.PitchDampCoeff;
                torque = torque - omegaPerp * damp;
                if (!Deterministic && t.TurbulenceIntensity > 0)
                {
                    Vec3d g = WindField.TurbulenceVelocity(s.Position * 1.7, s.Time * 1.9, t.TurbulenceIntensity, t.Altitude).PerpendicularTo(b);
                    torque = torque + Vec3d.Cross(b, g) * (q * A * Lr * 0.0025);
                }
            }

            // Actuators: fins scale with dynamic pressure and saturate; RCS is fixed.
            double authority = Math.Max(0, mods.Authority);
            bool precision = input.Precision && s.PrecisionLeft > 0 && V.PrecisionReserve > 0;
            if (precision) { authority *= V.PrecisionBoost; s.PrecisionLeft = Math.Max(0, s.PrecisionLeft - dt); }
            double iceFin = 1.0 - 0.6 * MathUtil.Clamp01(s.Ice / (V.MaxIceFraction * V.DryMass));
            double finMax = mods.FinsFrozen ? 0 : Math.Min(V.FinCoeff * q * A * Lr, V.FinMaxTorque) * iceFin * authority;
            double rcsMax = s.Fuel > 0 ? V.RcsTorque * authority : 0;
            double maxTorque = finMax + rcsMax;

            Vec3d command = Vec3d.Cross(b, input.Steer.PerpendicularTo(b).ClampLength(1.0));
            bool sas = (input.Sas || input.Stabilize) && mods.SasAvailable;
            if (sas && maxTorque > 0)
            {
                double gain = V.SasGain * (input.Stabilize ? 3.0 : 1.0);
                // Rate damping expressed as a fraction of available torque (respects saturation).
                Vec3d rateCmd = -omegaPerp * (gain * V.Inertia / Math.Max(maxTorque, 1e-6) * 4.0);
                command = command + rateCmd;
                if (input.Stabilize && vAir > 20)
                    command = command + Vec3d.Cross(b, flowDir) * (2.0 * gain);
                else if (input.Stabilize)
                    command = command + Vec3d.Cross(b, Planet.Up(s.Position)) * (2.0 * gain);
            }
            command = command.PerpendicularTo(b).ClampLength(1.0);
            Vec3d desired = command * maxTorque;
            s.AppliedTorque = s.AppliedTorque + (desired - s.AppliedTorque) * MathUtil.Approach(1.0 / V.ActuatorTimeConstant, dt);
            if (s.AppliedTorque.Length > maxTorque) s.AppliedTorque = s.AppliedTorque.ClampLength(maxTorque);
            torque = torque + s.AppliedTorque;

            t.CommandMagnitude = command.Length;
            t.ControlAuthority = maxTorque / V.Inertia;
            // Lateral acceleration available from leaning ~20 degrees (aero) plus engine tilt.
            double lean = 20.0 * Math.PI / 180.0;
            double aeroLat = q * A * Math.Abs(V.AxialCoeff * Math.Sin(lean) * Math.Cos(lean) - V.NormalSlope * Math.Sin(lean) * Math.Cos(lean)) / m;
            double engLat = s.Fuel > 0 ? V.EngineThrust * Math.Sin(lean) / m : 0;
            t.TrajectoryAuthority = (mods.FinsFrozen && rcsMax <= 0) ? 0 : aeroLat + engLat;

            // ---------- Propellant ----------
            // Allocation: aerodynamic fins carry the command first; RCS supplies only the remainder.
            double rcsTorqueUsed = Math.Max(0, s.AppliedTorque.Length - finMax);
            double fuelUse = rcsTorqueUsed / V.RcsLever / (V.RcsIsp * Constants.G0) + thrust / (V.EngineIsp * Constants.G0);
            s.Fuel = Math.Max(0, s.Fuel - fuelUse * dt);

            // ---------- Integrate (semi-implicit Euler) ----------
            Vec3d prevPos = s.Position;
            Vec3d accel = force / m;
            s.Velocity = s.Velocity + accel * dt;
            s.Position = s.Position + s.Velocity * dt;
            // Roll is damped strongly (axisymmetric body), transverse rates from torque.
            Vec3d omegaRoll = b * Vec3d.Dot(s.AngularVelocity, b);
            Vec3d omega = s.AngularVelocity - omegaRoll * MathUtil.Approach(3.0, dt);
            omega = omega + torque.PerpendicularTo(b) * (dt / V.Inertia);
            omega = omega * (1.0 - 0.02 * dt); // small structural damping
            s.AngularVelocity = omega.ClampLength(MaxAngularRate);
            s.Attitude = s.Attitude.Integrate(s.AngularVelocity, dt);
            s.Time += dt;
            t.Acceleration = Vec3d.Dot(accel, s.Velocity.Normalized);

            // ---------- Thermal / structural ----------
            if (!Deterministic)
            {
                double tAmb = t.Temperature;
                double radiated = V.Emissivity * Constants.StefanBoltzmann * (Math.Pow(s.SkinTemperature, 4) - Math.Pow(tAmb, 4));
                double convective = 6.0 * (s.SkinTemperature - tAmb) * Math.Sqrt(Math.Max(t.Density, 0) / 1.225 + 1e-4);
                s.SkinTemperature += (t.HeatFlux - radiated - convective) / V.ShieldArealHeatCapacity * dt;
                s.SkinTemperature = MathUtil.Clamp(s.SkinTemperature, 150, 4000);
                if (s.SkinTemperature > V.MaxSkinTemp)
                    s.Integrity -= (s.SkinTemperature - V.MaxSkinTemp) / 300.0 * 0.25 * dt;
                if (t.GLoad > V.MaxGLoad)
                    s.Integrity -= (t.GLoad - V.MaxGLoad) / V.MaxGLoad * 0.8 * dt;
                // Ice accretion in supercooled cloud; heater and warm air melt it.
                double iceMax = V.MaxIceFraction * V.DryMass;
                if (t.IcingConditions && vAir < 400)
                    s.Ice = Math.Min(iceMax, s.Ice + 0.004 * t.CloudDensity * vAir * A * 0.05 * dt);
                bool heating = input.Heater && s.Battery > 0;
                if (heating) s.Battery = Math.Max(0, s.Battery - 0.02 * dt);
                double melt = (heating ? 0.4 : 0) + (t.Temperature > 275 ? 0.15 : 0) + (s.SkinTemperature > 320 ? 0.5 : 0);
                s.Ice = Math.Max(0, s.Ice - melt * dt);
                if (s.Integrity <= 0)
                {
                    s.Integrity = 0;
                    s.FailureReason = s.SkinTemperature > V.MaxSkinTemp ? "Heat shield failure" : "Structural failure";
                }
            }

            // ---------- Numerical safeguards ----------
            if (!s.Position.IsFinite || !s.Velocity.IsFinite || !s.Attitude.IsFinite || !s.AngularVelocity.IsFinite)
            {
                s.NumericalFault = true;
                s.Position = prevPos;
                s.Velocity = Vec3d.Zero;
                s.AngularVelocity = Vec3d.Zero;
                s.Attitude = Quatd.Identity;
            }

            CheckContact(s, prevPos);
            return t;
        }

        void CheckContact(FlightState s, Vec3d prevPos)
        {
            double alt = Planet.Altitude(s.Position);
            Planet.Horizontal(s.Position, out double hx, out double hz);
            double ground = World.SurfaceHeight(hx, hz, s.Time, out SurfaceKind kind);
            double gear = kind == SurfaceKind.Water ? V.GearHeight * 0.3 : V.GearHeight;
            if (alt > ground + gear) return;
            // Interpolate to the contact point between the previous and current positions.
            double prevAlt = Planet.Altitude(prevPos);
            Planet.Horizontal(prevPos, out double px, out double pz);
            double prevGround = World.SurfaceHeight(px, pz, s.Time, out _);
            double a0 = prevAlt - prevGround - gear, a1 = alt - ground - gear;
            double f = a0 - a1 > 1e-9 ? MathUtil.Clamp01(a0 / (a0 - a1)) : 1.0;
            Vec3d hit = prevPos + (s.Position - prevPos) * f;
            Planet.Horizontal(hit, out double cx, out double cz);
            Vec3d rel = s.Velocity - (kind == SurfaceKind.Deck ? World.ZoneVelocity : Vec3d.Zero);
            double groundAtHit = World.SurfaceHeight(cx, cz, s.Time, out _);
            s.ContactPoint = Planet.PointAbove(cx, cz, groundAtHit);
            s.Position = Planet.PointAbove(cx, cz, groundAtHit + gear);
            s.ContactSpeed = rel.Length;
            Vec3d upC = Planet.Up(s.ContactPoint);
            s.ContactVerticalSpeed = -Vec3d.Dot(rel, upC);
            s.ContactHorizontalSpeed = rel.PerpendicularTo(upC).Length;
            s.Contact = true;
            // Survivable envelope: vertical sink within the vehicle limit, horizontal skid within twice it
            // (landing legs / water absorb more).
            double limit = V.MaxTouchdownSpeed * (kind == SurfaceKind.Water ? 1.6 : 1.0);
            double tilt = Math.Acos(MathUtil.Clamp(Vec3d.Dot(s.BodyAxis, upC), -1, 1));
            bool tooFast = s.ContactVerticalSpeed > limit || s.ContactHorizontalSpeed > limit * 2.0;
            if (tooFast || s.Integrity <= 0 || (kind != SurfaceKind.Water && tilt > 0.9 && s.ContactSpeed > 3))
            {
                s.ContactKind = ContactResult.Crashed;
                if (string.IsNullOrEmpty(s.FailureReason))
                    s.FailureReason = s.ContactVerticalSpeed > limit ? "Impact too fast"
                        : tooFast ? "Skidded over at touchdown" : "Tipped over on touchdown";
            }
            else
            {
                s.ContactKind = kind == SurfaceKind.Water ? ContactResult.Splashdown
                    : kind == SurfaceKind.Deck ? ContactResult.DeckLanding : ContactResult.Landed;
            }
            s.Velocity = World.ZoneVelocity * (kind == SurfaceKind.Deck ? 1 : 0);
            s.AngularVelocity = Vec3d.Zero;
        }
    }
}
