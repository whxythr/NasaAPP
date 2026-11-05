// Procedural ground detail for close range (no textures needed). Coordinates are planet-fixed metres modulo
// SPACESIM_DETAIL_PERIOD (see CoordinateSystem.FillMesh): every noise below repeats exactly with that period, so chunks join
// seamlessly. Detail fades out with distance, where the terrain mesh itself is too coarse to carry it.
#ifndef SPACESIM_DETAIL_INCLUDED
#define SPACESIM_DETAIL_INCLUDED

#define SPACESIM_DETAIL_PERIOD 4096.0

float SSHash(float3 c)
{
    c = frac(c * float3(0.1031, 0.1030, 0.0973));
    c += dot(c, c.yzx + 33.33);
    return frac((c.x + c.y) * c.z);
}

// Value noise on a lattice of 'cell' metres, periodic with SPACESIM_DETAIL_PERIOD (period / cell must be an integer).
float SSNoise(float3 x, float cell)
{
    float period = SPACESIM_DETAIL_PERIOD / cell;
    float3 p = x / cell;
    float3 i = floor(p);
    float3 f = p - i;
    float3 u = f * f * (3.0 - 2.0 * f);
    float3 a = i - period * floor(i / period);
    float3 b = (i + 1.0) - period * floor((i + 1.0) / period);
    float n000 = SSHash(float3(a.x, a.y, a.z)), n100 = SSHash(float3(b.x, a.y, a.z));
    float n010 = SSHash(float3(a.x, b.y, a.z)), n110 = SSHash(float3(b.x, b.y, a.z));
    float n001 = SSHash(float3(a.x, a.y, b.z)), n101 = SSHash(float3(b.x, a.y, b.z));
    float n011 = SSHash(float3(a.x, b.y, b.z)), n111 = SSHash(float3(b.x, b.y, b.z));
    float x00 = lerp(n000, n100, u.x), x10 = lerp(n010, n110, u.x);
    float x01 = lerp(n001, n101, u.x), x11 = lerp(n011, n111, u.x);
    return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
}

float SSFbm(float3 x, float cell)
{
    float s = 0.0, w = 0.5;
    [unroll] for (int o = 0; o < 4; o++)
    {
        s += w * SSNoise(x, cell);
        cell *= 0.5;
        w *= 0.5;
    }
    return s / 0.9375;
}

// Adds close-range variation to the biome albedo and bumps the normal. 'fixedPos' = planet-fixed position mod period,
// 'normal' = OBJECT-space (planet-fixed axes) normal, 'dist' = distance to the camera [m], 'water' = water mask.
void SpaceSimGroundDetail(float3 fixedPos, float dist, float water, inout float3 albedo, inout float3 normal)
{
    if (water > 0.5) return;
    float fadeMacro = saturate(1.0 - dist / 4000.0);
    float fadeMid = saturate(1.0 - dist / 400.0);
    float fadeFine = saturate(1.0 - dist / 50.0);
    if (fadeMacro <= 0.0) return;

    float macro = SSFbm(fixedPos, 64.0);          // patches of tens of metres
    float mid = SSFbm(fixedPos, 4.0);             // clumps, stones
    float fine = fadeFine > 0.0 ? SSNoise(fixedPos, 0.25) : 0.5; // grain, pebbles

    float lum = dot(albedo, float3(0.333, 0.333, 0.334));
    bool greenish = albedo.g > albedo.r * 1.05 && albedo.g > albedo.b;
    // Vegetation: darker clumps and lighter dry patches; bare ground: gentler variation.
    float amp = greenish ? 0.45 : 0.30;
    float v = (macro - 0.5) * 0.6 * fadeMacro + (mid - 0.5) * amp * fadeMid + (fine - 0.5) * 0.25 * fadeFine;
    albedo *= saturate(1.0 + v);
    if (greenish) albedo = lerp(albedo, albedo * float3(1.08, 1.0, 0.75), saturate((macro - 0.55) * 3.0) * fadeMacro); // dry, yellowish patches

    // Bump from the mid layer: finite differences in a planet-fixed frame, projected onto the surface.
    if (fadeMid > 0.0)
    {
        const float e = 0.5;
        float c0 = SSFbm(fixedPos, 4.0);
        float3 grad = float3(SSFbm(fixedPos + float3(e, 0, 0), 4.0) - c0,
                             SSFbm(fixedPos + float3(0, e, 0), 4.0) - c0,
                             SSFbm(fixedPos + float3(0, 0, e), 4.0) - c0) / e;
        grad -= normal * dot(grad, normal);
        normal = normalize(normal - grad * 3.0 * fadeMid);
    }
}

#endif
