using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OrbitalDescent.Core;

// Usage:
//   dotnet run -- simulate <levelIndex> [vehicleId]     autopilot run with a telemetry trace
//   dotnet run -- campaign                               autopilot run of every level, summary table
//   dotnet run -- export <outDir>                        noise volumes + level weather JSON for tools/ShaderPreview
//   dotnet run -- bench                                  cost of one simulation step and one predictor step
static class Program
{
    static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string cmd = args.Length > 0 ? args[0] : "campaign";
        var shape = NoiseVolume.GenerateCloudShape(64, 1);
        var cov = NoiseMap.GenerateCoverage(256, 2);
        switch (cmd)
        {
            case "simulate":
                Simulate(Levels.All[int.Parse(args[1])], args.Length > 2 ? args[2] : null, shape, cov, true);
                return 0;
            case "campaign":
                foreach (var l in Levels.All)
                    foreach (var v in VehicleConfig.All)
                        Simulate(l, v.Id, shape, cov, false);
                return 0;
            case "bench":
                Bench(shape, cov);
                return 0;
            case "export":
                Export(args.Length > 1 ? args[1] : "out", shape, cov);
                return 0;
        }
        Console.Error.WriteLine("unknown command");
        return 1;
    }

    /// <summary>Times FlightModel steps in full (hazard-free) and predictor (deterministic) mode on a
    /// cloudy level, so the per-frame CPU budget on phones can be estimated from desktop numbers.</summary>
    static void Bench(NoiseVolume shape, NoiseMap cov)
    {
        var lvl = Levels.All[3];
        var world = new World(lvl, shape, cov);
        foreach (bool det in new[] { false, true })
        {
            var model = new FlightModel(VehicleConfig.Dart, world) { Deterministic = det };
            var s = model.CreateState(Planet.PointAbove(-3000, -2000, 6000), new Vec3d(40, -160, 30));
            var input = new ControlInput { Sas = true };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int n = 0;
            while (sw.ElapsedMilliseconds < 1500)
            {
                for (int i = 0; i < 200; i++, n++)
                {
                    model.Step(s, input, ControlModifiers.Nominal, FlightModel.DefaultStep);
                    if (s.Contact || Planet.Altitude(s.Position) < 2500)
                        s = model.CreateState(Planet.PointAbove(-3000, -2000, 6000), new Vec3d(40, -160, 30));
                }
            }
            double us = sw.Elapsed.TotalMilliseconds * 1000.0 / n;
            Console.WriteLine($"{(det ? "predictor step" : "simulation step"),-16} {us,6:F2} us  ({1e6 / us:F0} steps/s)");
        }
    }

    static void Simulate(LevelDefinition lvl, string vehicleId, NoiseVolume shape, NoiseMap cov, bool trace)
    {
        var v = VehicleConfig.ById(vehicleId ?? lvl.RecommendedVehicle);
        var m = new MissionSession(lvl, v, shape, cov);
        m.Release();
        var ap = new Autopilot();
        double next = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int steps = 0;
        while (m.Phase == MissionPhase.Descent && m.State.Time < 2000)
        {
            var input = ap.Update(m);
            steps += m.Tick(1.0 / 60.0, input, 1.0, 64);
            if (trace && m.State.Time >= next)
            {
                var t = m.Telemetry;
                var hz = string.Join("; ", m.Hazards.ActiveStatuses().Select(h => h.Message));
                Console.WriteLine($"t={m.State.Time,6:F1} alt={t.Altitude,8:F0} agl={t.AltitudeAboveGround,7:F0} v={t.Speed,7:F1} vs={t.VerticalSpeed,7:F1} M={t.Mach,4:F1} q={t.DynamicPressure,6:F0} heat={t.HeatFlux / 1000,5:F0}kW T={m.State.SkinTemperature,5:F0} g={t.GLoad,4:F1} fuel={m.State.Fuel,5:F1} chute={m.State.Chute,-8} miss={m.Predictor.Latest.ErrorToZone,6:F0} cloud={t.CloudDensity:F2} turb={t.TurbulenceIntensity:F2} {hz}");
                next = m.State.Time + (t.AltitudeAboveGround < 400 ? 1 : 10);
            }
        }
        var r = m.Result;
        double ms = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"{lvl.Name,-18} {v.Name,-9} {r?.Outcome,-11} err {r?.ErrorMeters,6:F0} m  touchdown {r?.TouchdownSpeed,5:F1} m/s  stars {r?.Stars}  score {r?.Total,5}  time {m.State.Time,4:F0} s  fuel {m.State.Fuel,5:F1}  strikes {m.Record.Strikes}  peakT {m.Record.PeakSkinTemp,5:F0} K  maxG {m.Record.MaxGLoad,4:F1}  ({steps / (ms / 1000.0) / 1000.0:F0}k steps/s) {r?.FailureReason}");
    }

    static void Export(string dir, NoiseVolume shape, NoiseMap cov)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "shape64.bin"), shape.Data);
        File.WriteAllBytes(Path.Combine(dir, "coverage256.bin"), cov.Data);
        var sb = new StringBuilder("[\n");
        for (int i = 0; i < Levels.All.Count; i++)
        {
            var l = Levels.All[i];
            sb.Append("  {\"name\":\"").Append(l.Name).Append("\",\"ocean\":").Append(l.Ocean ? 1 : 0)
              .Append(",\"drift\":[").Append(l.CloudDriftEast).Append(',').Append(l.CloudDriftNorth).Append("],\"layers\":[");
            sb.Append(string.Join(",", l.Clouds.Select(c => $"[{c.Base},{c.Top},{c.Coverage},{c.Density},{(int)c.Type},{c.ShapeScale}]")));
            sb.Append("],\"storms\":[");
            sb.Append(string.Join(",", l.Storms.Select(s => $"[{s.X},{s.Z},{s.Radius},{s.Strength}]")));
            sb.Append("]}").Append(i < Levels.All.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("]\n");
        File.WriteAllText(Path.Combine(dir, "levels.json"), sb.ToString());
        Console.WriteLine($"exported to {dir}");
    }
}
