using System;

namespace OrbitalDescent.Core
{
    /// <summary>Double-precision 3D vector. The simulation runs entirely in doubles so that
    /// orbital-scale positions keep millimetre precision; rendering converts to float relative
    /// to a floating origin.</summary>
    public struct Vec3d
    {
        public double x, y, z;

        public Vec3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }

        public static readonly Vec3d Zero = new Vec3d(0, 0, 0);
        public static readonly Vec3d UnitX = new Vec3d(1, 0, 0);
        public static readonly Vec3d UnitY = new Vec3d(0, 1, 0);
        public static readonly Vec3d UnitZ = new Vec3d(0, 0, 1);

        public static Vec3d operator +(Vec3d a, Vec3d b) => new Vec3d(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vec3d operator -(Vec3d a, Vec3d b) => new Vec3d(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vec3d operator -(Vec3d a) => new Vec3d(-a.x, -a.y, -a.z);
        public static Vec3d operator *(Vec3d a, double s) => new Vec3d(a.x * s, a.y * s, a.z * s);
        public static Vec3d operator *(double s, Vec3d a) => new Vec3d(a.x * s, a.y * s, a.z * s);
        public static Vec3d operator /(Vec3d a, double s) => new Vec3d(a.x / s, a.y / s, a.z / s);

        public static double Dot(Vec3d a, Vec3d b) => a.x * b.x + a.y * b.y + a.z * b.z;

        public static Vec3d Cross(Vec3d a, Vec3d b) =>
            new Vec3d(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

        public double SqrLength => x * x + y * y + z * z;
        public double Length => Math.Sqrt(x * x + y * y + z * z);

        public Vec3d Normalized
        {
            get
            {
                double l = Length;
                return l > 1e-300 ? this / l : Zero;
            }
        }

        public bool IsFinite => MathUtil.IsFinite(x) && MathUtil.IsFinite(y) && MathUtil.IsFinite(z);

        /// <summary>Component of this vector perpendicular to the unit vector n.</summary>
        public Vec3d PerpendicularTo(Vec3d n) => this - n * Dot(this, n);

        public Vec3d ClampLength(double max)
        {
            double l = Length;
            return l > max && l > 0 ? this * (max / l) : this;
        }

        public override string ToString() => $"({x:F3}, {y:F3}, {z:F3})";
    }

    /// <summary>Unit quaternion (w, x, y, z) for vehicle attitude. Right-handed math.</summary>
    public struct Quatd
    {
        public double w, x, y, z;

        public Quatd(double w, double x, double y, double z) { this.w = w; this.x = x; this.y = y; this.z = z; }

        public static readonly Quatd Identity = new Quatd(1, 0, 0, 0);

        public static Quatd operator *(Quatd a, Quatd b) => new Quatd(
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z,
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w);

        public static Quatd FromAxisAngle(Vec3d axis, double angle)
        {
            Vec3d n = axis.Normalized;
            double s = Math.Sin(angle * 0.5);
            return new Quatd(Math.Cos(angle * 0.5), n.x * s, n.y * s, n.z * s);
        }

        /// <summary>Shortest rotation taking unit vector a onto unit vector b.</summary>
        public static Quatd FromTo(Vec3d a, Vec3d b)
        {
            a = a.Normalized; b = b.Normalized;
            double d = Vec3d.Dot(a, b);
            if (d < -0.999999)
            {
                Vec3d axis = Vec3d.Cross(Vec3d.UnitX, a);
                if (axis.SqrLength < 1e-8) axis = Vec3d.Cross(Vec3d.UnitZ, a);
                return FromAxisAngle(axis, Math.PI);
            }
            Vec3d c = Vec3d.Cross(a, b);
            return new Quatd(1 + d, c.x, c.y, c.z).Normalized;
        }

        public Quatd Normalized
        {
            get
            {
                double l = Math.Sqrt(w * w + x * x + y * y + z * z);
                return l > 1e-300 ? new Quatd(w / l, x / l, y / l, z / l) : Identity;
            }
        }

        public Vec3d Rotate(Vec3d v)
        {
            // v' = v + 2w(q x v) + 2 q x (q x v)
            Vec3d q = new Vec3d(x, y, z);
            Vec3d t = Vec3d.Cross(q, v) * 2.0;
            return v + t * w + Vec3d.Cross(q, t);
        }

        /// <summary>Integrates a world-frame angular velocity over dt (exact for constant omega).</summary>
        public Quatd Integrate(Vec3d omegaWorld, double dt)
        {
            double rate = omegaWorld.Length;
            if (rate * dt < 1e-12) return this;
            return (FromAxisAngle(omegaWorld, rate * dt) * this).Normalized;
        }

        public bool IsFinite => MathUtil.IsFinite(w) && MathUtil.IsFinite(x) && MathUtil.IsFinite(y) && MathUtil.IsFinite(z);
    }

    public static class MathUtil
    {
        public static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
        public static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3 - 2 * t);
        }

        /// <summary>Exponential approach factor for frame-rate independent smoothing.</summary>
        public static double Approach(double rate, double dt) => 1.0 - Math.Exp(-rate * dt);
    }
}
