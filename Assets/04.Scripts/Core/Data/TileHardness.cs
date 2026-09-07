/// <summary>
/// 타일별 채광 경도 — 채광 소요 시간의 배율.
///
/// 진행도 설계상 '깊이'가 주축이므로, 깊은 층에서만 나오는 광물일수록 경도가 높다.
/// 물리적으로 막지는 않는다(도구 티어 없음). 대신 시간 비용이 급격히 늘어나
/// 채광 속도 확보(직원 스킬 + 연구 MiningSpeedBonus) 없이는 심층 채굴이 비현실적이 된다.
///
/// <b>값은 더 이상 여기 있지 않다.</b> TileDefinition 에셋의 hardness 필드에서 읽어 온다.
/// 이 클래스는 기존 호출부(<c>TileHardness.Get(tileId)</c>)를 그대로 살리기 위한 얇은 창구다.
///
/// 깊이 분포는 MapGenerator.PlaceMineralClusters 참고:
///   석탄 -3~-20 · 구리 -10~-30 · 철 -20~-45 · 은 -25~-55 · 금 -40~-70 · 수정 -55~-90
/// </summary>
public static class TileHardness
{
    /// <summary>정의를 찾지 못한 타일의 경도</summary>
    public const float DEFAULT = 1.0f;

    /// <summary>
    /// 타일 채광 시간 배율을 반환합니다. 정의가 없으면 <see cref="DEFAULT"/>.
    /// </summary>
    public static float Get(TileType tile) => Get((int)tile);

    /// <summary>타일 ID(정수)로 조회합니다.</summary>
    public static float Get(int tileId)
    {
        var def = TileDefinitionLookup.Find(tileId);
        return def != null ? def.hardness : DEFAULT;
    }
}
