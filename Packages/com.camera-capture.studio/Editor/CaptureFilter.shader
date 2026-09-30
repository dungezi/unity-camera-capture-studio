Shader "Hidden/CameraCaptureStudio/Filter"
{
    Properties { _MainTex ("Source", 2D) = "white" {} _Preset ("Preset", Float) = 0 _PreserveAlpha ("Preserve Alpha", Float) = 0 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Preset;
            float _PreserveAlpha;

            float3 Contrast(float3 color, float strength)
            {
                return saturate((color - 0.5) * strength + 0.5);
            }

            float3 Saturation(float3 color, float amount)
            {
                float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
                return lerp(luminance.xxx, color, amount);
            }

            fixed4 frag(v2f_img input) : SV_Target
            {
                float4 source = tex2D(_MainTex, input.uv);
                float3 color = source.rgb;
                float luma = dot(color, float3(0.2126, 0.7152, 0.0722));

                if (_Preset < 0.5) // Original
                {
                    return float4(color, lerp(1.0, source.a, _PreserveAlpha));
                }
                else if (_Preset < 1.5) // Black & White
                {
                    color = Contrast(luma.xxx, 1.15);
                }
                else if (_Preset < 2.5) // Vintage Film
                {
                    color = Saturation(color, 0.76);
                    color = Contrast(color, 0.92);
                    color = lerp(color, float3(0.88, 0.70, 0.51) * luma, 0.17);
                    color = color * 0.91 + 0.055;
                    float noise = frac(sin(dot(floor(input.uv * _MainTex_TexelSize.zw), float2(12.9898, 78.233))) * 43758.5453);
                    color += (noise - 0.5) * 0.025;
                }
                else if (_Preset < 3.5) // Teal & Orange
                {
                    color = Contrast(color, 1.08);
                    float shadow = saturate((0.58 - luma) * 1.45);
                    float highlight = saturate((luma - 0.35) * 1.25);
                    color += float3(-0.09, 0.035, 0.07) * shadow;
                    color += float3(0.07, 0.015, -0.045) * highlight;
                }
                else if (_Preset < 4.5) // Warm Sunlight
                {
                    color = Saturation(color, 1.09);
                    color += float3(0.065, 0.025, -0.035);
                    color = Contrast(color, 1.03);
                }
                else if (_Preset < 5.5) // Cool Mood
                {
                    color = Saturation(color, 0.84);
                    color += float3(-0.035, 0.007, 0.055);
                    color = Contrast(color, 1.09);
                }
                else // Sepia
                {
                    color = mul(float3x3(0.393, 0.769, 0.189,
                                          0.349, 0.686, 0.168,
                                          0.272, 0.534, 0.131), color);
                }
                return float4(saturate(color), lerp(1.0, source.a, _PreserveAlpha));
            }
            ENDCG
        }
    }
    Fallback Off
}
