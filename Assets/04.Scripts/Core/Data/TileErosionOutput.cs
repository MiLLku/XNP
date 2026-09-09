/// <summary>
/// 타일이 스스로 뿜는 침식 — 방의 경계에 맞닿아 있으면 그 방의 침식을 올립니다.
///
/// <see cref="TileHeatOutput"/>과 완전히 대칭입니다. 등록·해제가 없고,
/// 방을 재계산할 때 경계 접촉면을 훑으며 합산하므로 채굴하거나 벽으로 덮으면 자동 반영됩니다.
/// 맞닿은 면이 넓을수록 더 더러워지는 것도 그대로 따라옵니다.
///
/// 개체 발원지(<c>TerrainErosionEmitter</c>)와 다른 점은 <b>지형 그 자체가 오염원</b>이라는 것입니다 —
/// 제거하려면 캐내야 하고, 캐는 동안 작업자가 침식을 뒤집어씁니다.
/// </summary>
public static class TileErosionOutput
{
    /// <summary>침식을 뿜지 않는 타일의 값</summary>
    public const float NONE = 0f;

    /// <summary>접촉면 하나가 방에 넣는 초당 침식량. 정의가 없으면 0.</summary>
    public static float Get(TileType tile) => Get((int)tile);

    /// <inheritdoc cref="Get(TileType)"/>
    public static float Get(int tileId)
    {
        var def = TileDefinitionLookup.Find(tileId);
        return def != null ? def.erosionOutput : NONE;
    }

    /// <summary>이 타일이 방 침식을 올릴 수 있는 한계치. 0 이하면 한계 없음.</summary>
    public static float GetSaturation(int tileId)
    {
        var def = TileDefinitionLookup.Find(tileId);
        return def != null ? def.erosionSaturation : 0f;
    }
}
