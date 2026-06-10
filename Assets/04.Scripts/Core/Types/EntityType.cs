/// <summary>
/// 맵에 배치되는 개체(식물·건물·바닥 타일) 식별자.
///
/// 정수값 = ResourceManager.EntityEntry / StampElement.id / MapEntity.id의 raw int 값과 동일합니다.
/// ResourceManager의 EntityEntry에서 enum으로 프리팹을 매핑할 수 있도록 정의합니다.
/// </summary>
public enum EntityType
{
    None = 0,

    // 식물 (Plants)
    ToxicFern         = 20,
    CorruptedMushroom = 21,
    Tree2x3           = 2001,
    BerryBush         = 2003,

    // 건물 / 구조물 (Buildings)
    HiringOffice = 2002,
    WoodLadder   = 2004,
    Chest        = 2005,
    WoodDoor     = 2006,

    // 바닥 타일 빌드 프리팹 (Floor build prefabs)
    WoodFloor   = 2010,
    StoneFloor  = 2011,
    MetalFloor  = 2012,
    DirtFloor   = 2013,
    CopperFloor = 2014,
    GoldFloor   = 2015,
    SilverFloor = 2016,
}
