Shader "Hidden/BallisticSniper/SceneGrade"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Exposure ("Exposure", Range(0.5, 2.0)) = 1.04
        _Contrast ("Contrast", Range(0.5, 1.5)) = 1.02
        _Saturation ("Saturation", Range(0.0, 2.0)) = 0.94
        _Vignette ("Vignette", Range(0.0, 0.5)) = 0.10
        _Sharpness ("Clarity", Range(0.0, 1.0)) = 0.42
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            half _Exposure;
            half _Contrast;
            half _Saturation;
            half _Vignette;
            half _Sharpness;

            fixed4 frag(v2f_img input) : SV_Target
            {
                half3 centre = tex2D(_MainTex, input.uv).rgb;
                float2 texel = _MainTex_TexelSize.xy;
                half3 neighbours =
                    tex2D(_MainTex, input.uv + float2(texel.x, 0.0)).rgb +
                    tex2D(_MainTex, input.uv - float2(texel.x, 0.0)).rgb +
                    tex2D(_MainTex, input.uv + float2(0.0, texel.y)).rgb +
                    tex2D(_MainTex, input.uv - float2(0.0, texel.y)).rgb;
                half3 clarity = centre + (centre - neighbours * 0.25h) * _Sharpness;
                half3 hdr = max(clarity, 0.0h);

                // Compress HDR highlights before grading. The old linear
                // multiply clipped the sky and pushed soil into neon orange.
                half3 colour = 1.0h - exp(-hdr * _Exposure);
                colour = (colour - 0.18h) * _Contrast + 0.18h;

                half luminance = dot(colour, half3(0.2126h, 0.7152h, 0.0722h));
                colour = lerp(luminance.xxx, colour, _Saturation);

                half2 centred = input.uv * 2.0h - 1.0h;
                half edge = saturate(dot(centred, centred) * 0.52h);
                colour *= 1.0h - edge * _Vignette;

                return fixed4(saturate(colour), 1.0h);
            }
            ENDCG
        }
    }

    Fallback Off
}
