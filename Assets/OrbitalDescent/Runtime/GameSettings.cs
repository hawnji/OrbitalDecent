using OrbitalDescent.Core;
using UnityEngine;

namespace OrbitalDescent
{
    public enum QualityTier { Low = 0, Medium = 1, High = 2 }

    /// <summary>Player options, persisted with PlayerPrefs.</summary>
    public sealed class GameSettings
    {
        public float HudScale = 1.0f;          // 0.8 .. 1.3
        public float SafeMargin = 0.0f;        // extra margin as fraction of screen height, 0 .. 0.08
        public bool Imperial;
        public bool ReducedShake;
        public bool ReducedMotion;
        public bool TiltSteering;
        public bool InvertVertical;
        public QualityTier Quality = QualityTier.Medium;
        public float Difficulty = 1.0f;        // hazard strength multiplier 0.75 / 1 / 1.25
        public float MasterVolume = 0.9f;
        public float EffectsVolume = 0.9f;
        public float AlertVolume = 0.8f;
        public float AmbienceVolume = 0.7f;
        public bool ShowTutorialHints = true;

        const string Prefix = "od.settings.";

        public static GameSettings Load()
        {
            var s = new GameSettings();
            if (!PlayerPrefs.HasKey(Prefix + "quality"))
            {
                s.Quality = DetectQuality();
                return s;
            }
            s.HudScale = PlayerPrefs.GetFloat(Prefix + "hudScale", s.HudScale);
            s.SafeMargin = PlayerPrefs.GetFloat(Prefix + "safeMargin", s.SafeMargin);
            s.Imperial = PlayerPrefs.GetInt(Prefix + "imperial", 0) == 1;
            s.ReducedShake = PlayerPrefs.GetInt(Prefix + "reducedShake", 0) == 1;
            s.ReducedMotion = PlayerPrefs.GetInt(Prefix + "reducedMotion", 0) == 1;
            s.TiltSteering = PlayerPrefs.GetInt(Prefix + "tilt", 0) == 1;
            s.InvertVertical = PlayerPrefs.GetInt(Prefix + "invert", 0) == 1;
            s.Quality = (QualityTier)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "quality", 1), 0, 2);
            s.Difficulty = PlayerPrefs.GetFloat(Prefix + "difficulty", 1f);
            s.MasterVolume = PlayerPrefs.GetFloat(Prefix + "volMaster", s.MasterVolume);
            s.EffectsVolume = PlayerPrefs.GetFloat(Prefix + "volFx", s.EffectsVolume);
            s.AlertVolume = PlayerPrefs.GetFloat(Prefix + "volAlert", s.AlertVolume);
            s.AmbienceVolume = PlayerPrefs.GetFloat(Prefix + "volAmb", s.AmbienceVolume);
            s.ShowTutorialHints = PlayerPrefs.GetInt(Prefix + "hints", 1) == 1;
            return s;
        }

        public void Save()
        {
            PlayerPrefs.SetFloat(Prefix + "hudScale", HudScale);
            PlayerPrefs.SetFloat(Prefix + "safeMargin", SafeMargin);
            PlayerPrefs.SetInt(Prefix + "imperial", Imperial ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "reducedShake", ReducedShake ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "reducedMotion", ReducedMotion ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "tilt", TiltSteering ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "invert", InvertVertical ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "quality", (int)Quality);
            PlayerPrefs.SetFloat(Prefix + "difficulty", Difficulty);
            PlayerPrefs.SetFloat(Prefix + "volMaster", MasterVolume);
            PlayerPrefs.SetFloat(Prefix + "volFx", EffectsVolume);
            PlayerPrefs.SetFloat(Prefix + "volAlert", AlertVolume);
            PlayerPrefs.SetFloat(Prefix + "volAmb", AmbienceVolume);
            PlayerPrefs.SetInt(Prefix + "hints", ShowTutorialHints ? 1 : 0);
            PlayerPrefs.Save();
            Units.Imperial = Imperial;
        }

        /// <summary>Conservative first-run default from device capability.</summary>
        public static QualityTier DetectQuality()
        {
            if (!Application.isMobilePlatform) return QualityTier.High;
            int mem = SystemInfo.systemMemorySize;
            int gpuMem = SystemInfo.graphicsMemorySize;
            if (mem >= 7000 && gpuMem >= 2000 && SystemInfo.processorCount >= 8) return QualityTier.Medium;
            return QualityTier.Low;
        }
    }

    /// <summary>Per-tier rendering parameters. Measured costs belong in docs/PERFORMANCE.md.</summary>
    public struct QualityProfile
    {
        public float RenderScale;       // 3D resolution relative to the screen
        public int CloudDownsample;     // cloud buffer divisor
        public int CloudSteps;
        public int CloudLightSteps;
        public int AtmosphereSteps;
        public int TerrainResolution;   // grid vertices per side
        public bool CloudShadows;
        public int TargetFrameRate;
        public int Antialiasing;        // MSAA samples

        public static QualityProfile For(QualityTier t)
        {
            switch (t)
            {
                case QualityTier.Low:
                    return new QualityProfile { RenderScale = 0.7f, CloudDownsample = 4, CloudSteps = 20, CloudLightSteps = 2, AtmosphereSteps = 6, TerrainResolution = 161, CloudShadows = false, TargetFrameRate = 30, Antialiasing = 1 };
                case QualityTier.High:
                    return new QualityProfile { RenderScale = 1.0f, CloudDownsample = 2, CloudSteps = 56, CloudLightSteps = 5, AtmosphereSteps = 12, TerrainResolution = 255, CloudShadows = true, TargetFrameRate = 60, Antialiasing = 4 };
                default:
                    return new QualityProfile { RenderScale = 0.85f, CloudDownsample = 2, CloudSteps = 32, CloudLightSteps = 3, AtmosphereSteps = 8, TerrainResolution = 201, CloudShadows = true, TargetFrameRate = 60, Antialiasing = 2 };
            }
        }
    }

    public sealed class PlayerPrefsStore : ISaveStore
    {
        public string Load(string key) => PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
        public void Save(string key, string value) { PlayerPrefs.SetString(key, value); PlayerPrefs.Save(); }
    }
}
