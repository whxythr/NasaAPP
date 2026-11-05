// Engine exhaust plume: additive, emissive, no lighting. The cone mesh carries uv.y = 0 at the nozzle → 1 at the tip and
// uv.x around the axis. Colour runs from a white-yellow core through orange to transparent; a faint flicker breaks the
// regularity. Shape (length, expansion in thin air) is set by the script from throttle and ambient pressure.
Shader "SpaceSim/EnginePlume"
{
    Properties
    {
        _Intensity ("Intensity (throttle)", Float) = 1
        _CoreColor ("Core colour", Color) = (1.0, 0.92, 0.70, 1)
        _EdgeColor ("Edge colour", Color) = (1.0, 0.45, 0.12, 1)
        _Vacuum ("Vacuum (0 = sea level, 1 = vacuum)", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite Off
            Cull Off
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Intensity;
            float4 _CoreColor;
            float4 _EdgeColor;
            float _Vacuum;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float facing : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float3 view = normalize(WorldSpaceViewDir(v.vertex));
                o.facing = abs(dot(n, view)); // 1 at the plume's centre line, 0 at its silhouette
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float along = i.uv.y;
                float flicker = 0.85 + 0.15 * sin(_Time.y * 43.0 + along * 31.0) * sin(_Time.y * 17.0 + i.uv.x * 40.0);
                // Bright near the nozzle, fading to the tip; in vacuum the plume is wider and dimmer.
                float fade = pow(saturate(1.0 - along), 1.6 + 1.4 * _Vacuum);
                float core = pow(i.facing, 2.0);
                float3 colour = lerp(_EdgeColor.rgb, _CoreColor.rgb, saturate(core * (1.0 - along)));
                float a = fade * (0.25 + 0.75 * core) * flicker * _Intensity * (1.0 - 0.55 * _Vacuum);
                return float4(colour * a * 2.2, a);
            }
            ENDCG
        }
    }
}
