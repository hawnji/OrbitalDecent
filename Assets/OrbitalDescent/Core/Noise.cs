using System;

namespace OrbitalDescent.Core
{
    /// <summary>Deterministic hashing and gradient noise. Everything here is seeded and
    /// platform independent so physics queries and the GPU textures built from the same data agree.</summary>
    public static class Noise
    {
        public static uint Hash(int x, int y, int z, uint seed)
        {
            unchecked
            {
                uint h = seed * 0x9E3779B1u;
                h ^= (uint)x * 0x85EBCA77u; h = (h << 13) | (h >> 19);
                h ^= (uint)y * 0xC2B2AE3Du; h = (h << 17) | (h >> 15);
                h ^= (uint)z * 0x27D4EB2Fu;
                h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
                return h;
            }
        }

        public static double Hash01(int x, int y, int z, uint seed) => (Hash(x, y, z, seed) & 0xFFFFFF) / 16777215.0;

        static int Wrap(int v, int period) { int m = v % period; return m < 0 ? m + period : m; }

        static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

        static double Grad(int ix, int iy, int iz, double fx, double fy, double fz, uint seed)
        {
            uint h = Hash(ix, iy, iz, seed) % 12u;
            // 12 edge directions of a cube
            switch (h)
            {
                case 0: return fx + fy;
                case 1: return -fx + fy;
                case 2: return fx - fy;
                case 3: return -fx - fy;
                case 4: return fx + fz;
                case 5: return -fx + fz;
                case 6: return fx - fz;
                case 7: return -fx - fz;
                case 8: return fy + fz;
                case 9: return -fy + fz;
                case 10: return fy - fz;
                default: return -fy - fz;
            }
        }

        /// <summary>Periodic 3D Perlin noise in roughly [-1, 1] with integer lattice period.</summary>
        public static double Perlin3(double x, double y, double z, int period, uint seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y), z0 = (int)Math.Floor(z);
            double fx = x - x0, fy = y - y0, fz = z - z0;
            double u = Fade(fx), v = Fade(fy), w = Fade(fz);
            int xa = Wrap(x0, period), xb = Wrap(x0 + 1, period);
            int ya = Wrap(y0, period), yb = Wrap(y0 + 1, period);
            int za = Wrap(z0, period), zb = Wrap(z0 + 1, period);
            double n000 = Grad(xa, ya, za, fx, fy, fz, seed);
            double n100 = Grad(xb, ya, za, fx - 1, fy, fz, seed);
            double n010 = Grad(xa, yb, za, fx, fy - 1, fz, seed);
            double n110 = Grad(xb, yb, za, fx - 1, fy - 1, fz, seed);
            double n001 = Grad(xa, ya, zb, fx, fy, fz - 1, seed);
            double n101 = Grad(xb, ya, zb, fx - 1, fy, fz - 1, seed);
            double n011 = Grad(xa, yb, zb, fx, fy - 1, fz - 1, seed);
            double n111 = Grad(xb, yb, zb, fx - 1, fy - 1, fz - 1, seed);
            double x00 = MathUtil.Lerp(n000, n100, u), x10 = MathUtil.Lerp(n010, n110, u);
            double x01 = MathUtil.Lerp(n001, n101, u), x11 = MathUtil.Lerp(n011, n111, u);
            return MathUtil.Lerp(MathUtil.Lerp(x00, x10, v), MathUtil.Lerp(x01, x11, v), w);
        }

        /// <summary>Periodic Worley (cellular) noise: distance to the nearest feature point, ~[0, 1].</summary>
        public static double Worley3(double x, double y, double z, int period, uint seed)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z);
            double best = 10;
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = xi + dx, cy = yi + dy, cz = zi + dz;
                int wx = Wrap(cx, period), wy = Wrap(cy, period), wz = Wrap(cz, period);
                double px = cx + Hash01(wx, wy, wz, seed);
                double py = cy + Hash01(wx, wy, wz, seed + 101u);
                double pz = cz + Hash01(wx, wy, wz, seed + 202u);
                double ddx = px - x, ddy = py - y, ddz = pz - z;
                double d = ddx * ddx + ddy * ddy + ddz * ddz;
                if (d < best) best = d;
            }
            return Math.Min(1.0, Math.Sqrt(best));
        }

        /// <summary>Smooth non-periodic 2D value noise in [0, 1] (used for terrain).</summary>
        public static double Value2(double x, double y, uint seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            double u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy);
            double a = Hash01(x0, y0, 0, seed), b = Hash01(x0 + 1, y0, 0, seed);
            double c = Hash01(x0, y0 + 1, 0, seed), d = Hash01(x0 + 1, y0 + 1, 0, seed);
            return MathUtil.Lerp(MathUtil.Lerp(a, b, u), MathUtil.Lerp(c, d, u), v);
        }

        /// <summary>Ridged multifractal in [0, ~1] for mountain ranges.</summary>
        public static double Ridged2(double x, double y, int octaves, uint seed)
        {
            double sum = 0, amp = 0.5, freq = 1, weight = 1, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                double n = 1.0 - Math.Abs(Value2(x * freq, y * freq, seed + (uint)i * 31u) * 2 - 1);
                n *= n;
                n *= weight;
                weight = MathUtil.Clamp01(n * 2);
                sum += n * amp;
                norm += amp;
                freq *= 2.03;
                amp *= 0.5;
            }
            return sum / norm;
        }

        public static double Fbm2(double x, double y, int octaves, uint seed)
        {
            double sum = 0, amp = 0.5, freq = 1, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value2(x * freq, y * freq, seed + (uint)i * 17u) * amp;
                norm += amp;
                freq *= 2.01;
                amp *= 0.5;
            }
            return sum / norm;
        }
    }

    /// <summary>
    /// A tileable 3D byte texture (values 0-255) sampled with the same trilinear, wrap-mode,
    /// texel-centre convention as GPU hardware. The Unity side uploads <see cref="Data"/> as an
    /// R8 Texture3D, so cloud density seen by the physics matches the rendered clouds up to
    /// hardware filtering precision.
    /// </summary>
    public sealed class NoiseVolume
    {
        public readonly int Size;
        public readonly byte[] Data;

        public NoiseVolume(int size, byte[] data) { Size = size; Data = data; }

        /// <summary>Perlin-Worley cloud shape noise: billowy Worley fbm eroding a Perlin base.</summary>
        public static NoiseVolume GenerateCloudShape(int size, uint seed)
        {
            var raw = new double[size * size * size];
            int basePeriod = 4;
            double inv = 1.0 / size;
            for (int z = 0; z < size; z++)
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double u = (x + 0.5) * inv, v = (y + 0.5) * inv, w = (z + 0.5) * inv;
                double perlin = 0, amp = 1, norm = 0;
                for (int o = 0; o < 3; o++)
                {
                    int p = basePeriod << o;
                    perlin += amp * Noise.Perlin3(u * p, v * p, w * p, p, seed + (uint)o);
                    norm += amp; amp *= 0.5;
                }
                perlin = MathUtil.Clamp01(perlin / norm * 0.8 + 0.5);
                double worley = 0; amp = 1; norm = 0;
                for (int o = 0; o < 3; o++)
                {
                    int p = (basePeriod << o) * 2;
                    worley += amp * (1.0 - Noise.Worley3(u * p, v * p, w * p, p, seed + 50u + (uint)o));
                    norm += amp; amp *= 0.55;
                }
                worley /= norm;
                // Perlin-Worley: remap perlin from [worley - 1, 1] to [0, 1] (billowy cauliflower edges).
                raw[x + size * (y + size * z)] = (perlin - (worley - 1.0)) / (2.0 - worley);
            }
            // Stretch the 2nd..98th percentile to the full byte range so density thresholds behave predictably.
            var sorted = (double[])raw.Clone();
            Array.Sort(sorted);
            double lo = sorted[(int)(sorted.Length * 0.02)], hi = sorted[(int)(sorted.Length * 0.98)];
            var data = new byte[raw.Length];
            for (int i = 0; i < raw.Length; i++)
                data[i] = (byte)Math.Round(MathUtil.Clamp01((raw[i] - lo) / (hi - lo)) * 255.0);
            return new NoiseVolume(size, data);
        }

        /// <summary>Trilinear sample, coordinates in texture space (1.0 = one tile), wrap addressing.</summary>
        public double Sample(double u, double v, double w)
        {
            double fx = u * Size - 0.5, fy = v * Size - 0.5, fz = w * Size - 0.5;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy), z0 = (int)Math.Floor(fz);
            double tx = fx - x0, ty = fy - y0, tz = fz - z0;
            int x1 = x0 + 1, y1 = y0 + 1, z1 = z0 + 1;
            x0 = W(x0); x1 = W(x1); y0 = W(y0); y1 = W(y1); z0 = W(z0); z1 = W(z1);
            double c000 = At(x0, y0, z0), c100 = At(x1, y0, z0), c010 = At(x0, y1, z0), c110 = At(x1, y1, z0);
            double c001 = At(x0, y0, z1), c101 = At(x1, y0, z1), c011 = At(x0, y1, z1), c111 = At(x1, y1, z1);
            double a = MathUtil.Lerp(MathUtil.Lerp(c000, c100, tx), MathUtil.Lerp(c010, c110, tx), ty);
            double b = MathUtil.Lerp(MathUtil.Lerp(c001, c101, tx), MathUtil.Lerp(c011, c111, tx), ty);
            return MathUtil.Lerp(a, b, tz) / 255.0;
        }

        int W(int i) { int m = i % Size; return m < 0 ? m + Size : m; }
        double At(int x, int y, int z) => Data[x + Size * (y + Size * z)];
    }

    /// <summary>A tileable 2D byte map (cloud coverage) with GPU-equivalent bilinear sampling.</summary>
    public sealed class NoiseMap
    {
        public readonly int Size;
        public readonly byte[] Data;

        public NoiseMap(int size, byte[] data) { Size = size; Data = data; }

        public static NoiseMap GenerateCoverage(int size, uint seed)
        {
            var data = new byte[size * size];
            double inv = 1.0 / size;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double u = (x + 0.5) * inv, v = (y + 0.5) * inv;
                double n = 0, amp = 1, norm = 0;
                for (int o = 0; o < 5; o++)
                {
                    int p = 3 << o;
                    n += amp * Noise.Perlin3(u * p, v * p, 0.5, p, seed + (uint)o * 7u);
                    norm += amp; amp *= 0.5;
                }
                double c = MathUtil.Clamp01(n / norm * 0.9 + 0.5);
                data[x + size * y] = (byte)Math.Round(c * 255.0);
            }
            return new NoiseMap(size, data);
        }

        public double Sample(double u, double v)
        {
            double fx = u * Size - 0.5, fy = v * Size - 0.5;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            double tx = fx - x0, ty = fy - y0;
            int x1 = W(x0 + 1), y1 = W(y0 + 1);
            x0 = W(x0); y0 = W(y0);
            double a = MathUtil.Lerp(Data[x0 + Size * y0], Data[x1 + Size * y0], tx);
            double b = MathUtil.Lerp(Data[x0 + Size * y1], Data[x1 + Size * y1], tx);
            return MathUtil.Lerp(a, b, ty) / 255.0;
        }

        int W(int i) { int m = i % Size; return m < 0 ? m + Size : m; }
    }
}
