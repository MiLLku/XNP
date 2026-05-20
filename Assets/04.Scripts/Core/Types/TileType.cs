/// <summary>
/// 타일 종류 식별자 (GameMap.TileGrid 값).
///
/// 정수값 = GameMap.TileGrid의 raw int 값과 동일합니다.
/// ResourceManager의 TileEntry / DropEntry에서 enum으로 선택할 수 있도록 정의합니다.
/// </summary>
public enum TileType
{
    /// <summary>빈 공간 (이동 가능)</summary>
    Air = 0,

    /// <summary>흙</summary>
    Dirt = 1,

    /// <summary>돌</summary>
    Stone = 2,

    /// <summary>철 광석 타일</summary>
    IronOre = 3,

    /// <summary>구리 광석 타일</summary>
    CopperOre = 4,

    /// <summary>은 광석 타일</summary>
    SilverOre = 5,

    /// <summary>금 광석 타일</summary>
    GoldOre = 6,

    /// <summary>바닥/통로 타일 (이동 가능, 채광 불가)</summary>
    Floor = 7,

    /// <summary>특수 타일 (마커·UI용)</summary>
    Special = 99,
}
