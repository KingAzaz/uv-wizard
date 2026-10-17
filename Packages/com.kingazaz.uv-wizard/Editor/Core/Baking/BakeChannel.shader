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
        _SourceEncoding ("Normal encoding: 0 RG or AG, 1 AG, 2 XYZ", Float) = 0
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

        // Normal: the source decoded at its strength the way UnpackScaleNormal decodes it, and written as XYZ into a
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
                bool rgOrAg = _SourceEncoding < 0.5;
                bool ag = _SourceEncoding > 0.5 && _SourceEncoding < 1.5;
                bool xyz = _SourceEncoding > 1.5;
                float x = ag ? packed.a : xyz ? packed.r : packed.r * packed.a;
                // As the device's UnpackScaleNormal does (UnityStandardUtils.cginc, UnpackScaleNormalRGorAG). For
                // RG or AG it scales x and y, then rebuilds z from them; for AG, the mobile ASTC branch, it rebuilds
                // z first and scales after; for XYZ, the mobile UNITY_NO_DXT5nm branch, it keeps the stored z and
                // never reads alpha. Either way the vector may be longer than 1, so it's normalized for the merged
                // shader to read at strength 1, and one of length 0 bakes flat.
                float2 xy = float2(x, packed.g) * 2 - 1;
                float z = xyz ? packed.b * 2 - 1 : sqrt(1 - saturate(dot(xy, xy)));
                xy *= _Strength;
                if (rgOrAg)
                    z = sqrt(1 - saturate(dot(xy, xy)));
                float3 n = float3(xy, z);
                n = dot(n, n) > 0 ? normalize(n) : float3(0, 0, 1);
                return float4(n * 0.5 + 0.5, 1);
            }
            ENDCG
        }
    }
}
