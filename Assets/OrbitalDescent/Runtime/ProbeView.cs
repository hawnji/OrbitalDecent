using System.Collections.Generic;
using OrbitalDescent.Core;
using UnityEngine;

namespace OrbitalDescent
{
    /// <summary>
    /// Procedural probe model for each vehicle class. Its transform is set every frame from the
    /// simulated attitude (never from the camera), and the heat shield glow, landing legs, plume and
    /// parachute are all driven by simulated state.
    /// </summary>
    public sealed class ProbeView : MonoBehaviour
    {
        public VehicleConfig Vehicle { get; private set; }
        public float Radius { get; private set; }
        public float Height { get; private set; }
        public Material ShieldMat, HullMat, DarkMat, AccentMat;

        Transform body, legsRoot, chuteRoot, canopy;
        readonly List<Transform> legs = new List<Transform>();
        readonly List<Vector3> legDirs = new List<Vector3>();
        readonly List<Transform> nozzles = new List<Transform>();
        LineRenderer risers;
        float legDeploy;
        float cgOffset;

        public IReadOnlyList<Transform> Nozzles => nozzles;
        public Transform Canopy => canopy;

        public static ProbeView Create(VehicleConfig v, Shader lit, Shader unlit)
        {
            var go = new GameObject("Probe-" + v.Name);
            var p = go.AddComponent<ProbeView>();
            p.Build(v, lit, unlit);
            return p;
        }

        static Material Mat(Shader s, Color albedo, float metallic, float roughness, Color emission)
        {
            var m = new Material(s);
            m.SetColor("_Color", albedo);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Roughness", roughness);
            m.SetColor("_Emission", emission);
            return m;
        }

        void Build(VehicleConfig v, Shader lit, Shader unlit)
        {
            Vehicle = v;
            Radius = Mathf.Sqrt((float)v.RefArea / Mathf.PI);
            float r = Radius;
            bool biconic = v.Id == "wildcard";
            Height = biconic ? (float)v.RefLength : r * (v.Id == "hammer" ? 2.5f : 2.1f);
            float h = Height;
            cgOffset = h * 0.3f;

            ShieldMat = Mat(lit, new Color(0.11f, 0.09f, 0.08f), 0.0f, 0.85f, Color.black);
            HullMat = Mat(lit, v.Id == "phantom" ? new Color(0.16f, 0.17f, 0.2f) : new Color(0.82f, 0.83f, 0.85f), v.Id == "phantom" ? 0.6f : 0.85f, v.Id == "phantom" ? 0.35f : 0.28f, Color.black);
            DarkMat = Mat(lit, new Color(0.06f, 0.06f, 0.07f), 0.5f, 0.5f, Color.black);
            Color accent = v.Id == "dart" ? new Color(1f, 0.45f, 0.1f) : v.Id == "hammer" ? new Color(0.95f, 0.8f, 0.15f)
                         : v.Id == "phantom" ? new Color(0.3f, 0.85f, 1f) : new Color(0.9f, 0.2f, 0.35f);
            AccentMat = Mat(lit, accent, 0.2f, 0.45f, accent * 0.05f);

            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            body.localPosition = new Vector3(0, -cgOffset, 0);

            // ---- Hull (lathe) : submesh 0 shield, 1 backshell, 2 dark cover, 3 accent band ----
            var mb = new MeshBuilder(4);
            int seg = 40;
            mb.SetSubmesh(0);
            var shield = new List<Vector2>();
            for (int i = 0; i <= 8; i++)
            {
                float t = i / 8f;
                float rr = r * Mathf.Sin(t * Mathf.PI * 0.5f);
                shield.Add(new Vector2(rr, -0.12f * h * Mathf.Cos(t * Mathf.PI * 0.5f)));
            }
            shield.Add(new Vector2(r * 1.01f, 0.02f * h));
            mb.Lathe(shield, seg, Matrix4x4.identity, Color.white);

            mb.SetSubmesh(1);
            if (biconic)
                mb.Lathe(new[] { new Vector2(r * 1.0f, 0.02f * h), new Vector2(r * 0.86f, 0.45f * h), new Vector2(r * 0.62f, 0.55f * h), new Vector2(r * 0.22f, 0.97f * h) }, seg, Matrix4x4.identity, Color.white, 6f);
            else
                mb.Lathe(new[] { new Vector2(r * 1.0f, 0.02f * h), new Vector2(r * 0.94f, 0.08f * h), new Vector2(r * 0.52f, 0.72f * h), new Vector2(r * 0.42f, 0.78f * h) }, seg, Matrix4x4.identity, Color.white, 6f);

            mb.SetSubmesh(2);
            if (biconic)
                mb.Lathe(new[] { new Vector2(r * 0.22f, 0.97f * h), new Vector2(r * 0.12f, 1.0f * h), new Vector2(0.0f, 1.01f * h) }, seg, Matrix4x4.identity, Color.white);
            else
                mb.Lathe(new[] { new Vector2(r * 0.42f, 0.78f * h), new Vector2(r * 0.36f, 0.86f * h), new Vector2(r * 0.18f, 0.9f * h), new Vector2(0.0f, 0.91f * h) }, seg, Matrix4x4.identity, Color.white);

            mb.SetSubmesh(3);
            float bandY = biconic ? 0.5f * h : 0.3f * h;
            float bandR = biconic ? r * 0.79f : r * 0.81f;
            mb.Lathe(new[] { new Vector2(bandR + 0.004f, bandY), new Vector2(bandR * 0.97f + 0.004f, bandY + 0.05f * h) }, seg, Matrix4x4.identity, Color.white);

            // RCS pods and engine nozzles (dark)
            mb.SetSubmesh(2);
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float y = biconic ? 0.62f * h : 0.55f * h;
                float rr = biconic ? r * 0.58f : r * 0.62f;
                mb.Box(dir * (rr + 0.02f) + Vector3.up * y, new Vector3(0.12f, 0.1f, 0.12f) * r * 1.4f, Quaternion.LookRotation(dir), Color.white);
            }
            var hull = new GameObject("Hull");
            hull.transform.SetParent(body, false);
            hull.AddComponent<MeshFilter>().sharedMesh = mb.Build("Hull-" + v.Id);
            hull.AddComponent<MeshRenderer>().sharedMaterials = new[] { ShieldMat, HullMat, DarkMat, AccentMat };

            // ---- Control surfaces ----
            var fins = new MeshBuilder(1);
            int count = biconic ? 2 : 4;
            for (int i = 0; i < count; i++)
            {
                float a = (i * (360f / count)) * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Vector3 side = Vector3.Cross(Vector3.up, d) * 0.5f;
                if (v.Id == "hammer")
                {
                    // Grid fins near the top
                    float y = 0.7f * h;
                    Vector3 c0 = d * (r * 0.55f);
                    for (int g = 0; g < 4; g++)
                    {
                        Vector3 o = c0 + d * (g * 0.08f * r) + Vector3.up * y;
                        fins.Panel(o - side * 0.5f * r, o + side * 0.5f * r, o + side * 0.5f * r + Vector3.up * 0.25f * r, o - side * 0.5f * r + Vector3.up * 0.25f * r, Color.white);
                    }
                }
                else if (biconic)
                {
                    float y0 = 0.05f * h, y1 = 0.35f * h;
                    Vector3 b0 = d * (r * 0.98f), b1 = d * (r * 1.45f);
                    fins.Panel(b0 + Vector3.up * y0, b1 + Vector3.up * y0, b1 * 0.8f + Vector3.up * y1, b0 * 0.92f + Vector3.up * y1, Color.white);
                }
                else
                {
                    float y0 = 0.12f * h, y1 = 0.32f * h;
                    Vector3 b0 = d * (r * 0.9f), b1 = d * (r * 1.18f);
                    fins.Panel(b0 + Vector3.up * y0, b1 + Vector3.up * (y0 + 0.03f * h), b1 * 0.95f + Vector3.up * y1, b0 * 0.85f + Vector3.up * y1, Color.white);
                }
            }
            var finGo = new GameObject("Fins");
            finGo.transform.SetParent(body, false);
            finGo.AddComponent<MeshFilter>().sharedMesh = fins.Build("Fins-" + v.Id);
            finGo.AddComponent<MeshRenderer>().sharedMaterial = v.Id == "hammer" ? DarkMat : AccentMat;

            // ---- Engine nozzles (fire along -B, through the shield shoulder) ----
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f) * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var nz = new GameObject("Nozzle" + i).transform;
                nz.SetParent(body, false);
                nz.localPosition = d * (r * 0.8f) + Vector3.up * (0.12f * h);
                nz.localRotation = Quaternion.FromToRotation(Vector3.up, (-Vector3.up + d * 0.25f).normalized);
                nozzles.Add(nz);
                var nm = new MeshBuilder(1);
                nm.Lathe(new[] { new Vector2(0.05f * r, 0), new Vector2(0.09f * r, 0.12f * r), new Vector2(0.13f * r, 0.25f * r) }, 12, Matrix4x4.identity, Color.white);
                var ng = new GameObject("Bell");
                ng.transform.SetParent(nz, false);
                ng.AddComponent<MeshFilter>().sharedMesh = nm.Build("Bell");
                ng.AddComponent<MeshRenderer>().sharedMaterial = DarkMat;
            }

            // ---- Landing legs ----
            legsRoot = new GameObject("Legs").transform;
            legsRoot.SetParent(body, false);
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var pivot = new GameObject("Leg" + i).transform;
                pivot.SetParent(legsRoot, false);
                pivot.localPosition = d * (r * 0.75f) + Vector3.up * (0.35f * h);
                pivot.localRotation = Quaternion.LookRotation(d, Vector3.up);
                var lm = new MeshBuilder(1);
                float len = 0.62f * h;
                lm.Box(new Vector3(0, -len * 0.5f, 0), new Vector3(0.05f * r, len, 0.05f * r), Quaternion.identity, Color.white);
                lm.Box(new Vector3(0, -len, 0.02f * r), new Vector3(0.22f * r, 0.03f * r, 0.22f * r), Quaternion.identity, Color.white);
                var lg = new GameObject("Strut");
                lg.transform.SetParent(pivot, false);
                lg.AddComponent<MeshFilter>().sharedMesh = lm.Build("Leg");
                lg.AddComponent<MeshRenderer>().sharedMaterial = DarkMat;
                legs.Add(pivot);
                legDirs.Add(d);
            }

            // ---- Parachute ----
            chuteRoot = new GameObject("Chute").transform;
            chuteRoot.SetParent(transform, false);
            canopy = new GameObject("Canopy").transform;
            canopy.SetParent(chuteRoot, false);
            var cm = new MeshBuilder(1);
            float cr = Mathf.Sqrt((float)v.ChuteCdA / 0.8f / Mathf.PI);
            var prof = new List<Vector2>();
            for (int i = 0; i <= 10; i++)
            {
                float t = i / 10f;
                prof.Add(new Vector2(cr * Mathf.Cos(t * Mathf.PI * 0.5f) * (1 - 0.06f * Mathf.Sin(t * 30f)), cr * 0.55f * Mathf.Sin(t * Mathf.PI * 0.5f)));
            }
            prof.Reverse();
            for (int i = 0; i < prof.Count; i++) prof[i] = new Vector2(prof[i].x, prof[i].y);
            cm.Lathe(BottomToTop(prof), 24, Matrix4x4.identity, Color.white, 12f);
            cm.Lathe(prof, 24, Matrix4x4.identity, Color.white, 12f); // inner side
            canopy.gameObject.AddComponent<MeshFilter>().sharedMesh = cm.Build("Canopy");
            var canopyMat = Mat(lit, new Color(0.95f, 0.35f, 0.12f), 0f, 0.7f, Color.black);
            canopy.gameObject.AddComponent<MeshRenderer>().sharedMaterial = canopyMat;
            risers = chuteRoot.gameObject.AddComponent<LineRenderer>();
            risers.positionCount = 17;
            risers.widthMultiplier = 0.02f;
            risers.useWorldSpace = false;
            risers.material = new Material(unlit);
            risers.material.SetColor("_Color", new Color(0.7f, 0.7f, 0.7f, 1f));
            chuteRoot.gameObject.SetActive(false);
        }

        static List<Vector2> BottomToTop(List<Vector2> p)
        {
            var c = new List<Vector2>(p);
            c.Reverse();
            return c;
        }

        /// <summary>Synchronises the model with the simulated state. Called once per rendered frame.</summary>
        public void Apply(FlightState s, FlightTelemetry t, float dt)
        {
            transform.position = WorldSpace.ToUnity(s.Position);
            transform.rotation = WorldSpace.Rotation(s.Attitude);

            // Heat shield glow from the simulated skin temperature (approximate blackbody colour).
            float temp = (float)s.SkinTemperature;
            Color glow = Blackbody(temp) * Mathf.Clamp01((temp - 700f) / 900f) * 6f;
            ShieldMat.SetColor("_Emission", glow);
            HullMat.SetColor("_Emission", Blackbody(temp) * Mathf.Clamp01((temp - 1100f) / 1200f) * 0.8f);

            // Legs deploy below 600 m above ground.
            float target = t.AltitudeAboveGround < 600 || s.Contact ? 1f : 0f;
            legDeploy = Mathf.MoveTowards(legDeploy, target, dt * 0.7f);
            for (int i = 0; i < legs.Count; i++)
                legs[i].localRotation = Quaternion.LookRotation(legDirs[i], Vector3.up) * Quaternion.Euler(-Mathf.Lerp(6f, 34f, legDeploy), 0, 0);

            // Parachute
            bool chute = s.Chute == ChuteState.Opening || s.Chute == ChuteState.Open;
            chuteRoot.gameObject.SetActive(chute);
            if (chute)
            {
                Vector3 flow = t.AirSpeed > 0.5 ? -WorldSpace.Dir(t.AirVelocity).normalized : transform.up;
                float open = (float)s.ChuteOpen;
                float len = Height * 1.4f + Mathf.Sqrt((float)Vehicle.ChuteCdA) * 2.2f;
                chuteRoot.rotation = Quaternion.FromToRotation(Vector3.up, flow);
                chuteRoot.position = transform.position;
                float flutter = 1f + 0.03f * Mathf.Sin(Time.time * 7.3f) * open;
                canopy.localPosition = Vector3.up * len * Mathf.Lerp(0.4f, 1f, open);
                canopy.localScale = new Vector3(Mathf.Lerp(0.15f, 1f, open) * flutter, Mathf.Lerp(1.6f, 1f, open), Mathf.Lerp(0.15f, 1f, open) / flutter);
                float cr = Mathf.Sqrt((float)Vehicle.ChuteCdA / 0.8f / Mathf.PI) * Mathf.Lerp(0.15f, 1f, open);
                for (int i = 0; i < 8; i++)
                {
                    float a = i / 8f * Mathf.PI * 2f;
                    risers.SetPosition(i * 2, Vector3.up * (Height * 0.6f));
                    risers.SetPosition(i * 2 + 1, canopy.localPosition + new Vector3(Mathf.Cos(a) * cr, 0, Mathf.Sin(a) * cr));
                }
                risers.SetPosition(16, Vector3.up * (Height * 0.6f));
            }
        }

        public static Color Blackbody(float kelvin)
        {
            // Compact fit of blackbody chromaticity (Tanner Helland approximation), linear-ish.
            float t = Mathf.Clamp(kelvin, 800f, 12000f) / 100f;
            float r = t <= 66 ? 1f : Mathf.Clamp01(1.292936f * Mathf.Pow(t - 60f, -0.1332047f));
            float g = t <= 66 ? Mathf.Clamp01(0.39008157f * Mathf.Log(t) - 0.63184144f) : Mathf.Clamp01(1.1298909f * Mathf.Pow(t - 60f, -0.0755148f));
            float b = t >= 66 ? 1f : (t <= 19 ? 0f : Mathf.Clamp01(0.54320679f * Mathf.Log(t - 10f) - 1.19625409f));
            return new Color(r * r, g * g, b * b);
        }
    }
}
