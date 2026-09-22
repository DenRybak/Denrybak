Shader "BallisticSniper/TracerTrail"
{
    Properties
    {
        _Color ("Tint", Color) = (1.0, 0.55, 0.16, 0.55)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float across = 1.0 - abs(i.uv.y * 2.0 - 1.0);
                float softCore = smoothstep(0.0, 0.62, across);
                fixed4 c = i.color;
                c.a *= softCore;
                return c;
            }
            ENDCG
        }
    }

    FallBack Off
}
