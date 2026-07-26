/// <summary>
/// 타일별 채광 경도 — 채광 소요 시간의 배율.
///
/// 진행도 설계상 '깊이'가 주축이므로, 깊은 층에서만 나오는 광물일수록 경도가 높다.
/// 물리적으로 막지는 않는다(도구 티어 없음). 대신 시간 비용이 급격히 늘어나
/// 채광 속도 확보(직원 스킬 + 연구 MiningSpeedBonus) 없이는 심층 채굴이 비현실적이 된다.
///
/// 깊이 분포는 MapGenerator.PlaceMineralClusters 참고:
///   석탄 -3~-20 · 구리 -10~-30 · 철 -20~-45 · 은 -25~-55 · 금 -40~-70 · 수정 -55~-90
/// </summary>
public static class TileHardness
{
    /// <summary>
    /// 타일 채광 시간 배율을 반환합니다. 미등록 타일은 1.0(기본).
    /// </summary>
    public static float Get(TileType tile)
    {
        switch (tile)
        {
            // ── 지표층: 빠르게 파인다 ──
            case TileType.Dirt:
            case TileType.GrassDirt:
            case TileType.ProcessedDirt:
                return 0.6f;

            case TileType.Stone:
                return 1.0f;

            // ── T1 얕은 층 ──
            case TileType.Coal:
                return 1.2f;
            case TileType.CopperOre:
                return 1.5f;

            // ── T2 중간층 ──
            case TileType.IronOre:
                return 2.2f;

            // ── T3 깊은 층 ──
            case TileType.SilverOre:
                return 3.0f;
            case TileType.GoldOre:
                return 4.0f;

            // ── T4 심층 ──
            case TileType.Crystal:
                return 6.0f;

            default:
                return 1.0f;
        }
    }

    /// <summary>호환용 int 오버로드.</summary>
    public static float Get(int tileId) => Get((TileType)tileId);
}
