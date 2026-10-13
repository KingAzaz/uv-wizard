// Draws a source's triangles at their atlas UVs (the vertex positions) and samples the source
// texture at its own UVs, through its own sampler, times the tint. Alpha is 1 so the baker can
// tell covered texels from empty ones; the merged material is opaque anyway (ADR 0004).
Shader "Hidden/UVWizard/BakeAlbedo"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _SourceST ("Source scale and offset", Vector) = (1, 1, 0, 0)
    }
    SubShader
    {
        // Mirrored charts arrive with flipped winding.
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
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

            float4 frag(v2f i) : SV_Target
            {
                return float4(tex2D(_MainTex, i.uv).rgb * _Color.rgb, 1);
            }
            ENDCG
        }
    }
}
