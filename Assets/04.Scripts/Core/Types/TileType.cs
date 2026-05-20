/// <summary>
/// 타일 종류 식별자 (GameMap.TileGrid 값).
///
/// 정수값 = GameMap.TileGrid의 raw int 값과 동일합니다.
/// ResourceManager의 TileEntry / DropEntry에서 enum으로 선택할 수 있도록 정의합니다.
/// </summary>
public enum TileType
{
    Air = 0, // 빈 공간
    Dirt = 1,
    GrassDirt = 2,
    Stone = 3,
    IronOre = 4,
    CopperOre = 5,
    SilverOre = 6,
    GoldOre = 7,
    
    Special = 99,
}
