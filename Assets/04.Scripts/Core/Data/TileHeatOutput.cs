/// <summary>
/// 타일이 스스로 내는 열 — 방의 경계에 맞닿아 있으면 그 방을 데웁니다.
///
/// 깊이별 지열을 따로 계산하지 않고 <b>뜨거운 타일을 배치하는 것</b>으로 대신합니다.
/// 심층에만 나오는 광석에 발열량을 주면 "깊이 팔수록 덥다"가 저절로 성립하고,
/// 플레이어는 그것을 눈에 보이는 타일로 인지·대응할 수 있습니다.
///
/// 등록·해제가 없습니다. 방을 재계산할 때 경계 접촉면을 훑으며 합산하므로
/// 채굴해서 파내거나 단열 벽으로 덮으면 다음 재계산에 자동 반영됩니다.
/// 맞닿은 면이 넓을수록 더 뜨거워지는 것도 그대로 따라옵니다.
///
/// <see cref="TileConductivity"/>와 짝입니다 — 같은 접촉면 루프에서 함께 더해집니다.
/// </summary>
public static class TileHeatOutput
{
    /// <summary>발열하지 않는 타일의 값</summary>
    public const float NONE = 0f;

    /// <summary>
    /// 접촉면 하나가 방에 넣는 초당 열량. 미등록 타일은 0.
    /// </summary>
    public static float Get(TileType tile)
    {
        switch (tile)
        {
            // ── 심층 광물: 파고들면 방이 달아오른다 ──
            // 수정은 -55~-90 깊이에만 나오므로, 이 값 하나로 심층 채굴이 더워진다.
            case TileType.Crystal:
                return 3.0f;

            default:
                return NONE;
        }
    }

    /// <summary>타일 ID(정수)로 조회합니다.</summary>
    public static float Get(int tileId) => Get((TileType)tileId);
}
