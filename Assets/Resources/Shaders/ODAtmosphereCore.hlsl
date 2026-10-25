// Orbital Descent - atmosphere, planet and volumetric cloud model.
//
// Engine-agnostic HLSL: the includer must define
//   OD_TEX2D(name), OD_TEX3D(name)            texture declarations
//   OD_SAMPLE2D(name, uv), OD_SAMPLE3D(name, uvw)  level-0 bilinear samples
// before including this file. Unity (ODAtmosphere.shader) and the WebGL preview harness
// (tools/ShaderPreview) both compile this same file.
//
// Coordinates: local landing-zone frame in metres, +Y up at the zone, planet centre at (0, -R, 0).
// The cloud density function mirrors OrbitalDescent.Core.CloudField so that what the player sees is
// what the physics flies through. Keep them in sync.

#ifndef OD_ATMOSPHERE_CORE
#define OD_ATMOSPHERE_CORE

#define OD_PI 3.14159265

// ---------------------------------------------------------------- parameters
float _PlanetRadius;      // m
float _AtmoTop;           // m, top of the scattering shell
float _CamAlt;            // m, camera altitude (computed in double on the CPU)
float3 _CamUp;            // radial up at the camera
float3 _CamLocal;         // camera position, local frame
float3 _SunDir;           // towards the sun
float _SunIntensity;
float _Exposure;

float4 _CloudLayerA[4];   // base, top, coverage, density
float4 _CloudLayerB[4];   // type (0 cirrus 1 stratus 2 cumulus 3 storm), shape scale, -, -
float _CloudLayerCount;
float4 _StormCells[4];    // x, z, radius, strength
float _StormCount;
float2 _CloudDrift;       // accumulated drift (m) east / north
float _CoverageTile;      // m per coverage texture tile
float _ExtinctionScale;   // 1/m at density 1
float _CloudSteps;
float _LightSteps;
float _AtmoSteps;
float4 _Flash;            // lightning: local position xyz, intensity w
float _FrameJitter;       // 0..1 per-frame offset for ray-march jitter
float _CloudBandMin;      // lowest cloud base, m
float _CloudBandMax;      // highest cloud top, m

OD_TEX3D(_CloudShapeTex);
OD_TEX2D(_CloudCoverageTex);

// ---------------------------------------------------------------- scattering constants
static const float3 kRayleighBeta = float3(5.802e-6, 13.558e-6, 33.1e-6);
static const float kRayleighH = 8000.0;
static const float kMieBeta = 3.996e-6;
static const float kMieExt = 4.44e-6;
static const float kMieH = 1200.0;
static const float3 kOzoneBeta = float3(0.65e-6, 1.881e-6, 0.085e-6);

// ---------------------------------------------------------------- geometry
float AltitudeLocal(float3 p)
{
    float R = _PlanetRadius;
    float3 d = float3(p.x, p.y + R, p.z);
    return (dot(p, p) + 2.0 * R * p.y) / (length(d) + R);
}

float2 HorizontalLocal(float3 p)
{
    float R = _PlanetRadius;
    return p.xz * (R / (p.y + R));
}

float3 UpLocal(float3 p)
{
    return normalize(float3(p.x, p.y + _PlanetRadius, p.z));
}

// Ray from the camera against a sphere of radius R + hs. Returns (t0, t1), or (-1, -1) on a miss.
// Uses the camera altitude directly to avoid float cancellation at planetary radii.
float2 RaySphereCam(float3 dir, float hs)
{
    float R = _PlanetRadius;
    float r0 = R + _CamAlt;
    float mu = dot(dir, _CamUp);
    float b = r0 * mu;
    float c = (_CamAlt - hs) * (r0 + R + hs);
    float D = b * b - c;
    if (D < 0.0) return float2(-1.0, -1.0);
    float s = sqrt(D);
    float q = -b - (b >= 0.0 ? s : -s);
    float t0 = q;
    float t1 = abs(q) > 1e-6 ? c / q : 0.0;
    return float2(min(t0, t1), max(t0, t1));
}

// ---------------------------------------------------------------- phase functions
float PhaseRayleigh(float c) { return 3.0 / (16.0 * OD_PI) * (1.0 + c * c); }

float PhaseHG(float c, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * OD_PI * pow(max(1e-4, 1.0 + g2 - 2.0 * g * c), 1.5));
}

// ---------------------------------------------------------------- optical depth
// Chapman grazing-incidence approximation (C. Schueler). X = R/H, h = alt/H. Returns the column
// density from the point to infinity along a direction with the given zenith cosine, in units of H.
float Chapman(float X, float h, float cosZ)
{
    float c = sqrt(X + h);
    if (cosZ >= 0.0)
        return c / (c * cosZ + 1.0) * exp(-h);
    float x0 = sqrt(max(0.0, 1.0 - cosZ * cosZ)) * (X + h);
    float c0 = sqrt(x0);
    return 2.0 * c0 * exp(min(X - x0, 60.0)) - c / (1.0 - c * cosZ) * exp(-h);
}

// Transmittance from a point at altitude alt toward the sun.
float3 SunTransmittance(float alt, float cosSun)
{
    float R = _PlanetRadius;
    alt = max(alt, 0.0);
    // Below the geometric horizon the planet blocks the sun.
    float horizon = -sqrt(max(0.0, 1.0 - (R * R) / ((R + alt) * (R + alt))));
    if (cosSun < horizon) return float3(0, 0, 0);
    float odR = kRayleighH * Chapman(R / kRayleighH, alt / kRayleighH, cosSun);
    float odM = kMieH * Chapman(R / kMieH, alt / kMieH, cosSun);
    float odO = odR * 1.9; // ozone column roughly proportional to the Rayleigh column above ~20 km
    float3 tau = kRayleighBeta * odR + kMieExt * odM + kOzoneBeta * odO;
    return exp(-tau);
}

// ---------------------------------------------------------------- atmosphere integral
struct AtmoResult
{
    float3 inscatter;
    float3 transmittance;
};

// Single scattering along the camera ray from t = 0 to tMax. The density integral uses the
// log-mean of each segment, which is exact for exponential variation along the segment.
AtmoResult IntegrateAtmosphere(float3 dir, float tMax, float steps)
{
    AtmoResult r;
    r.inscatter = float3(0, 0, 0);
    r.transmittance = float3(1, 1, 1);
    float2 shell = RaySphereCam(dir, _AtmoTop);
    if (shell.y <= 0.0) return r;
    float t0 = max(shell.x, 0.0);
    float t1 = min(shell.y, tMax);
    if (t1 <= t0) return r;

    float mu = dot(dir, _SunDir);
    float pr = PhaseRayleigh(mu);
    float pm = PhaseHG(mu, 0.76);
    float3 odSum = float3(0, 0, 0);    // accumulated (rayleigh, mie, ozone) column (m)
    float n = max(steps, 2.0);
    float prevT = t0;
    float3 prevP = _CamLocal + dir * t0;
    float prevAlt = AltitudeLocal(prevP);
    float prevRho = exp(-prevAlt / kRayleighH);
    float prevMie = exp(-prevAlt / kMieH);
    [loop]
    for (float i = 1.0; i <= n; i += 1.0)
    {
        // Quadratic step distribution: dense near the camera, sparse far away.
        float f = i / n;
        float t = t0 + (t1 - t0) * f * f;
        float3 p = _CamLocal + dir * t;
        float alt = AltitudeLocal(p);
        float rho = exp(-max(alt, 0.0) / kRayleighH);
        float mie = exp(-max(alt, 0.0) / kMieH);
        float ozone = max(0.0, 1.0 - abs(alt - 25000.0) / 15000.0);
        float ds = t - prevT;
        // Log-mean density over the segment; log(rho/prevRho) = -(alt - prevAlt)/H exactly, which avoids
        // dividing tiny underflowing densities.
        float dAlt = max(alt, 0.0) - max(prevAlt, 0.0);
        float rR = abs(dAlt) > 1.0 ? (rho - prevRho) / (-dAlt / kRayleighH) : 0.5 * (rho + prevRho);
        float rM = abs(dAlt) > 1.0 ? (mie - prevMie) / (-dAlt / kMieH) : 0.5 * (mie + prevMie);
        float3 segOD = float3(rR, rM, ozone) * ds;
        float3 midOD = odSum + segOD * 0.5;
        odSum += segOD;
        float3 tauView = kRayleighBeta * midOD.x + kMieExt * midOD.y + kOzoneBeta * midOD.z;
        float3 sunT = SunTransmittance(alt, dot(UpLocal(p), _SunDir));
        float3 scatter = (kRayleighBeta * rR * pr + kMieBeta * rM * pm) * ds;
        r.inscatter += exp(-tauView) * sunT * scatter;
        prevT = t; prevRho = rho; prevMie = mie; prevAlt = alt;
    }
    r.transmittance = exp(-(kRayleighBeta * odSum.x + kMieExt * odSum.y + kOzoneBeta * odSum.z));
    r.inscatter *= _SunIntensity;
    return r;
}

// Sky irradiance proxy for ambient lighting at an altitude (cheap, analytic).
float3 SkyAmbient(float alt)
{
    float d = exp(-max(alt, 0.0) / 9000.0);
    float3 day = float3(0.35, 0.55, 0.95) * 0.9 * _SunIntensity * 0.06;
    return day * (0.15 + 0.85 * d) * saturate(_SunDir.y * 2.0 + 0.4);
}

// ---------------------------------------------------------------- ground
// Albedo of the far-field planet surface. The terrain mesh shader uses the same function so the
// near patch blends into the analytic planet without a visible seam.
float3 GroundAlbedo(float2 horiz, float height, float slope, float ocean)
{
    float2 uv = horiz / (_CoverageTile * 11.0);
    float large = OD_SAMPLE2D(_CloudCoverageTex, uv + float2(0.37, 0.71)).r;
    float detail = OD_SAMPLE2D(_CloudCoverageTex, horiz / 2300.0).r;
    float3 grass = lerp(float3(0.085, 0.11, 0.045), float3(0.15, 0.15, 0.07), large);
    float3 dry = float3(0.22, 0.19, 0.13);
    float3 rock = float3(0.17, 0.16, 0.15);
    float3 snow = float3(0.8, 0.82, 0.86);
    float3 c = lerp(grass, dry, saturate((large - 0.55) * 4.0));
    c *= 0.8 + 0.4 * detail;
    c = lerp(c, rock, saturate((slope - 0.12) * 5.0));
    c = lerp(c, snow, saturate((height - 2200.0) / 400.0) * saturate(1.0 - slope * 2.0));
    // Far-field seas on land levels: low-frequency mask.
    float sea = saturate((0.32 - large) * 12.0);
    float3 water = float3(0.01, 0.025, 0.045);
    c = lerp(c, water, max(sea, ocean));
    return c;
}

// ---------------------------------------------------------------- clouds
float CloudHeightProfile(float type, float h)
{
    if (type < 0.5) return saturate(h / 0.3) * saturate((1.0 - h) / 0.3);
    if (type < 1.5) return saturate(h / 0.15) * saturate((1.0 - h) / 0.25);
    if (type > 2.5) return saturate(h / 0.05) * saturate((1.0 - h) / 0.2);
    return saturate(h / 0.1) * saturate((1.0 - h) / 0.55);
}

float SmoothStepF(float a, float b, float x)
{
    float t = saturate((x - a) / (b - a));
    return t * t * (3.0 - 2.0 * t);
}

float StormCoverage(float2 xz)
{
    float c = 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        if (float(i) < _StormCount)
        {
            float4 s = _StormCells[i];
            float d = length(xz - s.xy);
            c = max(c, s.w * (1.0 - SmoothStepF(s.z * 0.35, s.z, d)));
        }
    }
    return c;
}

// Two rotated scales of the coverage map break up visible tiling. Mirrors CloudField.RawCoverage.
float RawCoverage(float2 xz)
{
    float a = OD_SAMPLE2D(_CloudCoverageTex, xz / _CoverageTile).r;
    float2 r = float2(xz.x * 0.8 - xz.y * 0.6, xz.x * 0.6 + xz.y * 0.8);
    float b = OD_SAMPLE2D(_CloudCoverageTex, r / (_CoverageTile * 4.37) + float2(0.31, 0.67)).r;
    return saturate(0.5 + (0.55 * a + 0.45 * b - 0.5) * 1.35);
}

// Mirrors CloudField.Density (C#). lod in [0, 1] fades the 3D shape noise toward its mean for
// distant samples, which removes minification shimmer (physics always uses lod 0).
float CloudDensity(float3 p, float alt, float lod)
{
    float2 xz = HorizontalLocal(p) - _CloudDrift;
    float total = 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        if (float(i) < _CloudLayerCount)
        {
            float4 A = _CloudLayerA[i];
            float4 B = _CloudLayerB[i];
            if (alt > A.x && alt < A.y)
            {
                float h = (alt - A.x) / (A.y - A.x);
                float cov;
                if (B.x > 2.5) cov = StormCoverage(xz);
                else
                {
                    float raw = RawCoverage(xz);
                    cov = saturate((raw - (1.0 - A.z)) / max(0.05, A.z) * 1.5);
                }
                float baseShape = cov * CloudHeightProfile(B.x, h);
                if (baseShape > 0.0)
                {
                    float n = 0.55;
                    if (lod < 0.999) n = lerp(OD_SAMPLE3D(_CloudShapeTex, float3(xz.x, alt, xz.y) / B.y).r, 0.55, lod);
                    float d = saturate((n - (1.0 - baseShape)) / max(0.05, baseShape));
                    total += d * A.w;
                }
            }
        }
    }
    return total;
}

struct CloudResult
{
    float3 scatter;       // in-scattered light (already attenuated by haze in front)
    float transmittance;  // view transmittance through the clouds
    float depth;          // transmittance-weighted mean depth (m)
};

// Volumetric cloud ray march between the camera and tMax (scene depth or planet).
CloudResult MarchClouds(float3 dir, float tMax, float jitter)
{
    CloudResult res;
    res.scatter = float3(0, 0, 0);
    res.transmittance = 1.0;
    res.depth = tMax;
    if (_CloudLayerCount < 0.5) return res;

    float2 top = RaySphereCam(dir, _CloudBandMax);
    float2 bot = RaySphereCam(dir, _CloudBandMin);
    float tStart, tEnd;
    if (_CamAlt > _CloudBandMax)
    {
        if (top.y <= 0.0) return res;
        tStart = max(top.x, 0.0);
        tEnd = (bot.y > 0.0 && bot.x > 0.0) ? bot.x : top.y;
    }
    else if (_CamAlt >= _CloudBandMin)
    {
        tStart = 0.0;
        tEnd = (bot.y > 0.0 && bot.x > 0.0) ? bot.x : top.y;
    }
    else
    {
        tStart = max(bot.y, 0.0);
        tEnd = top.y;
    }
    tEnd = min(tEnd, tMax);
    if (tEnd <= tStart) return res;

    const float kMaxMarch = 60000.0;
    float marchEnd = min(tEnd, tStart + kMaxMarch);
    float n = max(_CloudSteps, 4.0);
    float stepScale = 48.0 / n;
    float muSun = dot(dir, _SunDir);
    float phase = lerp(PhaseHG(muSun, 0.6), PhaseHG(muSun, -0.25), 0.25) * 4.0 * OD_PI;
    float camCloudAlt = clamp(_CamAlt, _CloudBandMin, _CloudBandMax);
    float3 sunCol = SunTransmittance(camCloudAlt, dot(_CamUp, _SunDir)) * _SunIntensity;
    float3 amb = SkyAmbient(camCloudAlt) * 1.4;
    float lightN = max(_LightSteps, 1.0);
    float weightSum = 0.0, depthSum = 0.0;

    // Distance-adaptive steps: fine near the camera, coarser with distance (pixel footprint grows).
    float t = tStart;
    float ds = clamp(t * 0.012, 35.0, 900.0) * stepScale;
    t += ds * jitter;
    float i = 0.0;
    [loop]
    for (i = 0.0; i < n; i += 1.0)
    {
        if (res.transmittance < 0.01 || t > marchEnd) break;
        ds = clamp(t * 0.012, 35.0, 900.0) * stepScale;
        float lod = saturate((ds - 120.0) / 500.0);
        float3 p = _CamLocal + dir * t;
        float alt = AltitudeLocal(p);
        float d = CloudDensity(p, alt, lod);
        if (d > 0.001)
        {
            float ext = d * _ExtinctionScale;
            // Light march toward the sun with growing steps.
            float od = 0.0;
            float ls = 90.0;
            float3 lp = p;
            [loop]
            for (float j = 0.0; j < lightN; j += 1.0)
            {
                lp += _SunDir * ls;
                od += CloudDensity(lp, AltitudeLocal(lp), max(lod, 0.5)) * ls;
                ls *= 2.4;
            }
            float sunVis = exp(-od * _ExtinctionScale);
            float powder = 1.0 - exp(-ext * 2.0 * 160.0);
            float hFrac = saturate((alt - _CloudBandMin) / max(1.0, _CloudBandMax - _CloudBandMin));
            float3 light = sunCol * sunVis * phase * lerp(0.6, 1.0, powder) + amb * (0.45 + 0.55 * hFrac);
            // Lightning lights the cloud from inside.
            float fl = _Flash.w / (1.0 + dot(p - _Flash.xyz, p - _Flash.xyz) * 2e-6);
            light += float3(0.75, 0.8, 1.0) * fl;
            float stepT = exp(-ext * ds);
            float3 integ = light * (1.0 - stepT);   // energy-conserving (scattering albedo ~ 1)
            res.scatter += res.transmittance * integ;
            float w = res.transmittance * (1.0 - stepT);
            weightSum += w; depthSum += w * t;
            res.transmittance *= stepT;
        }
        t += ds;
    }
    // Remainder of the band beyond the march: coarse coverage-only integration.
    if (t < tEnd && res.transmittance > 0.01)
    {
        float remain = tEnd - t;
        float segs = 6.0;
        float sl = remain / segs;
        float3 light = sunCol * 0.35 + amb;
        [loop]
        for (float k = 0.0; k < segs; k += 1.0)
        {
            float tm = t + sl * (k + 0.5);
            float3 p = _CamLocal + dir * tm;
            float d = CloudDensity(p, AltitudeLocal(p), 1.0);
            float slab = exp(-d * _ExtinctionScale * sl * 0.35);
            res.scatter += res.transmittance * light * (1.0 - slab);
            float w = res.transmittance * (1.0 - slab);
            weightSum += w; depthSum += w * tm;
            res.transmittance *= slab;
        }
    }
    if (weightSum > 1e-4) res.depth = depthSum / weightSum;
    return res;
}

// Cloud shadow toward the sun at a surface point (cheap: coverage-only, 3 samples).
float CloudShadow(float3 p)
{
    if (_CloudLayerCount < 0.5) return 1.0;
    float alt = AltitudeLocal(p);
    float od = 0.0;
    float mid = (_CloudBandMin + _CloudBandMax) * 0.5;
    [unroll]
    for (int k = 0; k < 3; k++)
    {
        float h = lerp(_CloudBandMin, _CloudBandMax, (k + 0.5) / 3.0);
        float t = (h - alt) / max(0.05, _SunDir.y);
        if (t > 0.0)
        {
            float3 q = p + _SunDir * t;
            od += CloudDensity(q, h, 1.0) * (_CloudBandMax - _CloudBandMin) / 3.0;
        }
    }
    return lerp(0.25, 1.0, exp(-od * _ExtinctionScale * 0.25));
}

// ---------------------------------------------------------------- sky background
float Hash31(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float3 Stars(float3 dir)
{
    float3 cell = floor(dir * 320.0);
    float h = Hash31(cell);
    float star = step(0.9975, h);
    float3 center = (cell + 0.5) / 320.0;
    float d = length(normalize(center) - dir) * 320.0;
    float intensity = pow(saturate(1.0 - d * 1.6), 3.0) * star * (0.4 + 3.0 * frac(h * 91.7));
    float3 tint = lerp(float3(0.8, 0.85, 1.0), float3(1.0, 0.9, 0.75), frac(h * 13.3));
    return tint * intensity * 0.35;
}

float3 SunDisc(float3 dir)
{
    float c = dot(dir, _SunDir);
    float disc = SmoothStepF(0.99996, 0.99999, c);
    float glow = pow(saturate(c), 2500.0);
    return float3(1.0, 0.96, 0.9) * (disc * 2000.0 + glow * 6.0);
}

float3 TonemapACES(float3 x)
{
    x *= _Exposure;
    const float a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
    return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
}

// ---------------------------------------------------------------- composition
float _OceanWorld;        // 1 on ocean levels (far-field planet is sea)

float3 ShadeFarGround(float3 p, float3 dir)
{
    float3 n = UpLocal(p);
    float cosSun = dot(n, _SunDir);
    float3 albedo = GroundAlbedo(HorizontalLocal(p), 0.0, 0.0, _OceanWorld);
    float3 sunT = SunTransmittance(0.0, cosSun) * _SunIntensity;
    float3 c = albedo * (sunT * saturate(cosSun) * CloudShadow(p) / OD_PI + SkyAmbient(0.0));
    // Specular sun glint on water.
    float sea = _OceanWorld > 0.5 ? 1.0 : saturate((0.32 - OD_SAMPLE2D(_CloudCoverageTex, HorizontalLocal(p) / (_CoverageTile * 11.0) + float2(0.37, 0.71)).r) * 12.0);
    float3 h = normalize(_SunDir - dir);
    float spec = pow(saturate(dot(n, h)), 600.0) * 60.0 + pow(saturate(dot(n, h)), 40.0) * 0.4;
    float fres = 0.02 + 0.98 * pow(1.0 - saturate(dot(n, -dir)), 5.0);
    c += sea * (sunT * spec * 0.02 + SkyAmbient(0.0) * fres * 2.0);
    return c;
}

// Low-resolution cloud pass output: rgb = cloud light (with haze in front), a = cloud transmittance.
float4 CloudPass(float3 dir, float tMax, float jitter)
{
    CloudResult c = MarchClouds(dir, tMax, jitter);
    if (c.transmittance > 0.999) return float4(0, 0, 0, 1);
    AtmoResult a = IntegrateAtmosphere(dir, c.depth, 4.0);
    float3 rgb = c.scatter * a.transmittance + a.inscatter * (1.0 - c.transmittance);
    return float4(rgb, c.transmittance);
}

// Full-resolution composition of scene, far planet, sky, sun, stars, atmosphere and clouds.
// hasScene: 1 where rasterised geometry (terrain, probe) covers the pixel.
float3 CompositeView(float3 dir, float sceneDist, float hasScene, float3 sceneColor, float4 cloud)
{
    float tEnd;
    float3 bg;
    if (hasScene > 0.5)
    {
        tEnd = sceneDist;
        bg = sceneColor;
    }
    else
    {
        float2 g = RaySphereCam(dir, 0.0);
        if (g.x > 0.0)
        {
            tEnd = g.x;
            bg = ShadeFarGround(_CamLocal + dir * g.x, dir);
        }
        else
        {
            tEnd = 1e9;
            bg = Stars(dir) + SunDisc(dir);
        }
    }
    AtmoResult a = IntegrateAtmosphere(dir, tEnd, _AtmoSteps);
    return (bg * a.transmittance + a.inscatter) * cloud.a + cloud.rgb;
}

#endif
