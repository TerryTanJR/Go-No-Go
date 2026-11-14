// 行星外圍的大氣光暈：比行星大一點的球殼，加法混合，只畫邊緣，背光那一側淡出。
Shader "GoNoGo/Atmosphere"
{
    Properties
    {
        _Color ("Color", Color) = (0.30, 0.58, 1.0, 1)
        _Power ("Rim power", Range(0.5, 12)) = 4.5
        _Strength ("Strength", Range(0, 4)) = 1.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Power, _Strength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 L = normalize(GetMainLight().direction);
                float rim = pow(1.0 - saturate(dot(N, V)), _Power);
                // 邊緣最外圈淡出，光暈才不會有硬邊
                float edge = saturate(dot(N, V) * 6.0);
                float sun = saturate(dot(N, L) * 1.2 + 0.4);
                return half4(_Color.rgb * rim * edge * sun * _Strength, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
