// Star field at infinity. Each star is a screen-space quad around a fixed direction in the inertial frame
// (Unity world axes), projected with the camera's rotation only — so stars never move with camera translation
// and stay fixed relative to inertial space. Drawn first (Background queue); later passes overwrite/attenuate it.
Shader "SpaceSim/Starfield"
{
    Properties
    {
        _Brightness ("Brightness (visibility)", Float) = 1
        _PixelSize ("Star size [px]", Float) = 1.6
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "IgnoreProjector" = "True" "PreviewType" = "Skybox" }

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Brightness;
            float _PixelSize;

            struct appdata
            {
                float4 vertex : POSITION;   // star direction
                float4 color : COLOR;       // colour × relative flux
                float2 uv : TEXCOORD0;      // quad corner (−1..1)
                float2 size : TEXCOORD1;    // x = size multiplier
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 dir = normalize(v.vertex.xyz);
                float4 clip = mul(UNITY_MATRIX_VP, float4(dir, 0.0)); // w = 0: rotation only, point at infinity
            #if defined(UNITY_REVERSED_Z)
                clip.z = 1e-6 * clip.w;
            #else
                clip.z = 0.999999 * clip.w;
            #endif
                float2 pixel = 2.0 / _ScreenParams.xy;
                clip.xy += v.uv * pixel * _PixelSize * v.size.x * clip.w;
                o.pos = clip;
                o.color = v.color * _Brightness;
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float r2 = dot(i.uv, i.uv);
                float a = exp(-r2 * 3.0) * step(r2, 1.0);
                return float4(i.color.rgb * a, 0);
            }
            ENDCG
        }
    }
}
