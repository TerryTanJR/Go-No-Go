// 行星表面：白天貼圖 + 夜晚燈光 + 雲層 + 海面反光 + 大氣邊緣光，太陽方向取場景的主光源。
// UV 由物件空間的法線算經緯度（和 GeoMath.LatLonToLocal 同一套座標），
// 所以地面站標記的位置和貼圖完全對齊。經度 180° 接縫用兩組導數擇一，避免 mip 跳級出現細線。
Shader "GoNoGo/Planet"
{
    Properties
    {
        _DayTex ("Day", 2D) = "white" {}
        _NightTex ("Night lights", 2D) = "black" {}
        _CloudTex ("Clouds", 2D) = "black" {}
        _WaterMask ("Water mask", 2D) = "black" {}
        _NightIntensity ("Night intensity", Range(0, 4)) = 1.6
        _CloudOpacity ("Cloud opacity", Range(0, 1)) = 0.85
        _CloudDrift ("Cloud drift (u per second)", Float) = 0.002
        _SpecStrength ("Ocean specular", Range(0, 2)) = 0.6
        _AtmoColor ("Atmosphere color", Color) = (0.35, 0.62, 1.0, 1)
        _AtmoStrength ("Atmosphere rim", Range(0, 4)) = 1.4
        _Ambient ("Ambient", Range(0, 0.5)) = 0.03
        _Exposure ("Exposure", Range(0.2, 3)) = 1.15
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_DayTex); SAMPLER(sampler_DayTex);
            TEXTURE2D(_NightTex); SAMPLER(sampler_NightTex);
            TEXTURE2D(_CloudTex); SAMPLER(sampler_CloudTex);
            TEXTURE2D(_WaterMask); SAMPLER(sampler_WaterMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _DayTex_ST;
                float _NightIntensity, _CloudOpacity, _CloudDrift, _SpecStrength;
                float4 _AtmoColor;
                float _AtmoStrength, _Ambient, _Exposure;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.dirOS = v.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            // 經緯度 → UV（u：經度 -180..180，v：緯度 -90..90）
            float2 LatLonUV(float3 n)
            {
                float lon = atan2(n.z, n.x);
                float lat = asin(clamp(n.y, -1.0, 1.0));
                return float2(lon / (2.0 * PI) + 0.5, lat / PI + 0.5);
            }

            float4 SampleSeamless(TEXTURE2D_PARAM(tex, smp), float2 uv)
            {
                float2 dx = ddx(uv), dy = ddy(uv);
                float u2 = frac(uv.x + 0.5) - 0.5;
                float2 dx2 = float2(ddx(u2), dx.y), dy2 = float2(ddy(u2), dy.y);
                bool useAlt = abs(dx2.x) + abs(dy2.x) < abs(dx.x) + abs(dy.x);
                return SAMPLE_TEXTURE2D_GRAD(tex, smp, uv, useAlt ? dx2 : dx, useAlt ? dy2 : dy);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.dirOS);
                float2 uv = LatLonUV(n);
                float3 N = normalize(i.normalWS);
                Light sun = GetMainLight();
                float3 L = normalize(sun.direction);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));

                float ndl = dot(N, L);
                float day = saturate(ndl * 4.0 + 0.15);      // 晨昏線柔和過渡
                float lit = saturate(ndl);

                float3 albedo = SampleSeamless(TEXTURE2D_ARGS(_DayTex, sampler_DayTex), uv).rgb;
                float2 cuv = uv + float2(_Time.y * _CloudDrift, 0);
                float cloud = SampleSeamless(TEXTURE2D_ARGS(_CloudTex, sampler_CloudTex), cuv).r * _CloudOpacity;
                float water = SampleSeamless(TEXTURE2D_ARGS(_WaterMask, sampler_WaterMask), uv).r;
                float3 night = SampleSeamless(TEXTURE2D_ARGS(_NightTex, sampler_NightTex), uv).rgb;

                float3 col = albedo * (lit * sun.color + _Ambient);
                // 海面太陽反光（Blinn-Phong）
                float3 H = normalize(L + V);
                float spec = pow(saturate(dot(N, H)), 60.0) * water * (1.0 - cloud) * _SpecStrength;
                col += spec * sun.color * day;
                // 雲：受光的白、背光的暗
                col = lerp(col, (lit * 0.95 + _Ambient) * sun.color, cloud);
                // 夜晚城市燈光（被雲遮住一部分）
                col += night * night * _NightIntensity * (1.0 - day) * (1.0 - cloud * 0.7);

                // 大氣：邊緣的藍色散射，只在朝太陽那一半明顯
                float rim = pow(1.0 - saturate(dot(N, V)), 3.0);
                float sunSide = saturate(ndl * 1.5 + 0.35);
                col += _AtmoColor.rgb * rim * _AtmoStrength * sunSide;
                // 晨昏線附近一點暖色
                col += float3(1.0, 0.45, 0.15) * rim * _AtmoStrength * 0.25 * saturate(1.0 - abs(ndl) * 4.0);

                return half4(col * _Exposure, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
