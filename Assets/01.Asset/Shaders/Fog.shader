// 안개: 미탐색 = 새까맣게, 탐색했지만 지금 시야 밖 = 어둡게만, 침식이 높은 곳 = 침식 안개.
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
            TEXTURE2D(_FogData); SAMPLER(sampler_FogData); // r = 탐색함, g = 지금 시야 안, b = 침식/100 (코드가 채움)
            float4 _MapSize;

            float _DimAlpha, _ErosionVisible, _ErosionMin, _ErosionMax, _ErosionAlphaMin, _ErosionAlphaMax, _FogCells;
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

                // 장면 위에 침식 안개, 그 위에 어둠을 차례로 덮은 결과를 한 번에 낸다
                float alpha = 1 - (1 - fogAlpha) * (1 - dark);
                float3 col = fogColor * fogAlpha * (1 - dark) / max(alpha, 1e-4);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
