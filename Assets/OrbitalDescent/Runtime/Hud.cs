using System;
using System.Collections.Generic;
using OrbitalDescent.Core;
using UnityEngine;
using UnityEngine.UI;

namespace OrbitalDescent
{
    /// <summary>
    /// Flight HUD built with uGUI from code. Always shows: altitude bar (log scale with reference marks),
    /// speed panel with history graph, navigation scope with zone, predicted impact and wind, distance
    /// and predicted error, vehicle status bars (authority, stability, heat, hull, fuel, ice, battery),
    /// hazard banners with disruption timers, and world-space zone / impact markers.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        GameSettings settings;
        RectTransform root, safe;
        readonly List<RectTransform> scaled = new List<RectTransform>();

        // altitude bar
        RectTransform altBar, altMarker;
        Text altText, aglText, regimeText;
        readonly List<RectTransform> altBands = new List<RectTransform>();
        const float AltBarHeight = 560f;
        const double AltBarTop = 150000;

        // speed
        Text speedText, speedSub, vsText, machText, gText, qText;
        UiPolyline speedGraph, vsGraph;
        readonly float[] speedHist = new float[90];
        readonly float[] vsHist = new float[90];
        float histTimer;

        // nav scope
        RectTransform scope, scopeImpact, scopeProbe, scopeZone, scopeWindTip, scopeNorth;
        Image scopeGood, scopeComplete, scopeUncertainty;
        UiPolyline scopeWind, scopePath;
        Text scopeRange, navText1, navText2, navText3, navLost;

        // status bars
        sealed class Bar { public Image Fill; public Text Value; }
        Bar authBar, stabBar, heatBar, hullBar, fuelBar, iceBar, battBar, boostBar;

        // hazards
        readonly List<(Image bg, Text text, Image timer)> banners = new List<(Image, Text, Image)>();

        // world markers
        RectTransform zoneMarker, impactMarker, zoneArrow;
        Text zoneMarkerText;

        // toast / hint
        Text toast, hint;
        float toastTime;
        string lastToast = "";

        public static Hud Create(Canvas canvas, GameSettings s)
        {
            var h = canvas.gameObject.AddComponent<Hud>();
            h.settings = s;
            h.Build(canvas.transform);
            return h;
        }

        RectTransform Group(string name, Vector2 anchor)
        {
            var g = Ui.Rect(name, safe);
            g.Place(anchor, anchor, Vector2.zero, new Vector2(10, 10));
            scaled.Add(g);
            return g;
        }

        void Build(Transform canvas)
        {
            root = Ui.Rect("Hud", canvas).Stretch();
            safe = Ui.Rect("Safe", root).Stretch();

            BuildAltitudeBar(Group("Left", new Vector2(0, 1)));
            BuildSpeedPanel(Group("TopLeft", new Vector2(0, 1)));
            BuildScope(Group("TopRight", new Vector2(1, 1)));
            BuildBanners(Group("Top", new Vector2(0.5f, 1)));

            var markers = Ui.Rect("Markers", root).Stretch();
            markers.SetAsFirstSibling();
            zoneMarker = Ui.Image(markers, "Zone", Palette.Zone, Ui.Ring).rectTransform;
            zoneMarker.sizeDelta = new Vector2(70, 70);
            zoneMarkerText = Ui.Label(zoneMarker, "", 24, Palette.Zone, TextAnchor.UpperCenter);
            zoneMarkerText.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(240, 30));
            impactMarker = Ui.Rect("Impact", markers);
            impactMarker.sizeDelta = new Vector2(56, 56);
            for (int i = 0; i < 2; i++)
            {
                var l = Ui.Image(impactMarker, "Arm", Palette.Impact).rectTransform;
                l.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56, 5));
                l.localRotation = Quaternion.Euler(0, 0, 45 + 90 * i);
            }
            zoneArrow = Ui.Image(markers, "ZoneArrow", Palette.Zone, Ui.Disc).rectTransform;
            zoneArrow.sizeDelta = new Vector2(26, 26);

            toast = Ui.Label(root, "", 40, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            toast.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -170), new Vector2(1200, 60));
            hint = Ui.Label(root, "", 28, Palette.TextDim, TextAnchor.MiddleCenter);
            hint.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(1000, 80));
        }

        // ---------------------------------------------------------------- builders

        void BuildAltitudeBar(RectTransform g)
        {
            var frame = Ui.Image(g, "AltFrame", Palette.Panel, Ui.Rounded).rectTransform;
            frame.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -130), new Vector2(64, AltBarHeight + 24));
            altBar = Ui.Image(frame, "Track", new Color(1, 1, 1, 0.12f)).rectTransform;
            altBar.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, AltBarHeight));
            // Reference marks: Karman line, stratopause, jet stream, zone.
            AltTick(frame, 100000, "100 km");
            AltTick(frame, 50000, "50 km");
            AltTick(frame, 10000, "10 km");
            AltTick(frame, 1000, "1 km");
            altMarker = Ui.Image(frame, "Marker", Palette.Hud, Ui.Disc).rectTransform;
            altMarker.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 30));
            regimeText = Ui.Label(g, "", 24, Palette.HudDim, TextAnchor.LowerLeft, FontStyle.Bold);
            regimeText.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 0), new Vector2(24, -124), new Vector2(360, 30));
            altText = Ui.Label(g, "", 34, Palette.Hud, TextAnchor.UpperLeft, FontStyle.Bold);
            altText.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -130 - AltBarHeight - 36), new Vector2(300, 40));
            aglText = Ui.Label(g, "", 26, Palette.HudDim, TextAnchor.UpperLeft);
            aglText.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -130 - AltBarHeight - 76), new Vector2(300, 34));
        }

        static float AltToBar(double alt)
        {
            double a = Math.Max(0, alt);
            double f = Math.Log10(1 + a / 50.0) / Math.Log10(1 + AltBarTop / 50.0);
            return (float)(MathUtil.Clamp01(f) * AltBarHeight) + 12f;
        }

        void AltTick(RectTransform frame, double alt, string label)
        {
            var t = Ui.Image(frame, "Tick", Palette.HudDim).rectTransform;
            t.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, AltToBar(alt)), new Vector2(34, 2));
            var l = Ui.Label(frame, label, 20, Palette.HudDim, TextAnchor.MiddleLeft);
            l.rectTransform.Place(new Vector2(1, 0), new Vector2(0, 0.5f), new Vector2(8, AltToBar(alt)), new Vector2(110, 24));
        }

        RectTransform AltBand(double lo, double hi, Color c, string label)
        {
            var frame = (RectTransform)altBar.parent;
            float y0 = AltToBar(lo), y1 = AltToBar(hi);
            var b = Ui.Image(frame, "Band", c).rectTransform;
            b.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, y0), new Vector2(22, Mathf.Max(3, y1 - y0)));
            b.SetSiblingIndex(1);
            if (!string.IsNullOrEmpty(label))
            {
                var l = Ui.Label(b, label, 18, new Color(c.r, c.g, c.b, 1f), TextAnchor.MiddleRight);
                l.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(-30, 0), new Vector2(120, 22));
            }
            altBands.Add(b);
            return b;
        }

        /// <summary>Marks the level's cloud layers, parachute window and zone elevation on the bar.</summary>
        public void SetupLevel(LevelDefinition L, VehicleConfig v)
        {
            foreach (var b in altBands) Destroy(b.gameObject);
            altBands.Clear();
            foreach (var c in L.Clouds)
                AltBand(c.Base, c.Top, c.Type == CloudType.Storm ? new Color(1f, 0.5f, 0.3f, 0.45f) : new Color(1, 1, 1, 0.3f), "");
            AltBand(L.ZoneElevation + 600, L.ZoneElevation + v.ChuteMaxDeployAltitude, new Color(1f, 0.78f, 0.2f, 0.35f), "CHUTE");
            AltBand(L.ZoneElevation, L.ZoneElevation + 15, Palette.Zone, "ZONE");
            boostBar.Fill.transform.parent.parent.gameObject.SetActive(v.PrecisionReserve > 0);
        }

        void BuildSpeedPanel(RectTransform g)
        {
            var p = Ui.Image(g, "SpeedPanel", Palette.Panel, Ui.Rounded).rectTransform;
            p.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(130, -24), new Vector2(430, 300));
            speedText = Ui.Label(p, "", 54, Palette.Hud, TextAnchor.UpperLeft, FontStyle.Bold);
            speedText.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -10), new Vector2(400, 64));
            speedSub = Ui.Label(p, "", 24, Palette.HudDim, TextAnchor.UpperLeft);
            speedSub.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -70), new Vector2(400, 30));
            vsText = Ui.Label(p, "", 28, Palette.Hud, TextAnchor.UpperLeft);
            vsText.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -104), new Vector2(200, 34));
            machText = Ui.Label(p, "", 28, Palette.Hud, TextAnchor.UpperLeft);
            machText.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -104), new Vector2(200, 34));
            gText = Ui.Label(p, "", 28, Palette.Hud, TextAnchor.UpperLeft);
            gText.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -138), new Vector2(200, 34));
            qText = Ui.Label(p, "", 28, Palette.Hud, TextAnchor.UpperLeft);
            qText.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -138), new Vector2(200, 34));
            var graphBg = Ui.Image(p, "Graph", new Color(1, 1, 1, 0.05f)).rectTransform;
            graphBg.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(16, 14), new Vector2(398, 110));
            speedGraph = Graph(graphBg, Palette.Hud);
            vsGraph = Graph(graphBg, Palette.Caution);

            // Status bars below the speed panel.
            var s = Ui.Image(g, "Status", Palette.Panel, Ui.Rounded).rectTransform;
            s.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(130, -336), new Vector2(430, 8 * 34 + 16));
            float y = 10;
            authBar = MakeBar(s, "AUTH", ref y);
            stabBar = MakeBar(s, "STAB", ref y);
            heatBar = MakeBar(s, "HEAT", ref y);
            hullBar = MakeBar(s, "HULL", ref y);
            fuelBar = MakeBar(s, "FUEL", ref y);
            iceBar = MakeBar(s, "ICE", ref y);
            battBar = MakeBar(s, "BATT", ref y);
            boostBar = MakeBar(s, "BOOST", ref y);
        }

        static UiPolyline Graph(RectTransform parent, Color c)
        {
            var rt = Ui.Rect("Line", parent).Stretch();
            var l = rt.gameObject.AddComponent<UiPolyline>();
            l.color = c;
            l.Thickness = 3f;
            l.raycastTarget = false;
            return l;
        }

        static Bar MakeBar(RectTransform parent, string label, ref float y)
        {
            var row = Ui.Rect(label, parent);
            row.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -y), new Vector2(400, 28));
            var l = Ui.Label(row, label, 22, Palette.HudDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            l.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(90, 28));
            var track = Ui.Image(row, "Track", new Color(1, 1, 1, 0.1f)).rectTransform;
            track.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(92, 0), new Vector2(210, 14));
            var fill = Ui.Image(track, "Fill", Palette.Hud);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(1, 1);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            fill.rectTransform.pivot = new Vector2(0, 0.5f);
            var v = Ui.Label(row, "", 22, Palette.Hud, TextAnchor.MiddleRight);
            v.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-4, 0), new Vector2(90, 28));
            y += 34;
            return new Bar { Fill = fill, Value = v };
        }

        static void SetBar(Bar b, double frac, string value, bool highIsBad, double caution = 0.6, double warning = 0.85)
        {
            float f = (float)MathUtil.Clamp01(frac);
            b.Fill.rectTransform.anchorMax = new Vector2(f, 1);
            double badness = highIsBad ? f : 1 - f;
            b.Fill.color = badness > warning ? Palette.Warning : badness > caution ? Palette.Caution : Palette.Hud;
            b.Value.text = value;
        }

        void BuildScope(RectTransform g)
        {
            const float size = 330;
            var bg = Ui.Image(g, "Scope", Palette.Panel, Ui.Disc).rectTransform;
            bg.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -110), new Vector2(size, size));
            scope = bg;
            Ui.Image(bg, "Rim", Palette.HudDim, Ui.Ring).rectTransform.Stretch();
            var cross1 = Ui.Image(bg, "X", new Color(1, 1, 1, 0.08f)).rectTransform;
            cross1.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, 2));
            var cross2 = Ui.Image(bg, "Y", new Color(1, 1, 1, 0.08f)).rectTransform;
            cross2.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2, size));
            scopeComplete = Ui.Image(bg, "Complete", new Color(0.35f, 0.95f, 1f, 0.35f), Ui.Ring);
            scopeGood = Ui.Image(bg, "Good", new Color(0.45f, 1f, 0.55f, 0.6f), Ui.Ring);
            scopeZone = Ui.Image(bg, "Zone", Palette.Zone, Ui.Disc).rectTransform;
            scopeZone.sizeDelta = new Vector2(16, 16);
            scopePath = Graph(Ui.Rect("Path", bg).Stretch(), new Color(1f, 0.55f, 0.95f, 0.5f));
            scopePath.Thickness = 2f;
            scopeUncertainty = Ui.Image(bg, "Uncertainty", new Color(1f, 0.55f, 0.95f, 0.25f), Ui.Disc);
            scopeImpact = Ui.Rect("Impact", bg);
            scopeImpact.sizeDelta = new Vector2(28, 28);
            for (int i = 0; i < 2; i++)
            {
                var l = Ui.Image(scopeImpact, "Arm", Palette.Impact).rectTransform;
                l.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28, 4));
                l.localRotation = Quaternion.Euler(0, 0, 45 + 90 * i);
            }
            scopeProbe = Ui.Image(bg, "Probe", Palette.Text, Ui.Disc).rectTransform;
            scopeProbe.sizeDelta = new Vector2(14, 14);
            scopeWind = Graph(Ui.Rect("Wind", bg).Stretch(), new Color(0.8f, 0.9f, 1f, 0.8f));
            scopeWindTip = Ui.Rect("WindTip", bg);
            scopeNorth = Ui.Label(bg, "N", 22, Palette.HudDim, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform;
            scopeNorth.sizeDelta = new Vector2(30, 30);
            scopeRange = Ui.Label(bg, "", 20, Palette.HudDim, TextAnchor.LowerRight);
            scopeRange.rectTransform.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-10, 6), new Vector2(150, 24));
            navLost = Ui.Label(bg, "", 30, Palette.Warning, TextAnchor.MiddleCenter, FontStyle.Bold);
            navLost.rectTransform.Stretch(20, 20, 20, 20);

            var info = Ui.Image(g, "NavInfo", Palette.Panel, Ui.Rounded).rectTransform;
            info.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -450), new Vector2(size, 128));
            navText1 = Ui.Label(info, "", 30, Palette.Zone, TextAnchor.UpperLeft, FontStyle.Bold);
            navText1.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -8), new Vector2(size - 30, 36));
            navText2 = Ui.Label(info, "", 28, Palette.Impact, TextAnchor.UpperLeft, FontStyle.Bold);
            navText2.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -46), new Vector2(size - 30, 34));
            navText3 = Ui.Label(info, "", 24, Palette.HudDim, TextAnchor.UpperLeft);
            navText3.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -84), new Vector2(size - 30, 30));
        }

        void BuildBanners(RectTransform g)
        {
            for (int i = 0; i < 3; i++)
            {
                var bg = Ui.Image(g, "Banner" + i, Palette.Panel, Ui.Rounded);
                bg.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -110 - i * 66), new Vector2(640, 58));
                var t = Ui.Label(bg.transform, "", 28, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                t.rectTransform.Stretch(12, 12, 0, 6);
                var timer = Ui.Image(bg.transform, "Timer", Palette.Text);
                timer.rectTransform.anchorMin = new Vector2(0, 0); timer.rectTransform.anchorMax = new Vector2(1, 0);
                timer.rectTransform.pivot = new Vector2(0, 0);
                timer.rectTransform.offsetMin = new Vector2(8, 4); timer.rectTransform.offsetMax = new Vector2(-8, 9);
                bg.gameObject.SetActive(false);
                banners.Add((bg, t, timer));
            }
        }

        // ---------------------------------------------------------------- runtime

        public void SetVisible(bool v) { root.gameObject.SetActive(v); }

        void ApplyLayout()
        {
            // Safe area (notches, rounded corners) plus the player's extra margin, in canvas units.
            var canvasRect = (RectTransform)root.parent;
            Rect sa = Screen.safeArea;
            float sx = canvasRect.rect.width / Mathf.Max(1, Screen.width), sy = canvasRect.rect.height / Mathf.Max(1, Screen.height);
            float extra = settings.SafeMargin * canvasRect.rect.height;
            safe.offsetMin = new Vector2(sa.xMin * sx + extra, sa.yMin * sy + extra);
            safe.offsetMax = new Vector2(-(Screen.width - sa.xMax) * sx - extra, -(Screen.height - sa.yMax) * sy - extra);
            foreach (var g in scaled) g.localScale = Vector3.one * settings.HudScale;
        }

        public void UpdateHud(MissionSession m, Camera cam, float dt)
        {
            ApplyLayout();
            var t = m.Telemetry;
            var s = m.State;
            var v = m.Vehicle;

            // ---- altitude ----
            altMarker.anchoredPosition = new Vector2(0, AltToBar(t.Altitude));
            altText.text = "ALT " + Units.Altitude(t.Altitude);
            aglText.text = "AGL " + Units.Altitude(Math.Max(0, t.AltitudeAboveGround));
            regimeText.text = RegimeName(m.Regime);

            // ---- speed ----
            speedText.text = Units.Speed(t.Speed);
            speedSub.text = Units.SpeedSecondary(t.Speed) + "   AIR " + Units.Speed(t.AirSpeed);
            vsText.text = "V/S " + Units.Speed(t.VerticalSpeed);
            vsText.color = t.AltitudeAboveGround < 300 && -t.VerticalSpeed > v.MaxTouchdownSpeed ? Palette.Warning : Palette.Hud;
            machText.text = $"MACH {t.Mach:F1}";
            gText.text = $"G {t.GLoad:F1}";
            gText.color = t.GLoad > v.MaxGLoad * 0.8 ? Palette.Warning : t.GLoad > v.MaxGLoad * 0.5 ? Palette.Caution : Palette.Hud;
            qText.text = $"Q {t.DynamicPressure / 1000:F1} kPa";
            histTimer -= dt;
            if (histTimer <= 0)
            {
                histTimer = 0.33f;
                Array.Copy(speedHist, 1, speedHist, 0, speedHist.Length - 1);
                Array.Copy(vsHist, 1, vsHist, 0, vsHist.Length - 1);
                speedHist[speedHist.Length - 1] = (float)t.Speed;
                vsHist[vsHist.Length - 1] = (float)-t.VerticalSpeed;
                FillGraph(speedGraph, speedHist);
                FillGraph(vsGraph, vsHist);
            }

            // ---- status bars ----
            double nominalAuth = Math.Max(1e-3, 2.0 * v.RcsTorque / v.Inertia);
            SetBar(authBar, t.ControlAuthority / nominalAuth, $"{Math.Min(999, t.ControlAuthority / nominalAuth * 100):F0}%", false, 0.7, 0.85);
            double stab = 1 - MathUtil.Clamp01(t.AngularRate / 2.0);
            SetBar(stabBar, stab, $"{t.AngularRate * 57.3:F0}°/s", false, 0.5, 0.75);
            SetBar(heatBar, s.SkinTemperature / v.MaxSkinTemp, $"{s.SkinTemperature:F0} K", true, 0.7, 0.9);
            SetBar(hullBar, s.Integrity, $"{s.Integrity * 100:F0}%", false, 0.4, 0.7);
            SetBar(fuelBar, s.Fuel / Math.Max(1e-6, v.FuelMass), $"{s.Fuel:F0} kg", false, 0.7, 0.9);
            double iceMax = v.MaxIceFraction * v.TotalMass;
            SetBar(iceBar, s.Ice / Math.Max(1e-6, iceMax), $"{s.Ice:F1} kg", true, 0.4, 0.7);
            SetBar(battBar, s.Battery, $"{s.Battery * 100:F0}%", false, 0.7, 0.9);
            if (v.PrecisionReserve > 0) SetBar(boostBar, s.PrecisionLeft / v.PrecisionReserve, $"{s.PrecisionLeft:F1} s", false, 0.7, 0.9);

            // ---- navigation ----
            UpdateNav(m, cam);

            // ---- hazards ----
            int i = 0;
            foreach (var h in m.Hazards.ActiveStatuses())
            {
                if (i >= banners.Count) break;
                var (bg, text, timer) = banners[i];
                bg.gameObject.SetActive(true);
                bool warn = h.Level == HazardLevel.Warning;
                bg.color = warn ? new Color(0.45f, 0.05f, 0.04f, 0.85f) : new Color(0.4f, 0.28f, 0.02f, 0.8f);
                string timerText = h.TimeRemaining > 0 ? $"  {h.TimeRemaining:F1} s" : "";
                text.text = $"{h.Label}: {h.Message}{timerText}";
                bool timed = h.TimeRemaining > 0;
                timer.gameObject.SetActive(timed || h.Intensity > 0);
                timer.rectTransform.localScale = new Vector3((float)MathUtil.Clamp01(timed ? h.TimeRemaining / 5.0 : h.Intensity), 1, 1);
                // Flash warnings unless reduced motion is on.
                float a = warn && !settings.ReducedMotion ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 10f) : 1f;
                text.color = new Color(1, 1, 1, a);
                i++;
            }
            for (; i < banners.Count; i++) banners[i].bg.gameObject.SetActive(false);

            // ---- toast ----
            if (m.LastMessage != lastToast)
            {
                lastToast = m.LastMessage;
                if (!string.IsNullOrEmpty(lastToast)) { toast.text = lastToast; toastTime = 2.5f; }
            }
            toastTime -= dt;
            toast.color = new Color(1, 1, 1, Mathf.Clamp01(toastTime));
        }

        static void FillGraph(UiPolyline g, float[] data)
        {
            var r = ((RectTransform)g.transform).rect;
            float max = 1f;
            foreach (var d in data) max = Mathf.Max(max, Mathf.Abs(d));
            g.Points.Clear();
            for (int k = 0; k < data.Length; k++)
                g.Points.Add(new Vector2(r.xMin + r.width * k / (data.Length - 1), r.yMin + r.height * Mathf.Clamp01(data[k] / max)));
            g.SetDirty();
        }

        static string RegimeName(FlightRegime r)
        {
            switch (r)
            {
                case FlightRegime.Orbital: return "ORBITAL";
                case FlightRegime.UpperAtmosphere: return "ENTRY";
                case FlightRegime.CloudDescent: return "CLOUD DESCENT";
                case FlightRegime.FinalApproach: return "FINAL APPROACH";
                default: return "SURFACE";
            }
        }

        static readonly double[] Ranges = { 250, 500, 1000, 2000, 5000, 10000, 20000, 50000, 100000, 200000, 400000 };

        void UpdateNav(MissionSession m, Camera cam)
        {
            var s = m.State;
            var nav = m.Nav;
            var p = m.Predictor.Latest;
            Vec3d zone = m.ZonePosition;
            Planet.Horizontal(zone, out double zx, out double zz);
            Planet.Horizontal(s.Position, out double px, out double pz);
            // Heading-up map: rotate so the camera's horizontal forward points up the scope.
            Vector3 camF = cam.transform.forward;
            if (Mathf.Abs(camF.y) > 0.9f) camF = cam.transform.up;
            float heading = Mathf.Atan2(camF.x, camF.z);
            double distance = Math.Sqrt((px - zx) * (px - zx) + (pz - zz) * (pz - zz));
            bool navOk = nav.Available && p != null && p.Valid;
            double ie = 0, iz = 0;
            if (p != null && p.Valid) { ie = p.ErrorEast + nav.OffsetEast; iz = p.ErrorNorth + nav.OffsetNorth; }
            double miss = Math.Sqrt(ie * ie + iz * iz);
            double need = Math.Max(navOk ? miss : 0, Math.Min(distance, 400000)) * 1.15;
            double range = Ranges[Ranges.Length - 1];
            foreach (var r in Ranges) if (r >= need) { range = r; break; }
            float radius = scope.rect.width * 0.5f - 8f;
            float k = radius / (float)range;
            Vector2 Map(double e, double n)
            {
                float c = Mathf.Cos(heading), sn = Mathf.Sin(heading);
                // rotate world (east, north) by -heading
                float x = (float)(e * c - n * sn), y = (float)(e * sn + n * c);
                Vector2 v = new Vector2(x, y) * k;
                return v.magnitude > radius ? v.normalized * radius : v;
            }
            scopeRange.text = Units.Distance(range);
            scopeComplete.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * Mathf.Min(radius * 2, (float)m.Level.CompleteRadius * 2 * k));
            scopeGood.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * Mathf.Max(6, Mathf.Min(radius * 2, (float)m.Level.GoodRadius * 2 * k)));
            scopeZone.anchoredPosition = Vector2.zero;
            scopeProbe.anchoredPosition = Map(px - zx, pz - zz);
            scopeNorth.anchoredPosition = Map(0, range * 0.92);
            // Wind arrow from the centre (surface wind at the probe's altitude), 10 m/s = 30% radius.
            Vector3 w = WorldSpace.Dir(m.Telemetry.Wind);
            scopeWind.Points.Clear();
            Vector2 wv = new Vector2(w.x * Mathf.Cos(heading) - w.z * Mathf.Sin(heading), w.x * Mathf.Sin(heading) + w.z * Mathf.Cos(heading)) * radius * 0.03f;
            wv = Vector2.ClampMagnitude(wv, radius * 0.8f);
            Vector2 c0 = Vector2.zero;
            scopeWind.Points.Add(c0); scopeWind.Points.Add(c0 + wv);
            if (wv.magnitude > 8f)
            {
                Vector2 back = -wv.normalized * 12f, side = new Vector2(-wv.y, wv.x).normalized * 7f;
                scopeWind.Points.Add(c0 + wv + back + side); scopeWind.Points.Add(c0 + wv); scopeWind.Points.Add(c0 + wv + back - side);
            }
            scopeWind.SetDirty();

            navLost.text = navOk ? "" : "NO NAV\n" + (string.IsNullOrEmpty(m.Hazards.Effects.NavLossReason) ? "" : m.Hazards.Effects.NavLossReason);
            scopeImpact.gameObject.SetActive(navOk);
            scopeUncertainty.gameObject.SetActive(navOk && nav.Uncertainty > 1);
            scopePath.gameObject.SetActive(navOk);
            if (navOk)
            {
                scopeImpact.anchoredPosition = Map(ie, iz);
                scopeUncertainty.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Map(ie, iz), Vector2.one * Mathf.Min(radius * 2, (float)nav.Uncertainty * 2 * k));
                scopePath.Points.Clear();
                var path = p.Path;
                int stride = Math.Max(1, path.Count / 40);
                for (int j = 0; j < path.Count; j += stride)
                {
                    Planet.Horizontal(path[j], out double qx, out double qz);
                    scopePath.Points.Add(Map(qx - zx + nav.OffsetEast, qz - zz + nav.OffsetNorth));
                }
                scopePath.SetDirty();
            }

            double bearing = (Math.Atan2(zx - px, zz - pz) * 180 / Math.PI + 360) % 360;
            navText1.text = $"ZONE {Units.Distance(distance)}  {bearing:000}°";
            if (navOk)
            {
                double tti = Math.Max(0, p.ImpactTime - s.Time);
                navText2.text = $"MISS {Units.Distance(miss)}";
                navText2.color = miss < m.Level.GoodRadius ? Palette.Good : miss < m.Level.CompleteRadius ? Palette.Caution : Palette.Warning;
                navText3.text = $"IMPACT T-{(int)(tti / 60)}:{(int)(tti % 60):00}   {Units.Speed(p.ImpactSpeed)}";
            }
            else
            {
                navText2.text = "MISS ---";
                navText2.color = Palette.Warning;
                navText3.text = "Prediction unavailable";
            }

            // ---- world markers ----
            PlaceMarker(cam, WorldSpace.ToUnity(zone), zoneMarker, true);
            zoneMarkerText.text = Units.Distance(distance);
            impactMarker.gameObject.SetActive(navOk);
            if (navOk)
            {
                Vec3d ip = p.ImpactPoint + new Vec3d(nav.OffsetEast, 0, nav.OffsetNorth);
                PlaceMarker(cam, WorldSpace.ToUnity(ip), impactMarker, false);
            }
        }

        void PlaceMarker(Camera cam, Vector3 worldPos, RectTransform marker, bool showArrow)
        {
            var canvasRect = (RectTransform)marker.parent;
            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            bool behind = sp.z < 0;
            bool onScreen = !behind && sp.x >= 0 && sp.x <= Screen.width && sp.y >= 0 && sp.y <= Screen.height;
            marker.gameObject.SetActive(onScreen);
            if (onScreen)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, null, out var lp);
                marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
                marker.anchoredPosition = lp;
            }
            if (!showArrow) return;
            zoneArrow.gameObject.SetActive(!onScreen);
            if (!onScreen)
            {
                // Edge arrow pointing toward the off-screen zone.
                Vector2 c = new Vector2(Screen.width, Screen.height) * 0.5f;
                Vector2 d = new Vector2(sp.x, sp.y) - c;
                if (behind) d = -d;
                if (d.sqrMagnitude < 1) d = Vector2.down;
                d.Normalize();
                float sx = (Screen.width * 0.5f - 60) / Mathf.Max(1e-3f, Mathf.Abs(d.x));
                float sy = (Screen.height * 0.5f - 60) / Mathf.Max(1e-3f, Mathf.Abs(d.y));
                Vector2 edge = c + d * Mathf.Min(sx, sy);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, edge, null, out var lp);
                zoneArrow.anchorMin = zoneArrow.anchorMax = new Vector2(0.5f, 0.5f);
                zoneArrow.anchoredPosition = lp;
            }
        }

        /// <summary>Contextual tutorial hint (first levels, can be turned off in settings).</summary>
        public void SetHint(string text) { hint.text = settings.ShowTutorialHints ? text : ""; }
    }
}
