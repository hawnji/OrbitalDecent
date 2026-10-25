using System;

namespace OrbitalDescent.Core
{
    /// <summary>
    /// Procedural terrain heightfield around the landing zone (metres above sea level). The Unity
    /// terrain mesh is built from this exact function, and collision uses it directly.
    /// Beyond <see cref="PatchRadius"/> the terrain fades to sea level, where the far-field planet
    /// sphere (rendered analytically in the atmosphere shader) takes over.
    /// </summary>
    public sealed class TerrainField
    {
        public const double PatchRadius = 42000.0;
        public const double PadRadius = 60.0;
        public const double PadThickness = 0.4; // raised concrete pad, m
        readonly LevelDefinition L;

        public TerrainField(LevelDefinition level) { L = level; }

        public double Height(double x, double z)
        {
            double r = Math.Sqrt(x * x + z * z);
            double edge = 1.0 - MathUtil.SmoothStep(PatchRadius * 0.7, PatchRadius, r);
            if (L.Ocean)
            {
                // Sea floor well below the water; a few distant islands for visual interest.
                double isl = Noise.Fbm2(x / 14000.0, z / 14000.0, 4, L.TerrainSeed);
                return -60.0 + Math.Max(0, isl - 0.62) * 900.0 * edge * MathUtil.SmoothStep(6000, 12000, r);
            }
            double mtn = Noise.Ridged2(x / L.MountainScale, z / L.MountainScale, 5, L.TerrainSeed);
            double rolling = Noise.Fbm2(x / 3000.0, z / 3000.0, 4, L.TerrainSeed + 99u) - 0.5;
            double valley = MathUtil.SmoothStep(PadRadius * 2.0, L.ValleyRadius, r);
            double h = L.ZoneElevation + (mtn * L.MountainHeight + rolling * 60.0) * valley;
            // Flatten the pad exactly to zone elevation.
            double pad = 1.0 - MathUtil.SmoothStep(PadRadius, PadRadius * 2.0, r);
            h = MathUtil.Lerp(h, L.ZoneElevation, pad);
            return h * edge;
        }

        /// <summary>Surface normal from central differences (local frame, ignores curvature).</summary>
        public Vec3d Normal(double x, double z)
        {
            const double e = 15.0;
            double hx = Height(x + e, z) - Height(x - e, z);
            double hz = Height(x, z + e) - Height(x, z - e);
            return new Vec3d(-hx, 2 * e, -hz).Normalized;
        }
    }

    public enum SurfaceKind { Ground, Water, Deck }

    /// <summary>Everything outside the vehicle: atmosphere, weather, terrain and the recovery platform.</summary>
    public sealed class World
    {
        public readonly LevelDefinition Level;
        public readonly CloudField Clouds;
        public readonly WindField Wind;
        public readonly TerrainField Terrain;

        public World(LevelDefinition level, NoiseVolume shape, NoiseMap coverage)
        {
            Level = level;
            Clouds = new CloudField(level, shape, coverage);
            Wind = new WindField(level, Clouds);
            Terrain = new TerrainField(level);
        }

        /// <summary>Landing zone (pad centre or platform deck centre) position at time t.</summary>
        public Vec3d ZonePosition(double t)
        {
            if (!Level.HasPlatform) return Planet.PointAbove(0, 0, Level.ZoneElevation);
            return Planet.PointAbove(Level.PlatformEast * t, Level.PlatformNorth * t, Level.DeckHeight);
        }

        public Vec3d ZoneVelocity => Level.HasPlatform ? new Vec3d(Level.PlatformEast, 0, Level.PlatformNorth) : Vec3d.Zero;

        /// <summary>Height of whatever surface is under (x, z) at time t, and its kind.</summary>
        public double SurfaceHeight(double x, double z, double t, out SurfaceKind kind)
        {
            double h = Terrain.Height(x, z);
            kind = SurfaceKind.Ground;
            if (!Level.Ocean && x * x + z * z < TerrainField.PadRadius * TerrainField.PadRadius) h += TerrainField.PadThickness;
            if (Level.Ocean && h < 0) { h = 0; kind = SurfaceKind.Water; }
            if (Level.HasPlatform)
            {
                double px = Level.PlatformEast * t, pz = Level.PlatformNorth * t;
                double dx = x - px, dz = z - pz;
                if (dx * dx + dz * dz < Level.DeckRadius * Level.DeckRadius)
                {
                    h = Level.DeckHeight; kind = SurfaceKind.Deck;
                }
            }
            return h;
        }
    }
}
