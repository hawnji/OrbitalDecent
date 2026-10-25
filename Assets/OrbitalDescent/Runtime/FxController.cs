using System.Collections.Generic;
using OrbitalDescent.Core;
using UnityEngine;

namespace OrbitalDescent
{
    /// <summary>
    /// Visual effects driven by simulated quantities only: plasma sheath (heat flux), engine plumes
    /// (throttle), ionised trail, speed streaks (air speed and cloud density) and lightning bolts.
    /// Everything uses the additive OrbitalDescent/Fx shader; no particle systems are needed.
    /// </summary>
    public sealed class FxController : MonoBehaviour
    {
        ProbeView probe;
        Transform sheath, sheathInner;
        Material sheathMat, sheathInnerMat, plumeMat, trailMat, streakMat, boltMat;
        readonly List<Transform> plumes = new List<Transform>();
        TrailRenderer trail;
        Mesh streakMesh;
        Transform streakRoot;
        LineRenderer bolt;
        float boltTimer;
        float plumeLevel;

        const int StreakCount = 48;
        readonly Vector3[] streakPos = new Vector3[StreakCount];
        readonly Vector3[] verts = new Vector3[StreakCount * 4];
        readonly Color[] cols = new Color[StreakCount * 4];
        System.Random rng = new System.Random(5);

        public float PlasmaLevel { get; private set; }

        public static FxController Create(ProbeView probe, Shader fx)
        {
            var go = new GameObject("Effects");
            var c = go.AddComponent<FxController>();
            c.Build(probe, fx);
            return c;
        }

        /// <param name="fadeLength">Fade-out distance along lathe profiles (uv.y, m); 0 disables.</param>
        static Material FxMat(Shader s, Color c, float softness, float fadeLength = 0f)
        {
            var m = new Material(s);
            m.SetColor("_Color", c);
            m.SetFloat("_Intensity", 0f);
            m.SetFloat("_Softness", softness);
            m.SetFloat("_FadeLength", fadeLength);
            return m;
        }

        void Build(ProbeView p, Shader fx)
        {
            probe = p;
            float r = p.Radius;

            // Bow-shock sheath: a paraboloid shell standing off the shield, opening downstream (+Y local).
            var prof = new List<Vector2>();
            for (int i = 0; i <= 12; i++)
            {
                float u = i / 12f;
                float rad = r * (0.05f + 1.5f * Mathf.Sqrt(u));
                prof.Add(new Vector2(rad, -0.35f * r + u * r * 5.5f));
            }
            var mb = new MeshBuilder(1);
            mb.Lathe(prof, 32, Matrix4x4.identity, Color.white);
            sheath = MakeChild("Sheath", mb.Build("Sheath"), sheathMat = FxMat(fx, new Color(1f, 0.45f, 0.2f), 0.85f, r * 6.5f));
            var inner = new List<Vector2>();
            for (int i = 0; i <= 8; i++)
            {
                float u = i / 8f;
                inner.Add(new Vector2(r * (0.02f + 1.08f * Mathf.Sqrt(u)), -0.18f * r + u * r * 1.8f));
            }
            var mi = new MeshBuilder(1);
            mi.Lathe(inner, 32, Matrix4x4.identity, Color.white);
            sheathInner = MakeChild("SheathCore", mi.Build("SheathCore"), sheathInnerMat = FxMat(fx, new Color(1f, 0.8f, 0.6f), 0.6f, r * 2.4f));

            // Engine plumes: one cone per nozzle, parented so they follow the vehicle.
            plumeMat = FxMat(fx, new Color(1f, 0.62f, 0.3f), 0.7f, r * 2.9f);
            var pm = new MeshBuilder(1);
            var pprof = new List<Vector2>();
            for (int i = 0; i <= 8; i++)
            {
                float u = i / 8f;
                pprof.Add(new Vector2(r * (0.06f + 0.22f * u), r * (0.25f + 2.6f * u)));
            }
            pm.Lathe(pprof, 12, Matrix4x4.identity, Color.white);
            var plumeMesh = pm.Build("Plume");
            foreach (var nz in p.Nozzles)
            {
                var t = new GameObject("Plume").transform;
                t.SetParent(nz, false);
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = plumeMesh;
                t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = plumeMat;
                t.gameObject.SetActive(false);
                plumes.Add(t);
            }

            // Ionised wake trail.
            trailMat = FxMat(fx, new Color(1f, 0.5f, 0.3f), 0f);
            trail = p.gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = trailMat;
            trail.time = 2.5f;
            trail.minVertexDistance = r * 2f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0, r * 1.6f), new Keyframe(1, r * 5f));
            trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                alphaKeys = new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0f, 1) },
            };
            trail.emitting = false;

            // Speed streaks: camera-space quads recycled around the probe.
            streakRoot = new GameObject("Streaks").transform;
            streakRoot.SetParent(transform, false);
            streakMesh = new Mesh { name = "Streaks" };
            streakMesh.MarkDynamic();
            var tris = new int[StreakCount * 6];
            var uvs = new Vector2[StreakCount * 4];
            for (int i = 0; i < StreakCount; i++)
            {
                tris[i * 6] = i * 4; tris[i * 6 + 1] = i * 4 + 1; tris[i * 6 + 2] = i * 4 + 2;
                tris[i * 6 + 3] = i * 4; tris[i * 6 + 4] = i * 4 + 2; tris[i * 6 + 5] = i * 4 + 3;
                streakPos[i] = RandomInSphere(30f);
            }
            streakMesh.vertices = verts;
            streakMesh.uv = uvs;
            streakMesh.colors = cols;
            streakMesh.triangles = tris;
            streakMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 200f);
            streakRoot.gameObject.AddComponent<MeshFilter>().sharedMesh = streakMesh;
            streakRoot.gameObject.AddComponent<MeshRenderer>().sharedMaterial = streakMat = FxMat(fx, new Color(0.85f, 0.9f, 1f), 0f);

            // Lightning bolt.
            boltMat = FxMat(fx, new Color(0.75f, 0.82f, 1f), 0f);
            var bgo = new GameObject("Bolt");
            bgo.transform.SetParent(transform, false);
            bolt = bgo.AddComponent<LineRenderer>();
            bolt.sharedMaterial = boltMat;
            bolt.positionCount = 24;
            bolt.widthMultiplier = 6f;
            bolt.useWorldSpace = true;
            bolt.enabled = false;
        }

        Transform MakeChild(string name, Mesh mesh, Material m)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = m;
            return t;
        }

        Vector3 RandomInSphere(float radius)
        {
            Vector3 v;
            do v = new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1);
            while (v.sqrMagnitude > 1f);
            return v * radius;
        }

        /// <summary>Per-frame update from the simulation. throttle is the engine command actually applied.</summary>
        public void Apply(FlightState s, FlightTelemetry t, double throttle, Camera cam, bool reducedMotion, float dt)
        {
            Vector3 pos = probe.transform.position;
            Vector3 air = WorldSpace.Dir(t.AirVelocity);
            float airSpeed = air.magnitude;
            Vector3 flowDir = airSpeed > 1f ? air / airSpeed : -probe.transform.up;

            // ---- Plasma sheath: brightness from convective heat flux (W/m^2) ----
            float q = (float)t.HeatFlux;
            float plasma = Mathf.Clamp01((q - 25000f) / 350000f);
            plasma = Mathf.Sqrt(plasma);
            PlasmaLevel = plasma;
            bool showSheath = plasma > 0.01f;
            sheath.gameObject.SetActive(showSheath);
            sheathInner.gameObject.SetActive(showSheath);
            if (showSheath)
            {
                // Shell axis points downstream (-flowDir); its apex stands off just upstream of the shield.
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, -flowDir);
                Vector3 shield = pos + flowDir * probe.Radius * 0.3f;
                sheath.SetPositionAndRotation(shield, rot);
                sheathInner.SetPositionAndRotation(shield, sheath.rotation);
                float flicker = 1f + 0.12f * Mathf.PerlinNoise(Time.time * 9f, 0.3f) - 0.06f;
                float temp = (float)s.SkinTemperature;
                Color hot = Color.Lerp(new Color(1f, 0.35f, 0.15f), new Color(1f, 0.55f, 0.85f), Mathf.Clamp01((q - 150000f) / 500000f));
                sheathMat.SetColor("_Color", hot);
                sheathMat.SetFloat("_Intensity", plasma * 1.6f * flicker);
                sheathInnerMat.SetFloat("_Intensity", plasma * 3.5f * flicker + Mathf.Clamp01((temp - 1200f) / 1500f));
            }

            // ---- Trail ----
            trail.emitting = plasma > 0.05f;
            trailMat.SetFloat("_Intensity", plasma * 0.8f);

            // ---- Engine plumes: expand at low pressure (high altitude), flicker ----
            plumeLevel = Mathf.MoveTowards(plumeLevel, (float)throttle, dt * 12f);
            bool burning = plumeLevel > 0.02f;
            float expand = Mathf.Lerp(2.2f, 1f, Mathf.Clamp01((float)t.Density / 1.0f));
            for (int i = 0; i < plumes.Count; i++)
            {
                plumes[i].gameObject.SetActive(burning);
                if (!burning) continue;
                float f = 0.85f + 0.3f * Mathf.PerlinNoise(Time.time * 23f + i * 3.1f, i);
                plumes[i].localScale = new Vector3(expand, plumeLevel * f, expand);
            }
            plumeMat.SetFloat("_Intensity", plumeLevel * 2.2f);

            // ---- Speed streaks: visible in thick air or inside clouds, moving with the air ----
            float cloud = Mathf.Clamp01((float)t.CloudDensity * 2f);
            float density = Mathf.Clamp01((float)t.Density / 0.4f);
            float streakAmt = Mathf.Clamp01(airSpeed / 60f) * Mathf.Max(density * 0.35f, cloud) * (reducedMotion ? 0.4f : 1f);
            streakRoot.gameObject.SetActive(streakAmt > 0.01f);
            if (streakAmt > 0.01f) UpdateStreaks(cam, pos, air, streakAmt, cloud, dt);

            // ---- Lightning bolt fade ----
            if (boltTimer > 0f)
            {
                boltTimer -= dt;
                boltMat.SetFloat("_Intensity", Mathf.Clamp01(boltTimer / 0.25f) * 6f * (Mathf.Repeat(boltTimer * 30f, 1f) > 0.3f ? 1f : 0.4f));
                if (boltTimer <= 0f) bolt.enabled = false;
            }
        }

        void UpdateStreaks(Camera cam, Vector3 center, Vector3 air, float amount, float cloud, float dt)
        {
            const float range = 30f;
            Vector3 camPos = cam.transform.position;
            Vector3 step = -air * dt;   // the air moves past the probe opposite to its air-relative velocity
            float len = Mathf.Clamp(air.magnitude * 0.03f, 0.2f, 8f);
            Vector3 dir = air.sqrMagnitude > 1e-4f ? -air.normalized : Vector3.up;
            Color col = Color.Lerp(new Color(0.6f, 0.65f, 0.75f), new Color(1f, 1f, 1f), cloud) * amount;
            for (int i = 0; i < StreakCount; i++)
            {
                streakPos[i] += step;
                Vector3 rel = streakPos[i];
                if (rel.sqrMagnitude > range * range)
                {
                    // Respawn upstream so streaks keep flowing toward and past the camera.
                    streakPos[i] = -dir * range * 0.9f + RandomInSphere(range * 0.6f);
                }
                Vector3 p = center + streakPos[i];
                Vector3 toCam = (camPos - p).normalized;
                Vector3 side = Vector3.Cross(dir, toCam).normalized * 0.03f;
                verts[i * 4] = streakPos[i] - side;
                verts[i * 4 + 1] = streakPos[i] + side;
                verts[i * 4 + 2] = streakPos[i] + side + dir * len;
                verts[i * 4 + 3] = streakPos[i] - side + dir * len;
                Color c = col; c.a = 1f;
                Color tail = col * 0f; tail.a = 1f;
                cols[i * 4] = c; cols[i * 4 + 1] = c; cols[i * 4 + 2] = tail; cols[i * 4 + 3] = tail;
            }
            streakRoot.position = center;
            streakRoot.rotation = Quaternion.identity;
            streakMesh.vertices = verts;
            streakMesh.colors = cols;
            streakMat.SetFloat("_Intensity", 1.5f);
        }

        /// <summary>Draws a branching-looking bolt from cloud base to the probe.</summary>
        public void Strike(Vector3 probePos, Vector3 up)
        {
            Vector3 start = probePos + up * 900f + new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 600f;
            for (int i = 0; i < bolt.positionCount; i++)
            {
                float u = i / (float)(bolt.positionCount - 1);
                Vector3 p = Vector3.Lerp(start, probePos, u);
                if (i > 0 && i < bolt.positionCount - 1)
                    p += new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 120f * Mathf.Sin(u * Mathf.PI);
                bolt.SetPosition(i, p);
            }
            bolt.enabled = true;
            boltTimer = 0.45f;
        }
    }
}
