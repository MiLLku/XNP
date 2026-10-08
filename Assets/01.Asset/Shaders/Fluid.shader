// 액체(물·용암): 맵 전체를 덮는 쿼드 1장. 칸마다 (종류, 양, 막힘)을 담은 데이터 텍스처(_FluidData)를 읽어
// 칸 아래쪽부터 그 양만큼 채워 그린다. 옆 칸과 수면 높이를 이어 칸 경계에서 계단이 지지 않게 한다.
Shader "XNP/Fluid"
{
    Properties
    {
        [Header(Water)]
        _WaterColor ("물 색 (알파 = 비치는 정도)", Color) = (0.20, 0.50, 0.86, 0.60)
        _WaterDeepColor ("깊은 물 색", Color) = (0.10, 0.28, 0.62, 0.72)
        _WaterSurfaceColor ("수면 띠 색", Color) = (0.78, 0.92, 1.0, 0.90)

        [Header(Lava)]
        _LavaColor ("용암 색", Color) = (0.92, 0.28, 0.05, 1)
        _LavaHotColor ("용암 밝은 색", Color) = (1.0, 0.80, 0.25, 1)
        _LavaFlow ("용암 무늬 흐름 속도", Float) = 0.35

        [Header(Surface)]
        _SurfaceWidth ("수면 띠 두께 (칸)", Range(0, 0.3)) = 0.07
        _WaveHeight ("잔물결 높이 (칸)", Range(0, 0.05)) = 0.012
        _WaveFrequency ("잔물결 촘촘함", Float) = 5
        _WaveSpeed ("잔물결 속도", Float) = 2
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

            TEXTURE2D(_FluidData); // r = 종류(0 없음, 1 물, 2 용암), g = 양(0~1), b = 막힌 칸(지형·차단 건물) (코드가 채움)
            float4 _MapSize;

            float4 _WaterColor, _WaterDeepColor, _WaterSurfaceColor, _LavaColor, _LavaHotColor;
            float _LavaFlow, _SurfaceWidth, _WaveHeight, _WaveFrequency, _WaveSpeed;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 map : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.map = i.positionOS.xy; // 쿼드가 (0,0)~(가로,세로)라 로컬 좌표 = 칸 좌표
                return o;
            }

            // x = 종류, y = 양, z = 막힘
            float3 Cell(float2 c)
            {
                float4 d = LOAD_TEXTURE2D(_FluidData, int2(clamp(c, 0, _MapSize.xy - 1)));
                return float3(round(d.r * 255.0), d.g, d.b);
            }

            bool Same(float3 a, float3 b) { return a.x > 0.5 && abs(a.x - b.x) < 0.5; }

            // 옆 칸과 만나는 경계에서의 수면 높이
            float Boundary(float3 self, float height, bool covered, float3 side, float3 sideAbove)
            {
                if (covered) return 1;            // 위에도 액체가 있는 기둥은 깎지 않는다
                if (side.z > 0.5) return height;  // 벽 — 평평하게 붙는다
                if (Same(self, side))             // 같은 액체 — 두 칸의 중간 높이로 잇는다
                    return (height + (Same(side, sideAbove) ? 1 : side.y)) * 0.5;
                if (side.x > 0.5) return height;  // 다른 액체
                return 0;                         // 마른 빈 칸 — 가장자리로 갈수록 얕아진다
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.map;
                float2 c = floor(p);
                float2 f = frac(p);

                float3 self = Cell(c);
                clip(self.x - 0.5); // 액체 없는 칸

                // 위에도 같은 액체가 있거나, 가득 찬 채 천장에 닿아 있으면 수면이 없는 칸이다
                float3 up = Cell(c + float2(0, 1));
                bool covered = Same(self, up) || (up.z > 0.5 && self.y > 0.99);
                float height = covered ? 1 : self.y;

                float left  = Boundary(self, height, covered, Cell(c + float2(-1, 0)), Cell(c + float2(-1, 1)));
                float right = Boundary(self, height, covered, Cell(c + float2( 1, 0)), Cell(c + float2( 1, 1)));
                float surface = f.x < 0.5 ? lerp(left, height, f.x * 2) : lerp(height, right, (f.x - 0.5) * 2);

                bool lava = self.x > 1.5;
                if (!covered && !lava)
                    surface += sin(p.x * _WaveFrequency + _Time.y * _WaveSpeed) * _WaveHeight * saturate(height * 10);
                surface = saturate(surface);

                // 수면 아래만 칠한다 (경계는 한 픽셀 폭으로 부드럽게)
                float depth = surface - f.y;
                float inside = covered ? 1 : saturate(depth / max(fwidth(p.y), 1e-5) + 0.5); // 수면 없는 칸은 줄 사이에 틈이 보이지 않게 꽉 채운다
                float band = covered ? 0 : saturate(1 - depth / max(_SurfaceWidth, 1e-4));

                half4 col;
                if (lava)
                {
                    float t = _Time.y * _LavaFlow;
                    float glow = sin(p.x * 2.3 + t) * sin(p.y * 3.1 - t * 0.7) * 0.5 + 0.5;
                    col = lerp(_LavaColor, _LavaHotColor, saturate(glow * 0.55 + band * 0.6));
                }
                else
                {
                    col = lerp(_WaterColor, _WaterDeepColor, covered ? 1 : saturate(depth));
                    col = lerp(col, _WaterSurfaceColor, band * band);
                }

                col.a *= inside;
                return col;
            }
            ENDHLSL
        }
    }
}
