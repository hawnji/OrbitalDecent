using OrbitalDescent.Core;
using UnityEngine;

namespace OrbitalDescent
{
    /// <summary>
    /// Procedural sound: every clip is synthesised at start-up (no audio assets). Loops are driven by
    /// simulated quantities (dynamic pressure, heat flux, throttle, actuator use) and alerts follow
    /// hazard levels. Volumes are grouped into effects / alerts / ambience, scaled by the master level.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        const int Rate = 22050;
        AudioSource wind, plasma, engine, rcs, ambience, alerts, oneShots;
        AudioClip beepCaution, beepWarning, thunder, chuteClip, thump, crash, click, release, success;
        GameSettings settings;
        float alertCooldown;
        HazardLevel lastLevel;
        float muffle = 1f;

        public static AudioDirector Create(GameSettings s)
        {
            var go = new GameObject("Audio");
            var a = go.AddComponent<AudioDirector>();
            a.settings = s;
            a.Build();
            return a;
        }

        void Build()
        {
            var r = new System.Random(11);
            wind = Loop("Wind", BrownNoise(r, 4f, 0.985f));
            plasma = Loop("Plasma", Crackle(r, 3f));
            engine = Loop("Engine", Rumble(r, 3f));
            rcs = Loop("Rcs", WhiteNoise(r, 2f, 0.25f));
            ambience = Loop("Ambience", Drone(6f));
            alerts = gameObject.AddComponent<AudioSource>();
            alerts.playOnAwake = false;
            oneShots = gameObject.AddComponent<AudioSource>();
            oneShots.playOnAwake = false;

            beepCaution = Tone("Caution", 0.18f, 880f, 0f);
            beepWarning = Tone("Warning", 0.5f, 1250f, 0.25f);
            thunder = Burst(r, "Thunder", 2.2f, 0.9f, 1.2f);
            chuteClip = Burst(r, "Chute", 0.9f, 0.4f, 6f);
            thump = Thump("Thump", 0.5f, 70f);
            crash = Burst(r, "Crash", 1.6f, 1f, 2.5f);
            click = Tone("Click", 0.04f, 2400f, 0f);
            release = Burst(r, "Release", 0.6f, 0.6f, 9f);
            success = Chord("Success");
        }

        AudioSource Loop(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.volume = 0f;
            src.playOnAwake = false;
            src.Play();
            return src;
        }

        static AudioClip Make(string name, float[] d)
        {
            var c = AudioClip.Create(name, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        // ---------------- synthesis ----------------

        static float[] BrownNoise(System.Random r, float seconds, float leak)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            float y = 0;
            for (int i = 0; i < n; i++) { y = y * leak + ((float)r.NextDouble() * 2 - 1) * 0.12f; d[i] = y; }
            Normalize(d, 0.8f);
            Crossfade(d);
            return d;
        }

        static float[] WhiteNoise(System.Random r, float seconds, float lowpass)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            float y = 0;
            for (int i = 0; i < n; i++) { y += (((float)r.NextDouble() * 2 - 1) - y) * lowpass; d[i] = y; }
            Normalize(d, 0.6f);
            Crossfade(d);
            return d;
        }

        static float[] Crackle(System.Random r, float seconds)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            float y = 0, env = 0;
            for (int i = 0; i < n; i++)
            {
                if (r.NextDouble() < 0.004) env = 0.6f + (float)r.NextDouble() * 0.4f;
                env *= 0.996f;
                y += (((float)r.NextDouble() * 2 - 1) - y) * 0.5f;
                d[i] = y * (0.25f + env);
            }
            Normalize(d, 0.7f);
            Crossfade(d);
            return d;
        }

        static float[] Rumble(System.Random r, float seconds)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            float a = 0, b = 0;
            for (int i = 0; i < n; i++)
            {
                float w = (float)r.NextDouble() * 2 - 1;
                a += (w - a) * 0.08f;
                b += (w - b) * 0.4f;
                d[i] = a * 0.8f + b * 0.25f + Mathf.Sin(i * 2f * Mathf.PI * 55f / Rate) * 0.08f;
            }
            Normalize(d, 0.8f);
            Crossfade(d);
            return d;
        }

        static float[] Drone(float seconds)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                // Whole-number cycles per loop length keep the loop seamless.
                d[i] = Mathf.Sin(2 * Mathf.PI * 55f * t) * 0.3f + Mathf.Sin(2 * Mathf.PI * 82.5f * t) * 0.18f
                     + Mathf.Sin(2 * Mathf.PI * 110.5f * t) * 0.08f * (0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * t / seconds));
            }
            return d;
        }

        AudioClip Tone(string name, float seconds, float hz, float gap)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                bool on = gap <= 0 || (t % (gap * 2)) < gap;
                float env = Mathf.Min(1f, t * 200f) * Mathf.Min(1f, (seconds - t) * 60f);
                d[i] = on ? Mathf.Sin(2 * Mathf.PI * hz * t) * 0.5f * env : 0f;
            }
            return Make(name, d);
        }

        static AudioClip Burst(System.Random r, string name, float seconds, float level, float decay)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            float y = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                y += (((float)r.NextDouble() * 2 - 1) - y) * 0.3f;
                d[i] = y * level * Mathf.Exp(-t * decay) * Mathf.Min(1f, t * 400f);
            }
            Normalize(d, level);
            return Make(name, d);
        }

        static AudioClip Thump(string name, float seconds, float hz)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = hz * (1f + 2f * Mathf.Exp(-t * 30f));
                d[i] = Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t * 9f);
            }
            return Make(name, d);
        }

        static AudioClip Chord(string name)
        {
            int n = (int)(1.4f * Rate);
            var d = new float[n];
            float[] f = { 523.25f, 659.25f, 783.99f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float s = 0;
                for (int k = 0; k < 3; k++) if (t > k * 0.12f) s += Mathf.Sin(2 * Mathf.PI * f[k] * t) * Mathf.Exp(-(t - k * 0.12f) * 2.5f);
                d[i] = s * 0.25f;
            }
            return Make(name, d);
        }

        static void Normalize(float[] d, float peak)
        {
            float m = 1e-6f;
            foreach (var v in d) m = Mathf.Max(m, Mathf.Abs(v));
            for (int i = 0; i < d.Length; i++) d[i] *= peak / m;
        }

        // Blend the tail into the head so noise loops have no click.
        static void Crossfade(float[] d)
        {
            int n = Mathf.Min(d.Length / 8, Rate / 10);
            for (int i = 0; i < n; i++)
            {
                float w = i / (float)n;
                d[i] = d[i] * w + d[d.Length - n + i] * (1 - w);
            }
        }

        // ---------------- runtime ----------------

        float Fx => settings.MasterVolume * settings.EffectsVolume;
        float Al => settings.MasterVolume * settings.AlertVolume;
        float Amb => settings.MasterVolume * settings.AmbienceVolume;

        /// <summary>Paused or in menus: fade the flight loops out.</summary>
        public void Silence(float dt)
        {
            foreach (var s in new[] { wind, plasma, engine, rcs })
                s.volume = Mathf.MoveTowards(s.volume, 0f, dt * 2f);
            ambience.volume = Mathf.MoveTowards(ambience.volume, Amb * 0.25f, dt);
        }

        public void UpdateFlight(FlightTelemetry t, float plasmaLevel, double throttle, HazardLevel worst, bool navLost, float dt)
        {
            // Sound needs air: everything but alerts fades out in near-vacuum.
            float air = Mathf.Clamp01((float)t.Density / 0.002f);
            float q = (float)t.DynamicPressure;
            float windTarget = Mathf.Clamp01(Mathf.Log10(1f + q) / 4.3f) * air;
            wind.volume = Mathf.Lerp(wind.volume, windTarget * Fx * muffle, dt * 4f);
            wind.pitch = Mathf.Lerp(0.6f, 1.6f, Mathf.Clamp01((float)t.AirSpeed / 900f));
            plasma.volume = Mathf.Lerp(plasma.volume, plasmaLevel * 0.9f * Fx, dt * 6f);
            plasma.pitch = 0.8f + plasmaLevel * 0.5f;
            engine.volume = Mathf.Lerp(engine.volume, (float)throttle * 0.9f * Fx * Mathf.Lerp(0.35f, 1f, air), dt * 10f);
            engine.pitch = 0.85f + 0.3f * (float)throttle;
            rcs.volume = Mathf.Lerp(rcs.volume, Mathf.Clamp01((float)t.CommandMagnitude) * 0.35f * Fx, dt * 12f);
            // Space ambience fades as the atmosphere thickens.
            ambience.volume = Mathf.Lerp(ambience.volume, (1f - air) * 0.35f * Amb, dt);

            alertCooldown -= dt;
            if (worst > lastLevel || (worst == HazardLevel.Warning && alertCooldown <= 0f))
            {
                if (worst == HazardLevel.Warning) { alerts.PlayOneShot(beepWarning, Al); alertCooldown = 2.5f; }
                else if (worst == HazardLevel.Caution) alerts.PlayOneShot(beepCaution, Al * 0.8f);
            }
            lastLevel = worst;
        }

        /// <summary>Inside dense cloud the outside world sounds muffled.</summary>
        public void SetMuffle(float m) { muffle = Mathf.Clamp(m, 0.3f, 1f); }

        public void PlayThunder(float distance01) => oneShots.PlayOneShot(thunder, Fx * Mathf.Lerp(1f, 0.4f, distance01));
        public void PlayChute() => oneShots.PlayOneShot(chuteClip, Fx);
        public void PlayRelease() => oneShots.PlayOneShot(release, Fx);
        public void PlayTouchdown(bool crashed)
        {
            oneShots.PlayOneShot(crashed ? crash : thump, Fx);
            if (!crashed) oneShots.PlayOneShot(success, Al * 0.7f);
        }
        public void PlayClick() => oneShots.PlayOneShot(click, Al * 0.4f);
    }
}
