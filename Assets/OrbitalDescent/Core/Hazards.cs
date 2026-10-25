using System;
using System.Collections.Generic;

namespace OrbitalDescent.Core
{
    public enum HazardLevel { None = 0, Caution = 1, Warning = 2 }

    public sealed class HazardStatus
    {
        public string Id = "";
        public string Label = "";
        public HazardLevel Level;
        public string Message = "";
        public bool Active;            // effect currently applied (vs. only a warning)
        public double TimeRemaining;   // for timed disruptions, s (0 = not timed)
        public double Intensity;       // 0..1 for gauges
    }

    /// <summary>Everything a hazard may read each simulation step.</summary>
    public sealed class HazardContext
    {
        public FlightState State;
        public FlightTelemetry Telemetry;
        public World World;
        public VehicleConfig Vehicle;
        public PredictionResult Prediction;
        public ControlInput Input;
        public double Dt;
        public double DifficultyScale = 1.0;
        public bool RebootRequested;
    }

    /// <summary>Aggregated effects of all hazards for this step.</summary>
    public sealed class HazardEffects
    {
        public ControlModifiers Controls = ControlModifiers.Nominal;
        public bool NavAvailable = true;
        public string NavLossReason = "";
        public double NavNoise;          // metres of navigation uncertainty
        public double InputDelay;        // s
        public double CameraShake;       // 0..1
        public bool LightningStrike;     // event this step
        public double StaticCharge;      // 0..1
        public void Reset()
        {
            Controls = ControlModifiers.Nominal;
            NavAvailable = true; NavLossReason = ""; NavNoise = 0; InputDelay = 0;
            CameraShake = 0; LightningStrike = false; StaticCharge = 0;
        }
    }

    public abstract class Hazard
    {
        public readonly HazardStatus Status = new HazardStatus();
        public int Encounters;     // times the hazard became active
        public int Recoveries;     // times the player came out of it in control
        protected bool wasActive;

        protected Hazard(string id, string label) { Status.Id = id; Status.Label = label; }

        public abstract void Update(HazardContext c, HazardEffects fx);

        protected void TrackActivation(bool active, bool recoveredCleanly)
        {
            if (active && !wasActive) Encounters++;
            if (!active && wasActive && recoveredCleanly) Recoveries++;
            wasActive = active;
        }

        protected void Set(HazardLevel level, string message, bool active, double intensity = 0, double remaining = 0)
        {
            Status.Level = level; Status.Message = message; Status.Active = active;
            Status.Intensity = intensity; Status.TimeRemaining = remaining;
        }
    }

    /// <summary>1. Wind shear: sharp wind change across a layer. Warns while approaching from above.</summary>
    public sealed class WindShearHazard : Hazard
    {
        public WindShearHazard() : base("shear", "WIND SHEAR") { }
        public override void Update(HazardContext c, HazardEffects fx)
        {
            double alt = c.Telemetry.Altitude;
            Set(HazardLevel.None, "", false);
            bool active = false;
            foreach (var s in c.World.Level.Shear)
            {
                double delta = Math.Sqrt(s.DeltaEast * s.DeltaEast + s.DeltaNorth * s.DeltaNorth) * c.DifficultyScale;
                double above = alt - s.Altitude;
                if (Math.Abs(above) < s.Thickness * 1.5)
                {
                    active = true;
                    Set(HazardLevel.Warning, $"WIND SHEAR {delta:F0} m/s", true, MathUtil.Clamp01(delta / 40.0));
                    fx.CameraShake = Math.Max(fx.CameraShake, 0.35);
                }
                else if (above > 0 && above < 4000 + s.Thickness * 1.5)
                {
                    Set(HazardLevel.Caution, $"SHEAR LAYER {s.Altitude / 1000:F1} km  {delta:F0} m/s", false, MathUtil.Clamp01(delta / 40.0));
                }
            }
            TrackActivation(active, c.State.AngularVelocity.Length < 0.6);
        }
    }

    /// <summary>2. Turbulence pockets: bounded gusts inside convective cloud; looks ahead along the path.</summary>
    public sealed class TurbulenceHazard : Hazard
    {
        public TurbulenceHazard() : base("turb", "TURBULENCE") { }
        public override void Update(HazardContext c, HazardEffects fx)
        {
            double i = c.Telemetry.TurbulenceIntensity;
            fx.CameraShake = Math.Max(fx.CameraShake, MathUtil.Clamp01(i * 1.2) * MathUtil.Clamp01(c.Telemetry.DynamicPressure / 1500.0));
            // Look ahead 4 and 8 s along the current velocity.
            double ahead = 0;
            for (int k = 1; k <= 2; k++)
            {
                Vec3d p = c.State.Position + c.State.Velocity * (4.0 * k);
                Planet.Horizontal(p, out double x, out double z);
                ahead = Math.Max(ahead, c.World.Wind.TurbulenceIntensity(x, z, Planet.Altitude(p), c.State.Time));
            }
            bool active = i > 0.35;
            if (active) Set(i > 0.6 ? HazardLevel.Warning : HazardLevel.Caution, i > 0.6 ? "SEVERE TURBULENCE" : "TURBULENCE", true, i);
            else if (ahead > 0.4) Set(HazardLevel.Caution, "TURBULENCE AHEAD", false, ahead);
            else Set(HazardLevel.None, "", false, i);
            TrackActivation(active, c.State.AngularVelocity.Length < 0.8);
        }
    }

    /// <summary>3. Lightning: static charge builds inside storm cells (visible gauge and warning),
    /// a strike knocks out the fins and stability assist and cuts RCS authority. Rebooting the avionics
    /// shortens the outage.</summary>
    public sealed class LightningHazard : Hazard
    {
        public double Charge;
        public double Outage;
        public int Strikes;
        double rebootTimer;
        public const double RebootTime = 1.2;
        public LightningHazard() : base("lightning", "LIGHTNING") { }

        public override void Update(HazardContext c, HazardEffects fx)
        {
            var w = c.World;
            Planet.Horizontal(c.State.Position, out double x, out double z);
            double alt = c.Telemetry.Altitude;
            double storm = w.Clouds.StormCoverage(x - w.Level.CloudDriftEast * c.State.Time, z - w.Level.CloudDriftNorth * c.State.Time);
            bool inStormBand = alt > 1500 && alt < 12000;
            double rate = inStormBand ? storm * 0.11 * c.DifficultyScale : 0;
            if (rate > 0.005) Charge += rate * c.Dt;
            else Charge = Math.Max(0, Charge - 0.08 * c.Dt);
            fx.StaticCharge = MathUtil.Clamp01(Charge);

            if (Charge >= 1.0 && Outage <= 0)
            {
                Strikes++;
                Charge = 0.15;
                Outage = 4.5 * (1.0 - 0.5 * c.Vehicle.GuidanceReliability);
                rebootTimer = 0;
                fx.LightningStrike = true;
            }

            if (Outage > 0)
            {
                if (c.RebootRequested && rebootTimer <= 0) { rebootTimer = RebootTime; Outage = Math.Min(Outage, RebootTime); }
                Outage = Math.Max(0, Outage - c.Dt);
                if (rebootTimer > 0) rebootTimer -= c.Dt;
                fx.Controls.FinsFrozen = true;
                fx.Controls.SasAvailable = false;
                fx.Controls.Authority *= rebootTimer > 0 ? 0.0 : 0.3;
                fx.CameraShake = Math.Max(fx.CameraShake, 0.5);
                Set(HazardLevel.Warning, rebootTimer > 0 ? "AVIONICS REBOOTING" : "LIGHTNING STRIKE - AVIONICS FAULT  [REBOOT]", true, 1, Math.Max(0, Outage));
            }
            else if (Charge > 0.5) Set(HazardLevel.Warning, "STATIC CHARGE CRITICAL - LEAVE STORM CELL", false, Charge);
            else if (Charge > 0.2) Set(HazardLevel.Caution, "STATIC BUILDUP", false, Charge);
            else if (storm > 0.2 && inStormBand) Set(HazardLevel.Caution, "STORM CELL", false, Charge);
            else Set(HazardLevel.None, "", false, Charge);
            TrackActivation(Outage > 0, true);
        }
    }

    /// <summary>4. Electromagnetic interference band: navigation drifts (uncertainty grows) and control
    /// inputs lag. Altitude and speed (inertial) remain valid.</summary>
    public sealed class EmInterferenceHazard : Hazard
    {
        double level;
        public EmInterferenceHazard() : base("emi", "EM INTERFERENCE") { }
        public override void Update(HazardContext c, HazardEffects fx)
        {
            var L = c.World.Level;
            Set(HazardLevel.None, "", false);
            if (L.EmBandHigh <= L.EmBandLow) { TrackActivation(false, true); return; }
            double alt = c.Telemetry.Altitude;
            bool inside = alt > L.EmBandLow && alt < L.EmBandHigh;
            level += ((inside ? 1.0 : 0.0) - level) * MathUtil.Approach(inside ? 0.8 : 0.35, c.Dt);
            double strength = L.EmStrength * c.DifficultyScale * (1.0 - 0.6 * c.Vehicle.GuidanceReliability);
            if (level > 0.02)
            {
                fx.NavNoise = Math.Max(fx.NavNoise, 900.0 * strength * level);
                fx.InputDelay = Math.Max(fx.InputDelay, 0.3 * strength * level);
                Set(inside ? HazardLevel.Warning : HazardLevel.Caution, inside ? "EM INTERFERENCE - NAV DEGRADED" : "NAV RECONVERGING", inside, level);
            }
            else if (alt > L.EmBandHigh && alt < L.EmBandHigh + 12000)
                Set(HazardLevel.Caution, $"EM BAND BELOW {L.EmBandHigh / 1000:F0} km", false);
            TrackActivation(inside, true);
        }
    }

    /// <summary>5. Plasma blackout: high heat flux ionizes the air around the shield and navigation
    /// updates are lost until it subsides. Emerges from the physics (speed and density).</summary>
    public sealed class PlasmaBlackoutHazard : Hazard
    {
        public PlasmaBlackoutHazard() : base("plasma", "PLASMA BLACKOUT") { }
        public override void Update(HazardContext c, HazardEffects fx)
        {
            double th = c.World.Level.PlasmaHeatFluxThreshold;
            double r = c.Telemetry.HeatFlux / th;
            bool active = r >= 1.0;
            if (active)
            {
                fx.NavAvailable = false;
                fx.NavLossReason = "PLASMA BLACKOUT";
                Set(HazardLevel.Warning, "PLASMA BLACKOUT - INERTIAL DATA ONLY", true, MathUtil.Clamp01(r - 1));
            }
            else if (r > 0.65 && c.Telemetry.Acceleration > -50) Set(HazardLevel.Caution, "IONIZATION RISING", false, r);
            else Set(HazardLevel.None, "", false, r);
            TrackActivation(active, true);
        }
    }

    /// <summary>6. Guidance instability: detects oscillation (rate sign reversals with growing amplitude),
    /// typically pilot-induced or from a low-stability airframe. Recovery: release input, use SAS/STAB.</summary>
    public sealed class OscillationHazard : Hazard
    {
        Vec3d lastRate;
        double reversalScore;
        public OscillationHazard() : base("pio", "OSCILLATION") { }
        public override void Update(HazardContext c, HazardEffects fx)
        {
            Vec3d w = c.State.AngularVelocity;
            if (Vec3d.Dot(w, lastRate) < 0 && w.Length > 0.25) reversalScore += 1.0;
            reversalScore = Math.Max(0, reversalScore - c.Dt * 0.6);
            lastRate = w;
            double rate = w.Length;
            bool tumbling = rate > 2.5 && c.Telemetry.DynamicPressure > 1500;
            bool osc = reversalScore > 2.5 && rate > 0.4;
            if (tumbling) Set(HazardLevel.Warning, "TUMBLE - HOLD STAB", true, MathUtil.Clamp01(rate / 4));
            else if (osc) Set(HazardLevel.Warning, "OSCILLATION - EASE INPUTS / SAS", true, MathUtil.Clamp01(reversalScore / 6));
            else if (rate > 1.2 && c.Telemetry.DynamicPressure > 800) Set(HazardLevel.Caution, "HIGH ROTATION RATE", false, MathUtil.Clamp01(rate / 4));
            else Set(HazardLevel.None, "", false, MathUtil.Clamp01(rate / 4));
            TrackActivation(tumbling || osc, true);
        }
    }

    /// <summary>7. Icing in supercooled cloud: adds mass and drag and weakens fins (in the flight model).
    /// Recovery: shield heater (battery) or descend into warmer air.</summary>
    public sealed class IcingHazard : Hazard
    {
        public IcingHazard() : base("ice", "ICING") { }
        public override void Update(HazardContext c, HazardEffects fx)
        {
            double frac = c.State.Ice / (c.Vehicle.MaxIceFraction * c.Vehicle.DryMass);
            bool active = frac > 0.05;
            if (active) Set(frac > 0.5 ? HazardLevel.Warning : HazardLevel.Caution, $"ICING {frac * 100:F0}%  [HEATER]", true, frac);
            else if (c.Telemetry.IcingConditions) Set(HazardLevel.Caution, "ICING CONDITIONS", false, frac);
            else Set(HazardLevel.None, "", false, frac);
            TrackActivation(active, true);
        }
    }

    /// <summary>8. Dense cloud: visibility collapses; HUD switches to instrument emphasis.</summary>
    public sealed class DenseCloudHazard : Hazard
    {
        public DenseCloudHazard() : base("imc", "LOW VISIBILITY") { }
        public override void Update(HazardContext c, HazardEffects fx)
        {
            double vis = c.Telemetry.Visibility;
            bool active = vis < 300;
            if (active) Set(HazardLevel.Caution, $"IN CLOUD - VIS {vis:F0} m", true, MathUtil.Clamp01(1 - vis / 300));
            else Set(HazardLevel.None, "", false);
            TrackActivation(active, true);
        }
    }

    /// <summary>9. Terrain: warns when the predicted touchdown is on high or steep ground, and on
    /// sink rate close to the surface.</summary>
    public sealed class TerrainHazard : Hazard
    {
        public TerrainHazard() : base("terrain", "TERRAIN") { }
        public override void Update(HazardContext c, HazardEffects fx)
        {
            var t = c.Telemetry;
            Set(HazardLevel.None, "", false);
            double agl = Math.Max(0, t.AltitudeAboveGround);
            double sink = -t.VerticalSpeed;
            double tti = sink > 0.5 ? agl / sink : double.PositiveInfinity;
            bool tooFast = t.Speed > c.Vehicle.MaxTouchdownSpeed * 1.5;
            if (tti < 12 && tooFast && agl < 3000)
            {
                Set(HazardLevel.Warning, tti < 6 ? "SINK RATE - BURN!" : "SINK RATE", true, MathUtil.Clamp01(1 - tti / 12));
            }
            else if (c.Prediction != null && c.Prediction.Valid && agl < 25000)
            {
                Planet.Horizontal(c.Prediction.ImpactPoint, out double x, out double z);
                var n = c.World.Terrain.Normal(x, z);
                double slope = Math.Acos(MathUtil.Clamp(n.y, -1, 1)) * 180 / Math.PI;
                double h = c.World.Terrain.Height(x, z);
                if (!c.World.Level.Ocean && (slope > 12 || h > c.World.Level.ZoneElevation + 200))
                    Set(HazardLevel.Caution, $"TERRAIN AT TOUCHDOWN  {slope:F0}° SLOPE", false, MathUtil.Clamp01(slope / 30));
            }
            TrackActivation(Status.Active, true);
        }
    }

    /// <summary>10. Thermal and structural limits (overheat, over-g).</summary>
    public sealed class LoadsHazard : Hazard
    {
        public LoadsHazard() : base("loads", "LOADS") { }
        public override void Update(HazardContext c, HazardEffects fx)
        {
            var v = c.Vehicle;
            double tRatio = c.State.SkinTemperature / v.MaxSkinTemp;
            double gRatio = c.Telemetry.GLoad / v.MaxGLoad;
            bool active = tRatio > 1 || gRatio > 1;
            if (tRatio > 1) Set(HazardLevel.Warning, "SHIELD OVERTEMP - SHALLOW/SLOW DOWN", true, MathUtil.Clamp01(tRatio - 1));
            else if (gRatio > 1) Set(HazardLevel.Warning, "OVER-G", true, MathUtil.Clamp01(gRatio - 1));
            else if (tRatio > 0.85) Set(HazardLevel.Caution, "SHIELD HOT", false, tRatio);
            else if (gRatio > 0.8) Set(HazardLevel.Caution, "HIGH G", false, gRatio);
            else Set(HazardLevel.None, "", false, Math.Max(tRatio, gRatio));
            TrackActivation(active, c.State.Integrity > 0);
        }
    }

    public sealed class HazardSystem
    {
        public readonly List<Hazard> Hazards = new List<Hazard>();
        public readonly HazardEffects Effects = new HazardEffects();
        public readonly LightningHazard Lightning = new LightningHazard();

        public HazardSystem()
        {
            Hazards.Add(new WindShearHazard());
            Hazards.Add(new TurbulenceHazard());
            Hazards.Add(Lightning);
            Hazards.Add(new EmInterferenceHazard());
            Hazards.Add(new PlasmaBlackoutHazard());
            Hazards.Add(new OscillationHazard());
            Hazards.Add(new IcingHazard());
            Hazards.Add(new DenseCloudHazard());
            Hazards.Add(new TerrainHazard());
            Hazards.Add(new LoadsHazard());
        }

        public HazardEffects Update(HazardContext c)
        {
            Effects.Reset();
            foreach (var h in Hazards) h.Update(c, Effects);
            return Effects;
        }

        /// <summary>Most severe current status for the HUD banner (warnings before cautions).</summary>
        public IEnumerable<HazardStatus> ActiveStatuses()
        {
            foreach (var lvl in new[] { HazardLevel.Warning, HazardLevel.Caution })
                foreach (var h in Hazards)
                    if (h.Status.Level == lvl) yield return h.Status;
        }

        public int TotalEncounters { get { int n = 0; foreach (var h in Hazards) n += h.Encounters; return n; } }
        public int TotalRecoveries { get { int n = 0; foreach (var h in Hazards) n += h.Recoveries; return n; } }
    }

    /// <summary>Delays control inputs (EM interference). Deterministic ring buffer by sim time.</summary>
    public sealed class InputDelayLine
    {
        readonly Queue<KeyValuePair<double, ControlInput>> queue = new Queue<KeyValuePair<double, ControlInput>>();
        ControlInput current;

        public ControlInput Process(ControlInput input, double time, double delay)
        {
            queue.Enqueue(new KeyValuePair<double, ControlInput>(time, input));
            while (queue.Count > 0 && queue.Peek().Key <= time - delay) current = queue.Dequeue().Value;
            if (delay <= 0) { queue.Clear(); current = input; }
            // Safety-relevant discrete commands are never delayed.
            current.Throttle = input.Throttle;
            current.Heater = input.Heater;
            return current;
        }
    }

    /// <summary>Navigation estimate shown on the HUD. Physics truth is untouched; this only perturbs
    /// what the player is told about the landing-zone relative solution.</summary>
    public sealed class NavigationFilter
    {
        double ex, ez, phase;
        public double OffsetEast => ex;
        public double OffsetNorth => ez;
        public double Uncertainty { get; private set; }
        public bool Available { get; private set; } = true;
        public double LastFixTime { get; private set; }

        public void Update(HazardEffects fx, double time, double dt)
        {
            Available = fx.NavAvailable;
            if (Available) LastFixTime = time;
            Uncertainty += (fx.NavNoise - Uncertainty) * MathUtil.Approach(0.7, dt);
            phase += dt;
            double tx = Math.Sin(phase * 0.37 + 1.1) * 0.7 + Math.Sin(phase * 1.13) * 0.3;
            double tz = Math.Sin(phase * 0.29 + 2.3) * 0.7 + Math.Sin(phase * 0.91 + 0.5) * 0.3;
            ex = tx * Uncertainty; ez = tz * Uncertainty;
        }
    }
}
