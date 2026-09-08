/// <summary>
/// 타일이 스스로 내는 열 — 방의 경계에 맞닿아 있으면 그 방을 데웁니다.
///
/// "깊이 팔수록 덥다"의 <b>기준선</b>은 <see cref="TemperatureManager.GetAmbientAtDepth"/>의
/// 지열이 담당합니다. 뜨거운 타일은 그 위에 얹히는 <b>국소 스파이크</b>입니다 —
/// "이 광맥 옆은 유난히 더 뜨겁다"를 눈에 보이는 타일로 표현하고, 플레이어가 대응할 수 있게 합니다.
///
/// 등록·해제가 없습니다. 방을 재계산할 때 경계 접촉면을 훑으며 합산하므로
/// 채굴해서 파내거나 단열 벽으로 덮으면 다음 재계산에 자동 반영됩니다.
/// 맞닿은 면이 넓을수록 더 뜨거워지는 것도 그대로 따라옵니다.
///
/// <b>값은 더 이상 여기 있지 않습니다.</b> TileDefinition 에셋의 heatOutput 필드에서
/// 읽어 옵니다. <see cref="TileConductivity"/>와 짝이며 같은 접촉면 루프에서 함께 더해집니다.
/// </summary>
public static class TileHeatOutput
{
    /// <summary>발열하지 않는 타일의 값</summary>
    public const float NONE = 0f;

    /// <summary>
    /// 접촉면 하나가 방에 넣는 초당 열량. 정의가 없으면 0.
    /// </summary>
    public static float Get(TileType tile) => Get((int)tile);

    /// <summary>타일 ID(정수)로 조회합니다.</summary>
    public static float Get(int tileId)
    {
        var def = TileDefinitionLookup.Find(tileId);
        return def != null ? def.heatOutput : NONE;
    }
}
