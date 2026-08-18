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
        _EmissionColor ("Emission", Color) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 250

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        float4 _AtlasCell;
        float4 _Tiling;
        half _Metallic;
        half _Glossiness;
        fixed4 _EmissionColor;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 localUv = frac(IN.uv_MainTex * max(_Tiling.xy, float2(0.001, 0.001)));
            localUv = lerp(float2(0.004, 0.004), float2(0.996, 0.996), localUv);
            float2 atlasUv = _AtlasCell.xy + localUv * _AtlasCell.zw;
            fixed4 sampled = tex2D(_MainTex, atlasUv) * _Color;
            o.Albedo = sampled.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Emission = _EmissionColor.rgb;
            o.Alpha = sampled.a;
        }
        ENDCG
    }
    FallBack "Standard"
}
