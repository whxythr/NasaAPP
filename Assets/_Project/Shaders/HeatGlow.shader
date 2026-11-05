// Re-entry / ascent heating glow around the nose: additive shell whose brightness the script sets from the Sutton–Graves
// heat flux. Colour follows the equilibrium temperature (dull red → orange → yellow-white).
Shader "SpaceSim/HeatGlow"
{
    Properties
    {
        _Color ("Colour", Color) = (1.0, 0.45, 0.15, 1)
        _Intensity ("Intensity", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite Off
            Cull Back
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Intensity;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 view : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.view = WorldSpaceViewDir(v.vertex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float facing = saturate(dot(normalize(i.normal), normalize(i.view)));
                float a = _Intensity * (0.35 * pow(facing, 0.5) + 0.65 * pow(1.0 - facing, 2.0));
                return float4(_Color.rgb * a, a);
            }
            ENDCG
        }
    }
}
