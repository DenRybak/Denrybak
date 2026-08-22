Shader "BallisticSniper/GradientSky"
{
    Properties
    {
        _HorizonColor ("Horizon", Color) = (0.30,0.40,0.46,1)
        _ZenithColor ("Zenith", Color) = (0.055,0.14,0.30,1)
        _GroundColor ("Ground", Color) = (0.12,0.14,0.14,1)
        _SunColor ("Sun", Color) = (1.0,0.90,0.75,1)
        _SunDirection ("Sun Direction", Vector) = (0,0.7,-0.7,0)
        _SunIntensity ("Sun Intensity", Range(0,2)) = 0.42
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
            #pragma target 2.0
            #include "UnityCG.cginc"

            fixed4 _HorizonColor;
            fixed4 _ZenithColor;
            fixed4 _GroundColor;
            fixed4 _SunColor;
            float4 _SunDirection;
            half _SunIntensity;

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
                output.direction = mul((float3x3)unity_ObjectToWorld, input.vertex.xyz);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                half3 direction = normalize(input.direction);
                half altitude = direction.y;
                half skyBlend = pow(saturate(altitude), 0.42h);
                half groundBlend = pow(saturate(-altitude), 0.32h);
                half3 colour = altitude >= 0.0h
                    ? lerp(_HorizonColor.rgb, _ZenithColor.rgb, skyBlend)
                    : lerp(_HorizonColor.rgb, _GroundColor.rgb, groundBlend);

                half haze = exp2(-abs(altitude) * 13.0h);
                colour = lerp(colour, _HorizonColor.rgb, haze * 0.16h);

                half sunDot = saturate(dot(direction, normalize(_SunDirection.xyz)));
                half halo = pow(sunDot, 96.0h) * 0.10h;
                half disc = pow(sunDot, 1024.0h);
                colour += _SunColor.rgb * (halo + disc) * _SunIntensity;
                return fixed4(colour, 1.0h);
            }
            ENDCG
        }
    }

    Fallback Off
}
