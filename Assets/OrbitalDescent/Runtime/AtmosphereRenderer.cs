using OrbitalDescent.Core;
using UnityEngine;

namespace OrbitalDescent
{
    /// <summary>
    /// Image effect that renders the sky, far planet, aerial perspective and volumetric clouds with the
    /// shared HLSL core (ODAtmosphereCore.hlsl). Runs after opaque geometry so transparent effects
    /// (plasma, plume, beacon) draw on top. Clouds are marched at reduced resolution (quality tier) and
    /// upsampled bilinearly in the composite pass.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class AtmosphereRenderer : MonoBehaviour
    {
        public const float ScatteringTop = 110000f;

        Camera cam;
        Material mat;
        NoiseAssets noise;
        LevelDefinition level;
        QualityProfile quality;
        double simTime;
        int frame;

        public float Exposure = 0.6f;
        public Color FadeColor = new Color(0, 0, 0, 0);
        public float Vignette = 0.25f;
        public Vector3 FlashPosition;   // Unity space
        public float FlashIntensity;

        static readonly int IdCloudTex = Shader.PropertyToID("_CloudTex");

        public void Init(Shader shader, NoiseAssets n, QualityProfile q)
        {
            cam = GetComponent<Camera>();
            mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            noise = n;
            quality = q;
            Shader.SetGlobalTexture("_CloudShapeTex", n.ShapeTexture);
            Shader.SetGlobalTexture("_CloudCoverageTex", n.CoverageTexture);
            mat.SetTexture("_DitherTex", n.BlueNoise);
            Shader.SetGlobalFloat("_PlanetRadius", (float)Planet.R);
            Shader.SetGlobalFloat("_AtmoTop", ScatteringTop);
            Shader.SetGlobalFloat("_CoverageTile", (float)CloudField.CoverageTile);
            Shader.SetGlobalFloat("_ExtinctionScale", (float)CloudField.ExtinctionScale);
            Shader.SetGlobalVector("_SunDir", Sun.Direction);
            Shader.SetGlobalFloat("_SunIntensity", Sun.Intensity);
            SetQuality(q);
            SetLevel(null);
        }

        public void SetQuality(QualityProfile q)
        {
            quality = q;
            Shader.SetGlobalFloat("_CloudSteps", q.CloudSteps);
            Shader.SetGlobalFloat("_LightSteps", q.CloudLightSteps);
            Shader.SetGlobalFloat("_AtmoSteps", q.AtmosphereSteps);
            Shader.SetGlobalFloat("_ODCloudShadows", q.CloudShadows ? 1f : 0f);
        }

        /// <summary>Uploads a level's cloud layers and storm cells (null clears them, e.g. for menus).</summary>
        public void SetLevel(LevelDefinition l)
        {
            level = l;
            var a = new Vector4[4];
            var b = new Vector4[4];
            var s = new Vector4[4];
            int layers = 0, storms = 0;
            float bandMin = 1e6f, bandMax = 0f;
            if (l != null)
            {
                foreach (var c in l.Clouds)
                {
                    if (layers >= 4) break;
                    a[layers] = new Vector4((float)c.Base, (float)c.Top, (float)c.Coverage, (float)c.Density);
                    b[layers] = new Vector4((float)c.Type, (float)c.ShapeScale, 0, 0);
                    bandMin = Mathf.Min(bandMin, (float)c.Base);
                    bandMax = Mathf.Max(bandMax, (float)c.Top);
                    layers++;
                }
                foreach (var c in l.Storms)
                {
                    if (storms >= 4) break;
                    s[storms++] = new Vector4((float)c.X, (float)c.Z, (float)c.Radius, (float)c.Strength);
                }
            }
            if (layers == 0) { bandMin = 0; bandMax = 1; }
            Shader.SetGlobalVectorArray("_CloudLayerA", a);
            Shader.SetGlobalVectorArray("_CloudLayerB", b);
            Shader.SetGlobalFloat("_CloudLayerCount", layers);
            Shader.SetGlobalVectorArray("_StormCells", s);
            Shader.SetGlobalFloat("_StormCount", storms);
            Shader.SetGlobalFloat("_CloudBandMin", bandMin);
            Shader.SetGlobalFloat("_CloudBandMax", bandMax);
            Shader.SetGlobalFloat("_OceanWorld", l != null && l.Ocean ? 1f : 0f);
        }

        public void SetTime(double t) { simTime = t; }

        void OnPreRender() { UpdateGlobals(); }

        void UpdateGlobals()
        {
            if (cam == null) return;
            // Camera position in the landing-zone frame, with altitude computed in double precision.
            Vec3d camCore = WorldSpace.ToCore(transform.position);
            double alt = Planet.Altitude(camCore);
            Vec3d up = Planet.Up(camCore);
            Shader.SetGlobalFloat("_CamAlt", (float)alt);
            Shader.SetGlobalVector("_CamUp", WorldSpace.Dir(up));
            Shader.SetGlobalVector("_CamLocal", new Vector3((float)camCore.x, (float)camCore.y, (float)camCore.z));
            Shader.SetGlobalVector("_ODOrigin", new Vector3((float)WorldSpace.Origin.x, (float)WorldSpace.Origin.y, (float)WorldSpace.Origin.z));
            Shader.SetGlobalFloat("_Exposure", Exposure);
            if (level != null)
                Shader.SetGlobalVector("_CloudDrift", new Vector4((float)(level.CloudDriftEast * simTime), (float)(level.CloudDriftNorth * simTime), 0, 0));
            Vector3 fl = FlashPosition + (Vector3)new Vector3((float)WorldSpace.Origin.x, (float)WorldSpace.Origin.y, (float)WorldSpace.Origin.z);
            Shader.SetGlobalVector("_Flash", new Vector4(fl.x, fl.y, fl.z, FlashIntensity));
            Shader.SetGlobalFloat("_ODFlashAmbient", FlashIntensity * 0.15f);
            // Golden-ratio sequence decorrelates the ray-march jitter between frames.
            frame++;
            Shader.SetGlobalFloat("_FrameJitter", Mathf.Repeat(frame * 0.61803398f, 1f));

            mat.SetVector("_CamRight", transform.right);
            mat.SetVector("_CamUpV", transform.up);
            mat.SetVector("_CamFwd", transform.forward);
            mat.SetFloat("_TanHalfFov", Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            mat.SetFloat("_Aspect", cam.aspect);
            mat.SetColor("_FadeColor", FadeColor);
            mat.SetFloat("_Vignette", Vignette);
        }

        [ImageEffectOpaque]
        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (mat == null) { Graphics.Blit(src, dst); return; }
            int ds = Mathf.Max(1, quality.CloudDownsample);
            var clouds = RenderTexture.GetTemporary(Mathf.Max(1, src.width / ds), Mathf.Max(1, src.height / ds), 0, RenderTextureFormat.ARGBHalf);
            clouds.filterMode = FilterMode.Bilinear;
            Graphics.Blit(src, clouds, mat, 0);
            mat.SetTexture(IdCloudTex, clouds);
            Graphics.Blit(src, dst, mat, 1);
            RenderTexture.ReleaseTemporary(clouds);
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }
    }
}
