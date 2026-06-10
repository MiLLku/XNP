/// <summary>
/// 전력 건물 종류 식별자.
///
/// 정수값 = BuildingData.buildingID = GameIDRegistry.Buildings의 전력 대역(3400~3499)과 일치합니다.
/// (enum↔id 연동 규칙: raw int 상수 대신 enum으로 의미를 명확히 하고 충돌을 방지합니다.
///  기존 패턴: TileType, EntityType.)
/// </summary>
public enum PowerBuildingType
{
    /// <summary>풍력 발전기 (무한 가동, 4x2)</summary>
    WindGenerator = 3400,

    /// <summary>축전기 (전력 저장, 1x2)</summary>
    Battery = 3401,

    /// <summary>전선 (전력 전송, 1x1, 건축물과 겹쳐 설치)</summary>
    Wire = 3402,

    /// <summary>나무 화력 발전기 (연료 소비, 2x2)</summary>
    WoodGenerator = 3403,

    /// <summary>침식 융해 발전기 (2x2)</summary>
    ErosionGenerator = 3404,
}
