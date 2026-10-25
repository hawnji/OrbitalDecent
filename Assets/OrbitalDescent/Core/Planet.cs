using System;

namespace OrbitalDescent.Core
{
    /// <summary>Physical constants (SI units throughout the project).</summary>
    public static class Constants
    {
        public const double GM = 3.986004418e14;        // Earth gravitational parameter, m^3/s^2
        public const double EarthRadius = 6371000.0;    // mean radius, m
        public const double G0 = 9.80665;               // standard gravity, m/s^2
        public const double StefanBoltzmann = 5.670374419e-8;
        public const double AirGasConstant = 287.05287; // J/(kg K)
        public const double AirGamma = 1.4;
    }

    /// <summary>
    /// Coordinate system: a local Cartesian frame whose origin is the landing zone on the
    /// sea-level sphere, +Y up at the origin, X east, Z north. The planet centre sits at
    /// (0, -R, 0), so gravity and altitude are fully spherical while near-zone coordinates
    /// stay small. All positions are doubles.
    /// </summary>
    public static class Planet
    {
        public const double R = Constants.EarthRadius;
        public static readonly Vec3d Center = new Vec3d(0, -R, 0);

        /// <summary>Altitude above the sea-level sphere, computed without catastrophic
        /// cancellation: |p - C| - R = (|p|^2 + 2 R p.y) / (|p - C| + R).</summary>
        public static double Altitude(Vec3d p)
        {
            Vec3d rc = p - Center;
            return (p.SqrLength + 2.0 * R * p.y) / (rc.Length + R);
        }

        public static Vec3d Up(Vec3d p) => (p - Center).Normalized;

        public static double RadiusAt(Vec3d p) => (p - Center).Length;

        /// <summary>Inverse-square gravitational acceleration g(r) = GM / r^2 toward the centre.</summary>
        public static Vec3d Gravity(Vec3d p)
        {
            Vec3d r = p - Center;
            double d = r.Length;
            return r * (-Constants.GM / (d * d * d));
        }

        /// <summary>Great-circle surface distance between the ground points beneath a and b.</summary>
        public static double SurfaceDistance(Vec3d a, Vec3d b)
        {
            Vec3d ua = Up(a), ub = Up(b);
            Vec3d c = Vec3d.Cross(ua, ub);
            double ang = Math.Atan2(c.Length, Vec3d.Dot(ua, ub));
            return ang * R;
        }

        /// <summary>Position at the given altitude above the local (x, z) horizontal coordinate.</summary>
        public static Vec3d PointAbove(double x, double z, double altitude)
        {
            // Project (x, 0, z) onto the sphere direction and extend to R + altitude.
            Vec3d dir = (new Vec3d(x, 0, z) - Center).Normalized;
            return Center + dir * (R + altitude);
        }

        /// <summary>Local horizontal "east/north" coordinates of a point (projection onto
        /// the tangent plane at the landing zone). Used for terrain and weather lookups.</summary>
        public static void Horizontal(Vec3d p, out double x, out double z)
        {
            // Gnomonic-like projection: scale the radial direction onto the tangent plane y = 0.
            Vec3d d = p - Center;
            double s = R / d.y;
            x = d.x * s;
            z = d.z * s;
        }
    }
}
