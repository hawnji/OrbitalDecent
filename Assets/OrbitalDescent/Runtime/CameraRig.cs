using OrbitalDescent.Core;
using UnityEngine;

namespace OrbitalDescent
{
    public enum CameraMode { Chase = 0, Close = 1, Cinematic = 2 }

    /// <summary>
    /// Follows the probe from behind its direction of motion. Camera "up" is the local horizontal
    /// direction of travel, so steering input stays consistent when looking almost straight down.
    /// Shake and FOV kick are driven by simulated turbulence and g-load, scaled by accessibility settings.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        public CameraMode Mode = CameraMode.Chase;
        public float ShakeScale = 1f;
        public bool ReducedMotion;

        Vector3 smoothedBack = Vector3.up, smoothedUp = Vector3.forward;
        Vector3 shakeOffset;
        float cinematicAngle;
        float fovKick;

        public static CameraRig Create(QualityProfile q)
        {
            var go = new GameObject("Camera");
            var rig = go.AddComponent<CameraRig>();
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.allowHDR = true;
            cam.allowMSAA = q.Antialiasing > 1;
            cam.depthTextureMode = DepthTextureMode.Depth;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 120000f;
            go.AddComponent<AudioListener>();
            rig.Cam = cam;
            return rig;
        }

        public void Cycle() { Mode = (CameraMode)(((int)Mode + 1) % 3); }

        /// <summary>Snaps the smoothing state (used on release and retry).</summary>
        public void Reset(Vector3 back, Vector3 up)
        {
            smoothedBack = back.normalized;
            smoothedUp = up.normalized;
        }

        /// <summary>Orbital pre-release framing: side-on, showing the probe against the curved horizon.</summary>
        public void UpdateAiming(Vector3 probePos, Vector3 velocityDir, Vector3 localUp, float probeSize, float dt)
        {
            Vector3 side = Vector3.Cross(localUp, velocityDir).normalized;
            Vector3 pos = probePos - velocityDir * probeSize * 4.5f + side * probeSize * 3.2f + localUp * probeSize * 1.6f;
            transform.position = pos;
            Vector3 look = (probePos + velocityDir * probeSize * 6f - localUp * probeSize * 1.5f) - pos;
            transform.rotation = Quaternion.LookRotation(look, localUp);
            Cam.fieldOfView = 58f;
            SetClip(probeSize, 2.5e6f);
        }

        public void UpdateFlight(Vector3 probePos, Vector3 airVelocity, Vector3 groundVelocity, Vector3 localUp, Vector3 toZone,
                                 float probeSize, float shake, float gLoad, float agl, float dt, bool landed)
        {
            Vector3 motion = airVelocity.sqrMagnitude > 4f ? airVelocity : groundVelocity;
            if (motion.sqrMagnitude < 1f) motion = -localUp;
            Vector3 back = -motion.normalized;
            // Keep a minimum elevation above the probe so the view never tunnels under the horizon.
            back = (back + localUp * 0.35f).normalized;
            if (landed) back = (localUp * 0.45f + Vector3.ProjectOnPlane(-toZone, localUp).normalized * 0.9f + smoothedBack * 0.4f).normalized;
            float k = 1f - Mathf.Exp(-dt * (ReducedMotion ? 2.0f : 3.5f));
            smoothedBack = Vector3.Slerp(smoothedBack, back, k).normalized;

            Vector3 horiz = Vector3.ProjectOnPlane(motion, localUp);
            Vector3 upRef = horiz.sqrMagnitude > 25f ? horiz.normalized : Vector3.ProjectOnPlane(toZone, localUp).normalized;
            if (upRef.sqrMagnitude < 0.5f) upRef = smoothedUp;
            // Blend from "horizontal travel is up" (looking down) to "world up" (looking level).
            float level = Mathf.Abs(Vector3.Dot(smoothedBack, localUp));
            Vector3 targetUp = Vector3.Slerp(localUp, upRef, Mathf.SmoothStep(0.45f, 0.9f, level)).normalized;
            smoothedUp = Vector3.Slerp(smoothedUp, targetUp, 1f - Mathf.Exp(-dt * 2.5f)).normalized;

            float dist;
            Vector3 lateral = Vector3.zero;
            switch (Mode)
            {
                case CameraMode.Close:
                    dist = probeSize * 2.6f;
                    lateral = Vector3.Cross(smoothedBack, smoothedUp).normalized * probeSize * 0.9f;
                    break;
                case CameraMode.Cinematic:
                    dist = probeSize * 22f;
                    cinematicAngle += dt * 0.12f;
                    lateral = (Quaternion.AngleAxis(Mathf.Sin(cinematicAngle) * 50f, smoothedUp) * smoothedBack - smoothedBack) * dist;
                    break;
                default:
                    dist = probeSize * 6.5f;
                    break;
            }
            // Do not let the camera go below the ground near touchdown.
            Vector3 pos = probePos + smoothedBack * dist + lateral;
            float camAgl = agl + Vector3.Dot(pos - probePos, localUp);
            if (camAgl < 1.5f) pos += localUp * (1.5f - camAgl);

            // Shake: bounded Perlin noise, scaled by settings.
            float amp = shake * ShakeScale * (ReducedMotion ? 0.3f : 1f) * probeSize * 0.12f;
            float tt = Time.time * 18f;
            Vector3 target = new Vector3(Mathf.PerlinNoise(tt, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, tt) - 0.5f, Mathf.PerlinNoise(tt, tt * 0.5f) - 0.5f) * 2f * amp;
            shakeOffset = Vector3.Lerp(shakeOffset, target, 1f - Mathf.Exp(-dt * 30f));
            transform.position = pos + shakeOffset;
            Vector3 lookAt = probePos - smoothedBack * probeSize * 3f;
            transform.rotation = Quaternion.LookRotation(lookAt - transform.position, smoothedUp);

            float kick = ReducedMotion ? 0f : Mathf.Clamp(gLoad - 1f, 0f, 6f) * 0.8f;
            fovKick = Mathf.Lerp(fovKick, kick, 1f - Mathf.Exp(-dt * 3f));
            Cam.fieldOfView = (Mode == CameraMode.Cinematic ? 42f : Mode == CameraMode.Close ? 70f : 62f) + fovKick;
            SetClip(probeSize, Mathf.Max(90000f, agl * 1.6f + 80000f));
        }

        void SetClip(float probeSize, float far)
        {
            Cam.nearClipPlane = Mathf.Max(0.1f, probeSize * 0.5f);
            Cam.farClipPlane = far;
        }
    }
}
