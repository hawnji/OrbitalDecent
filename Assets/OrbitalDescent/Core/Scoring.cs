using System;

namespace OrbitalDescent.Core
{
    /// <summary>Accumulated performance measurements for one descent (all from the simulation).</summary>
    public sealed class FlightRecord
    {
        public double FlightTime;
        public double ControlEffort;      // integral of |steer| dt
        public double RateSquaredIntegral;// integral of |omega|^2 dt (only while q > 200 Pa)
        public double RateTime;
        public double MaxGLoad;
        public double PeakHeatFlux;
        public double PeakSkinTemp;
        public double FuelUsed;
        public bool ChuteUsed;
        public int Strikes;
        public int HazardEncounters;
        public int HazardRecoveries;

        public double RmsRate => RateTime > 0 ? Math.Sqrt(RateSquaredIntegral / RateTime) : 0;
        public double MeanEffort => FlightTime > 0 ? ControlEffort / FlightTime : 0;

        public void Accumulate(FlightTelemetry t, FlightState s, ControlInput input, double dt)
        {
            FlightTime += dt;
            ControlEffort += Math.Min(1.0, input.Steer.Length) * dt + input.Throttle * dt * 0.5;
            if (t.DynamicPressure > 200)
            {
                RateSquaredIntegral += s.AngularVelocity.SqrLength * dt;
                RateTime += dt;
            }
            MaxGLoad = Math.Max(MaxGLoad, t.GLoad);
            PeakHeatFlux = Math.Max(PeakHeatFlux, t.HeatFlux);
            PeakSkinTemp = Math.Max(PeakSkinTemp, s.SkinTemperature);
            if (s.Chute != ChuteState.Stowed) ChuteUsed = true;
        }
    }

    public enum MissionOutcome { Landed, DeckLanding, Splashdown, Crashed, BurnedUp, StructuralFailure }

    public sealed class ScoreResult
    {
        public MissionOutcome Outcome;
        public bool Success;
        public double ErrorMeters;
        public double TouchdownSpeed;
        public int Stars;
        public bool ObjectiveMet;
        public int AccuracyPoints, SoftnessPoints, StabilityPoints, EfficiencyPoints, HazardPoints, ObjectivePoints;
        public int Total => AccuracyPoints + SoftnessPoints + StabilityPoints + EfficiencyPoints + HazardPoints + ObjectivePoints;
        public string FailureReason = "";
        public string StarReason1 = "", StarReason2 = "", StarReason3 = "";
    }

    public static class ScoreCalculator
    {
        public static ScoreResult Evaluate(LevelDefinition L, VehicleConfig v, FlightState s, FlightRecord r, Vec3d zoneAtTouchdown)
        {
            var res = new ScoreResult();
            res.TouchdownSpeed = s.ContactSpeed;
            res.ErrorMeters = s.Contact ? Planet.SurfaceDistance(s.ContactPoint, zoneAtTouchdown) : double.PositiveInfinity;
            res.FailureReason = s.FailureReason;

            if (!s.Contact || s.Integrity <= 0)
                res.Outcome = s.FailureReason.StartsWith("Heat") ? MissionOutcome.BurnedUp : MissionOutcome.StructuralFailure;
            else if (s.ContactKind == ContactResult.Crashed) res.Outcome = MissionOutcome.Crashed;
            else if (s.ContactKind == ContactResult.DeckLanding) res.Outcome = MissionOutcome.DeckLanding;
            else if (s.ContactKind == ContactResult.Splashdown) res.Outcome = MissionOutcome.Splashdown;
            else res.Outcome = MissionOutcome.Landed;

            bool survived = res.Outcome == MissionOutcome.Landed || res.Outcome == MissionOutcome.DeckLanding || res.Outcome == MissionOutcome.Splashdown;
            res.Success = survived && res.ErrorMeters <= L.CompleteRadius;

            res.ObjectiveMet = ObjectiveMet(L, v, s, r, res);
            res.StarReason1 = $"Touch down intact within {Units.Distance(L.CompleteRadius, false)}";
            res.StarReason2 = $"Within {Units.Distance(L.GoodRadius, false)} at under {L.SoftSpeed:F0} m/s";
            res.StarReason3 = $"Within {Units.Distance(L.PreciseRadius, false)} and: {L.Objective.Text}";
            if (res.Success)
            {
                res.Stars = 1;
                if (res.ErrorMeters <= L.GoodRadius && res.TouchdownSpeed <= L.SoftSpeed) res.Stars = 2;
                if (res.Stars == 2 && res.ErrorMeters <= L.PreciseRadius && res.ObjectiveMet) res.Stars = 3;
            }

            if (survived)
            {
                res.AccuracyPoints = (int)Math.Round(4000 * Math.Exp(-res.ErrorMeters / Math.Max(1, L.GoodRadius)));
                res.SoftnessPoints = (int)Math.Round(1500 * MathUtil.Clamp01(1.0 - res.TouchdownSpeed / v.MaxTouchdownSpeed));
                res.StabilityPoints = (int)Math.Round(1000 * Math.Exp(-r.RmsRate / 0.35));
                double fuelLeft = v.FuelMass > 0 ? s.Fuel / v.FuelMass : 0;
                res.EfficiencyPoints = (int)Math.Round(600 * (1.0 - MathUtil.Clamp01(r.MeanEffort)) + 400 * fuelLeft);
                res.HazardPoints = Math.Min(1500, 150 * r.HazardRecoveries);
                res.ObjectivePoints = res.ObjectiveMet ? 1000 : 0;
            }
            return res;
        }

        static bool ObjectiveMet(LevelDefinition L, VehicleConfig v, FlightState s, FlightRecord r, ScoreResult res)
        {
            var o = L.Objective;
            switch (o.Type)
            {
                case ObjectiveType.FuelRemaining: return v.FuelMass > 0 && s.Fuel / v.FuelMass >= o.Value;
                case ObjectiveType.NoParachute: return !r.ChuteUsed;
                case ObjectiveType.LowRotation: return r.RmsRate <= o.Value;
                case ObjectiveType.SurviveStrikes: return r.Strikes >= (int)o.Value;
                case ObjectiveType.DeckLanding: return res.Outcome == MissionOutcome.DeckLanding;
                default: return true;
            }
        }
    }

    public static class Units
    {
        public static bool Imperial;

        public static string Altitude(double m, bool? imperial = null)
        {
            bool imp = imperial ?? Imperial;
            if (imp)
            {
                double ft = m * 3.28084;
                return Math.Abs(ft) >= 10000 ? $"{ft / 1000:F1} kft" : $"{ft:F0} ft";
            }
            return Math.Abs(m) >= 10000 ? $"{m / 1000:F1} km" : (Math.Abs(m) >= 1000 ? $"{m / 1000:F2} km" : $"{m:F0} m");
        }

        public static string Distance(double m, bool? imperial = null)
        {
            bool imp = imperial ?? Imperial;
            if (imp) return m >= 1609 ? $"{m / 1609.344:F1} mi" : $"{m * 3.28084:F0} ft";
            return m >= 1000 ? $"{m / 1000:F1} km" : $"{m:F0} m";
        }

        public static string Speed(double ms, bool? imperial = null)
        {
            bool imp = imperial ?? Imperial;
            return imp ? $"{ms * 3.28084:F0} ft/s" : $"{ms:F0} m/s";
        }

        public static string SpeedSecondary(double ms, bool? imperial = null)
        {
            bool imp = imperial ?? Imperial;
            return imp ? $"{ms * 2.23694:F0} mph" : $"{ms * 3.6:F0} km/h";
        }
    }
}
