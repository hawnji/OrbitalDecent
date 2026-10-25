using System;
using OrbitalDescent.Core;
using UnityEngine;
using UnityEngine.UI;

namespace OrbitalDescent
{
    /// <summary>
    /// On-screen controls for phones (floating stick on the left half, action buttons on the right),
    /// optional tilt steering, and keyboard / gamepad-style keys for the editor and desktop.
    /// Produces the raw <see cref="ControlInput"/>; steering is expressed camera-relative and
    /// converted to a world direction so "push right" always moves the impact point right on screen.
    /// </summary>
    public sealed class TouchControls : MonoBehaviour
    {
        GameSettings settings;
        RectTransform root;
        VirtualStick stick;
        HoldButton burn, chute, stab, sas, heat, boost, reboot, cam, warp, pause;
        Vector3 tiltZero;
        bool tiltCalibrated;

        public event Action ChutePressed, RebootPressed, CameraPressed, WarpPressed, PausePressed;

        public bool SasOn => sas.Toggled;
        public bool HeaterOn => heat.Toggled;

        public static TouchControls Create(Canvas canvas, GameSettings s)
        {
            var c = canvas.gameObject.AddComponent<TouchControls>();
            c.settings = s;
            c.Build(canvas.transform);
            return c;
        }

        HoldButton MakeButton(Transform parent, string label, Vector2 anchor, Vector2 pos, Vector2 size, bool toggle, int font = 30)
        {
            var img = Ui.Image(parent, "Btn:" + label, Palette.Button, Ui.Rounded);
            img.raycastTarget = true;
            img.rectTransform.Place(anchor, anchor, pos, size);
            var t = Ui.Label(img.transform, label, font, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.rectTransform.Stretch(4, 4, 2, 2);
            var hb = img.gameObject.AddComponent<HoldButton>();
            hb.Background = img;
            hb.Caption = t;
            hb.IsToggle = toggle;
            return hb;
        }

        void Build(Transform canvas)
        {
            root = Ui.Rect("Controls", canvas).Stretch();

            // Stick area: lower-left half of the screen.
            var area = Ui.Image(root, "StickArea", new Color(0, 0, 0, 0));
            area.raycastTarget = true;
            area.rectTransform.anchorMin = new Vector2(0, 0);
            area.rectTransform.anchorMax = new Vector2(0.45f, 0.62f);
            area.rectTransform.offsetMin = area.rectTransform.offsetMax = Vector2.zero;
            stick = area.gameObject.AddComponent<VirtualStick>();
            var b = Ui.Image(area.transform, "Base", new Color(1, 1, 1, 0.12f), Ui.Disc).rectTransform;
            b.sizeDelta = new Vector2(300, 300);
            var ring = Ui.Image(b, "Ring", new Color(1, 1, 1, 0.35f), Ui.Ring).rectTransform;
            ring.Stretch();
            var k = Ui.Image(area.transform, "Knob", new Color(1, 1, 1, 0.55f), Ui.Disc).rectTransform;
            k.sizeDelta = new Vector2(120, 120);
            stick.Base = b; stick.Knob = k; stick.Radius = 150f;
            var steerLabel = Ui.Label(b, "STEER", 22, new Color(1, 1, 1, 0.4f), TextAnchor.MiddleCenter, FontStyle.Bold);
            steerLabel.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(200, 30));

            // Right-hand action cluster.
            var br = new Vector2(1, 0);
            burn = MakeButton(root, "BURN", br, new Vector2(-60, 60), new Vector2(230, 230), false, 40);
            burn.ActiveColor = new Color(0.85f, 0.45f, 0.1f, 0.9f);
            chute = MakeButton(root, "CHUTE", br, new Vector2(-310, 60), new Vector2(170, 110), false);
            stab = MakeButton(root, "STAB", br, new Vector2(-310, 190), new Vector2(170, 110), false);
            boost = MakeButton(root, "BOOST", br, new Vector2(-60, 310), new Vector2(230, 100), false);
            sas = MakeButton(root, "SAS", br, new Vector2(-500, 60), new Vector2(150, 90), true);
            heat = MakeButton(root, "HEAT", br, new Vector2(-500, 170), new Vector2(150, 90), true);
            reboot = MakeButton(root, "REBOOT", new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(300, 110), false, 36);
            reboot.IdleColor = new Color(0.6f, 0.1f, 0.08f, 0.85f);
            reboot.Refresh(false);

            var tr = new Vector2(1, 1);
            pause = MakeButton(root, "II", tr, new Vector2(-24, -24), new Vector2(90, 70), false, 30);
            cam = MakeButton(root, "CAM", tr, new Vector2(-124, -24), new Vector2(110, 70), false, 26);
            warp = MakeButton(root, "WARP", tr, new Vector2(-244, -24), new Vector2(120, 70), false, 26);

            sas.Toggled = true;
            chute.Tapped += () => ChutePressed?.Invoke();
            reboot.Tapped += () => RebootPressed?.Invoke();
            cam.Tapped += () => CameraPressed?.Invoke();
            warp.Tapped += () => WarpPressed?.Invoke();
            pause.Tapped += () => PausePressed?.Invoke();
        }

        public void SetVisible(bool v)
        {
            root.gameObject.SetActive(v);
            if (!v) foreach (var h in new[] { burn, stab, boost }) h.Release();
        }

        public void Configure(VehicleConfig v)
        {
            boost.gameObject.SetActive(v.PrecisionReserve > 0);
            heat.Toggled = false;
            sas.Toggled = true;
            tiltCalibrated = false;
        }

        public void SetWarpLabel(float factor) { warp.Caption.text = factor > 1.01f ? $"x{factor:F0}" : "WARP"; }

        /// <summary>Reads touch, tilt and keyboard into a control input. cameraTransform defines "right" and "up" on screen.</summary>
        public ControlInput Read(Transform cameraTransform, Vec3d probeUp, bool rebootVisible, bool chuteAvailable)
        {
            Vector2 s = stick.Value;

            // Keyboard (editor / desktop / Chromebooks).
            Vector2 k = new Vector2(Axis(KeyCode.D, KeyCode.RightArrow) - Axis(KeyCode.A, KeyCode.LeftArrow),
                                    Axis(KeyCode.W, KeyCode.UpArrow) - Axis(KeyCode.S, KeyCode.DownArrow));
            if (k.sqrMagnitude > 0) s = Vector2.ClampMagnitude(k, 1f);

            if (settings.TiltSteering && !stick.Active && SystemInfo.supportsAccelerometer)
            {
                Vector3 a = Input.acceleration;
                if (!tiltCalibrated) { tiltZero = a; tiltCalibrated = true; }
                Vector3 d = a - tiltZero;
                Vector2 tilt = new Vector2(d.x, d.y) * 3.5f;
                if (tilt.magnitude > 0.06f) s = Vector2.ClampMagnitude(tilt, 1f);
            }
            if (settings.InvertVertical) s.y = -s.y;

            // Screen axes -> horizontal world directions.
            Vector3 up = WorldSpace.Dir(probeUp);
            Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, up).normalized;
            Vector3 fwd = Vector3.ProjectOnPlane(cameraTransform.up, up);
            if (fwd.sqrMagnitude < 0.05f) fwd = Vector3.ProjectOnPlane(cameraTransform.forward, up);
            fwd.Normalize();
            Vector3 steer = right * s.x + fwd * s.y;

            bool burnHeld = burn.Held || Input.GetKey(KeyCode.Space);
            bool stabHeld = stab.Held || Input.GetKey(KeyCode.X);
            bool boostHeld = boost.Held || Input.GetKey(KeyCode.LeftShift);
            if (Input.GetKeyDown(KeyCode.Q)) sas.Toggled = !sas.Toggled;
            if (Input.GetKeyDown(KeyCode.H)) heat.Toggled = !heat.Toggled;
            if (Input.GetKeyDown(KeyCode.C)) ChutePressed?.Invoke();
            if (Input.GetKeyDown(KeyCode.R)) RebootPressed?.Invoke();
            if (Input.GetKeyDown(KeyCode.V)) CameraPressed?.Invoke();
            if (Input.GetKeyDown(KeyCode.T)) WarpPressed?.Invoke();
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)) PausePressed?.Invoke();

            burn.Refresh(burnHeld);
            stab.Refresh(stabHeld);
            boost.Refresh(boostHeld);
            sas.Refresh(sas.Toggled);
            heat.Refresh(heat.Toggled);
            reboot.gameObject.SetActive(rebootVisible);
            chute.Background.color = chuteAvailable ? Palette.Button : new Color(0.08f, 0.1f, 0.12f, 0.45f);

            return new ControlInput
            {
                Steer = WorldSpace.DirToCore(steer),
                Throttle = burnHeld ? 1.0 : 0.0,
                Sas = sas.Toggled,
                Stabilize = stabHeld,
                Precision = boostHeld,
                Heater = heat.Toggled,
            };
        }

        static float Axis(KeyCode a, KeyCode b) => Input.GetKey(a) || Input.GetKey(b) ? 1f : 0f;
    }
}
