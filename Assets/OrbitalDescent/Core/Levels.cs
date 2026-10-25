using System.Collections.Generic;

namespace OrbitalDescent.Core
{
    /// <summary>The handcrafted campaign. Each level introduces one mechanic before combining them.</summary>
    public static class Levels
    {
        public static readonly IReadOnlyList<LevelDefinition> All = Build();

        static List<LevelDefinition> Build()
        {
            var list = new List<LevelDefinition>();

            list.Add(new LevelDefinition
            {
                Id = "first-fall", Name = "First Fall",
                Briefing = "Calm skies over the Kessel plains. Learn to lean the probe: tilt toward where you want to go once the air thickens. Deploy the parachute below 7 km, then use the engine to touch down softly.",
                ReleaseSpeed = 1500, ReleaseFlightPathDeg = -30, AimBiasCross = 900, AimBiasDown = -1800,
                Clouds = new[]
                {
                    new CloudLayer { Type = CloudType.Cirrus, Base = 9000, Top = 10500, Coverage = 0.35, Density = 0.25, ShapeScale = 9000 },
                    new CloudLayer { Type = CloudType.Cumulus, Base = 1600, Top = 3200, Coverage = 0.35, Density = 0.9, ShapeScale = 3500 },
                },
                SurfaceWindSpeed = 4, JetSpeed = 15, TurbulenceBase = 0.02, ConvectiveTurbulence = 0.35,
                ZoneElevation = 180, MountainHeight = 250, MountainScale = 12000, ValleyRadius = 4000,
                CompleteRadius = 5000, GoodRadius = 500, PreciseRadius = 80, SoftSpeed = 5,
                Objective = new Objective { Type = ObjectiveType.FuelRemaining, Value = 0.25, Text = "keep 25% fuel" },
                RecommendedVehicle = "dart", WeatherSeed = 11, TerrainSeed = 3,
            });

            list.Add(new LevelDefinition
            {
                Id = "crosswind", Name = "Crosswind",
                Briefing = "A strong jet stream and a sharp shear layer near 9 km will push you sideways. Watch the predicted touchdown marker and correct before the shear, not after.",
                ReleaseSpeed = 1600, ReleaseFlightPathDeg = -28, ReleaseHeadingDeg = 20, AimBiasCross = -1500, AimBiasDown = 1200,
                Clouds = new[]
                {
                    new CloudLayer { Type = CloudType.Cirrus, Base = 10000, Top = 11800, Coverage = 0.55, Density = 0.3, ShapeScale = 9000 },
                    new CloudLayer { Type = CloudType.Cumulus, Base = 1800, Top = 3600, Coverage = 0.45, Density = 1.0, ShapeScale = 3500 },
                },
                SurfaceWindSpeed = 11, SurfaceWindDirDeg = 90, JetSpeed = 45, JetAltitude = 10500, WindVeerDegPer10km = 40,
                Shear = new[] { new ShearLayer { Altitude = 9000, Thickness = 350, DeltaEast = -28, DeltaNorth = 8 } },
                TurbulenceBase = 0.08,
                ZoneElevation = 320, MountainHeight = 450, ValleyRadius = 3000,
                CompleteRadius = 4500, GoodRadius = 450, PreciseRadius = 70, SoftSpeed = 5,
                Objective = new Objective { Type = ObjectiveType.LowRotation, Value = 0.25, Text = "keep rotation smooth (RMS < 0.25 rad/s)" },
                RecommendedVehicle = "dart", WeatherSeed = 21, TerrainSeed = 5,
            });

            list.Add(new LevelDefinition
            {
                Id = "cumulus-field", Name = "Cumulus Field",
                Briefing = "A deep, broken cumulus deck with supercooled cloud between 2 and 5 km. Expect turbulence and ice on the fins. Use the heater and look for gaps to see the pad.",
                ReleaseSpeed = 1550, ReleaseFlightPathDeg = -32, ReleaseHeadingDeg = -35, AimBiasCross = 1800, AimBiasDown = -1500,
                Clouds = new[]
                {
                    new CloudLayer { Type = CloudType.Cirrus, Base = 9500, Top = 11000, Coverage = 0.3, Density = 0.25, ShapeScale = 9000 },
                    new CloudLayer { Type = CloudType.Cumulus, Base = 1500, Top = 5200, Coverage = 0.7, Density = 1.2, ShapeScale = 3000 },
                    new CloudLayer { Type = CloudType.Stratus, Base = 600, Top = 1100, Coverage = 0.4, Density = 0.7, ShapeScale = 5000 },
                },
                SurfaceWindSpeed = 7, JetSpeed = 25, TurbulenceBase = 0.05, ConvectiveTurbulence = 0.75,
                ZoneElevation = 150, MountainHeight = 380, ValleyRadius = 2500,
                CompleteRadius = 4000, GoodRadius = 400, PreciseRadius = 60, SoftSpeed = 5,
                Objective = new Objective { Type = ObjectiveType.FuelRemaining, Value = 0.2, Text = "keep 20% fuel" },
                RecommendedVehicle = "hammer", WeatherSeed = 31, TerrainSeed = 8,
            });

            list.Add(new LevelDefinition
            {
                Id = "storm-front", Name = "Storm Front",
                Briefing = "Two thunderstorm cells straddle the approach. Static charge builds inside them; a strike knocks out fins and stability assist. Reboot the avionics fast, or route around the cells.",
                ReleaseSpeed = 1650, ReleaseFlightPathDeg = -27, ReleaseHeadingDeg = 60, AimBiasCross = 1200, AimBiasDown = 2000,
                Clouds = new[]
                {
                    new CloudLayer { Type = CloudType.Cirrus, Base = 10500, Top = 12500, Coverage = 0.6, Density = 0.35, ShapeScale = 10000 },
                    new CloudLayer { Type = CloudType.Storm, Base = 900, Top = 11500, Coverage = 1.0, Density = 1.4, ShapeScale = 4500 },
                    new CloudLayer { Type = CloudType.Cumulus, Base = 1400, Top = 3400, Coverage = 0.5, Density = 1.0, ShapeScale = 3000 },
                },
                Storms = new[]
                {
                    new StormCell { X = -2500, Z = -9000, Radius = 4500, Strength = 1.0 },
                    new StormCell { X = 5500, Z = -2500, Radius = 3500, Strength = 0.9 },
                },
                SurfaceWindSpeed = 9, SurfaceWindDirDeg = 200, JetSpeed = 30, TurbulenceBase = 0.08, ConvectiveTurbulence = 0.7,
                ZoneElevation = 240, MountainHeight = 400, ValleyRadius = 3000,
                CompleteRadius = 4000, GoodRadius = 400, PreciseRadius = 60, SoftSpeed = 5,
                Objective = new Objective { Type = ObjectiveType.NoParachute, Value = 0, Text = "land without the parachute" },
                RecommendedVehicle = "phantom", WeatherSeed = 41, TerrainSeed = 12,
            });

            list.Add(new LevelDefinition
            {
                Id = "highlands", Name = "Highlands",
                Briefing = "A narrow valley pad among 2 km peaks, with an EM interference band between 35 and 60 km that blurs navigation. Use the steep entry to get close early; the mountains do not forgive a long glide.",
                ReleaseSpeed = 1700, ReleaseFlightPathDeg = -35, ReleaseHeadingDeg = 135, AimBiasCross = -2200, AimBiasDown = -2400,
                Clouds = new[]
                {
                    new CloudLayer { Type = CloudType.Cirrus, Base = 9000, Top = 10800, Coverage = 0.45, Density = 0.3, ShapeScale = 9000 },
                    new CloudLayer { Type = CloudType.Cumulus, Base = 2600, Top = 4800, Coverage = 0.5, Density = 1.1, ShapeScale = 3000 },
                },
                SurfaceWindSpeed = 8, SurfaceWindDirDeg = 300, JetSpeed = 35, TurbulenceBase = 0.12, ConvectiveTurbulence = 0.6,
                Shear = new[] { new ShearLayer { Altitude = 5500, Thickness = 300, DeltaEast = 14, DeltaNorth = -18 } },
                EmBandLow = 35000, EmBandHigh = 60000, EmStrength = 1.0,
                ZoneElevation = 900, MountainHeight = 1500, MountainScale = 7000, ValleyRadius = 1400,
                CompleteRadius = 3000, GoodRadius = 300, PreciseRadius = 50, SoftSpeed = 5,
                Objective = new Objective { Type = ObjectiveType.LowRotation, Value = 0.3, Text = "keep rotation smooth (RMS < 0.3 rad/s)" },
                RecommendedVehicle = "wildcard", WeatherSeed = 51, TerrainSeed = 17,
            });

            list.Add(new LevelDefinition
            {
                Id = "blackout", Name = "Blackout Recovery",
                Briefing = "A fast, steep return to a drifting recovery ship. Peak heating will ionize the air and black out navigation for a while, so line up before it hits. Splashdown nearby counts; landing on the deck earns the top star.",
                ReleaseAltitude = 125000, ReleaseSpeed = 2300, ReleaseFlightPathDeg = -24, ReleaseHeadingDeg = -80, AimBiasCross = 2500, AimBiasDown = 3000,
                PlasmaHeatFluxThreshold = 160000,
                Clouds = new[]
                {
                    new CloudLayer { Type = CloudType.Cirrus, Base = 9500, Top = 11500, Coverage = 0.4, Density = 0.3, ShapeScale = 9000 },
                    new CloudLayer { Type = CloudType.Stratus, Base = 1800, Top = 2600, Coverage = 0.75, Density = 1.0, ShapeScale = 5000 },
                    new CloudLayer { Type = CloudType.Cumulus, Base = 700, Top = 1700, Coverage = 0.35, Density = 0.9, ShapeScale = 2500 },
                },
                SurfaceWindSpeed = 12, SurfaceWindDirDeg = 60, JetSpeed = 40, TurbulenceBase = 0.1, ConvectiveTurbulence = 0.6,
                Shear = new[] { new ShearLayer { Altitude = 3000, Thickness = 250, DeltaEast = -10, DeltaNorth = 16 } },
                EmBandLow = 70000, EmBandHigh = 85000, EmStrength = 0.6,
                Ocean = true, HasPlatform = true, PlatformEast = 6, PlatformNorth = 3, DeckHeight = 14, DeckRadius = 35, ZoneElevation = 14,
                CompleteRadius = 4000, GoodRadius = 300, PreciseRadius = 35, SoftSpeed = 5,
                Objective = new Objective { Type = ObjectiveType.DeckLanding, Value = 0, Text = "land on the ship's deck" },
                RecommendedVehicle = "phantom", WeatherSeed = 61, TerrainSeed = 19,
            });

            for (int i = 0; i < list.Count; i++) list[i].Index = i;
            return list;
        }
    }
}
