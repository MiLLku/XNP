/// <summary>
/// 오락 건물 종류 식별자.
///
/// 정수값 = BuildingData.buildingID = GameIDRegistry.Buildings의 오락 대역(3500~3599)과 일치합니다.
/// (enum↔id 연동 규칙: raw int 상수 대신 enum으로 의미를 명확히 하고 충돌을 방지합니다.
///  기존 패턴: TileType, EntityType, PowerBuildingType.)
/// </summary>
public enum RecreationBuildingType
{
    /// <summary>다트판 (1x2, 무전력, 저성능 오락)</summary>
    DartBoard = 3500,

    /// <summary>게임기 (2x2, 전력 소비, 고성능 오락)</summary>
    ArcadeMachine = 3501,
}
