Shader "BallisticSniper/TransparentLit"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,0.7)
        _MainTex ("4x4 Material Atlas", 2D) = "white" {}
        _AtlasCell ("Cell: Offset XY, Scale ZW", Vector) = (0,0,1,1)
        _Tiling ("Local Tiling", Vector) = (1,1,0,0)
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.82
        [HideInInspector] _SrcBlend ("Source Blend", Float) = 5
        [HideInInspector] _DstBlend ("Destination Blend", Float) = 10
        [HideInInspector] _ZWrite ("Depth Write", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 220
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]

        CGPROGRAM
        #pragma surface surf Standard alpha:fade fullforwardshadows
        #pragma target 3.0
        #pragma multi_compile_instancing

        sampler2D _MainTex;
        fixed4 _Color;
        float4 _AtlasCell;
        float4 _Tiling;
        half _Metallic;
        half _Glossiness;

        struct Input
        {
            float2 uv_MainTex;
            float3 viewDir;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 localUv = frac(IN.uv_MainTex * max(_Tiling.xy, float2(0.001, 0.001)));
            localUv = lerp(float2(0.006, 0.006), float2(0.994, 0.994), localUv);
            float2 atlasUv = _AtlasCell.xy + localUv * _AtlasCell.zw;
            fixed4 sampled = tex2D(_MainTex, atlasUv) * _Color;
            half fresnel = pow(1.0h - saturate(dot(normalize(IN.viewDir), half3(0, 0, 1))), 3.0h);

            o.Albedo = sampled.rgb * lerp(0.58h, 0.92h, fresnel);
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Emission = sampled.rgb * fresnel * 0.12h;
            o.Alpha = saturate(sampled.a * (0.70h + fresnel * 0.30h));
        }
        ENDCG
    }
    FallBack Off
}
