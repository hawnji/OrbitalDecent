using System;
using System.Collections.Generic;
using OrbitalDescent.Core;
using UnityEngine;
using UnityEngine.UI;

namespace OrbitalDescent
{
    public enum Screen2 { None, Loading, Title, Levels, Vehicles, Briefing, Aiming, Pause, Results, Settings }

    /// <summary>All non-flight screens, built from code: title, level and vehicle selection, briefing,
    /// release aiming, pause, results and settings.</summary>
    public sealed class Menus : MonoBehaviour
    {
        public interface IHost
        {
            Progression Progress { get; }
            GameSettings Settings { get; }
            int SelectedLevel { get; set; }
            string SelectedVehicle { get; set; }
            void StartBriefing();
            void StartMission();
            void Release();
            void SetAim(double heading, double fpa);
            void Resume();
            void Retry();
            void NextLevel();
            void QuitToTitle();
            void SettingsChanged();
            void Click();
        }

        IHost host;
        RectTransform root;
        readonly Dictionary<Screen2, RectTransform> screens = new Dictionary<Screen2, RectTransform>();
        public Screen2 Current { get; private set; }
        Screen2 settingsReturn;

        Text loadingText;
        Text titleStars;
        RectTransform levelGrid, vehicleGrid, resultsBody, briefingBody;
        Text aimReadout, aimHeadingText, aimFpaText;
        Slider aimHeading, aimFpa;

        public static Menus Create(Canvas canvas, IHost host)
        {
            var m = canvas.gameObject.AddComponent<Menus>();
            m.host = host;
            m.Build(canvas.transform);
            return m;
        }

        RectTransform Screen(Screen2 id, bool dim = true)
        {
            var rt = Ui.Rect(id.ToString(), root).Stretch();
            if (dim)
            {
                var bg = rt.gameObject.AddComponent<Image>();
                bg.sprite = Ui.White;
                bg.color = new Color(0.01f, 0.02f, 0.04f, 0.55f);
            }
            rt.gameObject.SetActive(false);
            screens[id] = rt;
            return rt;
        }

        Button Btn(Transform parent, string label, Action a, Vector2 anchor, Vector2 pos, Vector2 size, int font = 36)
        {
            var b = Ui.Button(parent, label, () => { host.Click(); a(); }, font);
            ((RectTransform)b.transform).Place(anchor, anchor, pos, size);
            return b;
        }

        static Text Title(Transform parent, string text, int size, float y)
        {
            var t = Ui.Label(parent, text, size, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(1600, size + 20));
            return t;
        }

        void Build(Transform canvas)
        {
            root = Ui.Rect("Menus", canvas).Stretch();

            // Loading
            var load = Screen(Screen2.Loading);
            load.GetComponent<Image>().color = Color.black;
            Title(load, "ORBITAL DESCENT", 90, -380);
            loadingText = Ui.Label(load, "Preparing atmosphere...", 32, Palette.TextDim, TextAnchor.MiddleCenter);
            loadingText.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(1000, 50));

            // Title
            var title = Screen(Screen2.Title, false);
            Title(title, "ORBITAL DESCENT", 110, -150);
            var sub = Title(title, "Guide a reentry probe from orbit to a precise, soft landing", 34, -290);
            sub.color = Palette.TextDim;
            sub.fontStyle = FontStyle.Normal;
            Btn(title, "PLAY", () => Show(Screen2.Levels), new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(460, 120), 48);
            Btn(title, "SETTINGS", () => OpenSettings(Screen2.Title), new Vector2(0.5f, 0.5f), new Vector2(0, -210), new Vector2(460, 100));
            titleStars = Ui.Label(title, "", 30, Palette.Caution, TextAnchor.MiddleCenter);
            titleStars.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 60), new Vector2(800, 40));

            // Levels
            var lv = Screen(Screen2.Levels);
            Title(lv, "SELECT MISSION", 64, -60);
            levelGrid = Ui.Rect("Grid", lv);
            levelGrid.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(1500, 700));
            Btn(lv, "BACK", () => Show(Screen2.Title), new Vector2(0, 0), new Vector2(160, 70), new Vector2(240, 90));

            // Vehicles
            var vh = Screen(Screen2.Vehicles);
            Title(vh, "SELECT PROBE", 64, -60);
            vehicleGrid = Ui.Rect("Grid", vh);
            vehicleGrid.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(1700, 720));
            Btn(vh, "BACK", () => Show(Screen2.Levels), new Vector2(0, 0), new Vector2(160, 70), new Vector2(240, 90));

            // Briefing
            var br = Screen(Screen2.Briefing);
            briefingBody = Ui.Rect("Body", br);
            briefingBody.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(1400, 760));
            Btn(br, "BACK", () => Show(Screen2.Vehicles), new Vector2(0, 0), new Vector2(160, 70), new Vector2(240, 90));
            Btn(br, "START", () => host.StartMission(), new Vector2(1, 0), new Vector2(-200, 70), new Vector2(340, 110), 44);

            // Aiming (no dimming: the player is looking at the orbital view)
            var aim = Screen(Screen2.Aiming, false);
            var panel = Ui.Image(aim, "Panel", Palette.PanelSolid, Ui.Rounded).rectTransform;
            panel.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(1500, 300));
            var hdr = Ui.Label(panel, "RELEASE PLANNING  -  adjust the release, then let go", 30, Palette.TextDim, TextAnchor.UpperLeft, FontStyle.Bold);
            hdr.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -16), new Vector2(1200, 40));
            aimHeadingText = Ui.Label(panel, "", 28, Palette.Text);
            aimHeadingText.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -80), new Vector2(380, 40));
            aimHeading = Ui.Slider(panel, -1, 1, 0, _ => AimChanged());
            ((RectTransform)aimHeading.transform).Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(420, -76), new Vector2(560, 50));
            aimFpaText = Ui.Label(panel, "", 28, Palette.Text);
            aimFpaText.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -150), new Vector2(380, 40));
            aimFpa = Ui.Slider(panel, -1, 1, 0, _ => AimChanged());
            ((RectTransform)aimFpa.transform).Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(420, -146), new Vector2(560, 50));
            aimReadout = Ui.Label(panel, "", 28, Palette.Impact, TextAnchor.UpperLeft, FontStyle.Bold);
            aimReadout.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -214), new Vector2(950, 70));
            Btn(panel, "RELEASE", () => host.Release(), new Vector2(1, 0.5f), new Vector2(-30, 10), new Vector2(380, 160), 48);

            // Pause
            var pause = Screen(Screen2.Pause);
            Title(pause, "PAUSED", 80, -200);
            Btn(pause, "RESUME", () => host.Resume(), new Vector2(0.5f, 0.5f), new Vector2(0, 80), new Vector2(460, 100));
            Btn(pause, "RESTART", () => host.Retry(), new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(460, 100));
            Btn(pause, "SETTINGS", () => OpenSettings(Screen2.Pause), new Vector2(0.5f, 0.5f), new Vector2(0, -160), new Vector2(460, 100));
            Btn(pause, "QUIT", () => host.QuitToTitle(), new Vector2(0.5f, 0.5f), new Vector2(0, -280), new Vector2(460, 100));

            // Results
            var res = Screen(Screen2.Results);
            resultsBody = Ui.Rect("Body", res);
            resultsBody.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(1400, 760));
            Btn(res, "MENU", () => host.QuitToTitle(), new Vector2(0.5f, 0), new Vector2(-420, 60), new Vector2(300, 110));
            Btn(res, "RETRY", () => host.Retry(), new Vector2(0.5f, 0), new Vector2(0, 60), new Vector2(360, 120), 48);
            Btn(res, "NEXT", () => host.NextLevel(), new Vector2(0.5f, 0), new Vector2(420, 60), new Vector2(300, 110));

            BuildSettings();
        }

        // ---------------------------------------------------------------- screens

        public void Show(Screen2 s)
        {
            foreach (var kv in screens) kv.Value.gameObject.SetActive(kv.Key == s);
            Current = s;
            switch (s)
            {
                case Screen2.Title: titleStars.text = $"Stars earned: {host.Progress.TotalStars} / {Levels.All.Count * 3}"; break;
                case Screen2.Levels: FillLevels(); break;
                case Screen2.Vehicles: FillVehicles(); break;
            }
        }

        public void SetLoading(string text) { loadingText.text = text; }

        static void Clear(RectTransform r) { for (int i = r.childCount - 1; i >= 0; i--) Destroy(r.GetChild(i).gameObject); }

        static void Stars(Transform parent, int n, Vector2 anchor, Vector2 pos, float size)
        {
            for (int i = 0; i < 3; i++)
            {
                var st = Ui.Image(parent, "Star", i < n ? Palette.Caution : new Color(1, 1, 1, 0.15f), Ui.Disc).rectTransform;
                st.Place(anchor, new Vector2(0.5f, 0.5f), pos + new Vector2((i - 1) * size * 1.3f, 0), new Vector2(size, size));
            }
        }

        void FillLevels()
        {
            Clear(levelGrid);
            var p = host.Progress;
            for (int i = 0; i < Levels.All.Count; i++)
            {
                var L = Levels.All[i];
                int col = i % 3, row = i / 3;
                bool unlocked = p.IsLevelUnlocked(i);
                int idx = i;
                var card = Ui.Button(levelGrid, "", () =>
                {
                    if (!unlocked) return;
                    host.Click();
                    host.SelectedLevel = idx;
                    Show(Screen2.Vehicles);
                });
                ((RectTransform)card.transform).Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(col * 505, -row * 360), new Vector2(480, 330));
                card.interactable = unlocked;
                var name = Ui.Label(card.transform, $"{i + 1}. {L.Name}", 38, Palette.Text, TextAnchor.UpperLeft, FontStyle.Bold);
                name.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -20), new Vector2(440, 50));
                string detail = unlocked ? Short(L.Briefing, 120) : "Locked: land on the previous mission to unlock.";
                var d = Ui.Label(card.transform, detail, 24, Palette.TextDim, TextAnchor.UpperLeft);
                d.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -80), new Vector2(430, 150));
                var lp = p.Levels[i];
                Stars(card.transform, lp.BestStars, new Vector2(0, 0), new Vector2(90, 40), 36);
                if (lp.Attempts > 0)
                {
                    var best = Ui.Label(card.transform, double.IsInfinity(lp.BestError) ? $"{lp.Attempts} tries" : $"best {Units.Distance(lp.BestError)}", 24, Palette.TextDim, TextAnchor.MiddleRight);
                    best.rectTransform.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-20, 22), new Vector2(260, 36));
                }
            }
        }

        static string Short(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 3).TrimEnd() + "...";

        void FillVehicles()
        {
            Clear(vehicleGrid);
            var p = host.Progress;
            var L = Levels.All[host.SelectedLevel];
            for (int i = 0; i < VehicleConfig.All.Count; i++)
            {
                var v = VehicleConfig.All[i];
                bool unlocked = p.IsVehicleUnlocked(v.Id);
                var card = Ui.Button(vehicleGrid, "", () =>
                {
                    if (!unlocked) return;
                    host.Click();
                    host.SelectedVehicle = v.Id;
                    host.StartBriefing();
                });
                ((RectTransform)card.transform).Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(i * 430, 0), new Vector2(410, 720));
                card.interactable = unlocked;
                var name = Ui.Label(card.transform, v.Name.ToUpperInvariant(), 44, Palette.Text, TextAnchor.UpperCenter, FontStyle.Bold);
                name.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(380, 56));
                if (L.RecommendedVehicle == v.Id)
                {
                    var rec = Ui.Label(card.transform, "RECOMMENDED", 22, Palette.Good, TextAnchor.UpperCenter, FontStyle.Bold);
                    rec.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -84), new Vector2(380, 30));
                }
                var tag = Ui.Label(card.transform, unlocked ? v.Tagline : $"Locked: earn {Progression.VehicleStarRequirement[v.Id]} stars", 26, Palette.TextDim, TextAnchor.UpperCenter);
                tag.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -124), new Vector2(360, 100));
                float y = 250;
                StatBar(card.transform, "Mass", (float)(v.TotalMass / 1300), $"{v.TotalMass:F0} kg", ref y);
                StatBar(card.transform, "Agility", (float)(v.RcsTorque / v.Inertia / 3.0), "", ref y);
                StatBar(card.transform, "Heat limit", (float)((v.MaxSkinTemp - 1200) / 1600), $"{v.MaxSkinTemp:F0} K", ref y);
                StatBar(card.transform, "Thrust/weight", (float)(v.EngineThrust / (v.TotalMass * Constants.G0) / 3.0), $"{v.EngineThrust / (v.TotalMass * Constants.G0):F1}", ref y);
                StatBar(card.transform, "Touchdown", (float)(v.MaxTouchdownSpeed / 12), $"{v.MaxTouchdownSpeed:F0} m/s", ref y);
                StatBar(card.transform, "Glide", (float)Math.Min(1, Math.Abs(v.NormalSlope) / 1.5), "", ref y);
            }
        }

        static void StatBar(Transform parent, string label, float frac, string value, ref float y)
        {
            var l = Ui.Label(parent, label, 24, Palette.TextDim, TextAnchor.MiddleLeft);
            l.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -y), new Vector2(220, 30));
            var v = Ui.Label(parent, value, 24, Palette.Text, TextAnchor.MiddleRight);
            v.rectTransform.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -y), new Vector2(160, 30));
            var track = Ui.Image(parent, "Track", new Color(1, 1, 1, 0.1f)).rectTransform;
            track.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -y - 34), new Vector2(360, 12));
            var fill = Ui.Image(track, "Fill", Palette.Hud).rectTransform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(Mathf.Clamp01(frac), 1);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            y += 70;
        }

        public void ShowBriefing(LevelDefinition L, VehicleConfig v)
        {
            Clear(briefingBody);
            var bg = Ui.Image(briefingBody, "Bg", Palette.PanelSolid, Ui.Rounded).rectTransform;
            bg.Stretch();
            var t = Ui.Label(bg, $"MISSION {L.Index + 1}: {L.Name.ToUpperInvariant()}", 52, Palette.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            t.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -30), new Vector2(1300, 64));
            var b = Ui.Label(bg, L.Briefing, 32, Palette.TextDim, TextAnchor.UpperLeft);
            b.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -120), new Vector2(1320, 200));
            string weather = L.Clouds.Length == 0 ? "Clear" : string.Join(", ", Array.ConvertAll(L.Clouds, c => $"{c.Type} {Units.Altitude(c.Base)}-{Units.Altitude(c.Top)}"));
            string info =
                $"Probe: {v.Name}  ({v.Tagline})\n" +
                $"Release: {Units.Altitude(L.ReleaseAltitude)} at {Units.Speed(L.ReleaseSpeed)}, {-L.ReleaseFlightPathDeg:F0}° descent\n" +
                $"Surface wind: {Units.Speed(L.SurfaceWindSpeed)}   Jet stream: {Units.Speed(L.JetSpeed)} at {Units.Altitude(L.JetAltitude)}\n" +
                $"Clouds: {weather}\n" +
                (L.Ocean ? (L.HasPlatform ? "Target: drifting recovery ship (ocean)\n" : "Target: ocean\n") : $"Zone elevation: {Units.Altitude(L.ZoneElevation)}\n");
            var i = Ui.Label(bg, info, 28, Palette.Text, TextAnchor.UpperLeft);
            i.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -340), new Vector2(1320, 220));
            string stars =
                $"★1  Land intact within {Units.Distance(L.CompleteRadius)}\n" +
                $"★2  Within {Units.Distance(L.GoodRadius)} at under {Units.Speed(L.SoftSpeed)}\n" +
                $"★3  Within {Units.Distance(L.PreciseRadius)} and {L.Objective.Text}";
            var s = Ui.Label(bg, stars.Replace("★", "*"), 30, Palette.Caution, TextAnchor.UpperLeft, FontStyle.Bold);
            s.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -580), new Vector2(1320, 150));
            Show(Screen2.Briefing);
        }

        // ---------------------------------------------------------------- aiming

        LevelDefinition aimLevel;
        bool aimUpdating;

        public void ShowAiming(LevelDefinition L)
        {
            aimLevel = L;
            aimUpdating = true;
            aimHeading.value = 0; aimFpa.value = 0;
            aimUpdating = false;
            Show(Screen2.Aiming);
            AimChanged();
        }

        void AimChanged()
        {
            if (aimUpdating || aimLevel == null) return;
            double h = aimHeading.value * aimLevel.AimHeadingRangeDeg;
            double f = aimFpa.value * aimLevel.AimFlightPathRangeDeg;
            aimHeadingText.text = $"Heading  {h:+0.0;-0.0;0.0}°";
            aimFpaText.text = $"Entry angle  {f:+0.0;-0.0;0.0}°";
            host.SetAim(h, f);
        }

        public void SetAimReadout(PredictionResult p)
        {
            if (p == null || !p.Valid) { aimReadout.text = "Predicted touchdown: off the map"; return; }
            aimReadout.text = $"Predicted touchdown if you do nothing: {Units.Distance(p.ErrorToZone)} from the zone\n" +
                              $"({(p.ErrorEast >= 0 ? "east" : "west")} {Units.Distance(Math.Abs(p.ErrorEast))}, {(p.ErrorNorth >= 0 ? "north" : "south")} {Units.Distance(Math.Abs(p.ErrorNorth))})";
        }

        // ---------------------------------------------------------------- results

        public void ShowResults(LevelDefinition L, ScoreResult r, bool newBest, bool hasNext)
        {
            Clear(resultsBody);
            var bg = Ui.Image(resultsBody, "Bg", Palette.PanelSolid, Ui.Rounded).rectTransform;
            bg.Stretch();
            string head;
            switch (r.Outcome)
            {
                case MissionOutcome.Landed: head = "TOUCHDOWN"; break;
                case MissionOutcome.DeckLanding: head = "DECK LANDING"; break;
                case MissionOutcome.Splashdown: head = "SPLASHDOWN"; break;
                case MissionOutcome.BurnedUp: head = "LOST IN ENTRY"; break;
                case MissionOutcome.StructuralFailure: head = "STRUCTURAL FAILURE"; break;
                default: head = "IMPACT"; break;
            }
            var t = Ui.Label(bg, head, 72, r.Success ? Palette.Good : Palette.Warning, TextAnchor.UpperCenter, FontStyle.Bold);
            t.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(1300, 84));
            Stars(bg, r.Stars, new Vector2(0.5f, 1), new Vector2(0, -160), 64);
            string summary = r.Success
                ? $"{Units.Distance(r.ErrorMeters)} from the zone at {Units.Speed(r.TouchdownSpeed)}" + (newBest ? "   NEW BEST" : "")
                : (string.IsNullOrEmpty(r.FailureReason) ? "The probe did not survive." : r.FailureReason);
            var s = Ui.Label(bg, summary, 34, Palette.Text, TextAnchor.UpperCenter);
            s.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -220), new Vector2(1300, 50));
            string pts =
                $"Accuracy {r.AccuracyPoints}\nSoftness {r.SoftnessPoints}\nStability {r.StabilityPoints}\n" +
                $"Efficiency {r.EfficiencyPoints}\nHazards {r.HazardPoints}\nObjective {r.ObjectivePoints}\n\nTOTAL {r.Total}";
            var p = Ui.Label(bg, pts, 30, Palette.TextDim, TextAnchor.UpperLeft);
            p.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(80, -300), new Vector2(500, 420));
            string reasons =
                $"{Tick(r.Stars >= 1)} {r.StarReason1}\n\n{Tick(r.Stars >= 2)} {r.StarReason2}\n\n{Tick(r.Stars >= 3)} {r.StarReason3}";
            var rr = Ui.Label(bg, reasons, 28, Palette.Text, TextAnchor.UpperLeft);
            rr.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(560, -300), new Vector2(780, 420));
            var next = screens[Screen2.Results].Find("Button:NEXT");
            if (next != null) next.GetComponent<Button>().interactable = hasNext;
            Show(Screen2.Results);
        }

        static string Tick(bool ok) => ok ? "[x]" : "[ ]";

        // ---------------------------------------------------------------- settings

        void OpenSettings(Screen2 from)
        {
            settingsReturn = from;
            Show(Screen2.Settings);
        }

        void BuildSettings()
        {
            var st = Screen(Screen2.Settings);
            st.GetComponent<Image>().color = new Color(0.01f, 0.02f, 0.04f, 0.92f);
            Title(st, "SETTINGS", 60, -40);
            var s = host.Settings;
            var left = Ui.Rect("Left", st);
            left.Place(new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(-40, -130), new Vector2(820, 860));
            var right = Ui.Rect("Right", st);
            right.Place(new Vector2(0.5f, 1), new Vector2(0, 1), new Vector2(40, -130), new Vector2(820, 860));
            float y = 0;
            SliderRow(left, "HUD scale", 0.8f, 1.3f, s.HudScale, v => s.HudScale = v, ref y);
            SliderRow(left, "Screen margin", 0f, 0.08f, s.SafeMargin, v => s.SafeMargin = v, ref y);
            ToggleRow(left, "Imperial units", s.Imperial, v => s.Imperial = v, ref y);
            ToggleRow(left, "Reduced camera shake", s.ReducedShake, v => s.ReducedShake = v, ref y);
            ToggleRow(left, "Reduced motion and flashing", s.ReducedMotion, v => s.ReducedMotion = v, ref y);
            ToggleRow(left, "Tutorial hints", s.ShowTutorialHints, v => s.ShowTutorialHints = v, ref y);
            ToggleRow(left, "Tilt steering", s.TiltSteering, v => s.TiltSteering = v, ref y);
            ToggleRow(left, "Invert vertical steering", s.InvertVertical, v => s.InvertVertical = v, ref y);
            y = 0;
            ChoiceRow(right, "Graphics", new[] { "Low", "Medium", "High" }, (int)s.Quality, i => s.Quality = (QualityTier)i, ref y);
            ChoiceRow(right, "Hazards", new[] { "Gentle", "Normal", "Hard" }, s.Difficulty < 0.9f ? 0 : s.Difficulty > 1.1f ? 2 : 1,
                i => s.Difficulty = i == 0 ? 0.75f : i == 2 ? 1.25f : 1f, ref y);
            SliderRow(right, "Master volume", 0, 1, s.MasterVolume, v => s.MasterVolume = v, ref y);
            SliderRow(right, "Effects", 0, 1, s.EffectsVolume, v => s.EffectsVolume = v, ref y);
            SliderRow(right, "Alerts", 0, 1, s.AlertVolume, v => s.AlertVolume = v, ref y);
            SliderRow(right, "Ambience", 0, 1, s.AmbienceVolume, v => s.AmbienceVolume = v, ref y);
            Btn(st, "DONE", () => { host.Settings.Save(); host.SettingsChanged(); Show(settingsReturn); }, new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(360, 100));
        }

        void SliderRow(Transform parent, string label, float min, float max, float value, Action<float> set, ref float y)
        {
            var row = Ui.Row(parent, ref y, 84);
            var l = Ui.Label(row, label, 30, Palette.Text);
            l.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(330, 60));
            var sl = Ui.Slider(row, min, max, value, v => { set(v); host.SettingsChanged(); });
            ((RectTransform)sl.transform).Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(340, 0), new Vector2(460, 60));
        }

        void ToggleRow(Transform parent, string label, bool value, Action<bool> set, ref float y)
        {
            var row = Ui.Row(parent, ref y, 84);
            var tg = Ui.Toggle(row, label, value, v => { set(v); host.SettingsChanged(); });
            ((RectTransform)tg.transform).Stretch();
        }

        void ChoiceRow(Transform parent, string label, string[] options, int value, Action<int> set, ref float y)
        {
            var row = Ui.Row(parent, ref y, 84);
            var l = Ui.Label(row, label, 30, Palette.Text);
            l.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(330, 60));
            var buttons = new List<Image>();
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                var b = Ui.Button(row, options[i], null, 28);
                ((RectTransform)b.transform).Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(340 + i * 158, 0), new Vector2(150, 70));
                var img = (Image)b.targetGraphic;
                buttons.Add(img);
                b.onClick.AddListener(() =>
                {
                    host.Click();
                    set(idx);
                    for (int k = 0; k < buttons.Count; k++) buttons[k].color = k == idx ? Palette.ButtonActive : Palette.Button;
                    host.SettingsChanged();
                });
                img.color = i == value ? Palette.ButtonActive : Palette.Button;
            }
        }
    }
}
