using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OrbitalDescent.Core
{
    /// <summary>Storage backend (PlayerPrefs in Unity, in-memory in tests).</summary>
    public interface ISaveStore
    {
        string Load(string key);
        void Save(string key, string value);
    }

    public sealed class MemorySaveStore : ISaveStore
    {
        readonly Dictionary<string, string> data = new Dictionary<string, string>();
        public string Load(string key) => data.TryGetValue(key, out var v) ? v : null;
        public void Save(string key, string value) => data[key] = value;
    }

    public sealed class LevelProgress
    {
        public int BestStars;
        public int BestScore;
        public double BestError = double.PositiveInfinity;
        public int Attempts;
    }

    /// <summary>
    /// Save policy: best stars/score/error per level are kept after every finished attempt
    /// (never overwritten by a worse run). Level N+1 unlocks when level N has at least one star.
    /// Vehicles unlock by total stars. Stored as a small versioned text record.
    /// </summary>
    public sealed class Progression
    {
        const string Key = "od.progress.v1";
        readonly ISaveStore store;
        public readonly LevelProgress[] Levels;

        public static readonly Dictionary<string, int> VehicleStarRequirement = new Dictionary<string, int>
        {
            { "dart", 0 }, { "hammer", 2 }, { "phantom", 4 }, { "wildcard", 7 },
        };

        public Progression(ISaveStore store, int levelCount)
        {
            this.store = store;
            Levels = new LevelProgress[levelCount];
            for (int i = 0; i < levelCount; i++) Levels[i] = new LevelProgress();
            Load();
        }

        public int TotalStars { get { int n = 0; foreach (var l in Levels) n += l.BestStars; return n; } }

        public bool IsLevelUnlocked(int index) => index == 0 || (index < Levels.Length && Levels[index - 1].BestStars > 0);

        public bool IsVehicleUnlocked(string id) =>
            !VehicleStarRequirement.TryGetValue(id, out int req) || TotalStars >= req;

        /// <summary>Records an attempt. Returns true if this was a new best score.</summary>
        public bool RecordResult(int levelIndex, ScoreResult r)
        {
            var p = Levels[levelIndex];
            p.Attempts++;
            bool best = false;
            if (r.Stars > p.BestStars) p.BestStars = r.Stars;
            if (r.Success && r.Total > p.BestScore) { p.BestScore = r.Total; best = true; }
            if (r.Success && r.ErrorMeters < p.BestError) p.BestError = r.ErrorMeters;
            Save();
            return best;
        }

        public void Save()
        {
            var sb = new StringBuilder("v1");
            foreach (var l in Levels)
            {
                sb.Append('|').Append(l.BestStars).Append(',').Append(l.BestScore).Append(',')
                  .Append(double.IsInfinity(l.BestError) ? "-" : l.BestError.ToString("R", CultureInfo.InvariantCulture))
                  .Append(',').Append(l.Attempts);
            }
            store.Save(Key, sb.ToString());
        }

        void Load()
        {
            string s = store.Load(Key);
            if (string.IsNullOrEmpty(s)) return;
            try
            {
                var parts = s.Split('|');
                if (parts[0] != "v1") return;
                for (int i = 1; i < parts.Length && i - 1 < Levels.Length; i++)
                {
                    var f = parts[i].Split(',');
                    var l = Levels[i - 1];
                    l.BestStars = Math.Max(0, Math.Min(3, int.Parse(f[0], CultureInfo.InvariantCulture)));
                    l.BestScore = int.Parse(f[1], CultureInfo.InvariantCulture);
                    l.BestError = f[2] == "-" ? double.PositiveInfinity : double.Parse(f[2], CultureInfo.InvariantCulture);
                    l.Attempts = int.Parse(f[3], CultureInfo.InvariantCulture);
                }
            }
            catch (Exception)
            {
                // Corrupt save: start fresh rather than crash.
                foreach (var l in Levels) { l.BestStars = 0; l.BestScore = 0; l.BestError = double.PositiveInfinity; l.Attempts = 0; }
            }
        }
    }
}
