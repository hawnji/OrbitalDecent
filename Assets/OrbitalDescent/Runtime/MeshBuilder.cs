using System.Collections.Generic;
using UnityEngine;

namespace OrbitalDescent
{
    /// <summary>Small procedural mesh toolkit (lathe, boxes, panels) so the game needs no imported models.</summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> n = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<Color> c = new List<Color>();
        readonly List<List<int>> subs = new List<List<int>>();
        int sub;

        public MeshBuilder(int submeshes = 1)
        {
            for (int i = 0; i < submeshes; i++) subs.Add(new List<int>());
        }

        public void SetSubmesh(int i) { sub = i; }

        int Add(Vector3 p, Vector3 normal, Vector2 t, Color col)
        {
            v.Add(p); n.Add(normal); uv.Add(t); c.Add(col);
            return v.Count - 1;
        }

        public void Tri(int a, int b, int d) { var l = subs[sub]; l.Add(a); l.Add(b); l.Add(d); }

        /// <summary>Surface of revolution around +Y from (radius, height) profile points, bottom to top.</summary>
        public void Lathe(IList<Vector2> profile, int segments, Matrix4x4 m, Color col, float uScale = 1f)
        {
            int start = v.Count;
            int rows = profile.Count;
            float accum = 0;
            for (int r = 0; r < rows; r++)
            {
                if (r > 0) accum += Vector2.Distance(profile[r], profile[r - 1]);
                // Profile tangent for smooth normals
                Vector2 a = profile[Mathf.Max(0, r - 1)], b = profile[Mathf.Min(rows - 1, r + 1)];
                Vector2 t = (b - a).normalized;
                Vector2 pn = new Vector2(t.y, -t.x); // outward for bottom-to-top ordering
                for (int s = 0; s <= segments; s++)
                {
                    float ang = s / (float)segments * Mathf.PI * 2f;
                    float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                    Vector3 p = new Vector3(profile[r].x * ca, profile[r].y, profile[r].x * sa);
                    Vector3 nn = new Vector3(pn.x * ca, pn.y, pn.x * sa);
                    Add(m.MultiplyPoint3x4(p), m.MultiplyVector(nn).normalized, new Vector2(s / (float)segments * uScale, accum), col);
                }
            }
            int w = segments + 1;
            for (int r = 0; r < rows - 1; r++)
                for (int s = 0; s < segments; s++)
                {
                    int i0 = start + r * w + s, i1 = i0 + 1, i2 = i0 + w, i3 = i2 + 1;
                    Tri(i0, i2, i1); Tri(i1, i2, i3);
                }
        }

        public void Box(Vector3 center, Vector3 size, Quaternion rot, Color col)
        {
            Vector3 h = size * 0.5f;
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
            {
                Vector3 u = Mathf.Abs(d.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 w = Vector3.Cross(d, u);
                Vector3 nrm = rot * d;
                Vector3 cc = center + rot * Vector3.Scale(d, h);
                Vector3 du = rot * Vector3.Scale(u, h), dw = rot * Vector3.Scale(w, h);
                int a = Add(cc - du - dw, nrm, new Vector2(0, 0), col);
                int b = Add(cc + du - dw, nrm, new Vector2(1, 0), col);
                int e = Add(cc + du + dw, nrm, new Vector2(1, 1), col);
                int f = Add(cc - du + dw, nrm, new Vector2(0, 1), col);
                Tri(a, b, e); Tri(a, e, f);
            }
        }

        /// <summary>Thin double-sided quad (fins, flaps).</summary>
        public void Panel(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col)
        {
            Vector3 nrm = Vector3.Cross(b - a, d - a).normalized;
            int i0 = Add(a, nrm, new Vector2(0, 0), col), i1 = Add(b, nrm, new Vector2(1, 0), col);
            int i2 = Add(cc, nrm, new Vector2(1, 1), col), i3 = Add(d, nrm, new Vector2(0, 1), col);
            Tri(i0, i1, i2); Tri(i0, i2, i3);
            int j0 = Add(a, -nrm, new Vector2(0, 0), col), j1 = Add(b, -nrm, new Vector2(1, 0), col);
            int j2 = Add(cc, -nrm, new Vector2(1, 1), col), j3 = Add(d, -nrm, new Vector2(0, 1), col);
            Tri(j0, j2, j1); Tri(j0, j3, j2);
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetColors(c);
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
