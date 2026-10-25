using System;

namespace OrbitalDescent.Core
{
    public enum MissionPhase { Aiming, Descent, Finished }

    /// <summary>Descent phase names used by the HUD altitude bar and the audio/visual director.</summary>
    public enum FlightRegime { Orbital, UpperAtmosphere, CloudDescent, FinalApproach, Surface }

    /// <summary>
    /// One attempt at a level: owns the world, vehicle model, predictor and hazards, and advances the
    /// simulation at a fixed step regardless of frame rate. Engine-independent so it can be tested
    /// headless and driven by Unity.
    /// </summary>
    public sealed class MissionSession
    {
        public readonly LevelDefinition Level;
        public readonly VehicleConfig Vehicle;
        public readonly World World;
        public readonly FlightModel Model;
        public readonly Predictor Predictor;
        public readonly HazardSystem Hazards = new HazardSystem();
        public readonly NavigationFilter Nav = new NavigationFilter();
        public readonly FlightRecord Record = new FlightRecord();
        readonly InputDelayLine delayLine = new InputDelayLine();
        readonly HazardContext ctx = new HazardContext();

        public MissionPhase Phase { get; private set; } = MissionPhase.Aiming;
        public FlightState State { get; private set; }
        public FlightTelemetry Telemetry { get; private set; }
        public ScoreResult Result { get; private set; }
        public double FixedStep = FlightModel.DefaultStep;
        public double DifficultyScale = 1.0;
        public int PredictionStepsPerTick = 400;
        public double PredictionInterval = 0.5;
        public string LastMessage = "";

        public double AimHeadingDeg { get; private set; }
        public double AimFlightPathDeg { get; private set; }
        public double ReleaseAltitude => Level.ReleaseAltitude;
        public bool RebootRequested;
        public event Action<MissionSession> LightningStrike;
        public event Action<MissionSession> Finished;

        Vec3d nominalPosition, nominalHorizontal;
        double accumulator, predictionTimer;
        ControlInput lastInput;

        public MissionSession(LevelDefinition level, VehicleConfig vehicle, NoiseVolume shape, NoiseMap coverage, double difficulty = 1.0)
        {
            Level = level;
            Vehicle = vehicle;
            DifficultyScale = difficulty;
            World = new World(level, shape, coverage);
            World.Wind.HazardScale = difficulty;
            Model = new FlightModel(vehicle, World);
            Predictor = new Predictor(vehicle, World);
            SolveRelease();
            SetAim(0, 0);
        }

        // ---------------- Release planning ----------------

        Vec3d HeadingVector(double headingDeg)
        {
            double h = headingDeg * Math.PI / 180.0;
            return new Vec3d(Math.Sin(h), 0, Math.Cos(h));
        }

        FlightState BuildReleaseState(Vec3d position, double headingDeg, double fpaDeg)
        {
            Vec3d up = Planet.Up(position);
            Vec3d horiz = HeadingVector(headingDeg).PerpendicularTo(up).Normalized;
            double f = fpaDeg * Math.PI / 180.0;
            Vec3d vel = (horiz * Math.Cos(f) + up * Math.Sin(f)) * Level.ReleaseSpeed;
            var s = Model.CreateState(position, vel);
            return s;
        }

        /// <summary>Places the release point up-range so that the un-steered nominal trajectory lands at
        /// the level's deliberate aim bias from the zone (the player must correct it).</summary>
        void SolveRelease()
        {
            Vec3d d = HeadingVector(Level.ReleaseHeadingDeg);
            Vec3d cross = new Vec3d(d.z, 0, -d.x);
            double uprange = 120000, crossOff = 0;
            for (int i = 0; i < 6; i++)
            {
                Vec3d h = -d * uprange + cross * crossOff;
                Vec3d pos = Planet.PointAbove(h.x, h.z, Level.ReleaseAltitude);
                var r = Predictor.RunToCompletion(BuildReleaseState(pos, Level.ReleaseHeadingDeg, Level.ReleaseFlightPathDeg), true);
                Planet.Horizontal(r.ImpactPoint, out double ix, out double iz);
                Planet.Horizontal(World.ZonePosition(r.ImpactTime), out double zx, out double zz);
                double ex = ix - zx, ez = iz - zz;
                double along = ex * d.x + ez * d.z;
                double side = ex * cross.x + ez * cross.z;
                uprange += along - Level.AimBiasDown;
                crossOff -= side - Level.AimBiasCross;
                if (Math.Abs(along - Level.AimBiasDown) < 5 && Math.Abs(side - Level.AimBiasCross) < 5) break;
            }
            Vec3d hf = -d * uprange + cross * crossOff;
            nominalPosition = Planet.PointAbove(hf.x, hf.z, Level.ReleaseAltitude);
            nominalHorizontal = d;
        }

        /// <summary>Adjusts the release direction within the level's limits and re-predicts.</summary>
        public PredictionResult SetAim(double headingOffsetDeg, double fpaOffsetDeg)
        {
            if (Phase != MissionPhase.Aiming) return Predictor.Latest;
            AimHeadingDeg = MathUtil.Clamp(headingOffsetDeg, -Level.AimHeadingRangeDeg, Level.AimHeadingRangeDeg);
            AimFlightPathDeg = MathUtil.Clamp(fpaOffsetDeg, -Level.AimFlightPathRangeDeg, Level.AimFlightPathRangeDeg);
            State = BuildReleaseState(nominalPosition, Level.ReleaseHeadingDeg + AimHeadingDeg, Level.ReleaseFlightPathDeg + AimFlightPathDeg);
            Telemetry = Model.Measure(State);
            return Predictor.RunToCompletion(State, true);
        }

        public void Release()
        {
            if (Phase != MissionPhase.Aiming) return;
            Phase = MissionPhase.Descent;
            accumulator = 0;
            predictionTimer = 0;
            LastMessage = "RELEASED";
        }

        // ---------------- Commands ----------------

        public bool RequestChute()
        {
            if (Phase != MissionPhase.Descent) return false;
            if (!Model.ChuteDeployAllowed(State, Telemetry, out string why)) { LastMessage = why; return false; }
            Model.DeployChute(State, Telemetry);
            LastMessage = "CHUTE DEPLOYED";
            return true;
        }

        public void CutChute()
        {
            Model.ReleaseChute(State);
            LastMessage = "CHUTE RELEASED";
        }

        // ---------------- Simulation ----------------

        /// <summary>Advances by real time * warp using fixed steps. Caps work per frame so a long hitch
        /// slows the simulation instead of destabilising it. Returns steps taken.</summary>
        public int Tick(double realDt, ControlInput input, double timeWarp = 1.0, int maxSteps = 64)
        {
            if (Phase != MissionPhase.Descent) return 0;
            accumulator += Math.Min(realDt, 0.25) * timeWarp;
            int steps = 0;
            while (accumulator >= FixedStep && steps < maxSteps && Phase == MissionPhase.Descent)
            {
                StepFixed(input);
                accumulator -= FixedStep;
                steps++;
            }
            if (steps >= maxSteps) accumulator = 0;
            UpdatePrediction(realDt * timeWarp);
            return steps;
        }

        public void StepFixed(ControlInput rawInput)
        {
            if (Phase != MissionPhase.Descent) return;
            var fx = Hazards.Effects;
            ControlInput input = delayLine.Process(rawInput, State.Time, fx.InputDelay);
            lastInput = input;
            var t = Model.Step(State, input, fx.Controls, FixedStep);
            Telemetry = Model.Measure(State);

            ctx.State = State; ctx.Telemetry = Telemetry; ctx.World = World; ctx.Vehicle = Vehicle;
            ctx.Prediction = Predictor.Latest; ctx.Input = input; ctx.Dt = FixedStep; ctx.DifficultyScale = DifficultyScale;
            ctx.RebootRequested = RebootRequested;
            RebootRequested = false;
            Hazards.Update(ctx);
            if (fx.LightningStrike) { Record.Strikes++; LightningStrike?.Invoke(this); }
            Nav.Update(fx, State.Time, FixedStep);
            Record.Accumulate(t, State, rawInput, FixedStep);
            Record.FuelUsed = Vehicle.FuelMass - State.Fuel;

            if (State.Contact || State.Integrity <= 0 || State.NumericalFault) Finish();
        }

        void UpdatePrediction(double simDt)
        {
            predictionTimer -= simDt;
            if (!Predictor.Running && predictionTimer <= 0)
            {
                Predictor.Begin(State, lastInput.Sas);
                predictionTimer = PredictionInterval;
            }
            if (Predictor.Running) Predictor.Advance(PredictionStepsPerTick);
        }

        void Finish()
        {
            if (Phase == MissionPhase.Finished) return;
            Phase = MissionPhase.Finished;
            Record.HazardEncounters = Hazards.TotalEncounters;
            Record.HazardRecoveries = Hazards.TotalRecoveries;
            if (State.NumericalFault && string.IsNullOrEmpty(State.FailureReason)) State.FailureReason = "Simulation fault";
            Result = ScoreCalculator.Evaluate(Level, Vehicle, State, Record, World.ZonePosition(State.Time));
            Finished?.Invoke(this);
        }

        // ---------------- Derived views for the HUD ----------------

        /// <summary>Descent progress 0..1 against a stable reference (release altitude to zone elevation).</summary>
        public double DescentProgress =>
            MathUtil.Clamp01((Level.ReleaseAltitude - Telemetry.Altitude) / (Level.ReleaseAltitude - Level.ZoneElevation));

        public FlightRegime Regime
        {
            get
            {
                double a = Telemetry.Altitude, agl = Telemetry.AltitudeAboveGround;
                if (State != null && State.Contact) return FlightRegime.Surface;
                if (a > 100000) return FlightRegime.Orbital;
                if (agl < 2500) return FlightRegime.FinalApproach;
                double cloudTop = 0;
                foreach (var c in Level.Clouds) if (c.Type != CloudType.Cirrus) cloudTop = Math.Max(cloudTop, c.Top);
                if (a < cloudTop + 2000) return FlightRegime.CloudDescent;
                return FlightRegime.UpperAtmosphere;
            }
        }

        public Vec3d ZonePosition => World.ZonePosition(State.Time);
        public double DistanceToZone => Planet.SurfaceDistance(State.Position, ZonePosition);
    }
}
