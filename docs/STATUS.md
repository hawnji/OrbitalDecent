# Implementation status

This states plainly what exists, how it was checked, and what is still open. "Verified" means
it was executed or compiled here. "Written" means the code exists but has not been run.

## Environment constraint

The project was built in a container without Unity. The Unity download CDN was blocked, and
Unity also needs a signed-in licence. So the game was split into:

- an **engine-independent core** (physics, weather, hazards, prediction, scoring, progression),
  which is fully executable and tested with .NET, and
- **Unity code and shaders**, which are compile-checked and type-checked but have never been run.

## Verified

| Area | How |
| --- | --- |
| Flight physics, atmosphere, wind, turbulence, clouds, heating, ice, damage, contact | 27 physics tests |
| Six of the ten hazards (lightning, EM interference, plasma blackout, wind shear, turbulence, icing), release solver, scoring, progression, retry determinism | 15 gameplay tests |
| Oscillation, dense-cloud, terrain and loads warnings | Exercised by the autopilot campaign only; no dedicated tests |
| Every mission is completable with every probe | Autopilot campaign, 24 of 24 runs land |
| Atmosphere, sky, far planet and volumetric clouds (shared HLSL) | Rendered in a WebGL preview, 9 scenarios, inspected |
| All Unity C# (runtime and editor) compiles | Built against Unity reference assemblies |
| All Unity shader passes type-check | glslang HLSL front end with stand-in Unity includes |

## Written, not yet run

- Unity scene assembly from code (`Bootstrap`, `GameController`), floating origin and camera rig
- Unity shaders: atmosphere image effect (cloud pass and composite), lit, terrain, water, effects,
  unlit
- Probe, terrain, ocean, ship and landing pad meshes (procedural)
- Effects: plasma sheath, engine plumes, ionised trail, speed and cloud streaks, lightning bolt and flash
- Procedural audio and its mixing
- HUD, touch controls, tilt steering, keyboard controls
- Menus: title with attract-mode demo flight, missions, probes, briefing, release aiming, pause,
  results, settings
- Settings persistence, quality tiers, safe area, HUD scale, units, accessibility options
- Editor auto-configuration and Android APK/AAB build (menu and batch mode)

## Known gaps and likely first fixes

- **First editor run.** Expect a few small issues that only show inside Unity: uGUI layout
  sizes, a shader keyword, or a texture-coordinate flip on a particular graphics API. The
  composite pass handles the usual `UNITY_UV_STARTS_AT_TOP` case, but it has not been seen on a real
  device.
- **Transparent effects over clouds.** The atmosphere pass runs before transparent geometry, so
  additive effects such as the beacon draw on top of clouds. The beacon is intentionally visible
  through cloud, but the plume and sheath are not fogged by clouds in front of them. That is
  acceptable because the camera is always close to the probe.
- **Cloud tiling seen from orbit.** Two rotated coverage scales hide most repetition, but a regular
  pattern is still faintly visible from 100+ km (see the first preview frame).
- **No temporal reprojection for clouds.** The ray-march jitter changes every frame, so expect some
  grain on Low quality.
- **Lightning in Storm Front.** The autopilot's path takes no strikes. Whether human players meet
  lightning often enough needs play-testing.
- **Blackout Recovery deck landing.** Only the Wildcard autopilot reached the ship's deck; check
  whether it is reachable for players with the other probes.
- **Device performance.** Not measured yet; see [PERFORMANCE.md](PERFORMANCE.md).
- **Not included:** cloud saves, achievements, localisation, gamepad support, haptics, store
  listing assets and an app icon (Unity's default icon is used).
