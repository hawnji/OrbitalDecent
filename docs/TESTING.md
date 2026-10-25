# Testing

Everything below was run in the build environment, which has no Unity, on the commit this file
ships with.

## 1. Simulation and gameplay tests (42, all passing)

```
dotnet test tools/CoreTests -c Release
```

`tools/CoreTests` compiles `Assets/OrbitalDescent/Core` and `Assets/OrbitalDescent/Tests/EditMode`
with NUnit 3.14 on .NET 8. Unity's Test Runner (EditMode) runs the same files.

**Physics** (`PhysicsTests.cs`, 27 cases)

| Test | What it proves |
| --- | --- |
| `Gravity_IsInverseSquare` | g scales as 1/r² and points at the planet centre |
| `Altitude_StableFormulaMatchesDirect` | the cancellation-free altitude matches a direct formula |
| `Atmosphere_DensityDecreasesMonotonically` | density falls monotonically with altitude |
| `Atmosphere_MatchesUSStandard1976` (5 cases) | density and temperature match published US76 values at 0, 11, 20, 50 and 80 km |
| `Vacuum_CircularOrbitConservesEnergyAndRadius` | the integrator keeps an orbit (energy and radius drift bounded) |
| `Vacuum_FreeFallMatchesAnalyticEnergy` | energy conservation in free fall |
| `TerminalVelocity_MatchesAnalytic` | settles at √(2mg/ρC_DA) |
| `Drag_OpposesAirRelativeMotion` | drag acts on velocity relative to the wind, not to the ground |
| `TimeStep_Independence` | a full entry with 1/60 s and 1/240 s steps lands within 60 m and 1 s of each other |
| `Predictor_AgreesWithSimulation` | the HUD predictor matches the real simulation |
| `Steering_LeaningShiftsImpactTowardLean` | leaning moves the touchdown point the way the player expects |
| `Steering_InVacuumRotatesButBarelyMovesTrajectory` | no aerodynamic steering without air |
| `Crosswind_LightProbeDriftsMoreThanHeavy` | ballistic coefficient matters |
| `Rotation_StabilizeRecoversFromTumble` | STAB recovers a tumble within finite torque |
| `Rotation_LowStabilityAirframeOscillatesLonger` | the Wildcard really is less stable |
| `Safeguard_AngularRateBounded`, `Safeguard_NonFiniteStateIsCaught` | numerical safety nets |
| `Contact_NoTunnelingAtHypersonicSpeed` | the contact point lies on the terrain even at 2.5 km/s |
| `Contact_SoftTouchdownIsLanding` | touchdown speed matches √(v² + 2gh) and counts as a landing |
| `Chute_InterlockAndDescentRate` | the deploy interlocks work and the descent rate under canopy is as designed |
| `Engine_ThrustDecelerates` | the engine works against gravity |
| `Heating_GrowsWithSpeedAndDensity` | Sutton-Graves trends |
| `FixedStep_FrameRateIndependentMission` | after 60 s of flight, 30 fps and 144 fps frame pacing end within 20 m (one fixed step) |

**Gameplay** (`GameplayTests.cs`, 15 cases)

| Test | What it proves |
| --- | --- |
| `Release_NominalLandsAtAimBias` | the release solver produces the designed initial miss |
| `Release_AimChangesPredictionAndIsClamped` | aiming works and stays within the level limits |
| `NoInput_DescentFinishesWithResult` | every attempt ends with a scored result |
| `Lightning_BuildsStrikesAndRebootRecovers` | charge builds, a strike happens, REBOOT shortens the outage |
| `EmInterference_DegradesNavAndDelaysInput_ThenRecovers` | navigation noise and input delay appear and then clear |
| `InputDelay_ShiftsSteeringInTime` | the delay line really delays |
| `PlasmaBlackout_FromPhysicalHeatFlux` | blackout follows the heat flux, not a script |
| `WindShear_IsASpatialGradient` | shear comes from the wind field |
| `Turbulence_IsBounded` | gust velocity stays within its bound |
| `Icing_AccretesInSupercooledCloudAndHeaterClears` | ice and heater behaviour |
| `CloudDensity_MatchesTextureSampling` | the physics cloud equals the GPU texture sampling convention |
| `Scoring_StarsFollowRules` | the star rules shown in briefings are the ones applied |
| `Progression_UnlocksAndPersists` | unlocks, best results, corrupt-save handling |
| `Retry_FreshSessionIsIdentical` | instant retry is deterministic |
| `Autopilot_CompletesEveryLevel` | every mission is completable with the scripted pilot |

## 2. Autopilot campaign

```
dotnet run --project tools/SimTool -c Release -- campaign
```

A scripted pilot flies every mission with every probe: 24 runs, all ending in a landing,
splashdown or deck landing. Misses range from 9 m to 954 m and touchdowns from 1.3 to 7.2 m/s.
The pilot is deliberately simple, so a human can do better. Two caveats came out of this run:

- In **Storm Front** the autopilot's path takes no lightning strikes. The strike mechanics are
  covered by `Lightning_BuildsStrikesAndRebootRecovers` instead.
- In **Blackout Recovery** only the Wildcard reaches the deck; the other probes splash down,
  which still earns a star. Play-testing should check that the deck is reachable for humans.

## 3. Unity C# compile check

```
dotnet build tools/CompileCheck -c Release
```

This compiles Core, Runtime and Editor code against Unity's reference assemblies (NuGet
`Unity3D.SDK` 2021.1), with signature stubs for the uGUI package. It catches C# errors and most API
misuse. It **cannot** catch Unity 6-only API differences. The two known ones (`NamedBuildTarget` and
`FindAnyObjectByType`) are behind `#if UNITY_2021_2_OR_NEWER` and `#if UNITY_2023_1_OR_NEWER` and
were checked by reading the Unity 6 API.

## 4. Shader checks

```
python3 tools/ShaderCheck/check.py     # needs glslang-tools
```

Every vertex and fragment entry point of every `.shader` pass is compiled with glslang's HLSL
front end, using stand-in `UnityCG.cginc` and `AutoLight.cginc` files. This catches syntax and type
errors. It does not test Unity's own shader compiler or platform variants.

```
tools/ShaderPreview/build.sh <out> && node tools/ShaderPreview/render.mjs <out> <data>
```

This renders `ODAtmosphereCore.hlsl`, the exact file the game includes, through glslang and
SPIRV-Cross to WebGL2 in headless Chromium. It covers nine scenarios from orbit to the ocean, and
`docs/images/shader-preview.jpg` comes from it. It was used to find and fix NaNs above 100 km,
cloud tiling and over-exposed, flat-lit clouds. `SimTool export <dir>` produces the data it needs.

## Not tested here

Nothing has been run inside the Unity editor or on an Android device. That means none of these
has been seen: the scene, uGUI layout, touch input, the Unity shader variants, shadows, audio, frame
rate or the APK build. See [STATUS.md](STATUS.md).
