Shader "Hidden/CameraCaptureStudio/Text"
{
    Properties { _MainTex ("Font Atlas", 2D) = "white" {} _Color ("Text Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Cull Off Lighting Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                fixed alpha = tex2D(_MainTex, input.uv).a;
                return fixed4(_Color.rgb, _Color.a * alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
