using System;
using System.Collections.Generic;

namespace OrbitalDescent.Core
{
    public sealed class PredictionResult
    {
        public bool Valid;
        public Vec3d ImpactPoint;
        public double ImpactTime;          // absolute sim time
        public double ImpactSpeed;
        public double ErrorToZone;         // surface distance to the zone at impact time, m
        public double ErrorEast, ErrorNorth; // impact minus zone, local horizontal, m
        public readonly List<Vec3d> Path = new List<Vec3d>();
    }

    /// <summary>
    /// Trajectory prediction that runs the *same* FlightModel forward (deterministic mode: mean wind,
    /// no turbulence, no damage) from a copy of the current state with neutral controls, keeping the
    /// current SAS setting and parachute state. It can run all at once (tests, release planning)
    /// or be advanced incrementally a fixed number of steps per frame.
    /// </summary>
    public sealed class Predictor
    {
        public double MaxTime = 1500;
        public double PathSampleInterval = 2.0;

        readonly FlightModel model;
        FlightState sim;
        ControlInput input;
        PredictionResult working;
        double nextSample, startTime;
        public PredictionResult Latest { get; private set; } = new PredictionResult();
        public bool Running => sim != null;

        public Predictor(VehicleConfig vehicle, World world)
        {
            model = new FlightModel(vehicle, world) { Deterministic = true };
        }

        public static double StepFor(FlightState s, double altitude)
        {
            // Large steps where nothing changes quickly, finer steps where aerodynamic stiffness is high.
            if (altitude > 70000) return 0.1;
            if (altitude > 2000) return 0.05;
            return 1.0 / 60.0;
        }

        public void Begin(FlightState current, bool sas)
        {
            sim = current.Clone();
            sim.Contact = false;
            input = new ControlInput { Sas = sas };
            working = new PredictionResult();
            nextSample = sim.Time;
            startTime = sim.Time;
        }

        /// <summary>Advances up to maxSteps; returns true when the prediction finished.</summary>
        public bool Advance(int maxSteps)
        {
            if (sim == null) return true;
            double tEnd = startTime + MaxTime;
            for (int i = 0; i < maxSteps; i++)
            {
                if (sim.Time >= nextSample)
                {
                    working.Path.Add(sim.Position);
                    nextSample = sim.Time + PathSampleInterval;
                }
                double dt = StepFor(sim, Planet.Altitude(sim.Position));
                model.Step(sim, input, ControlModifiers.Nominal, dt);
                if (sim.Contact || sim.Time > tEnd || sim.NumericalFault)
                {
                    Finish();
                    return true;
                }
            }
            return false;
        }

        void Finish()
        {
            working.Valid = sim.Contact && !sim.NumericalFault;
            working.ImpactPoint = sim.ContactPoint;
            working.ImpactTime = sim.Time;
            working.ImpactSpeed = sim.ContactSpeed;
            working.Path.Add(sim.Position);
            Vec3d zone = model.World.ZonePosition(sim.Time);
            working.ErrorToZone = Planet.SurfaceDistance(sim.ContactPoint, zone);
            Planet.Horizontal(sim.ContactPoint, out double ix, out double iz);
            Planet.Horizontal(zone, out double zx, out double zz);
            working.ErrorEast = ix - zx;
            working.ErrorNorth = iz - zz;
            Latest = working;
            sim = null;
        }

        public PredictionResult RunToCompletion(FlightState current, bool sas)
        {
            Begin(current, sas);
            while (!Advance(100000)) { }
            return Latest;
        }
    }
}
