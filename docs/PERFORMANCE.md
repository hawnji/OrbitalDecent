# Performance notes

## CPU

These were measured on the build machine, an x86-64 server core running .NET 8 Release, with
`dotnet run --project tools/SimTool -c Release -- bench`:

| Work | Cost |
| --- | --- |
| One simulation step (full model, turbulence, cloud and terrain lookups) | ~1.6 µs |
| One predictor step (deterministic mode) | ~0.9 µs |

Per rendered frame the game runs 120 Hz / fps simulation steps, which is two steps at 60 fps. It also
runs the predictor budget for the quality tier: 100, 150 or 260 steps. On the build machine that is
roughly 0.1–0.25 ms. Assuming a phone core running IL2CPP code is 5–10x slower, expect about
1–2.5 ms per frame. **This is an estimate; measure it with the Unity Profiler on a device.** If it is too
high, lower `PredictionSteps` in `QualityProfile`. The predictor then refreshes less often but stays
correct.

The noise textures (64³ shape and 256² coverage) are generated once on a worker thread at start-up.

## GPU

The expensive part is the cloud ray march in `ODAtmosphere.shader` pass 0. It runs at
1/`CloudDownsample` resolution in each axis, with `CloudSteps` view samples and `LightSteps` sun
samples per lit sample:

| Tier | Render scale | Cloud buffer | View / light steps | Shadows | Target |
| --- | --- | --- | --- | --- | --- |
| Low | 0.70 | 1/4 | 20 / 2 | off | 30 fps |
| Medium | 0.85 | 1/2 | 32 / 3 | hard, 160 m | 60 fps |
| High | 1.00 | 1/2 | 56 / 5 | hard, 160 m | 60 fps |

The first-run tier comes from `GameSettings.DetectQuality`: Medium for 8+ cores with 7+ GB RAM,
Low otherwise, and High on desktop. The march stops early once the view is opaque, and distant
samples skip the 3-D noise (`lod`), which helps both cost and shimmer.

**Nothing on the GPU side has been measured yet.** Suggested first device checks:

1. Run the Profiler and Frame Debugger on a mid-range phone in Cumulus Field and Storm Front, the
   worst cases for clouds.
2. If you are GPU-bound on Medium, try `CloudDownsample = 3` before cutting steps.
3. Watch for thermal throttling over a full 4–5 minute descent.
4. Check that `RenderTextureFormat.ARGBHalf` is supported. It is required for the cloud buffer and
   standard on GLES 3 and Vulkan devices.
