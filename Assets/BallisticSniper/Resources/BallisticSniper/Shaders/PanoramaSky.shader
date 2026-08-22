Shader "BallisticSniper/PanoramaSky"
{
    Properties
    {
        _PanoramaTex ("Cinematic Panorama", 2D) = "gray" {}
        _Tint ("Atmosphere Tint", Color) = (1,1,1,1)
        _Exposure ("Exposure", Range(0.25, 2.0)) = 1.0
        _Rotation ("Rotation", Range(0, 360)) = 0
        _HorizonBoost ("Horizon Detail", Range(0, 0.3)) = 0.1
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _PanoramaTex;
            half4 _Tint;
            half _Exposure;
            half _Rotation;
            half _HorizonBoost;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.direction = input.vertex.xyz;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float2 uv;
                uv.x = frac(atan2(direction.x, direction.z) / (2.0 * UNITY_PI) + 0.5 + _Rotation / 360.0);
                uv.y = saturate(asin(clamp(direction.y, -1.0, 1.0)) / UNITY_PI + 0.5);
                half3 colour = tex2D(_PanoramaTex, uv).rgb;
                half horizon = 1.0h - saturate(abs(direction.y) * 4.0h);
                colour *= _Tint.rgb * _Exposure;
                colour = lerp(colour, colour * (1.0h + _HorizonBoost), horizon);
                return fixed4(colour, 1.0h);
            }
            ENDCG
        }
    }
    Fallback Off
}
