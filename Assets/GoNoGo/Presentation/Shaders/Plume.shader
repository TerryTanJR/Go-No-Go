// 引擎尾焰：套在 RocketBuilder 的旋轉體網格上，uv.y 從噴口（0）到尾端（1）。
// 加法混合；核心白熱、往外變橘再淡出；流動雜訊做出閃爍與菱形震波的感覺。
Shader "GoNoGo/Plume"
{
    Properties
    {
        _CoreColor ("Core", Color) = (1.0, 0.95, 0.85, 1)
        _MidColor ("Mid", Color) = (1.0, 0.55, 0.15, 1)
        _TipColor ("Tip", Color) = (0.9, 0.25, 0.05, 1)
        _Intensity ("Intensity", Range(0, 12)) = 4
        _Throttle ("Throttle", Range(0, 1)) = 1
        _Diamonds ("Shock diamonds", Range(0, 1)) = 0.35
        _Speed ("Flow speed", Float) = 6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor, _MidColor, _TipColor;
                float _Intensity, _Throttle, _Diamonds, _Speed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.uv = v.uv;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float y = saturate(i.uv.y);               // 0 = 噴口，1 = 尾端（RocketBuilder 的旋轉體網格）
                float3 N = normalize(i.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float facing = saturate(abs(dot(N, V)));  // 中心亮、邊緣淡，像體積光
                float t = _Time.y * _Speed;
                float n = VNoise(float2(i.uv.x * 8.0, y * 10.0 - t)) * 0.6 + VNoise(float2(i.uv.x * 19.0, y * 23.0 - t * 1.7)) * 0.4;
                float diamonds = 0.5 + 0.5 * cos(y * 38.0) ;
                diamonds = lerp(1.0, 0.6 + 0.8 * diamonds * (1.0 - y), _Diamonds);
                float3 col = lerp(_CoreColor.rgb, _MidColor.rgb, saturate(y * 2.2));
                col = lerp(col, _TipColor.rgb, saturate(y * 1.4 - 0.4));
                float fade = pow(1.0 - y, 1.6) * smoothstep(0.0, 0.04, y + 0.02);
                float a = fade * facing * facing * (0.65 + 0.7 * n) * diamonds * _Throttle;
                return half4(col * a * _Intensity, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
