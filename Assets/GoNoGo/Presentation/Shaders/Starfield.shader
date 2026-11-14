// 程序產生的星空天空盒：三層不同密度的星點（有抗鋸齒）+ 淡淡的銀河帶，不需要貼圖。
Shader "GoNoGo/Starfield"
{
    Properties
    {
        _Brightness ("Brightness", Range(0, 4)) = 1.2
        _MilkyWay ("Milky Way", Range(0, 1)) = 0.35
        _Tint ("Background tint", Color) = (0.004, 0.006, 0.014, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Brightness, _MilkyWay;
                float4 _Tint;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float3 Hash3(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)), dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453123);
            }

            float Noise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash3(i).x, n100 = Hash3(i + float3(1, 0, 0)).x;
                float n010 = Hash3(i + float3(0, 1, 0)).x, n110 = Hash3(i + float3(1, 1, 0)).x;
                float n001 = Hash3(i + float3(0, 0, 1)).x, n101 = Hash3(i + float3(1, 0, 1)).x;
                float n011 = Hash3(i + float3(0, 1, 1)).x, n111 = Hash3(i + float3(1, 1, 1)).x;
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            // 一層星點：方向空間切格子，每格最多一顆星
            float3 Stars(float3 d, float scale, float density, float size)
            {
                float3 p = d * scale;
                float3 cell = floor(p);
                float3 h = Hash3(cell);
                if (h.x > density) return 0;
                float3 starPos = cell + 0.25 + 0.5 * Hash3(cell + 17.0);
                float dist = length(p - starPos);
                float w = fwidth(dist) + 1e-4;
                // 星點比一個像素還小時按面積變暗，否則細層的星會變成一片閃爍的雜訊
                float core = saturate(1.0 - (dist - size) / w) * saturate(size * 2.0 / w);
                float glow = exp(-dist * dist / (size * size * 9.0)) * 0.25;
                float mag = pow(h.y, 8.0) * 4.0 + 0.06; // 大多數很暗，少數很亮（像真的星空）
                // 色溫：大多偏白，少數偏藍或偏橘
                float3 tint = lerp(float3(1.0, 0.82, 0.62), float3(0.72, 0.84, 1.0), h.z);
                return tint * (core + glow) * mag;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 col = _Tint.rgb;
                col += Stars(d, 70.0, 0.22, 0.05) * 1.0;
                col += Stars(d, 180.0, 0.30, 0.07) * 0.35;
                col += Stars(d, 380.0, 0.30, 0.09) * 0.12;

                // 銀河帶：一個傾斜的大圓附近，用雜訊做出雲狀
                float3 gal = normalize(float3(0.35, 0.86, 0.37));
                float b = dot(d, gal);
                float band = exp(-b * b / 0.035);
                float n = Noise(d * 5.0) * 0.6 + Noise(d * 13.0) * 0.3 + Noise(d * 31.0) * 0.1;
                float dust = smoothstep(0.35, 0.75, Noise(d * 9.0 + 3.1));
                col += float3(0.55, 0.6, 0.78) * band * n * (1.0 - dust * 0.6) * _MilkyWay * 0.12;
                col += Stars(d, 700.0, 0.55 * band, 0.12) * band * 0.15 * _MilkyWay;

                return half4(col * _Brightness, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
