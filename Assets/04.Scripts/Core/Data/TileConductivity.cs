/// <summary>
/// 타일별 열 전도율 — 방의 벽을 통해 열이 새는 속도의 배율.
///
/// 값이 낮을수록 단열이 잘 됩니다. 돌(1.0)이 기준입니다.
/// 흙은 공기를 머금어 단열이 좋고, 금속 광맥은 열을 잘 흘려보냅니다.
///
/// 진행도상 깊은 층일수록 금속·수정이 많아지므로, 심층 기지는 같은 벽 두께로도
/// 온도를 유지하기 어려워집니다 — 깊이 중심 난이도와 같은 방향입니다.
///
/// <b>값은 더 이상 여기 있지 않습니다.</b> TileDefinition 에셋의 thermalConductivity 필드에서
/// 읽어 옵니다. 이 클래스는 기존 호출부를 살리기 위한 얇은 창구입니다.
/// </summary>
public static class TileConductivity
{
    /// <summary>기준 전도율 (돌). 정의를 찾지 못한 타일에도 이 값을 씁니다.</summary>
    public const float DEFAULT = 1.0f;

    /// <summary>
    /// 타일의 열 전도율을 반환합니다. 정의가 없으면 <see cref="DEFAULT"/>.
    /// </summary>
    public static float Get(TileType tile) => Get((int)tile);

    /// <summary>타일 ID(정수)로 조회합니다.</summary>
    public static float Get(int tileId)
    {
        var def = TileDefinitionLookup.Find(tileId);
        return def != null ? def.thermalConductivity : DEFAULT;
    }
}
