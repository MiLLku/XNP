/// <summary>
/// 세척 건물 종류 식별자.
///
/// 정수값 = BuildingData.buildingID = GameIDRegistry.Buildings의 위생 대역(3600~3699)과 일치합니다.
/// (enum↔id 연동 규칙: raw int 상수 대신 enum으로 의미를 명확히 하고 충돌을 방지합니다.
///  기존 패턴: TileType, EntityType, PowerBuildingType, RecreationBuildingType.)
///
/// 세 건물은 같은 WashStation 컴포넌트를 쓰고 인스펙터 값(동시 인원·세척 속도)만 다릅니다.
/// </summary>
public enum WashBuildingType
{
    /// <summary>간이 세척대 (4x3, 무전력, 동시 1명)</summary>
    SmallWashStation = 3600,

    /// <summary>세척실 (6x3, 전력 소비, 동시 2명)</summary>
    MediumWashStation = 3601,

    /// <summary>정화 세척실 (8x3, 전력 소비, 동시 4명)</summary>
    LargeWashStation = 3602,
}
