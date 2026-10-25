using System.Collections.Generic;

namespace OrbitalDescent.Core
{
    /// <summary>
    /// Vehicle parameters. Fields under "Physical" are genuine physical properties used by the
    /// force model. Fields under "Gameplay" are explicit abstractions (assistance, resistances)
    /// and are labelled as such in the UI.
    /// Body axis convention: +B points from the heat shield / engine end toward the top of the
    /// probe. In stable descent the probe falls shield-first, so B is opposite the airflow.
    /// </summary>
    public sealed class VehicleConfig
    {
        public string Id = "";
        public string Name = "";
        public string Tagline = "";

        // ---- Physical ----
        public double DryMass;            // kg
        public double FuelMass;           // kg (shared by RCS and the descent engine)
        public double RefArea;            // m^2 (shield frontal area)
        public double RefLength;          // m (moment reference length)
        public double AxialCoeff;         // C_A0: axial force coefficient at zero incidence (blunt-body drag)
        public double NormalSlope;        // C_N per (sin a cos a); negative for blunt bodies (adds lift toward the top's lean)
        public double CrossflowCoeff;     // C_N per sin^2 a: crossflow drag when broadside
        public double StabilityCoeff;     // Cm_alpha (per sin a), restoring when positive
        public double PitchDampCoeff;     // Cm_q damping derivative
        public double Inertia;            // kg m^2 about the transverse axis
        public double RcsTorque;          // N m
        public double RcsLever = 0.5;     // m, thruster moment arm (sets RCS propellant use)
        public double RcsIsp = 220;       // s
        public double FinCoeff;           // fin torque per (q A L)
        public double FinMaxTorque;       // N m structural/actuator saturation
        public double ActuatorTimeConstant = 0.08; // s
        public double EngineThrust;       // N, descent engine (fires along +B)
        public double EngineIsp = 240;    // s
        public double ChuteCdA;           // m^2 fully open
        public double ChuteMaxDeployQ = 4500; // Pa, above this the canopy would tear
        public double ChuteMaxDeployAltitude = 7000; // m above ground
        public double NoseRadius;         // m, shield radius of curvature (heating)
        public double ShieldArealHeatCapacity = 2500; // J/(m^2 K) lumped shield skin
        public double Emissivity = 0.85;
        public double MaxSkinTemp;        // K before ablation damage
        public double MaxGLoad;           // g before structural damage
        public double MaxTouchdownSpeed;  // m/s survivable impact
        public double MaxIceFraction = 0.06;

        // ---- Gameplay abstractions ----
        public double GuidanceReliability; // 0..1, shortens electronic disruptions
        public double PrecisionReserve;    // s of boosted actuator authority (Phantom)
        public double PrecisionBoost = 1.0;// authority multiplier while the reserve is used
        public double SasGain = 1.0;       // stability-assist rate-damping gain

        public double TotalMass => DryMass + FuelMass;
        public double BallisticCoefficient => TotalMass / (AxialCoeff * RefArea);

        public static readonly VehicleConfig Dart = new VehicleConfig
        {
            Id = "dart", Name = "Dart", Tagline = "Light, nimble capsule. Wind pushes it around.",
            DryMass = 230, FuelMass = 45, RefArea = 0.785, RefLength = 1.2,
            AxialCoeff = 1.15, NormalSlope = -0.25, CrossflowCoeff = 1.2, StabilityCoeff = 0.10, PitchDampCoeff = 0.6,
            Inertia = 32, RcsTorque = 70, FinCoeff = 0.07, FinMaxTorque = 2500,
            EngineThrust = 7000, ChuteCdA = 13, ChuteMaxDeployQ = 4000,
            NoseRadius = 0.6, MaxSkinTemp = 1700, MaxGLoad = 16, MaxTouchdownSpeed = 9,
            GuidanceReliability = 0.2, PrecisionReserve = 0, SasGain = 1.0,
        };

        public static readonly VehicleConfig Hammer = new VehicleConfig
        {
            Id = "hammer", Name = "Hammer", Tagline = "Heavy and fast. Plan early; it turns slowly.",
            DryMass = 1100, FuelMass = 150, RefArea = 1.54, RefLength = 2.2,
            AxialCoeff = 1.05, NormalSlope = -0.2, CrossflowCoeff = 1.2, StabilityCoeff = 0.15, PitchDampCoeff = 0.8,
            Inertia = 520, RcsTorque = 260, RcsLever = 0.8, FinCoeff = 0.06, FinMaxTorque = 9000,
            EngineThrust = 26000, ChuteCdA = 48, ChuteMaxDeployQ = 5000,
            NoseRadius = 1.0, MaxSkinTemp = 1900, MaxGLoad = 14, MaxTouchdownSpeed = 7,
            GuidanceReliability = 0.35, PrecisionReserve = 0, SasGain = 1.0,
        };

        public static readonly VehicleConfig Phantom = new VehicleConfig
        {
            Id = "phantom", Name = "Phantom", Tagline = "Precision lander with a short boost reserve.",
            DryMass = 480, FuelMass = 75, RefArea = 1.0, RefLength = 1.6,
            AxialCoeff = 1.1, NormalSlope = -0.3, CrossflowCoeff = 1.2, StabilityCoeff = 0.12, PitchDampCoeff = 0.75,
            Inertia = 110, RcsTorque = 140, RcsLever = 0.6, FinCoeff = 0.08, FinMaxTorque = 5000,
            EngineThrust = 12500, ChuteCdA = 24, ChuteMaxDeployQ = 4500,
            NoseRadius = 0.75, MaxSkinTemp = 1800, MaxGLoad = 15, MaxTouchdownSpeed = 8,
            GuidanceReliability = 0.75, PrecisionReserve = 10, PrecisionBoost = 2.5, SasGain = 1.3,
        };

        public static readonly VehicleConfig Wildcard = new VehicleConfig
        {
            Id = "wildcard", Name = "Wildcard", Tagline = "Biconic lifting body. Huge cross-range, twitchy.",
            DryMass = 600, FuelMass = 60, RefArea = 1.2, RefLength = 3.0,
            AxialCoeff = 0.85, NormalSlope = -1.3, CrossflowCoeff = 1.0, StabilityCoeff = 0.03, PitchDampCoeff = 0.25,
            Inertia = 160, RcsTorque = 180, RcsLever = 0.9, FinCoeff = 0.13, FinMaxTorque = 9000,
            EngineThrust = 14000, ChuteCdA = 26, ChuteMaxDeployQ = 4000,
            NoseRadius = 0.5, MaxSkinTemp = 1750, MaxGLoad = 12, MaxTouchdownSpeed = 8,
            GuidanceReliability = 0.3, PrecisionReserve = 0, SasGain = 0.7,
        };

        public static readonly IReadOnlyList<VehicleConfig> All = new[] { Dart, Hammer, Phantom, Wildcard };

        public static VehicleConfig ById(string id)
        {
            foreach (var v in All) if (v.Id == id) return v;
            return Dart;
        }

        public VehicleConfig Clone() => (VehicleConfig)MemberwiseClone();
    }
}
