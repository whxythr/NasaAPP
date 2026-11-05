// Single-scattering atmosphere (Rayleigh + Mie), ray-marched per pixel on a shell drawn with front-face culling,
// so the SAME computation runs whether the camera is on the ground, in the upper atmosphere or in space —
// there is no switch between a "sky" and a "space" mode, hence no visual jump at any altitude.
//
// Output is composited as  result = inscatter + scene × transmittance  (Blend One SrcAlpha), which gives the
// blue sky, aerial perspective over distant terrain, the thin limb seen from orbit, and dims the stars.
// Ray length is limited by the scene depth (requires the camera depth texture) and by the planet sphere.
// Plain CG (no pipeline includes) → works in URP and the Built-in pipeline.
Shader "SpaceSim/Atmosphere"
{
    Properties
    {
        _SunIntensity ("Sun intensity", Float) = 22
        _Exposure ("Exposure", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Cull Front
            ZWrite Off
            ZTest Always
            Blend One SrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            float3 _CameraPlanetPos;   // camera relative to planet centre (Unity axes) [m]
            float  _CameraHeight;      // camera height above the reference radius, computed in double [m]
            float  _PlanetRadius;      // [m]
            float  _AtmosphereHeight;  // visual shell height [m]
            float3 _BetaRayleigh;      // [1/m] at the surface
            float  _BetaMie;           // scattering [1/m]
            float  _MieExtinction;     // extinction [1/m]
            float  _ScaleHeightR;      // [m]
            float  _ScaleHeightM;      // [m]
            float  _MieG;
            float3 _SunDir;            // towards the Sun, Unity world space
            float  _SunIntensity;
            float  _Exposure;
            float  _ViewSamples;
            float  _LightSamples;
            float  _UseDepth;
            float  _RenderScale;       // 1 = true size; < 1 when the body is drawn in scaled space
            float3 _HazeScattering;    // high haze layer [1/m] at its peak
            float  _HazeAltitude;      // [m]
            float  _HazeThickness;     // [m] (> 0)

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.screenPos = ComputeScreenPos(o.pos);
                // The shell writes no depth and ignores the depth test, so its clip depth only decides clipping: keep the
                // far side inside the far plane. Near the ground the camera's far plane is ~900 km (real-time shadows need
                // it), shorter than the line of sight to the shell along the horizon.
            #if defined(UNITY_REVERSED_Z)
                o.pos.z = max(o.pos.z, 1e-6 * o.pos.w);
            #else
                o.pos.z = min(o.pos.z, 0.999999 * o.pos.w);
            #endif
                return o;
            }

            float PhaseRayleigh(float mu) { return 3.0 / (16.0 * UNITY_PI) * (1.0 + mu * mu); }

            float PhaseMie(float mu, float g)
            {
                // Cornette–Shanks
                float g2 = g * g;
                float denom = pow(max(1.0 + g2 - 2.0 * g * mu, 1e-4), 1.5);
                return 3.0 / (8.0 * UNITY_PI) * ((1.0 - g2) * (1.0 + mu * mu)) / ((2.0 + g2) * denom);
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.worldPos - _WorldSpaceCameraPos);
                float3 o = _CameraPlanetPos;
                float R = _PlanetRadius;
                float Ha = _AtmosphereHeight;
                float h = _CameraHeight;

                // |o|² − r² is formed as (h − H)(2R + h + H) to avoid catastrophic cancellation at 6·10⁶ m.
                float b = dot(o, d);
                float cA = (h - Ha) * (2.0 * R + h + Ha);
                float discA = b * b - cA;
                if (discA <= 0.0) return float4(0, 0, 0, 1);
                float sA = sqrt(discA);
                float t0 = max(0.0, -b - sA);
                float t1 = -b + sA;
                if (t1 <= 0.0) return float4(0, 0, 0, 1);

                float cP = h * (2.0 * R + h);
                float discP = b * b - cP;
                if (discP > 0.0)
                {
                    float tP = -b - sqrt(discP);
                    if (tP > 0.0) t1 = min(t1, tP);
                }

                if (_UseDepth > 0.5)
                {
                    float2 uv = i.screenPos.xy / i.screenPos.w;
                    float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
                #if defined(UNITY_REVERSED_Z)
                    bool sky = raw <= 1e-7;
                #else
                    bool sky = raw >= 1.0 - 1e-7;
                #endif
                    if (!sky)
                    {
                        float eye = LinearEyeDepth(raw);
                        float3 camForward = -UNITY_MATRIX_V[2].xyz;
                        // Depth is in (possibly scaled) Unity units; the ray march works in metres.
                        float dist = eye / max(dot(d, camForward), 1e-4) / max(_RenderScale, 1e-12);
                        t1 = min(t1, dist);
                    }
                }
                if (t1 <= t0) return float4(0, 0, 0, 1);

                int viewSteps = (int)_ViewSamples;
                int lightSteps = (int)_LightSamples;
                float ds = (t1 - t0) / viewSteps;
                float3 sumR = 0;
                float3 sumM = 0;
                float3 sumH = 0;
                float odR = 0;
                float odM = 0;
                float odH = 0;
                float3 L = _SunDir;

                [loop]
                for (int s = 0; s < viewSteps; s++)
                {
                    float3 p = o + d * (t0 + (s + 0.5) * ds);
                    float hp = length(p) - R;
                    float dR = exp(-hp / _ScaleHeightR) * ds;
                    float dM = exp(-hp / _ScaleHeightM) * ds;
                    float zh = (hp - _HazeAltitude) / _HazeThickness;
                    float dH = exp(-zh * zh) * ds;
                    odR += dR;
                    odM += dM;
                    odH += dH;

                    // Is the Sun hidden by the planet from p?
                    float bl = dot(p, L);
                    float cPl = hp * (2.0 * R + hp);
                    if (bl < 0.0 && bl * bl - cPl > 0.0) continue;

                    float cAl = (hp - Ha) * (2.0 * R + hp + Ha);
                    float tl = -bl + sqrt(max(0.0, bl * bl - cAl));
                    float dsl = tl / lightSteps;
                    float lR = 0;
                    float lM = 0;
                    float lH = 0;
                    [loop]
                    for (int k = 0; k < lightSteps; k++)
                    {
                        float3 q = p + L * ((k + 0.5) * dsl);
                        float hq = length(q) - R;
                        float zq = (hq - _HazeAltitude) / _HazeThickness;
                        lR += exp(-hq / _ScaleHeightR) * dsl;
                        lM += exp(-hq / _ScaleHeightM) * dsl;
                        lH += exp(-zq * zq) * dsl;
                    }
                    float3 tau = _BetaRayleigh * (odR + lR) + _MieExtinction * (odM + lM) + _HazeScattering * (odH + lH);
                    float3 att = exp(-tau);
                    sumR += att * dR;
                    sumM += att * dM;
                    sumH += att * dH;
                }

                float mu = dot(d, L);
                float3 inscatter = _SunIntensity * ((sumR * _BetaRayleigh + sumH * _HazeScattering) * PhaseRayleigh(mu)
                                                    + sumM * _BetaMie * PhaseMie(mu, _MieG));
                float3 transmittance = exp(-(_BetaRayleigh * odR + _MieExtinction * odM + _HazeScattering * odH));

                float3 color = 1.0 - exp(-inscatter * _Exposure);
                float alpha = dot(transmittance, float3(0.3333, 0.3333, 0.3334));
                return float4(color, alpha);
            }
            ENDCG
        }
    }
}
