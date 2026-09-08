Shader "BallisticSniper/AtlasLit"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("4x4 Material Atlas", 2D) = "white" {}
        _AtlasCell ("Cell: Offset XY, Scale ZW", Vector) = (0,0,1,1)
        _Tiling ("Local Tiling", Vector) = (1,1,0,0)
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.25
        _NormalStrength ("Generated Normal Strength", Range(0,4)) = 1.35
        _EmissionColor ("Emission", Color) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 250

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        #pragma multi_compile_instancing

        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        fixed4 _Color;
        float4 _AtlasCell;
        float4 _Tiling;
        half _Metallic;
        half _Glossiness;
        half _NormalStrength;
        fixed4 _EmissionColor;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 localUv = frac(IN.uv_MainTex * max(_Tiling.xy, float2(0.001, 0.001)));
            localUv = lerp(float2(0.006, 0.006), float2(0.994, 0.994), localUv);
            float2 atlasUv = _AtlasCell.xy + localUv * _AtlasCell.zw;
            fixed4 sampled = tex2D(_MainTex, atlasUv) * _Color;

            float2 texel = _MainTex_TexelSize.xy;
            half leftHeight = dot(tex2D(_MainTex, atlasUv - float2(texel.x, 0)).rgb, half3(0.299, 0.587, 0.114));
            half rightHeight = dot(tex2D(_MainTex, atlasUv + float2(texel.x, 0)).rgb, half3(0.299, 0.587, 0.114));
            half downHeight = dot(tex2D(_MainTex, atlasUv - float2(0, texel.y)).rgb, half3(0.299, 0.587, 0.114));
            half upHeight = dot(tex2D(_MainTex, atlasUv + float2(0, texel.y)).rgb, half3(0.299, 0.587, 0.114));
            half3 detailNormal = normalize(half3(
                (leftHeight - rightHeight) * _NormalStrength,
                (downHeight - upHeight) * _NormalStrength,
                1.0h));

            o.Albedo = sampled.rgb;
            o.Normal = detailNormal;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Occlusion = saturate(0.76h + dot(sampled.rgb, half3(0.12h, 0.24h, 0.04h)));
            o.Emission = _EmissionColor.rgb;
            o.Alpha = sampled.a;
        }
        ENDCG
    }
    FallBack "Standard"
}
