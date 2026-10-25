# Physics model

All of the simulation lives in `Assets/OrbitalDescent/Core` and runs in double precision with
no Unity types, so the same code runs in the game, in the tests and in `tools/SimTool`. Units are SI
throughout.

## Frame and planet

- The local frame has its origin at the landing zone, on the sea-level sphere. +Y is up at the
  zone, +X is east and +Z is north. The planet centre is at (0, −R, 0), with R = 6 371 km and
  GM = 3.986×10¹⁴ m³/s².
- Gravity is the inverse-square pull toward the centre, g = −GM·r̂/r², so a vacuum orbit stays an
  orbit (see the energy tests).
- Altitude uses a cancellation-free form, `(|p|² + 2R·p_y) / (|p − C| + R)`. This keeps
  centimetre accuracy at orbital distances, where `|p − C| − R` would lose about 1 m in double
  precision and far more in float.
- Unity renders with a floating origin: the probe is at the origin every frame, and all float
  coordinates handed to the GPU are camera-relative.

## Atmosphere (`Atmosphere.cs`)

US Standard Atmosphere 1976, with seven layers up to 86 km, provides temperature, pressure and
density from the hydrostatic equation and the speed of sound. Above that, density decays
exponentially with a 6 km scale height up to 140 km, where it is zero.

## Wind and weather (`Weather.cs`, `World.cs`)

- **Mean wind.** A surface wind with a boundary-layer profile, plus a jet stream (a Gaussian in
  altitude) that veers with height. On top of that are stratospheric wind and level-defined shear
  layers (tanh transitions), storm downdrafts and gust-front outflow.
- **Turbulence.** Clear-air turbulence plus convective turbulence scaled by local cloud density.
  It is generated from smooth noise, so it is bounded by construction: |v| ≤ 1.73·σ.
- **Clouds.** `CloudField.Density` is the same function as `CloudDensity` in
  `ODAtmosphereCore.hlsl`. It combines a 2-D coverage map, a per-type vertical profile, a 3-D
  Perlin-Worley shape texture and storm towers that narrow at the base and spread into an anvil.
  The physics samples the CPU copy of the exact bytes uploaded to the GPU. What you see is what you
  fly through, and the `CloudDensity_MatchesTextureSampling` test checks this.
- **Terrain.** A ridged-noise heightfield with a flattened valley and pad, or an ocean floor. A
  moving deck is used for the ship level.

## Aerodynamics (`FlightModel.Step`)

Forces are computed from the **air-relative** velocity v_air = v − wind, including turbulence.

- Body axis **B** runs from the heat shield toward the top. The angle of attack α is measured
  between B and the oncoming flow.
- **Axial force:** q·A·C_A·cos α along +B. C_A includes a Mach-dependent factor and extra drag from
  accreted ice.
- **Normal force:** q·A·(C_Nα·sin α·cos α + C_c·sin²α) in the B-flow plane. The first term is
  negative for blunt capsules, which is what makes a capsule *lift toward the side its top leans*.
  The second term is crossflow drag.
- **Parachute:** drag q·CdA·open², with a 2.5 s inflation. Deployment is refused above 7 km AGL or
  above the vehicle's maximum deploy q. Above 2.2× that q the canopy tears.
- **Engine:** thrust along +B, with propellant use from its Isp.
- **g-load** counts only non-gravitational force, which is what the structure feels.

## Rotation and control

- **Static stability** rotates B toward the flow (shield into the wind) with a restoring torque
  q·A·L·Cmα. A parachute adds riser stiffness.
- **Aerodynamic damping:** −q·A·L²/(2V)·Cmq·ω.
- **Turbulence torque** is a small, bounded random torque that scales with q.
- **Actuators.** Fins give q·A·L·k_fin, saturating at a structural limit; they are useless in thin
  air and reduced by ice. RCS gives a fixed torque but costs fuel through lever arm and Isp. Fins
  carry the command first, and RCS supplies only the remainder. The applied torque follows the
  command through a first-order lag (τ ≈ 80 ms).
- **SAS** (rate damping) and **STAB** (strong rate damping plus alignment with the flow) are explicit
  assists. They use the same saturated torque budget, so they cannot do the impossible.
- **Lightning outage** and **plasma blackout** act through `ControlModifiers` (authority, fins
  frozen, SAS unavailable) and through the navigation filter. They never change the physics.

## Heating, ice and damage

- **Convective heat flux** uses Sutton-Graves: q̇ = 1.7415×10⁻⁴·√(ρ/R_n)·v³ (W/m²).
- **Shield skin temperature** is a lumped heat balance: q̇ in, minus εσ(T⁴ − T_amb⁴), minus
  convective cooling, divided by the areal heat capacity (2500 J/m²K).
- Above the vehicle's limit the shield ablates and integrity drops. The plasma sheath brightness
  and the plasma-blackout hazard are both driven by the same q̇.
- **Ice** accretes in supercooled cloud (253–273.5 K), in proportion to cloud density and air
  speed. It adds mass and drag and weakens the fins. The heater removes it but drains the battery.
- **Structure** takes damage above the vehicle's g limit.

## Integration and robustness

- The integrator is semi-implicit Euler at a fixed 1/120 s step. Frame time is accumulated and
  consumed in whole steps, capped per frame, so the result does not depend on frame rate (see
  `FixedStep_FrameRateIndependentMission`).
- Attitude is a unit quaternion integrated with the world-frame angular velocity. Roll about the
  symmetry axis is strongly damped.
- **Safeguards:** angular rate is capped at 8 rad/s, and any non-finite state is caught, reported
  as a simulation fault and ends the attempt. It never propagates NaN.
- **Contact:** the step's segment is tested against the surface, using gear height and the
  terrain, water or deck height. The contact point is interpolated between steps, so even
  hypersonic impacts cannot tunnel through the ground (see `Contact_NoTunnelingAtHypersonicSpeed`).
- **Touchdown classification:** vertical speed at or below `MaxTouchdownSpeed` *and* horizontal
  speed at or below twice that counts as a landing (1.6× on water). Anything faster is a crash.

## Prediction and release planning

- `Predictor` runs the **same** `FlightModel` in deterministic mode: no turbulence, ice or damage.
  It uses larger steps high up and finer ones near the ground, and is advanced a fixed number of
  steps per frame so it never causes a hitch. The HUD shows its impact point and path.
  `Predictor_AgreesWithSimulation` bounds the difference.
- `MissionSession.SolveRelease` places the release point by iterating the predictor, so that an
  un-steered descent misses the zone by the level's designed aim bias. The player always has a
  correction to make.

## Hazards (`Hazards.cs`)

| Hazard | Trigger (physical) | Effect |
| --- | --- | --- |
| Wind shear | Vertical gradient of the mean wind field | Warning; the shear itself acts through the wind |
| Turbulence | Turbulence intensity at the probe | Camera shake and real gust forces and torques |
| Lightning | Charge builds inside storm cores and decays outside | Strike: fins frozen, SAS lost, outage timer; REBOOT shortens it |
| EM interference | Altitude inside the level's EM band | Navigation noise, input delay; reconverges after exit |
| Plasma blackout | Heat flux above the level's threshold | Navigation unavailable until the flux drops |
| Oscillation | Repeated rate reversals (pilot-induced oscillation) or tumbling above 2.5 rad/s in dense air | Warning to ease inputs, use SAS or hold STAB |
| Icing | Supercooled cloud | Ice mass, drag, weaker fins; heater clears it |
| Dense cloud | Visibility under 300 m from the cloud density | Caution; muffled audio, streaks and fog in the view |
| Terrain | Sink rate too high near the ground, or predicted touchdown on a slope or high ground | Warning ("SINK RATE - BURN!") or caution |
| Loads | g-load or skin temperature near limits | Warning before damage starts |
