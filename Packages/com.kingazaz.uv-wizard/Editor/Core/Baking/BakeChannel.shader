// Draws a source's triangles at their atlas UVs (the vertex positions) and samples the source
// texture at its own UVs, through its own sampler. The first pass writes colour, times the tint;
// the second writes the tangent-space normal a normal map holds. Alpha is 1 so the baker can tell
// covered texels from empty ones; the merged material is opaque anyway (ADR 0004).
Shader "Hidden/UVWizard/BakeChannel"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _SourceST ("Source scale and offset", Vector) = (1, 1, 0, 0)
        _Strength ("Normal strength", Float) = 1
        _SourceEncoding ("Normal encoding: 0 RG or AG, 1 AG", Float) = 0
    }
    SubShader
    {
        // Mirrored charts arrive with flipped winding.
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float4 _Color;
        float4 _SourceST;

        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        v2f vert(appdata v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.uv * _SourceST.xy + _SourceST.zw;
            return o;
        }
        ENDCG

        // Colour: the source times the tint.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 frag(v2f i) : SV_Target
            {
                return float4(tex2D(_MainTex, i.uv).rgb * _Color.rgb, 1);
            }
            ENDCG
        }

        // Normal: the source decoded, its x and y scaled by the strength, renormalized, and written as XYZ into a
        // linear target. Never tinted.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float _Strength;
            float _SourceEncoding;

            float4 frag(v2f i) : SV_Target
            {
                float4 packed = tex2D(_MainTex, i.uv);
                float x = _SourceEncoding > 0.5 ? packed.a : packed.r * packed.a;
                float2 xy = float2(x, packed.g) * 2 - 1;
                float3 n = float3(xy * _Strength, sqrt(1 - saturate(dot(xy, xy))));
                // Strength 0 on a texel lying flat along x and y leaves nothing to normalize: that's flat.
                n = dot(n, n) > 0 ? normalize(n) : float3(0, 0, 1);
                return float4(n * 0.5 + 0.5, 1);
            }
            ENDCG
        }
    }
}
