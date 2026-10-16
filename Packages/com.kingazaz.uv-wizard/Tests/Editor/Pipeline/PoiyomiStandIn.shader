// A stand-in for Poiyomi Toon, which CI's project lacks: its name puts it under PoiyomiReader, which reads each map
// with its own _ST, so a test can tile the emission map differently from the albedo. It declares only the properties
// those tests set, and draws the main texture unlit.
Shader ".poiyomi/UV Wizard Tests/Stand-in"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _EnableEmission ("Emission", Float) = 0
        _EmissionMap ("Emission Map", 2D) = "white" {}
        _EmissionColor ("Emission Colour", Color) = (1, 1, 1, 1)
        _EmissionStrength ("Emission Strength", Float) = 0
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * _Color;
            }
            ENDCG
        }
    }
}
