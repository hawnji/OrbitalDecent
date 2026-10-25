using System;

namespace OrbitalDescent.Core
{
    /// <summary>
    /// Cloud density field shared by physics and rendering. The shader ODAtmosphere implements the
    /// same formula (see CloudDensity in ODCommon.cginc); keep the two in sync. Density is a
    /// dimensionless 0..1 "cloud water" value; extinction = density * layer density * ExtinctionScale.
    /// </summary>
    public sealed class CloudField
    {
        public const double CoverageTile = 36000.0; // m covered by one tile of the coverage map
        public const double ExtinctionScale = 0.03; // 1/m at density 1 (visual range ~ 100 m)

        public readonly LevelDefinition Level;
        public readonly NoiseVolume Shape;
        public readonly NoiseMap Coverage;

        public CloudField(LevelDefinition level, NoiseVolume shape, NoiseMap coverage)
        {
            Level = level; Shape = shape; Coverage = coverage;
        }

        public static double HeightProfile(CloudType type, double h)
        {
            switch (type)
            {
                case CloudType.Cirrus: return MathUtil.Clamp01(h / 0.3) * MathUtil.Clamp01((1 - h) / 0.3);
                case CloudType.Stratus: return MathUtil.Clamp01(h / 0.15) * MathUtil.Clamp01((1 - h) / 0.25);
                case CloudType.Storm: return MathUtil.Clamp01(h / 0.05) * MathUtil.Clamp01((1 - h) / 0.2);
                default: return MathUtil.Clamp01(h / 0.1) * MathUtil.Clamp01((1 - h) / 0.55);
            }
        }

        public double StormCoverage(double x, double z)
        {
            double c = 0;
            foreach (var s in Level.Storms)
            {
                double dx = x - s.X, dz = z - s.Z;
                double d = Math.Sqrt(dx * dx + dz * dz);
                c = Math.Max(c, s.Strength * (1.0 - MathUtil.SmoothStep(s.Radius * 0.35, s.Radius, d)));
            }
            return c;
        }

        /// <summary>Two rotated scales of the coverage map (breaks visible tiling). Mirrors RawCoverage in
        /// ODAtmosphereCore.hlsl.</summary>
        public double RawCoverage(double x, double z)
        {
            double a = Coverage.Sample(x / CoverageTile, z / CoverageTile);
            double rx = x * 0.8 - z * 0.6, rz = x * 0.6 + z * 0.8;
            double b = Coverage.Sample(rx / (CoverageTile * 4.37) + 0.31, rz / (CoverageTile * 4.37) + 0.67);
            return MathUtil.Clamp01(0.5 + (0.55 * a + 0.45 * b - 0.5) * 1.35);
        }

        /// <summary>Cloud density at a local horizontal position and altitude, time t (s).</summary>
        public double Density(double x, double z, double altitude, double t)
        {
            double total = 0;
            double driftX = Level.CloudDriftEast * t, driftZ = Level.CloudDriftNorth * t;
            foreach (var layer in Level.Clouds)
            {
                if (altitude <= layer.Base || altitude >= layer.Top) continue;
                double h = (altitude - layer.Base) / (layer.Top - layer.Base);
                double cov;
                if (layer.Type == CloudType.Storm)
                {
                    cov = StormCoverage(x - driftX, z - driftZ);
                }
                else
                {
                    double raw = RawCoverage(x - driftX, z - driftZ);
                    cov = MathUtil.Clamp01((raw - (1.0 - layer.Coverage)) / Math.Max(0.05, layer.Coverage) * 1.5);
                }
                double baseShape = cov * HeightProfile(layer.Type, h);
                if (baseShape <= 0) continue;
                double s = layer.ShapeScale;
                double n = Shape.Sample((x - driftX) / s, altitude / s, (z - driftZ) / s);
                double d = MathUtil.Clamp01((n - (1.0 - baseShape)) / Math.Max(0.05, baseShape));
                total += d * layer.Density;
            }
            return total;
        }

        /// <summary>Approximate visibility in metres inside the cloud field.</summary>
        public static double Visibility(double density)
        {
            double ext = density * ExtinctionScale + 2e-5;
            return 3.0 / ext; // Koschmieder: contrast threshold ~5%
        }
    }

    /// <summary>
    /// Wind as a velocity field of the air mass (m/s, local frame). Aerodynamic forces use the
    /// air-relative velocity. Components: altitude profile with a jet stream and directional veer,
    /// shear layers (tanh transitions, so shear is a genuine spatial gradient), storm down-drafts
    /// and bounded turbulence (sum of sinusoids, amplitude scaled by local intensity).
    /// </summary>
    public sealed class WindField
    {
        readonly LevelDefinition L;
        readonly CloudField clouds;
        public double HazardScale = 1.0;

        public WindField(LevelDefinition level, CloudField clouds) { L = level; this.clouds = clouds; }

        public Vec3d MeanWind(double x, double z, double altitude)
        {
            double h = Math.Max(0, altitude);
            double boundary = MathUtil.SmoothStep(0, 1200, h);
            double speed = L.SurfaceWindSpeed * (0.6 + 0.4 * boundary)
                           + L.JetSpeed * Math.Exp(-Math.Pow((h - L.JetAltitude) / 4500.0, 2))
                           + L.StratosphereWind * MathUtil.SmoothStep(20000, 50000, h) * (1 - MathUtil.SmoothStep(90000, 120000, h));
            double dir = (L.SurfaceWindDirDeg + L.WindVeerDegPer10km * h / 10000.0) * Math.PI / 180.0;
            double e = Math.Sin(dir) * speed, n = Math.Cos(dir) * speed;
            foreach (var s in L.Shear)
            {
                double f = 0.5 * (1.0 + Math.Tanh((s.Altitude - h) / Math.Max(1.0, s.Thickness)));
                e += s.DeltaEast * f * HazardScale;
                n += s.DeltaNorth * f * HazardScale;
            }
            double vy = 0;
            // Storm cells: down-draft in the core and radial outflow near the ground.
            foreach (var c in L.Storms)
            {
                double dx = x - c.X, dz = z - c.Z;
                double d = Math.Sqrt(dx * dx + dz * dz);
                double core = 1.0 - MathUtil.SmoothStep(c.Radius * 0.2, c.Radius, d);
                if (core <= 0) continue;
                double band = MathUtil.SmoothStep(0, 1500, h) * (1 - MathUtil.SmoothStep(9000, 13000, h));
                vy -= 9.0 * c.Strength * core * band * HazardScale;
                double outflow = 12.0 * c.Strength * core * (1 - MathUtil.SmoothStep(300, 2000, h)) * HazardScale;
                if (d > 1) { e += dx / d * outflow; n += dz / d * outflow; }
            }
            return new Vec3d(e, vy, n);
        }

        /// <summary>Turbulence intensity 0..1 at a point (clear air + convective + storm).</summary>
        public double TurbulenceIntensity(double x, double z, double altitude, double t)
        {
            double cloud = clouds.Density(x, z, altitude, t);
            double storm = clouds.StormCoverage(x - L.CloudDriftEast * t, z - L.CloudDriftNorth * t) *
                           MathUtil.SmoothStep(500, 2000, altitude) * (1 - MathUtil.SmoothStep(10000, 13000, altitude));
            double jet = 0.25 * Math.Exp(-Math.Pow((altitude - L.JetAltitude) / 2500.0, 2)) * (L.JetSpeed / 40.0);
            double i = L.TurbulenceBase + jet + L.ConvectiveTurbulence * Math.Min(1.0, cloud) + 0.7 * storm;
            return MathUtil.Clamp01(i * HazardScale);
        }

        /// <summary>Bounded turbulence velocity (m/s). |v| <= 1.73 * sigma * intensity by construction.</summary>
        public static Vec3d TurbulenceVelocity(Vec3d p, double t, double intensity, double altitude)
        {
            if (intensity <= 0) return Vec3d.Zero;
            // sigma grows near the ground-coupled layers and is scaled by air density so very thin air
            // does not produce absurd gust speeds.
            double sigma = 9.0 * intensity;
            double ex = Math.Sin(0.9 * t + p.x * 0.0021 + p.y * 0.0013) * 0.6 + Math.Sin(2.3 * t + p.z * 0.0047 + 1.3) * 0.4;
            double ey = Math.Sin(1.3 * t + p.z * 0.0018 + 2.1) * 0.6 + Math.Sin(3.1 * t + p.x * 0.0039 + 0.4) * 0.4;
            double ez = Math.Sin(1.1 * t + p.y * 0.0025 + 4.2) * 0.6 + Math.Sin(2.7 * t + p.x * 0.0051 + 2.2) * 0.4;
            return new Vec3d(ex, ey * 0.6, ez) * sigma;
        }
    }
}
