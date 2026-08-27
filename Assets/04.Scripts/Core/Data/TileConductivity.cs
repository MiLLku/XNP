/// <summary>
/// 타일별 열 전도율 — 방의 벽을 통해 열이 새는 속도의 배율.
///
/// 값이 낮을수록 단열이 잘 됩니다. 돌(1.0)이 기준입니다.
/// 흙은 공기를 머금어 단열이 좋고, 금속 광맥은 열을 잘 흘려보냅니다.
///
/// 진행도상 깊은 층일수록 금속·수정이 많아지므로, 심층 기지는 같은 벽 두께로도
/// 온도를 유지하기 어려워집니다 — 깊이 중심 난이도와 같은 방향입니다.
///
/// <see cref="TileHardness"/>와 같은 형태로 두어 타일 데이터가 한 자리에 모이게 했습니다.
/// </summary>
public static class TileConductivity
{
    /// <summary>기준 전도율 (돌)</summary>
    public const float DEFAULT = 1.0f;

    /// <summary>
    /// 타일의 열 전도율을 반환합니다. 미등록 타일은 <see cref="DEFAULT"/>.
    /// </summary>
    public static float Get(TileType tile)
    {
        switch (tile)
        {
            // ── 흙 계열: 단열이 좋다 ──
            case TileType.Dirt:
            case TileType.GrassDirt:
                return 0.5f;

            case TileType.ProcessedDirt:
                return 0.4f;   // 다져서 더 촘촘하다

            // ── 기준 ──
            case TileType.Stone:
                return 1.0f;

            case TileType.Coal:
                return 0.6f;   // 탄소질이라 열을 덜 흘린다

            // ── 금속 광맥: 열을 잘 흘려보낸다 ──
            case TileType.CopperOre:
                return 2.2f;
            case TileType.IronOre:
                return 1.9f;
            case TileType.SilverOre:
                return 2.6f;
            case TileType.GoldOre:
                return 2.8f;

            case TileType.Crystal:
                return 1.5f;

            // ── 공기·사다리는 벽이 아니므로 경계에 나타나지 않는다 ──
            case TileType.Air:
            case TileType.Ladder:
                return DEFAULT;

            default:
                return DEFAULT;
        }
    }

    /// <summary>타일 ID(정수)로 조회합니다.</summary>
    public static float Get(int tileId) => Get((TileType)tileId);
}
