Shader "Hidden/BallisticSniper/SceneGrade"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Exposure ("Exposure", Range(0.5, 2.0)) = 1.12
        _Contrast ("Contrast", Range(0.5, 1.5)) = 1.075
        _Saturation ("Saturation", Range(0.0, 2.0)) = 1.10
        _Vignette ("Vignette", Range(0.0, 0.5)) = 0.15
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
            half _Exposure;
            half _Contrast;
            half _Saturation;
            half _Vignette;

            fixed4 frag(v2f_img input) : SV_Target
            {
                half3 colour = tex2D(_MainTex, input.uv).rgb;
                colour *= _Exposure;
                colour = (colour - 0.5h) * _Contrast + 0.5h;

                half luminance = dot(colour, half3(0.2126h, 0.7152h, 0.0722h));
                colour = lerp(luminance.xxx, colour, _Saturation);

                half2 centred = input.uv * 2.0h - 1.0h;
                half edge = saturate(dot(centred, centred) * 0.52h);
                colour *= 1.0h - edge * _Vignette;

                // A restrained warm highlight / cool shadow split keeps the
                // range readable without flattening its time-of-day lighting.
                half highlight = saturate(luminance * 1.35h);
                colour *= lerp(half3(0.95h, 0.98h, 1.04h), half3(1.04h, 1.01h, 0.96h), highlight);
                return fixed4(saturate(colour), 1.0h);
            }
            ENDCG
        }
    }

    Fallback Off
}
