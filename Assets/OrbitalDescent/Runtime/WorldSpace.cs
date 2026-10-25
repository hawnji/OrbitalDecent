using OrbitalDescent.Core;
using UnityEngine;

namespace OrbitalDescent
{
    /// <summary>
    /// Floating origin. The simulation runs in double precision in the landing-zone frame; every frame
    /// the render origin is moved to the camera focus (the probe) and all transforms are placed
    /// relative to it, so the GPU only ever sees small float coordinates near the camera.
    /// Unity's axes map 1:1 onto the core frame (X east, Y up, Z north).
    /// </summary>
    public static class WorldSpace
    {
        public static Vec3d Origin;

        public static Vector3 ToUnity(Vec3d p) => new Vector3((float)(p.x - Origin.x), (float)(p.y - Origin.y), (float)(p.z - Origin.z));

        public static Vector3 Dir(Vec3d d) => new Vector3((float)d.x, (float)d.y, (float)d.z);

        public static Vec3d ToCore(Vector3 v) => new Vec3d(v.x + Origin.x, v.y + Origin.y, v.z + Origin.z);

        public static Vec3d DirToCore(Vector3 v) => new Vec3d(v.x, v.y, v.z);

        /// <summary>Unity rotation for a core attitude (body +Y is the probe axis).</summary>
        public static Quaternion Rotation(Quatd q)
        {
            Vector3 up = Dir(q.Rotate(Vec3d.UnitY));
            Vector3 fwd = Dir(q.Rotate(Vec3d.UnitZ));
            return Quaternion.LookRotation(fwd, up);
        }
    }

    /// <summary>Sun and lighting constants shared by every renderer.</summary>
    public static class Sun
    {
        // Late-morning sun, from the south-east, ~35 degrees elevation.
        public static readonly Vector3 Direction = new Vector3(0.45f, 0.58f, -0.68f).normalized;
        public static readonly Color Color = new Color(1.0f, 0.97f, 0.92f);
        public const float Intensity = 20f;
    }
}
