using System;
using System.Collections;
using OrbitalDescent.Core;
using UnityEngine;

namespace OrbitalDescent
{
    /// <summary>
    /// Owns the game flow (loading, menus, release aiming, flight, pause, results) and wires the
    /// engine-independent simulation to the Unity views. The whole scene is built from code, so the
    /// project runs from an empty scene; <see cref="Bootstrap"/> creates this object at start-up.
    /// </summary>
    public sealed class GameController : MonoBehaviour, Menus.IHost
    {
        enum State { Loading, Menu, Aiming, Flight, Paused, Landed, Results }

        static readonly float[] WarpSteps = { 1f, 2f, 4f };
        const double WarpFloor = 25000;   // m: time warp is only available high above the surface

        GameSettings settings;
        Progression progress;
        NoiseAssets noise;
        QualityProfile quality;

        Shader litShader, unlitShader, terrainShader, waterShader, fxShader, atmoShader;
        CameraRig rig;
        AtmosphereRenderer atmosphere;
        Light sunLight;
        AudioDirector audioDirector;
        Hud hud;
        TouchControls controls;
        Menus menus;

        MissionSession session;
        ProbeView probe;
        TerrainView terrain;
        FxController fx;
        int terrainLevel = -1;
        QualityTier terrainQuality;

        State state = State.Loading;
        Autopilot demoPilot;
        bool demo;
        int warpIndex;
        float landedTimer;
        float flash;
        ControlInput lastInput;
        bool resultsShown;

        // ---------------------------------------------------------------- Menus.IHost
        public Progression Progress => progress;
        public GameSettings Settings => settings;
        public int SelectedLevel { get; set; }
        public string SelectedVehicle { get; set; } = "dart";

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.orientation = ScreenOrientation.AutoRotation;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Input.multiTouchEnabled = true;

            settings = GameSettings.Load();
            Units.Imperial = settings.Imperial;
            progress = new Progression(new PlayerPrefsStore(), Levels.All.Count);
            quality = QualityProfile.For(settings.Quality);

            litShader = LoadShader("ODLit");
            unlitShader = LoadShader("ODUnlit");
            terrainShader = LoadShader("ODTerrain");
            waterShader = LoadShader("ODWater");
            fxShader = LoadShader("ODFx");
            atmoShader = LoadShader("ODAtmosphere");

            Ui.Init();
            Ui.EnsureEventSystem();
            rig = CameraRig.Create(quality);
            rig.transform.SetParent(transform, false);
            atmosphere = rig.gameObject.AddComponent<AtmosphereRenderer>();

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            sunLight = sunGo.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.transform.rotation = Quaternion.LookRotation(-Sun.Direction);
            sunLight.color = Sun.Color;
            sunLight.intensity = 1f;
            sunLight.shadowBias = 0.05f;
            sunLight.shadowNormalBias = 0.4f;

            audioDirector = AudioDirector.Create(settings);
            audioDirector.transform.SetParent(transform, false);

            var hudCanvas = Ui.Canvas("HudCanvas", 0);
            hudCanvas.transform.SetParent(transform, false);
            controls = TouchControls.Create(hudCanvas, settings);
            hud = Hud.Create(hudCanvas, settings);
            var menuCanvas = Ui.Canvas("MenuCanvas", 10);
            menuCanvas.transform.SetParent(transform, false);
            menus = Menus.Create(menuCanvas, this);

            controls.ChutePressed += OnChute;
            controls.RebootPressed += () => { if (session != null) session.RebootRequested = true; };
            controls.CameraPressed += () => { rig.Cycle(); Click(); };
            controls.WarpPressed += CycleWarp;
            controls.PausePressed += () => { if (state == State.Flight || state == State.Aiming) Pause(); else if (state == State.Paused) Resume(); };

            hud.SetVisible(false);
            controls.SetVisible(false);
            menus.Show(Screen2.Loading);
            ApplyQuality();
        }

        static Shader LoadShader(string name)
        {
            var s = Resources.Load<Shader>("Shaders/" + name);
            if (s == null) Debug.LogError($"Orbital Descent: shader Resources/Shaders/{name} missing");
            return s;
        }

        IEnumerator Start()
        {
            menus.SetLoading("Generating cloud noise...");
            var task = NoiseAssets.GenerateAsync();
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
            {
                menus.SetLoading("Failed to generate noise: " + task.Exception?.GetBaseException().Message);
                yield break;
            }
            noise = task.Result;
            noise.CreateTextures();
            atmosphere.Init(atmoShader, noise, quality);
            menus.SetLoading("Ready");
            yield return null;
            GoToTitle();
        }

        // ---------------------------------------------------------------- flow

        void GoToTitle()
        {
            state = State.Menu;
            hud.SetVisible(false);
            controls.SetVisible(false);
            menus.Show(Screen2.Title);
            StartDemo();
        }

        /// <summary>Background attract mode: the autopilot flies the first mission behind the title menu.</summary>
        void StartDemo()
        {
            demo = true;
            demoPilot = new Autopilot();
            BuildMission(Levels.All[0], VehicleConfig.Dart);
            session.Release();
            rig.Mode = CameraMode.Cinematic;
            warpIndex = 0;
        }

        public void StartBriefing()
        {
            menus.ShowBriefing(Levels.All[SelectedLevel], VehicleConfig.ById(SelectedVehicle));
        }

        public void StartMission()
        {
            demo = false;
            BuildMission(Levels.All[SelectedLevel], VehicleConfig.ById(SelectedVehicle));
            state = State.Aiming;
            rig.Mode = CameraMode.Chase;
            hud.SetVisible(true);
            controls.SetVisible(false);
            menus.ShowAiming(session.Level);
            menus.SetAimReadout(session.Predictor.Latest);
        }

        void BuildMission(LevelDefinition level, VehicleConfig vehicle)
        {
            if (probe != null) Destroy(probe.gameObject);
            if (fx != null) Destroy(fx.gameObject);
            session = new MissionSession(level, vehicle, noise.Shape, noise.Coverage, settings.Difficulty);
            session.LightningStrike += OnStrike;
            // Terrain is rebuilt only when the level or detail level changes, so retries are instant.
            if (terrain == null || terrainLevel != level.Index || terrainQuality != settings.Quality)
            {
                if (terrain != null) Destroy(terrain.gameObject);
                terrain = TerrainView.Create(session.World, quality, terrainShader, litShader, waterShader, fxShader);
                terrainLevel = level.Index;
                terrainQuality = settings.Quality;
            }
            probe = ProbeView.Create(vehicle, litShader, unlitShader);
            fx = FxController.Create(probe, fxShader);
            atmosphere.SetLevel(level);
            hud.SetupLevel(level, vehicle);
            controls.Configure(vehicle);
            warpIndex = 0;
            controls.SetWarpLabel(1f);
            landedTimer = 0;
            resultsShown = false;
            flash = 0;
            lastInput = new ControlInput { Sas = true };
            rig.Reset(-WorldSpace.Dir(session.State.Velocity).normalized, Vector3.up);
            hud.SetHint("");
        }

        public void SetAim(double heading, double fpa)
        {
            if (session == null || session.Phase != MissionPhase.Aiming) return;
            var p = session.SetAim(heading, fpa);
            menus.SetAimReadout(p);
        }

        public void Release()
        {
            if (session == null) return;
            session.Release();
            audioDirector.PlayRelease();
            state = State.Flight;
            menus.Show(Screen2.None);
            controls.SetVisible(true);
            rig.Reset(-WorldSpace.Dir(session.State.Velocity).normalized, Vector3.up);
        }

        void Pause()
        {
            if (state == State.Aiming) { menus.Show(Screen2.Pause); state = State.Paused; pausedFromAiming = true; return; }
            pausedFromAiming = false;
            state = State.Paused;
            controls.SetVisible(false);
            menus.Show(Screen2.Pause);
        }
        bool pausedFromAiming;

        public void Resume()
        {
            if (state != State.Paused) return;
            if (pausedFromAiming) { state = State.Aiming; menus.ShowAiming(session.Level); return; }
            state = State.Flight;
            menus.Show(Screen2.None);
            controls.SetVisible(true);
        }

        public void Retry()
        {
            StartMission();
        }

        public void NextLevel()
        {
            int next = SelectedLevel + 1;
            if (next >= Levels.All.Count || !progress.IsLevelUnlocked(next)) return;
            SelectedLevel = next;
            if (!progress.IsVehicleUnlocked(SelectedVehicle)) SelectedVehicle = "dart";
            StartBriefing();
            state = State.Menu;
            hud.SetVisible(false);
            controls.SetVisible(false);
            StartDemoKeepMenu();
        }

        void StartDemoKeepMenu()
        {
            // Keep the briefing on screen with the attract flight running behind it.
            var current = menus.Current;
            StartDemo();
            menus.Show(current);
        }

        public void QuitToTitle() => GoToTitle();

        public void SettingsChanged()
        {
            Units.Imperial = settings.Imperial;
            rig.ShakeScale = settings.ReducedShake ? 0.25f : 1f;
            rig.ReducedMotion = settings.ReducedMotion;
            ApplyQuality();
        }

        public void Click() => audioDirector.PlayClick();

        void ApplyQuality()
        {
            quality = QualityProfile.For(settings.Quality);
            Application.targetFrameRate = quality.TargetFrameRate;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = quality.Antialiasing;
            QualitySettings.shadows = settings.Quality == QualityTier.Low ? ShadowQuality.Disable : ShadowQuality.HardOnly;
            QualitySettings.shadowDistance = 160f;
            QualitySettings.shadowCascades = 1;
            QualitySettings.shadowResolution = settings.Quality == QualityTier.High ? ShadowResolution.High : ShadowResolution.Medium;
            sunLight.shadows = settings.Quality == QualityTier.Low ? LightShadows.None : LightShadows.Hard;
            rig.Cam.allowMSAA = quality.Antialiasing > 1;
            if (Application.isMobilePlatform)
            {
                int w = Mathf.RoundToInt(Display.main.systemWidth * quality.RenderScale);
                int h = Mathf.RoundToInt(Display.main.systemHeight * quality.RenderScale);
                Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
            }
            if (noise != null) atmosphere.SetQuality(quality);
            rig.ShakeScale = settings.ReducedShake ? 0.25f : 1f;
            rig.ReducedMotion = settings.ReducedMotion;
        }

        void CycleWarp()
        {
            if (session == null || state != State.Flight) return;
            warpIndex = (warpIndex + 1) % WarpSteps.Length;
            if (!WarpAllowed()) warpIndex = 0;
            controls.SetWarpLabel(WarpSteps[warpIndex]);
            Click();
        }

        bool WarpAllowed()
        {
            if (session.Telemetry.AltitudeAboveGround < WarpFloor) return false;
            foreach (var h in session.Hazards.ActiveStatuses()) if (h.Level == HazardLevel.Warning) return false;
            return true;
        }

        void OnChute()
        {
            if (session == null || state != State.Flight) return;
            var s = session.State;
            if (s.Chute == ChuteState.Open || s.Chute == ChuteState.Opening) { session.CutChute(); Click(); return; }
            if (session.RequestChute()) audioDirector.PlayChute();
        }

        void OnStrike(MissionSession m)
        {
            Vector3 up = WorldSpace.Dir(Planet.Up(m.State.Position));
            Vector3 pos = WorldSpace.ToUnity(m.State.Position);
            if (!settings.ReducedMotion) fx.Strike(pos, up);
            flash = settings.ReducedMotion ? 0.25f : 1f;
            audioDirector.PlayThunder(0f);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && state == State.Flight) Pause();
        }

        // ---------------------------------------------------------------- frame

        void Update()
        {
            if (state == State.Loading || session == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            if (Input.GetKeyDown(KeyCode.Escape) && state == State.Menu) MenuBack();

            switch (state)
            {
                case State.Menu:
                    if (demo) TickDemo(dt);
                    audioDirector.Silence(dt);
                    break;
                case State.Aiming:
                    controls.Read(rig.transform, Planet.Up(session.State.Position), false, false);
                    audioDirector.Silence(dt);
                    break;
                case State.Flight:
                    TickFlight(dt);
                    break;
                case State.Paused:
                    audioDirector.Silence(dt);
                    break;
                case State.Landed:
                case State.Results:
                    TickLanded(dt);
                    break;
            }
            UpdateViews(dt);
        }

        void MenuBack()
        {
            switch (menus.Current)
            {
                case Screen2.Levels: menus.Show(Screen2.Title); break;
                case Screen2.Vehicles: menus.Show(Screen2.Levels); break;
                case Screen2.Briefing: menus.Show(Screen2.Vehicles); break;
                case Screen2.Title: Application.Quit(); break;
            }
        }

        void TickDemo(float dt)
        {
            if (session.Phase == MissionPhase.Descent)
            {
                var input = demoPilot.Update(session);
                lastInput = input;
                // Fast-forward the high, quiet part of the demo descent.
                double warp = session.Telemetry.AltitudeAboveGround > 30000 ? 4 : 1;
                session.Tick(dt, input, warp, 48);
            }
            else if (session.Phase == MissionPhase.Finished)
            {
                landedTimer += dt;
                if (landedTimer > 4f) StartDemo();
            }
        }

        void TickFlight(float dt)
        {
            var s = session.State;
            bool outage = session.Hazards.Lightning.Outage > 0;
            bool chuteOk = session.Model.ChuteDeployAllowed(s, session.Telemetry, out _) || s.Chute == ChuteState.Open || s.Chute == ChuteState.Opening;
            var input = controls.Read(rig.transform, Planet.Up(s.Position), outage, chuteOk);
            lastInput = input;
            if (warpIndex > 0 && !WarpAllowed()) { warpIndex = 0; controls.SetWarpLabel(1f); }
            int maxSteps = settings.Quality == QualityTier.Low ? 40 : 64;
            session.Tick(dt, input, WarpSteps[warpIndex], maxSteps);

            UpdateHints();
            if (session.Phase == MissionPhase.Finished)
            {
                state = State.Landed;
                controls.SetVisible(false);
                bool crashed = !session.Result.Success;
                audioDirector.PlayTouchdown(crashed);
                if (crashed && s.ContactKind == ContactResult.None) probe.gameObject.SetActive(false);
                if (crashed) flash = settings.ReducedMotion ? 0.2f : 0.7f;
            }
        }

        void TickLanded(float dt)
        {
            landedTimer += dt;
            audioDirector.Silence(dt);
            if (!resultsShown && landedTimer > 2.2f)
            {
                resultsShown = true;
                state = State.Results;
                bool best = progress.RecordResult(SelectedLevel, session.Result);
                bool hasNext = SelectedLevel + 1 < Levels.All.Count && progress.IsLevelUnlocked(SelectedLevel + 1);
                hud.SetVisible(false);
                menus.ShowResults(session.Level, session.Result, best, hasNext);
            }
        }

        void UpdateHints()
        {
            if (session.Level.Index > 1) { hud.SetHint(""); return; }
            var s = session.State;
            var t = session.Telemetry;
            string h;
            if (session.Hazards.Lightning.Outage > 0) h = "Avionics down: tap REBOOT";
            else if (session.Regime == FlightRegime.Orbital) h = "Thin air: steering has little effect yet. Use WARP to speed up.";
            else if (t.AltitudeAboveGround > 9000) h = "Drag on the left to lean. Put the pink marker on the blue zone.";
            else if (s.Chute == ChuteState.Stowed && t.AltitudeAboveGround > 600) h = "Tap CHUTE once you are inside the amber band on the altitude bar.";
            else if (t.AltitudeAboveGround < 600) h = $"Hold BURN to slow down. Touch down below {Units.Speed(session.Level.SoftSpeed)}.";
            else h = "";
            hud.SetHint(h);
        }

        void UpdateViews(float dt)
        {
            var s = session.State;
            var t = session.Telemetry;
            WorldSpace.Origin = s.Position;
            atmosphere.SetTime(s.Time);

            probe.Apply(s, t, dt);
            Vector3 probePos = probe.transform.position;
            Vector3 up = WorldSpace.Dir(Planet.Up(s.Position));
            terrain.UpdateView(s.Time, s.Position, t.AltitudeAboveGround);
            // Blob shadow (always) complements real shadow maps near the ground.
            float agl = (float)Math.Max(0, t.AltitudeAboveGround);
            Vector3 ground = probePos - up * (agl + (float)session.Vehicle.GearHeight);
            terrain.SetProbeShadow(ground, probe.Radius * 1.6f + agl * 0.05f, Mathf.Clamp01(1f - agl / 60f) * 0.6f);

            double throttle = s.Fuel > 0 ? lastInput.Throttle : 0;
            if (session.Phase != MissionPhase.Descent) throttle = 0;
            fx.Apply(s, t, throttle, rig.Cam, settings.ReducedMotion, dt);

            float size = Mathf.Max(probe.Radius * 2f, probe.Height);
            Vector3 toZone = WorldSpace.ToUnity(session.ZonePosition) - probePos;
            if (session.Phase == MissionPhase.Aiming)
                rig.UpdateAiming(probePos, WorldSpace.Dir(s.Velocity).normalized, up, size, dt);
            else
            {
                float shake = (float)(session.Hazards.Effects.CameraShake + t.TurbulenceIntensity * 0.5 + fx.PlasmaLevel * 0.15);
                rig.UpdateFlight(probePos, WorldSpace.Dir(t.AirVelocity), WorldSpace.Dir(s.Velocity), up, toZone,
                                 size, Mathf.Clamp01(shake), (float)t.GLoad, agl, dt, s.Contact);
            }

            // Lightning flash and crash white-out decay.
            flash = Mathf.MoveTowards(flash, 0f, dt * 2.5f);
            atmosphere.FlashPosition = probePos + up * 600f;
            atmosphere.FlashIntensity = flash * 30f;
            atmosphere.FadeColor = new Color(1, 1, 1, flash * 0.35f);
            // Slightly darker exposure under thick cloud keeps storms moody without crushing the HUD view.
            atmosphere.Exposure = Mathf.Lerp(atmosphere.Exposure, 0.6f + 0.25f * Mathf.Clamp01((float)t.CloudDensity), dt * 2f);

            if (state == State.Flight || state == State.Aiming)
            {
                hud.UpdateHud(session, rig.Cam, dt);
                audioDirector.SetMuffle(1f - Mathf.Clamp01((float)t.CloudDensity * 0.7f));
                if (state == State.Flight)
                {
                    HazardLevel worst = HazardLevel.None;
                    foreach (var h in session.Hazards.ActiveStatuses()) if (h.Level > worst) worst = h.Level;
                    audioDirector.UpdateFlight(t, fx.PlasmaLevel, throttle, worst, !session.Nav.Available, dt);
                }
            }
            else if (state == State.Landed)
            {
                hud.UpdateHud(session, rig.Cam, dt);
            }
        }
    }
}
