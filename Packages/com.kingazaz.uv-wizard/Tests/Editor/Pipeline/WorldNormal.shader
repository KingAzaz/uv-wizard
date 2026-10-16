// Writes each pixel's world-space normal, as n * 0.5 + 0.5, with its normal map applied the way Standard and
// Toon Standard apply it: decoded by UnpackScaleNormal and turned to world space through the mesh's own normals
// and tangents. Two renders that match here light the same under any light.
Shader "Hidden/UVWizard/Tests/WorldNormal"
{
    Properties
    {
        _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Strength", Float) = 1
    }
    SubShader
    {
        // Depth-tested, so overlapping surfaces show the nearest whatever order they're drawn in.
        Cull Off
        ZWrite On
        ZTest LEqual
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // As Standard and Toon Standard do. Below 3.0, UnpackScaleNormal ignores the strength.
            #pragma target 3.0
            #include "UnityStandardUtils.cginc"

            sampler2D _BumpMap;
            float4 _BumpMap_ST;
            float _BumpScale;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 tangent : TEXCOORD1;
                float3 binormal : TEXCOORD2;
                float3 normal : TEXCOORD3;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _BumpMap);
                float3x3 toWorld = CreateTangentToWorldPerVertex(UnityObjectToWorldNormal(v.normal),
                    UnityObjectToWorldDir(v.tangent.xyz), v.tangent.w);
                o.tangent = toWorld[0];
                o.binormal = toWorld[1];
                o.normal = toWorld[2];
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 n = UnpackScaleNormal(tex2D(_BumpMap, i.uv), _BumpScale);
                float3 world = normalize(i.tangent * n.x + i.binormal * n.y + i.normal * n.z);
                return float4(world * 0.5 + 0.5, 1);
            }
            ENDCG
        }
    }
}
