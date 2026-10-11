// 안개: 미탐색 = 새까맣게, 탐색했지만 지금 시야 밖 = 어둡게만, 침식이 높은 곳 = 침식 안개.
// 침식 필터 2단계에서는 안개 위에 칸마다 침식 수치를 칸 한가운데에 숫자로 적는다.
// 비가 오면 하늘이 뚫린 칸에 빗줄기를 그린다(침식 안개 위, 어둠 아래).
// 맵 전체를 덮는 쿼드 1장. 칸마다 (탐색 여부, 현재 시야, 침식)을 담은 데이터 텍스처(_FogData)를
// 부드럽게 보간해 읽으므로 경계가 칸 모양으로 끊기지 않는다.
Shader "XNP/Fog"
{
    Properties
    {
        _DimAlpha ("시야 밖 어둡기", Range(0, 1)) = 0.5

        [Header(Erosion Fog)]
        [NoScaleOffset] _FogTex ("침식 안개 텍스처 (이음매 없는 반복)", 2D) = "white" {}
        [Toggle] _ErosionVisible ("침식 안개 표시 (게임 중엔 버튼이 덮어씀)", Float) = 1
        _ErosionMin ("이 침식 수치부터 안개가 보임", Float) = 30
        _ErosionMax ("이 침식 수치에서 가장 짙음", Float) = 80
        _ErosionAlphaMin ("가장 옅을 때 불투명도", Range(0, 1)) = 0.1
        _ErosionAlphaMax ("가장 짙을 때 불투명도", Range(0, 1)) = 0.92
        _FogRect ("텍스처에서 쓸 영역 (x, y, 너비, 높이) — 라벨 등을 피해 반복 한 칸만 고를 때", Vector) = (0, 0, 1, 1)
        _FogCells ("고른 영역 한 장이 덮는 칸 수", Float) = 8
        _FogSpeed ("안개 흐름 속도 (칸/초)", Vector) = (0.25, 0.08, 0, 0)

        [Header(Erosion Numbers)]
        [NoScaleOffset] _DigitTex ("숫자 아틀라스 (0~9 가로 한 줄)", 2D) = "black" {}
        [Toggle] _ErosionNumbers ("칸마다 침식 수치 표시 (게임 중엔 버튼이 덮어씀)", Float) = 0
        _NumberHeight ("숫자 높이 (칸)", Range(0.1, 0.6)) = 0.34
        _NumberMinPixels ("화면을 축소했을 때 숫자 최소 높이 (픽셀) — 칸을 넘지 않는 선에서 키움", Float) = 14

        [Header(Rain)]
        _RainColor ("빗줄기 색 (알파 = 진하기)", Color) = (0.80, 0.90, 1.0, 0.55)
        _RainDensity ("빗줄기 촘촘함 (칸당 줄 수)", Float) = 2.2
        _RainLength ("빗줄기 간격 (칸)", Float) = 2.5
        _RainSpeed ("떨어지는 속도", Float) = 2.2
        _RainSlant ("기울기", Range(-1, 1)) = 0.18
        _RainWidth ("빗줄기 굵기", Range(0.01, 0.5)) = 0.07
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_FogTex);  SAMPLER(sampler_FogTex);
            TEXTURE2D(_FogData); SAMPLER(sampler_FogData); // r = 탐색함, g = 지금 시야 안, b = 침식/100, a = 숫자로 적을 침식 + 1 (0 = 안 적음) (코드가 채움)
            TEXTURE2D(_DigitTex); SAMPLER(sampler_DigitTex);
            float4 _MapSize;

            float _DimAlpha, _ErosionVisible, _ErosionMin, _ErosionMax, _ErosionAlphaMin, _ErosionAlphaMax, _FogCells;
            float _ErosionNumbers, _NumberHeight, _NumberMinPixels;

            TEXTURE2D(_SkyBottom); // 열마다 하늘이 시작되는 높이 (가로 한 줄, 코드가 채움)
            float _Rain;           // 1 = 비가 내리는 중 (코드가 채움)
            float4 _RainColor;
            float _RainDensity, _RainLength, _RainSpeed, _RainSlant, _RainWidth;

            // 그 자리의 빗줄기 진하기(0~1). 하늘이 뚫린 칸에만 내린다.
            float RainAlpha(float2 p)
            {
                float sky = LOAD_TEXTURE2D(_SkyBottom, int2(clamp(floor(p.x), 0, _MapSize.x - 1), 0)).r;
                if (p.y < sky) return 0;

                // 기울어진 세로 띠마다 줄 하나가 서로 다른 박자로 떨어진다
                float lane = (p.x + p.y * _RainSlant) * _RainDensity;
                float seed = frac(sin(floor(lane) * 127.1) * 43758.5453);
                float along = frac(p.y / _RainLength + _Time.y * _RainSpeed * (0.75 + 0.5 * seed) + seed * 13.0);
                float body = saturate(1 - along / 0.3);                                   // 머리가 진하고 꼬리로 옅어진다
                float thin = saturate(1 - abs(frac(lane) - 0.5) / max(_RainWidth, 1e-3)); // 띠 가운데의 가는 줄
                return body * thin * _RainColor.a;
            }

            // 아틀라스(1024x128, 숫자 한 칸 102.4px)에서 글자가 실제로 차지하는 가운데 영역 — Texture_Digits.png를 다시 만들면 함께 고칠 것
            #define DIGIT_CROP float2(0.78, 0.72)
            #define DIGIT_ASPECT 0.8667 // 그 영역의 가로/세로
            float4 _FogSpeed, _FogRect;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 map : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.map = i.positionOS.xy; // 쿼드가 (0,0)~(가로,세로)라 로컬 좌표 = 칸 좌표
                return o;
            }

            // 텍스처의 _FogRect 영역만 반복한다. frac의 불연속에서 밉 선이 생기지 않도록 미분을 따로 넘긴다.
            float3 SampleFog(float2 uv, float2 gx, float2 gy)
            {
                float2 t = _FogRect.xy + frac(uv) * _FogRect.zw;
                return SAMPLE_TEXTURE2D_GRAD(_FogTex, sampler_FogTex, t, gx * _FogRect.zw, gy * _FogRect.zw).rgb;
            }

            // 칸 한가운데에 그 칸의 침식 수치(정수)를 적는다. rgb = 글자색, a = 덮는 정도.
            half4 ErosionNumber(float2 p)
            {
                float raw = round(LOAD_TEXTURE2D(_FogData, int2(floor(p))).a * 255.0);
                if (raw < 0.5) return 0; // 막힌 칸·미탐색 칸

                float value = raw - 1;
                float count = value >= 100 ? 3 : (value >= 10 ? 2 : 1);

                // 화면을 축소해 글자가 너무 작아지면, 자릿수만큼의 글자가 칸을 넘지 않는 선에서 키운다
                float cellPixels = 1 / max(max(length(ddx(p)), length(ddy(p))), 1e-5);
                float h = min(max(_NumberHeight, _NumberMinPixels / cellPixels), min(0.9, 0.98 / (count * DIGIT_ASPECT)));
                float w = h * DIGIT_ASPECT;

                // q.x = 왼쪽부터 몇 번째 글자 상자의 어디인지, q.y = 상자 안 세로 위치
                float2 f = frac(p) - 0.5;
                float2 q = float2(f.x / w + count * 0.5, f.y / h + 0.5);
                if (q.x < 0 || q.x >= count || q.y < 0 || q.y > 1) return 0;

                float place = count - 1 - floor(q.x); // 0 = 일의 자리
                float divisor = place > 1.5 ? 100 : (place > 0.5 ? 10 : 1);
                float digit = fmod(floor(value / divisor + 0.001), 10);

                float2 uv = float2((digit + 0.5 + (frac(q.x) - 0.5) * DIGIT_CROP.x) / 10, 0.5 + (q.y - 0.5) * DIGIT_CROP.y);
                float2 scale = float2(DIGIT_CROP.x / (w * 10), DIGIT_CROP.y / h); // 칸 좌표 → 아틀라스 uv (글자 경계의 밉 선 방지용 미분)
                half4 glyph = SAMPLE_TEXTURE2D_GRAD(_DigitTex, sampler_DigitTex, uv, ddx(p) * scale, ddy(p) * scale);

                // 칸이 화면에서 너무 작으면(약 12픽셀 이하) 읽을 수 없으니 지운다
                glyph.a *= saturate((cellPixels - 12) / 6);
                return glyph;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.map;
                float3 data = SAMPLE_TEXTURE2D(_FogData, sampler_FogData, p / _MapSize.xy).rgb;

                // 어둠: 미탐색이면 1(완전 검정), 탐색했으면 시야 밖일 때만 _DimAlpha
                float dark = lerp(1, _DimAlpha * (1 - data.g), data.r);

                // 침식 안개: 기준 이상에서만, 수치가 높을수록 짙게
                float erosion = data.b * 100;
                float t = saturate((erosion - _ErosionMin) / max(_ErosionMax - _ErosionMin, 1e-3));
                float fogAlpha = lerp(_ErosionAlphaMin, _ErosionAlphaMax, t)
                               * saturate((erosion - _ErosionMin) * 0.5 + 0.5) // 기준 근처 ±1에서 부드럽게 나타남
                               * _ErosionVisible;

                // 서로 다른 속도로 흐르는 두 겹을 섞어 한 장이 그대로 미끄러지는 느낌을 없앤다
                float2 uv = p / _FogCells;
                float2 flow = _FogSpeed.xy / _FogCells * _Time.y;
                float3 a = SampleFog(uv + flow, ddx(uv), ddy(uv));
                float3 b = SampleFog(uv * 0.63 - flow * 0.7 + 0.37, ddx(uv) * 0.63, ddy(uv) * 0.63);
                float3 fogColor = (a + b) * 0.5;
                // 텍스처의 어두운 결을 옅은 부분으로 써서 뭉게뭉게한 농담을 만든다
                // 짙을수록 결이 덜 비치게 해서, 수치가 높으면 정말로 꽉 막힌 안개가 된다
                fogAlpha *= lerp(lerp(0.45, 0.9, t), 1.0, smoothstep(0.45, 0.85, dot(a * b, float3(0.33, 0.34, 0.33))));

                // 장면 위에 침식 안개 → 비 → 어둠을 차례로 덮은 결과를 한 번에 낸다
                float3 premul = fogColor * fogAlpha;
                float alpha = fogAlpha;
                if (_Rain > 0.5)
                {
                    float rain = RainAlpha(p);
                    premul = _RainColor.rgb * rain + premul * (1 - rain);
                    alpha = rain + alpha * (1 - rain);
                }
                premul *= 1 - dark;
                alpha = dark + alpha * (1 - dark);
                float3 col = premul / max(alpha, 1e-4);

                // 침식 수치는 맨 위에 얹는다
                if (_ErosionNumbers > 0.5)
                {
                    half4 number = ErosionNumber(p);
                    float outAlpha = number.a + alpha * (1 - number.a);
                    col = (number.rgb * number.a + col * alpha * (1 - number.a)) / max(outAlpha, 1e-4);
                    alpha = outAlpha;
                }
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
