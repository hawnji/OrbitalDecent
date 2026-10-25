using OrbitalDescent.Core;
using UnityEngine;

namespace OrbitalDescent
{
    /// <summary>
    /// Generates the shared noise data once (CPU, deterministic) and uploads it as GPU textures.
    /// The physics samples the same byte arrays, so rendered clouds and simulated cloud density agree.
    /// </summary>
    public sealed class NoiseAssets
    {
        public const int ShapeSize = 64;
        public const int CoverageSize = 256;

        public NoiseVolume Shape { get; private set; }
        public NoiseMap Coverage { get; private set; }
        public Texture3D ShapeTexture { get; private set; }
        public Texture2D CoverageTexture { get; private set; }
        public Texture2D BlueNoise { get; private set; }

        /// <summary>Generates the noise data on a worker thread (takes ~0.5-2 s on phones).</summary>
        public static System.Threading.Tasks.Task<NoiseAssets> GenerateAsync()
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                var a = new NoiseAssets();
                a.Shape = NoiseVolume.GenerateCloudShape(ShapeSize, 1);
                a.Coverage = NoiseMap.GenerateCoverage(CoverageSize, 2);
                return a;
            });
        }

        /// <summary>Uploads the generated data to GPU textures. Main thread only.</summary>
        public void CreateTextures()
        {
            var a = this;

            var t3 = new Texture3D(ShapeSize, ShapeSize, ShapeSize, TextureFormat.R8, false)
            {
                wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "CloudShape",
            };
            t3.SetPixelData(a.Shape.Data, 0);
            t3.Apply(false, true);
            a.ShapeTexture = t3;

            var t2 = new Texture2D(CoverageSize, CoverageSize, TextureFormat.R8, false, true)
            {
                wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "CloudCoverage",
            };
            t2.SetPixelData(a.Coverage.Data, 0);
            t2.Apply(false, true);
            a.CoverageTexture = t2;

            // Small interleaved-gradient style dither for ray-march jitter.
            const int n = 64;
            var bn = new byte[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = Mathf.Repeat(52.9829189f * Mathf.Repeat(0.06711056f * x + 0.00583715f * y, 1f), 1f);
                    bn[x + y * n] = (byte)(v * 255f);
                }
            var b = new Texture2D(n, n, TextureFormat.R8, false, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, name = "Dither" };
            b.SetPixelData(bn, 0);
            b.Apply(false, true);
            a.BlueNoise = b;
        }
    }
}
