Shader "Hidden/CameraCaptureStudio/Filter"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Preset ("Preset", Float) = 0
        _PreserveAlpha ("Preserve Alpha", Float) = 0
        _PreserveRed ("Preserve Red", Float) = 1
        _BlurStep ("Blur Step", Vector) = (0,0,0,0)
        _PremultiplyInput ("Premultiply Input", Float) = 0
        _Unpremultiply ("Unpremultiply", Float) = 0
    }
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
            float _PreserveRed;
            float _Unpremultiply;

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
                else if (_Preset < 6.5) // Sepia
                {
                    color = mul(float3x3(0.393, 0.769, 0.189,
                                          0.349, 0.686, 0.168,
                                          0.272, 0.534, 0.131), color);
                }
                else if (_Preset < 7.5) // High-contrast monochrome with selective red
                {
                    float3 monochrome = Contrast(luma.xxx, 2.4);
                    float redDominance = color.r - max(color.g, color.b);
                    float redSaturation = redDominance / max(color.r, 0.001);
                    float redMask = smoothstep(0.06, 0.20, redDominance)
                        * smoothstep(0.25, 0.55, redSaturation)
                        * smoothstep(0.08, 0.25, color.r) * _PreserveRed;
                    float3 accent = saturate(color * float3(1.15, 0.65, 0.65));
                    color = lerp(monochrome, accent, redMask);
                }
                else // Background blur, already processed by the separable pass
                {
                    if (_Unpremultiply > 0.5)
                        color = source.a > 0.00001 ? source.rgb / source.a : float3(0, 0, 0);
                }
                return float4(saturate(color), lerp(1.0, source.a, _PreserveAlpha));
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragBlur
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _BlurStep;
            float _PremultiplyInput;

            float4 SampleBlur(float2 uv)
            {
                float4 value = tex2D(_MainTex, uv);
                value.rgb *= lerp(1.0, value.a, _PremultiplyInput);
                return value;
            }

            float4 fragBlur(v2f_img input) : SV_Target
            {
                float2 step = _BlurStep.xy;
                float4 result = SampleBlur(input.uv) * 0.2270270270;
                result += (SampleBlur(input.uv + step) + SampleBlur(input.uv - step)) * 0.1945945946;
                result += (SampleBlur(input.uv + step * 2.0) + SampleBlur(input.uv - step * 2.0)) * 0.1216216216;
                result += (SampleBlur(input.uv + step * 3.0) + SampleBlur(input.uv - step * 3.0)) * 0.0540540541;
                result += (SampleBlur(input.uv + step * 4.0) + SampleBlur(input.uv - step * 4.0)) * 0.0162162162;
                return result;
            }
            ENDCG
        }
    }
    Fallback Off
}
