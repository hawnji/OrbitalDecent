namespace OrbitalDescent.Core
{
    public enum CloudType { Cirrus = 0, Stratus = 1, Cumulus = 2, Storm = 3 }

    public sealed class CloudLayer
    {
        public CloudType Type;
        public double Base;          // m ASL
        public double Top;           // m ASL
        public double Coverage;      // 0..1 fraction of sky
        public double Density = 1.0; // extinction multiplier
        public double ShapeScale = 4000; // m per noise tile
    }

    public sealed class StormCell
    {
        public double X, Z;       // local horizontal position, m
        public double Radius;     // m
        public double Strength = 1.0;
    }

    public sealed class ShearLayer
    {
        public double Altitude;   // m, centre of the transition
        public double Thickness;  // m, tanh half-width
        public double DeltaEast;  // m/s change in wind below the layer vs above
        public double DeltaNorth;
    }

    public enum ObjectiveType { None, FuelRemaining, NoParachute, LowRotation, SurviveStrikes, DeckLanding }

    public sealed class Objective
    {
        public ObjectiveType Type;
        public double Value;
        public string Text = "";
    }

    /// <summary>Hand-authored mission data. Every tunable for a level lives here, never in systems.</summary>
    public sealed class LevelDefinition
    {
        public int Index;
        public string Id = "";
        public string Name = "";
        public string Briefing = "";

        // Release (orbital platform separation)
        public double ReleaseAltitude = 120000;
        public double ReleaseSpeed = 1600;          // m/s
        public double ReleaseFlightPathDeg = -28;    // negative = descending
        public double ReleaseHeadingDeg = 0;        // horizontal heading, 0 = +Z (north), 90 = +X (east)
        public double AimHeadingRangeDeg = 6;
        public double AimFlightPathRangeDeg = 4;
        public double AimBiasCross = 1500;          // m cross-range error of the nominal release
        public double AimBiasDown = -2500;          // m down-range error of the nominal release

        // Weather
        public CloudLayer[] Clouds = new CloudLayer[0];
        public StormCell[] Storms = new StormCell[0];
        public double CloudDriftEast = 4, CloudDriftNorth = 1;
        public uint WeatherSeed = 1;

        public double SurfaceWindSpeed = 5;
        public double SurfaceWindDirDeg = 270;      // direction the wind blows toward
        public double JetSpeed = 25;
        public double JetAltitude = 11000;
        public double WindVeerDegPer10km = 25;
        public double StratosphereWind = 15;
        public ShearLayer[] Shear = new ShearLayer[0];
        public double TurbulenceBase = 0.05;        // 0..1 clear-air turbulence
        public double ConvectiveTurbulence = 0.6;   // scale applied to cloud density

        // Electromagnetic interference band (0, 0 = none)
        public double EmBandLow = 0, EmBandHigh = 0, EmStrength = 1;
        public double PlasmaHeatFluxThreshold = 120000; // W/m^2 at which ionization blacks out navigation

        // Terrain
        public uint TerrainSeed = 7;
        public double ZoneElevation = 200;
        public double MountainHeight = 600;
        public double MountainScale = 9000;
        public double ValleyRadius = 2500;
        public bool Ocean;

        // Recovery platform (ocean levels)
        public bool HasPlatform;
        public double PlatformEast, PlatformNorth;  // m/s drift
        public double DeckHeight = 14, DeckRadius = 35;

        // Scoring
        public double CompleteRadius = 4000;
        public double GoodRadius = 400;
        public double PreciseRadius = 60;
        public double SoftSpeed = 5.0;
        public Objective Objective = new Objective();

        public string RecommendedVehicle = "dart";
    }
}
