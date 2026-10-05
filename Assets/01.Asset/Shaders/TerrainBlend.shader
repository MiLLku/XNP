// 산소미포함식 지형 렌더링.
// 맵 전체를 덮는 쿼드 1장에 그리며, 칸마다 타일 id를 담은 데이터 텍스처(_MapTex)를 읽어
// 외곽 모서리를 둥글게 깎고, 서로 다른 타일이 만나는 경계를 노이즈로 뒤섞는다.
// 판정(채광·통행)은 여전히 네모 칸이고 이 셰이더는 그림만 바꾼다.
Shader "XNP/TerrainBlend"
{
    Properties
    {
        _TexCells ("이음매 없는 텍스처 한 장이 덮는 칸 수", Float) = 4
        _CornerRadius ("외곽 모서리 반경", Range(0, 0.5)) = 0.35
        _NoiseAmp ("외곽 울퉁불퉁함", Range(0, 0.3)) = 0.1
        _NoiseFreq ("외곽 노이즈 주파수", Float) = 2.2
        _MixAmp ("재료 경계 뒤섞임", Range(0, 0.4)) = 0.22
        _MixFreq ("재료 경계 노이즈 주파수", Float) = 1.8
        _PriorityBias ("우선순위 1당 덮는 거리", Range(0, 0.2)) = 0.07
        _OutlineWidth ("외곽 테두리 폭", Range(0.001, 0.4)) = 0.12
        _OutlineDark ("외곽 테두리 어둡기", Range(0, 1)) = 0.6
        _SeamWidth ("재료 경계 테두리 폭", Range(0.001, 0.3)) = 0.06
        _SeamDark ("재료 경계 테두리 어둡기", Range(0, 1)) = 0.3
        _GrassDepth ("잔디층 두께", Range(0, 1)) = 0.6
        _GrassSoft ("잔디-흙 뒤섞이는 폭", Range(0.01, 0.6)) = 0.3
        _GrassCrevice ("흙의 어두운 틈으로 잔디가 파고드는 정도", Range(0, 2)) = 1
        _GrassRagged ("잔디 안쪽 경계 들쭉날쭉", Range(0, 1)) = 0.5
        _GrassFreq ("잔디 들쭉날쭉 주파수", Float) = 3
        _GrassCells ("잔디 텍스처 한 장이 덮는 칸 수", Float) = 3
        [Header(Built Tiles)]
        _StructCells ("건설 타일 텍스처 한 장이 덮는 칸 수", Float) = 4
        _CapHeight ("윗면 덮개 띠 두께 (칸 안쪽을 차지)", Range(0, 0.5)) = 0.2
        _CapOverhang ("덮개 띠가 줄 끝에서 좌우로 튀어나오는 길이", Range(0, 0.3)) = 0.1
        _CapColor ("덮개 띠 색", Color) = (0.93, 0.89, 0.78, 1)
        _StructBorderWidth ("건설 타일 테두리 폭", Range(0, 0.3)) = 0.05
        _StructBorderColor ("건설 타일 테두리 색 (알파 = 진하기)", Color) = (0.07, 0.10, 0.12, 1)
        _UVInset ("칸 반복 타일: 그림 테두리 잘라내기", Range(0, 0.3)) = 0.12
        [Toggle] _Mirror ("칸 반복 타일: 거울 반복", Float) = 0
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
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_ARRAY(_TileTex); SAMPLER(sampler_TileTex); // 레이어 = 재료 그림 (코드가 채움)
            TEXTURE2D(_MapTex);

            float4 _MapSize;          // (가로, 세로)
            float4 _TileInfo[256];    // 타일 id별 (레이어+1, 이음매 없는 텍스처 여부, 잔디가 덮일 수 있는지). x=0이면 그림 없음 = 공기 취급
            float4 _GrassLayers;      // (일반 잔디 레이어, 침식된 잔디 레이어, 사용 여부)
            float _StructCells, _StructBorderWidth, _CapHeight, _CapOverhang;
            float4 _StructBorderColor, _CapColor;
            float _GrassDepth, _GrassSoft, _GrassCrevice, _GrassRagged, _GrassFreq, _GrassCells;
            float  _Priority[128];    // 높을수록 경계에서 상대를 덮는다

            float _CornerRadius, _NoiseAmp, _NoiseFreq, _MixAmp, _MixFreq, _PriorityBias;
            float _OutlineWidth, _OutlineDark, _SeamWidth, _SeamDark, _UVInset, _Mirror, _TexCells;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 map : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.map = i.positionOS.xy; // 쿼드가 (0,0)~(가로,세로)라 로컬 좌표 = 칸 좌표
                return o;
            }

            // 건설된 바닥 타일이 채운 칸(id = STRUCTURE_BASE + 종류): 모양 계산에서는 고체로 쳐서
            // 지형이 이쪽으로 모서리를 깎지 않게 한다. 그 칸 자체는 텍스처가 있는 종류만 여기서 그리고
            // (이웃 건설 타일과 그림이 이어지고, 건설 타일이 아닌 쪽에만 테두리), 없으면 프리팹 스프라이트에 맡긴다.
            #define STRUCTURE_BASE 200
            bool IsStructure(int id) { return id >= STRUCTURE_BASE; }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + 1), u.x), u.y);
            }

            float Fbm(float2 p) { return ValueNoise(p) * 0.65 + ValueNoise(p * 2.13 + 17.7) * 0.35; }

            // 지형 셰이더가 직접 그리는(텍스처가 있는) 건설 타일인지
            bool IsDrawnStructure(int id) { return id >= STRUCTURE_BASE && _TileInfo[id].x > 0; }

            // 윗면에 덮개 띠가 얹히는 칸인지 — 셰이더가 그리는 건설 타일이고 바로 위가 공기일 때
            bool IsCapped(int id, int above) { return IsDrawnStructure(id) && above == 0; }

            // (타일 id, 잔디 플래그). 플래그는 공기 칸에만 의미가 있다: 1 = 맞닿은 흙 면에 잔디, 2 = 침식된 잔디
            int2 ReadCell(int2 c)
            {
                if (c.x < 0 || c.y < 0 || c.x >= (int)_MapSize.x || c.y >= (int)_MapSize.y) return 0;
                int2 cell = (int2)round(LOAD_TEXTURE2D(_MapTex, c).rg * 255.0);
                if (IsStructure(cell.x)) return cell;
                if (cell.x >= 128 || _TileInfo[cell.x].x <= 0) cell.x = 0;
                return cell;
            }

            // 칸(한 변 1) 박스의 부호 거리. r은 픽셀 쪽 모서리 하나의 반경.
            float CellDist(float2 v, float r)
            {
                float2 q = abs(v) - 0.5 + r;
                return length(max(q, 0)) + min(max(q.x, q.y), 0) - r;
            }

            float3 SampleTile(int id, float2 p, float2 gx, float2 gy)
            {
                float2 info = _TileInfo[id].xy;
                float2 uv;
                float scale;
                if (info.y > 0)
                {
                    // 이음매 없는 텍스처: 월드 좌표로 그대로 반복 — 칸 격자가 드러나지 않는다
                    scale = 1 / _TexCells;
                    uv = p * scale;
                }
                else
                {
                    // 칸 하나짜리 타일 그림: 칸마다 반복. 홀수 칸을 뒤집으면(거울) 칸 경계가 이어진다
                    scale = 1 - 2 * _UVInset;
                    float2 local = frac(p);
                    local = lerp(local, 1 - local, fmod(floor(p), 2) * _Mirror);
                    uv = _UVInset + local * scale;
                }
                return SAMPLE_TEXTURE2D_ARRAY_GRAD(_TileTex, sampler_TileTex, uv, info.x - 1, gx * scale, gy * scale).rgb;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.map;
                float2 gx = ddx(p), gy = ddy(p);
                // 화면 1픽셀의 칸 단위 크기. 3×3 이웃이 칸 경계에서 바뀌어 점수가 불연속이라
                // fwidth(점수)로 AA를 잡으면 칸 경계마다 가는 선이 생긴다 — 좌표 미분만 쓴다.
                float px = max(length(gx) + length(gy), 1e-4);
                int2 cc = (int2)floor(p);

                int ids[9];
                int grass[9];
                [unroll] for (int k = 0; k < 9; k++)
                {
                    int2 cell = ReadCell(cc + int2(k % 3 - 1, k / 3 - 1));
                    ids[k] = cell.x;
                    grass[k] = cell.y;
                }

                // 고체/공기 각각까지의 거리. 모서리는 '그쪽 세 이웃이 전부 반대편'일 때만 깎는다 —
                // 고체 쪽이면 볼록 모서리가 둥글어지고, 공기 쪽이면 오목 모서리에 필렛이 생긴다.
                // 안쪽 이음매에는 반대편 박스가 없으니 구멍이 나지 않는다.
                float dS = 1e3, dA = 1e3, dT = 1e3; // 고체 전체 / 공기 / 지형만(건설 타일 제외)
                float d[9];
                [unroll] for (int k2 = 0; k2 < 9; k2++)
                {
                    int2 o = int2(k2 % 3 - 1, k2 / 3 - 1);
                    float2 v = p - (float2(cc + o) + 0.5);
                    int2 s = int2(v.x >= 0 ? 1 : -1, v.y >= 0 ? 1 : -1);
                    bool solid = ids[k2] != 0;
                    int2 a = o + int2(s.x, 0), b = o + int2(0, s.y), c = o + s;
                    bool exposed = ((ids[(a.y + 1) * 3 + a.x + 1] != 0) != solid)
                                && ((ids[(b.y + 1) * 3 + b.x + 1] != 0) != solid)
                                && ((ids[(c.y + 1) * 3 + c.x + 1] != 0) != solid);
                    d[k2] = CellDist(v, exposed ? _CornerRadius : 0);
                    if (solid) dS = min(dS, d[k2]); else dA = min(dA, d[k2]);
                    if (solid && !IsStructure(ids[k2])) dT = min(dT, d[k2]);
                }
                // ── 건설된 바닥 타일 ──
                // 몸통은 칸 크기 그대로(위아래·좌우 모두 칸 경계). 위가 공기인 칸에는 칸 안쪽 윗부분에 덮개 띠가 얹히고,
                // 이 띠만 연결된 줄의 좌우 끝에서 옆 공기 칸으로 _CapOverhang만큼 튀어나온다 (끝에 다른 블록이 있으면 안 나온다).
                float2 cf = p - cc;                       // 칸 안 좌표 0~1
                float capBottom = cf.y - (1 - _CapHeight); // 덮개 띠 아랫선에서의 높이 (음수면 띠 아래)
                bool capL = IsCapped(ids[3], ids[6]), capC = IsCapped(ids[4], ids[7]), capR = IsCapped(ids[5], ids[8]);

                if (capBottom > 0 || (ids[4] == 0 && capBottom > -px))
                {
                    // 덮개 띠 안에서 가장 가까운 가장자리까지의 거리(endDist)와, 공기 칸으로 튀어나온 부분의 덮임 정도(cover)
                    float endDist = 1e3, cover = 0;
                    if (capC)
                    {
                        cover = 1;
                        if (!capL) endDist = cf.x + (ids[3] == 0 ? _CapOverhang : 0);
                        if (!capR) endDist = min(endDist, 1 - cf.x + (ids[5] == 0 ? _CapOverhang : 0));
                    }
                    else if (ids[4] == 0)
                    {
                        if (capL && cf.x < _CapOverhang + px) endDist = _CapOverhang - cf.x;
                        else if (capR && cf.x > 1 - _CapOverhang - px) endDist = cf.x - (1 - _CapOverhang);
                        if (endDist < 1e3) cover = saturate(0.5 + endDist / px) * saturate(0.5 + capBottom / px);
                    }
                    if (cover > 0)
                    {
                        float capEdge = min(min(endDist, capBottom), 1 - cf.y);
                        float capBorder = saturate(0.5 + (_StructBorderWidth - capEdge) / px) * _StructBorderColor.a;
                        return half4(lerp(_CapColor.rgb, _StructBorderColor.rgb, capBorder), cover);
                    }
                }

                if (IsStructure(ids[4]))
                {
                    if (_TileInfo[ids[4]].x <= 0) return 0; // 텍스처 없는 종류 — 스프라이트가 그린다

                    // 월드 좌표로 반복하므로 이웃 건설 타일과 그림이 그대로 이어진다
                    float ss = 1 / _StructCells;
                    float3 sc = SAMPLE_TEXTURE2D_ARRAY_GRAD(_TileTex, sampler_TileTex, p * ss, _TileInfo[ids[4]].x - 1, gx * ss, gy * ss).rgb;

                    // 건설 타일이 아닌 이웃(지형·공기) 칸까지의 거리 — 대각선도 포함해 안쪽 모서리까지 테두리가 돈다
                    float edge = 1e3;
                    [unroll] for (int ks = 0; ks < 9; ks++)
                    {
                        if (ks == 4 || IsStructure(ids[ks])) continue;
                        edge = min(edge, CellDist(p - (float2(cc + int2(ks % 3 - 1, ks / 3 - 1)) + 0.5), 0));
                    }
                    float border = saturate(0.5 + (_StructBorderWidth - edge) / px) * _StructBorderColor.a;
                    return half4(lerp(sc, _StructBorderColor.rgb, border), 1);
                }
                if (dT >= 1e3) return 0;

                // 음수 = 고체 안쪽. 노이즈로 외곽선을 울퉁불퉁하게.
                // 공기 칸에서는 지형까지의 거리만 본다 — 건설 타일 가장자리를 따라 지형이 번져 그려지지 않게.
                float f = max(ids[4] == 0 ? dT : dS, 0) - max(dA, 0);
                f += (Fbm(p * _NoiseFreq) - 0.5) * 2 * _NoiseAmp;
                float alpha = saturate(0.5 - f / px);
                if (alpha <= 0) return 0;

                // 재료 고르기: 가까울수록·우선순위 높을수록·재료별 노이즈가 작을수록 이긴다.
                float score[9];
                int best = -1, second = -1;
                [unroll] for (int k3 = 0; k3 < 9; k3++)
                {
                    score[k3] = 1e3;
                    if (ids[k3] == 0 || IsStructure(ids[k3])) continue;
                    int id = ids[k3];
                    score[k3] = d[k3]
                              + (ValueNoise(p * _MixFreq + id * 7.31) - 0.5) * 2 * _MixAmp
                              - _Priority[id] * _PriorityBias;
                }
                float s1 = 1e3, s2 = 1e3;
                [unroll] for (int k4 = 0; k4 < 9; k4++)
                    if (score[k4] < s1) { s1 = score[k4]; best = k4; }
                if (best < 0) return 0; // 주변 고체가 건설 타일뿐
                [unroll] for (int k5 = 0; k5 < 9; k5++)
                    // 같은 그림을 쓰는 타일끼리(흙·잔디흙)는 경계를 그리지 않는다
                    if (ids[k5] != 0 && !IsStructure(ids[k5]) && _TileInfo[ids[k5]].x != _TileInfo[ids[best]].x && score[k5] < s2) { s2 = score[k5]; second = k5; }

                float3 col = SampleTile(ids[best], p, gx, gy);
                if (second >= 0)
                {
                    float gap = s2 - s1;
                    float t = saturate(0.5 + gap / (2 * px));
                    col = lerp(SampleTile(ids[second], p, gx, gy), col, t);
                    col *= 1 - (1 - smoothstep(0, _SeamWidth, gap)) * _SeamDark;
                }

                // 잔디층: 주변 '잔디가 자라는 공기'에 가까운 흙 면을 덮는다.
                // 딱 잘린 띠가 아니라 흙과 뒤섞이게 — 넓은 폭으로 서서히 옅어지고, 흙 그림의 어두운 틈부터 먼저 파고든다.
                // 외곽 테두리보다 먼저 얹어 흙과 같은 음영을 받는다.
                if (_GrassLayers.z > 0 && _TileInfo[ids[best]].z > 0)
                {
                    float dG = 1e3;
                    int kind = 0;
                    [unroll] for (int k6 = 0; k6 < 9; k6++)
                        if (k6 != 4 && ids[k6] == 0 && grass[k6] > 0 && d[k6] < dG) { dG = d[k6]; kind = grass[k6]; }
                    if (kind > 0)
                    {
                        float depth = _GrassDepth * lerp(1 - _GrassRagged, 1, Fbm(p * _GrassFreq));
                        float lum = dot(col, float3(0.3, 0.59, 0.11));
                        float m = saturate(0.5 + (depth - dG) / max(_GrassSoft, px) + (0.35 - lum) * _GrassCrevice);
                        float gs = 1 / _GrassCells;
                        float3 g = SAMPLE_TEXTURE2D_ARRAY_GRAD(_TileTex, sampler_TileTex, p * gs,
                                       kind > 1 ? _GrassLayers.y : _GrassLayers.x, gx * gs, gy * gs).rgb;
                        col = lerp(col, g, m);
                    }
                }

                // 공기와 맞닿은 외곽 테두리
                col *= 1 - (1 - smoothstep(0, _OutlineWidth, -f)) * _OutlineDark;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
