#ifndef SPACESIM_LIGHTING_INCLUDED
#define SPACESIM_LIGHTING_INCLUDED

// Shared, pipeline-independent lighting helpers (plain HLSL, no pipeline includes).
// Globals are set every frame by SolarLightingController / AtmosphereRenderer.

float3 _SpaceSimSunDir;          // unit vector towards the Sun (Unity world space)
float3 _SpaceSimSunColor;        // unattenuated sunlight (linear)
float3 _SpaceSimPlanetCenter;    // planet centre (Unity world space, origin-relative)
float  _SpaceSimPlanetRadius;    // reference radius [m]
float3 _SpaceSimBetaRayleigh;    // Rayleigh scattering at the surface [1/m]
float  _SpaceSimMieExtinction;   // Mie extinction at the surface [1/m]
float  _SpaceSimScaleHeightR;    // [m]
float  _SpaceSimScaleHeightM;    // [m]
float  _SpaceSimHasAtmosphere;   // 0 or 1
float3 _SpaceSimSkyAmbient;      // ambient sky light colour at full daylight

// Relative air mass along the direction to the Sun (Kasten & Young 1989), valid up to ~96° zenith.
float SpaceSimAirMass(float cosZenith)
{
    float zDeg = degrees(acos(clamp(cosZenith, -0.1, 1.0)));
    zDeg = min(zDeg, 95.9);
    return 1.0 / (max(cosZenith, -0.1) + 0.50572 * pow(96.07995 - zDeg, -1.6364));
}

// Spectral transmittance of sunlight reaching a point at height h whose local "up" makes cosZenith with the Sun.
float3 SpaceSimSunTransmittance(float h, float cosZenith)
{
    if (_SpaceSimHasAtmosphere < 0.5) return float3(1, 1, 1);
    float m = SpaceSimAirMass(cosZenith);
    float odR = _SpaceSimScaleHeightR * exp(-max(h, 0.0) / _SpaceSimScaleHeightR);
    float odM = _SpaceSimScaleHeightM * exp(-max(h, 0.0) / _SpaceSimScaleHeightM);
    return exp(-(_SpaceSimBetaRayleigh * odR + _SpaceSimMieExtinction * odM) * m);
}

float3 SpaceSimToLinear(float3 c)
{
#if defined(UNITY_COLORSPACE_GAMMA)
    return c;
#else
    return c * (c * (c * 0.305306011 + 0.682171111) + 0.012522878); // fast sRGB → linear
#endif
}

// Diffuse terrain + specular ocean. waterMask = 1 for water. shadow = real-time shadow attenuation (1 = lit).
float3 SpaceSimShadeSurface(float3 positionWS, float3 normalWS, float3 albedoSRGB, float waterMask,
                            float3 viewDirWS, float shadow, float ambientFloor, float waterSpecular)
{
    float3 albedo = SpaceSimToLinear(albedoSRGB);
    float3 toCenter = positionWS - _SpaceSimPlanetCenter;
    float r = length(toCenter);
    float3 up = toCenter / max(r, 1e-3);
    float h = r - _SpaceSimPlanetRadius;
    float3 L = _SpaceSimSunDir;
    float cosZ = dot(up, L);

    float3 sun = _SpaceSimSunColor * SpaceSimSunTransmittance(h, cosZ);
    // Geometric terminator: the Sun sets below the local horizon of the sphere.
    float sunVisible = smoothstep(-0.015, 0.015, cosZ);

    float3 N = normalize(lerp(normalWS, up, waterMask * 0.85));
    float ndl = saturate(dot(N, L));
    float3 diffuse = albedo * sun * ndl * sunVisible * shadow;

    // Sky light: scales with how much of the sky above is sunlit (crude, visual only).
    float skyDay = saturate(cosZ * 2.5 + 0.25) * _SpaceSimHasAtmosphere;
    float3 ambient = albedo * (_SpaceSimSkyAmbient * skyDay * saturate(0.5 + 0.5 * dot(normalWS, up)) + ambientFloor);

    float3 specular = 0;
    if (waterMask > 0.5)
    {
        float3 H = normalize(L + viewDirWS);
        float ndh = saturate(dot(up, H));
        float fresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(up, viewDirWS)), 5.0);
        specular = sun * sunVisible * shadow * waterSpecular * (pow(ndh, 400.0) * 40.0 + pow(ndh, 40.0) * 0.4) * fresnel;
    }
    return diffuse + ambient + specular;
}

#endif
