using OrbitalDescent.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace OrbitalDescent
{
    /// <summary>
    /// Builds the near-field surface: a curved heightfield patch from <see cref="TerrainField"/> (the same
    /// function the physics collides with), the landing pad with a sky beacon, an ocean surface and the
    /// recovery ship for ocean levels. The grid is denser near the pad (power-law spacing) so the final
    /// approach has detail while the 42 km patch edge stays cheap.
    /// </summary>
    public sealed class TerrainView : MonoBehaviour
    {
        World world;
        Transform terrain, pad, water, ship, beacon;
        Material terrainMat, padMat, waterMat, beaconMat;

        public static TerrainView Create(World world, QualityProfile q, Shader terrainShader, Shader lit, Shader water, Shader fx)
        {
            var go = new GameObject("Surface");
            var tv = go.AddComponent<TerrainView>();
            tv.Build(world, q, terrainShader, lit, water, fx);
            return tv;
        }

        static float Curvature(double x, double z) => (float)((x * x + z * z) / (2.0 * Planet.R));

        void Build(World w, QualityProfile q, Shader terrainShader, Shader lit, Shader waterShader, Shader fx)
        {
            world = w;
            var L = w.Level;
            int n = q.TerrainResolution;
            double extent = TerrainField.PatchRadius;
            var verts = new Vector3[n * n];
            var norms = new Vector3[n * n];
            var uv0 = new Vector2[n * n];   // local horizontal coordinates (km) for procedural shading
            var uv1 = new Vector2[n * n];   // height ASL (m), slope
            for (int j = 0; j < n; j++)
            {
                double vz = Map(j, n) * extent;
                for (int i = 0; i < n; i++)
                {
                    double vx = Map(i, n) * extent;
                    double h = w.Terrain.Height(vx, vz);
                    int k = i + j * n;
                    verts[k] = new Vector3((float)vx, (float)h - Curvature(vx, vz), (float)vz);
                    Vec3d nn = w.Terrain.Normal(vx, vz);
                    norms[k] = new Vector3((float)nn.x, (float)nn.y, (float)nn.z);
                    uv0[k] = new Vector2((float)(vx / 1000.0), (float)(vz / 1000.0));
                    uv1[k] = new Vector2((float)h, 1f - (float)nn.y);
                }
            }
            var tris = new int[(n - 1) * (n - 1) * 6];
            int t = 0;
            for (int j = 0; j < n - 1; j++)
                for (int i = 0; i < n - 1; i++)
                {
                    int a = i + j * n, b = a + 1, c = a + n, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            var mesh = new Mesh { name = "Terrain" };
            if (verts.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = verts; mesh.normals = norms; mesh.uv = uv0; mesh.uv2 = uv1; mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3((float)extent * 2.2f, 20000f, (float)extent * 2.2f));

            terrain = new GameObject("Terrain").transform;
            terrain.SetParent(transform, false);
            terrain.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            terrainMat = new Material(terrainShader);
            terrainMat.SetFloat("_ZoneElevation", (float)L.ZoneElevation);
            terrainMat.SetFloat("_SnowLine", (float)(L.ZoneElevation + L.MountainHeight * 0.55 + 600));
            terrainMat.SetFloat("_Ocean", L.Ocean ? 1 : 0);
            terrain.gameObject.AddComponent<MeshRenderer>().sharedMaterial = terrainMat;

            BuildPad(L, lit, fx);
            if (L.Ocean) BuildWater(waterShader);
            if (L.HasPlatform) BuildShip(L, lit);
        }

        /// <summary>Power-law grid mapping in [-1, 1] that clusters vertices near the centre.</summary>
        static double Map(int i, int n)
        {
            double u = i / (double)(n - 1) * 2.0 - 1.0;
            return System.Math.Sign(u) * u * u * (0.35 + 0.65 * System.Math.Abs(u)) / 1.0;
        }

        void BuildPad(LevelDefinition L, Shader lit, Shader fx)
        {
            pad = new GameObject("Pad").transform;
            pad.SetParent(transform, false);
            var mb = new MeshBuilder(2);
            float r = (float)TerrainField.PadRadius;
            mb.SetSubmesh(0);
            // Concrete disc and outer ring
            var disc = new[] { new Vector2(0.01f, 0.4f), new Vector2(r * 0.98f, 0.4f), new Vector2(r * 1.02f, -0.3f) };
            mb.Lathe(Reverse(disc), 48, Matrix4x4.identity, Color.white);
            mb.SetSubmesh(1);
            float ring = r * 0.82f;
            mb.Lathe(Reverse(new[] { new Vector2(ring - 1.2f, 0.43f), new Vector2(ring, 0.43f) }), 64, Matrix4x4.identity, Color.white);
            // Centre cross
            mb.Box(new Vector3(0, 0.42f, 0), new Vector3(16f, 0.04f, 2f), Quaternion.identity, Color.white);
            mb.Box(new Vector3(0, 0.42f, 0), new Vector3(2f, 0.04f, 16f), Quaternion.identity, Color.white);
            var padMesh = mb.Build("Pad");
            padMat = new Material(lit);
            padMat.SetColor("_Color", new Color(0.42f, 0.42f, 0.41f)); padMat.SetFloat("_Roughness", 0.9f); padMat.SetFloat("_Metallic", 0);
            var markMat = new Material(lit);
            markMat.SetColor("_Color", new Color(0.9f, 0.75f, 0.1f)); markMat.SetColor("_Emission", new Color(1.2f, 0.9f, 0.2f)); markMat.SetFloat("_Roughness", 0.6f);
            if (!L.HasPlatform)
            {
                pad.gameObject.AddComponent<MeshFilter>().sharedMesh = padMesh;
                pad.gameObject.AddComponent<MeshRenderer>().sharedMaterials = new[] { padMat, markMat };
            }

            // Beacon: tall additive light column so the zone is identifiable from altitude.
            beacon = new GameObject("Beacon").transform;
            beacon.SetParent(transform, false);
            var bm = new MeshBuilder(1);
            bm.Lathe(new[] { new Vector2(6f, 0f), new Vector2(3f, 2500f), new Vector2(0.5f, 6000f) }, 12, Matrix4x4.identity, Color.white);
            beacon.gameObject.AddComponent<MeshFilter>().sharedMesh = bm.Build("Beacon");
            beaconMat = new Material(fx);
            beaconMat.SetColor("_Color", new Color(0.35f, 0.9f, 1.0f, 1f));
            beaconMat.SetFloat("_Intensity", 0.6f);
            beacon.gameObject.AddComponent<MeshRenderer>().sharedMaterial = beaconMat;
        }

        static Vector2[] Reverse(Vector2[] p) { var c = (Vector2[])p.Clone(); System.Array.Reverse(c); return c; }

        void BuildWater(Shader waterShader)
        {
            water = new GameObject("Ocean").transform;
            water.SetParent(transform, false);
            // Polar grid: dense rings near the centre, curved with the planet.
            int rings = 64, seg = 96;
            var v = new Vector3[(rings + 1) * seg + 1];
            v[0] = Vector3.zero;
            for (int r = 1; r <= rings; r++)
            {
                double rad = TerrainField.PatchRadius * 1.15 * System.Math.Pow(r / (double)rings, 2.2);
                for (int s = 0; s < seg; s++)
                {
                    double a = s / (double)seg * System.Math.PI * 2;
                    double x = System.Math.Cos(a) * rad, z = System.Math.Sin(a) * rad;
                    v[1 + (r - 1) * seg + s] = new Vector3((float)x, -Curvature(x, z), (float)z);
                }
            }
            var tri = new System.Collections.Generic.List<int>();
            for (int s = 0; s < seg; s++) { tri.Add(0); tri.Add(1 + (s + 1) % seg); tri.Add(1 + s); }
            for (int r = 1; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = 1 + (r - 1) * seg + s, b = 1 + (r - 1) * seg + (s + 1) % seg;
                    int c = a + seg, d = b + seg;
                    tri.Add(a); tri.Add(b); tri.Add(c);
                    tri.Add(b); tri.Add(d); tri.Add(c);
                }
            var m = new Mesh { name = "Ocean" };
            m.vertices = v;
            var n = new Vector3[v.Length]; for (int i = 0; i < n.Length; i++) n[i] = Vector3.up;
            m.normals = n;
            m.triangles = tri.ToArray();
            m.bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 2000f, 1e5f));
            water.gameObject.AddComponent<MeshFilter>().sharedMesh = m;
            waterMat = new Material(waterShader);
            water.gameObject.AddComponent<MeshRenderer>().sharedMaterial = waterMat;
        }

        void BuildShip(LevelDefinition L, Shader lit)
        {
            ship = new GameObject("RecoveryShip").transform;
            ship.SetParent(transform, false);
            var mb = new MeshBuilder(3);
            float deck = (float)L.DeckHeight, rad = (float)L.DeckRadius;
            mb.SetSubmesh(0);
            mb.Box(new Vector3(0, deck * 0.5f - 4f, -10f), new Vector3(rad * 2.1f, deck + 8f, rad * 3.6f), Quaternion.identity, Color.white);
            mb.Box(new Vector3(0, deck + 7f, -rad * 1.45f), new Vector3(rad * 1.2f, 14f, 18f), Quaternion.identity, Color.white);
            mb.SetSubmesh(1);
            mb.Box(new Vector3(0, deck + 0.05f, 0), new Vector3(rad * 2.0f, 0.1f, rad * 2.0f), Quaternion.identity, Color.white);
            mb.SetSubmesh(2);
            mb.Lathe(new[] { new Vector2(rad * 0.86f, deck + 0.16f), new Vector2(rad * 0.8f, deck + 0.16f) }, 48, Matrix4x4.identity, Color.white);
            mb.Box(new Vector3(0, deck + 0.17f, 0), new Vector3(rad * 0.9f, 0.02f, 1.5f), Quaternion.identity, Color.white);
            mb.Box(new Vector3(0, deck + 0.17f, 0), new Vector3(1.5f, 0.02f, rad * 0.9f), Quaternion.identity, Color.white);
            var hull = new Material(lit); hull.SetColor("_Color", new Color(0.18f, 0.2f, 0.24f)); hull.SetFloat("_Roughness", 0.6f); hull.SetFloat("_Metallic", 0.4f);
            var deckM = new Material(lit); deckM.SetColor("_Color", new Color(0.3f, 0.32f, 0.33f)); deckM.SetFloat("_Roughness", 0.85f);
            var mark = new Material(lit); mark.SetColor("_Color", new Color(0.95f, 0.95f, 0.95f)); mark.SetColor("_Emission", new Color(0.6f, 0.6f, 0.6f));
            ship.gameObject.AddComponent<MeshFilter>().sharedMesh = mb.Build("Ship");
            ship.gameObject.AddComponent<MeshRenderer>().sharedMaterials = new[] { hull, deckM, mark };
        }

        /// <summary>Places all surface objects relative to the floating origin.</summary>
        public void UpdateView(double time, Vec3d probePos, double probeAgl)
        {
            terrain.position = WorldSpace.ToUnity(Vec3d.Zero);
            if (water != null) water.position = WorldSpace.ToUnity(Vec3d.Zero);
            Vec3d zone = world.ZonePosition(time);
            Planet.Horizontal(zone, out double zx, out double zz);
            if (ship != null)
            {
                Vec3d basePos = Planet.PointAbove(zx, zz, 0);
                ship.position = WorldSpace.ToUnity(basePos);
                ship.rotation = Quaternion.LookRotation(new Vector3((float)world.Level.PlatformEast, 0, (float)world.Level.PlatformNorth).normalized, Vector3.up);
            }
            pad.position = WorldSpace.ToUnity(Planet.PointAbove(0, 0, world.Level.ZoneElevation));
            beacon.position = WorldSpace.ToUnity(zone);
            // Fade the beacon as the probe nears the surface so it never obscures the pad.
            float fade = Mathf.Clamp01((float)(probeAgl - 150) / 1500f);
            beaconMat.SetFloat("_Intensity", 0.15f + 0.55f * fade);
            terrainMat.SetVector("_ProbeShadow", new Vector4(0, 0, 0, 0));
            if (waterMat != null) waterMat.SetFloat("_Time2", (float)time);
        }

        public void SetProbeShadow(Vector3 groundPoint, float radius, float strength)
        {
            terrainMat.SetVector("_ProbeShadow", new Vector4(groundPoint.x, groundPoint.y, groundPoint.z, radius));
            terrainMat.SetFloat("_ProbeShadowStrength", strength);
            if (padMat != null) { padMat.SetVector("_ProbeShadow", new Vector4(groundPoint.x, groundPoint.y, groundPoint.z, radius)); padMat.SetFloat("_ProbeShadowStrength", strength); }
        }

        public Material TerrainMaterial => terrainMat;
        public Material WaterMaterial => waterMat;
    }
}
