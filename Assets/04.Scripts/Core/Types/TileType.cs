/// <summary>
/// 타일 종류 식별자 (GameMap.TileGrid 값).
///
/// 정수값 = GameMap.TileGrid의 raw int 값과 동일합니다.
/// ResourceManager의 TileEntry / DropEntry에서 enum으로 선택할 수 있도록 정의합니다.
/// </summary>
public enum TileType
{
    Air          = 0,  // 빈 공간
    Dirt         = 1,  // 흙
    Stone        = 2,  // 돌
    CopperOre    = 3,  // 구리 광석
    IronOre      = 4,  // 철 광석
    GoldOre      = 5,  // 금 광석
    GrassDirt    = 6,  // 잔디흙 (지표면)
    ProcessedDirt = 7, // 가공된 흙
    Ladder       = 8,  // 사다리
    Coal         = 9,  // 석탄
    SilverOre    = 10, // 은 광석
    Crystal      = 11, // 수정

    Special = 99,
}
