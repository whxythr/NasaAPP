// Launch smoke puff: a sphere drawn as a soft, alpha-blended billboard-like blob (thick in the middle, fading towards its
// silhouette), shaded by the star's direction. Purely visual.
Shader "SpaceSim/Smoke"
{
    Properties
    {
        _Color ("Colour", Color) = (0.85, 0.84, 0.82, 1)
        _Alpha ("Opacity", Float) = 0.6
        _LightDir ("Light direction (world)", Vector) = (0, 1, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite Off
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Alpha;
            float4 _LightDir;

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
                float3 n = normalize(i.normal);
                float facing = saturate(dot(n, normalize(i.view)));
                float light = 0.55 + 0.45 * saturate(dot(n, normalize(_LightDir.xyz)));
                float a = _Alpha * pow(facing, 3.0); // soft edges: overlapping puffs read as one cloud
                return float4(_Color.rgb * light, a);
            }
            ENDCG
        }
    }
}
