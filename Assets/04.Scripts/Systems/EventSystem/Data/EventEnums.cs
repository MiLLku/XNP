/// <summary>
/// 이벤트 카테고리 열거형.
/// </summary>
public enum EventCategory
{
    /// <summary>지속형 (날씨 대체, 랜덤 기간 유지)</summary>
    Persistent,
    /// <summary>직원 (탈주, 질병, 갈등 등)</summary>
    Employee,
    /// <summary>자원 (발견, 고갈 등)</summary>
    Resource,
    /// <summary>방문자 (상인, 이민자 등)</summary>
    Visitor,
    /// <summary>스토리 이벤트</summary>
    Story,
    /// <summary>침략 (적 습격 등)</summary>
    Invasion,
    /// <summary>재해 (화재, 붕괴 등)</summary>
    Disaster,
    /// <summary>제노프스 등장</summary>
    XenopsAppearance
}

/// <summary>
/// 이벤트 발동 조건 타입 열거형.
/// </summary>
public enum ConditionType
{
    // 직원 관련
    /// <summary>직원 수</summary>
    EmployeeCount,
    /// <summary>평균 체력</summary>
    EmployeeAverageHealth,
    /// <summary>평균 정신력</summary>
    EmployeeAverageMental,
    /// <summary>평균 배고픔</summary>
    EmployeeAverageHunger,
    /// <summary>평균 피로도</summary>
    EmployeeAverageFatigue,
    /// <summary>작업 중인 직원 수</summary>
    EmployeeWorkingCount,
    /// <summary>유휴 직원 수</summary>
    EmployeeIdleCount,

    // 자원 관련
    /// <summary>특정 아이템 수량</summary>
    InventoryItemCount,
    /// <summary>전체 인벤토리 수량</summary>
    TotalInventoryCount,

    // 건물 관련
    /// <summary>건물 수</summary>
    BuildingCount,
    /// <summary>건설 중인 건물 수</summary>
    ConstructionCount,

    // 시간 관련
    /// <summary>플레이 시간 (초)</summary>
    PlayTime,
    /// <summary>경과 일수</summary>
    DayNumber,

    // 이벤트 관련
    /// <summary>특정 지속형 이벤트가 활성화 중인지</summary>
    ActivePersistentEvent,
    /// <summary>특정 이벤트가 이전에 발생했는지</summary>
    EventTriggeredBefore,

    // 기타
    /// <summary>순수 확률 (0~100)</summary>
    RandomChance,
    /// <summary>항상 참</summary>
    Always,

    // 침식/레이드
    /// <summary>모든 직원의 평균 침식 수치</summary>
    AverageErosionLevel,
    /// <summary>특정 침식 단계 이상인 직원 수 (value = 단계 int, threshold = 인원수)</summary>
    EmployeesAtErosionStage,
    /// <summary>레이드 진행 중 여부</summary>
    RaidActive,
    /// <summary>완료된 레이드 횟수</summary>
    RaidCount
}

/// <summary>
/// 비교 연산자 열거형.
/// </summary>
public enum ComparisonOperator
{
    /// <summary>초과 (>)</summary>
    GreaterThan,
    /// <summary>이상 (>=)</summary>
    GreaterOrEqual,
    /// <summary>미만 (&lt;)</summary>
    LessThan,
    /// <summary>이하 (&lt;=)</summary>
    LessOrEqual,
    /// <summary>같음 (==)</summary>
    Equal,
    /// <summary>다름 (!=)</summary>
    NotEqual
}

/// <summary>
/// 이벤트 효과 타입 열거형.
/// </summary>
public enum EffectType
{
    // 직원 스탯
    /// <summary>체력 변경</summary>
    ModifyHealth,
    /// <summary>정신력 변경</summary>
    ModifyMental,
    /// <summary>배고픔 변경</summary>
    ModifyHunger,
    /// <summary>피로도 변경</summary>
    ModifyFatigue,

    // 자원
    /// <summary>아이템 추가</summary>
    AddItem,
    /// <summary>아이템 제거</summary>
    RemoveItem,

    // 직원 관리
    /// <summary>직원 추가</summary>
    SpawnEmployee,
    /// <summary>랜덤 직원 제거 (탈주 등)</summary>
    RemoveRandomEmployee,

    // 건물
    /// <summary>랜덤 건물 피해</summary>
    DamageRandomBuilding,
    /// <summary>랜덤 건물 파괴</summary>
    DestroyRandomBuilding,

    // 맵
    /// <summary>랜덤 타일 파괴</summary>
    DestroyRandomTiles,

    // 작업
    /// <summary>작업 속도 변경 (지속형)</summary>
    ModifyWorkSpeed,
    /// <summary>모든 작업 일시정지</summary>
    PauseAllWork,

    // 특수
    /// <summary>다른 이벤트 발동</summary>
    TriggerEvent,
    /// <summary>지속형 이벤트 종료</summary>
    EndPersistentEvent,

    // 메시지
    /// <summary>알림 표시</summary>
    ShowNotification,

    // 제노프스
    /// <summary>제노프스 스폰 (targetId = XenopsData ID, 카메라 근처 위치에 등장)</summary>
    SpawnXenops,

    // 침식/레이드
    /// <summary>레이드 시작 (targetId = RaidData.raidId)</summary>
    StartRaid,
    /// <summary>대상 직원의 침식 수치 변경 (value = 변화량, 음수 = 감소)</summary>
    ModifyErosion,
    /// <summary>포스트 레이드 가속 회복 즉시 시작</summary>
    StartPostRaidRecovery,

    // 적 스폰
    /// <summary>안개 속 랜덤 위치에 제노프스 등장 (targetId = XenopsData ID)</summary>
    SpawnXenopsInFog
}

/// <summary>
/// 효과 대상 열거형.
/// </summary>
public enum EffectTarget
{
    /// <summary>모든 직원</summary>
    AllEmployees,
    /// <summary>랜덤 1명</summary>
    RandomEmployee,
    /// <summary>랜덤 N명 (value로 수량 지정)</summary>
    RandomEmployees,
    /// <summary>체력 가장 낮은 직원</summary>
    LowestHealthEmployee,
    /// <summary>정신력 가장 낮은 직원</summary>
    LowestMentalEmployee,
    /// <summary>피로도 가장 높은 직원</summary>
    HighestFatigueEmployee,
    /// <summary>작업 중인 직원들</summary>
    WorkingEmployees,
    /// <summary>유휴 직원들</summary>
    IdleEmployees,
    /// <summary>특정 아이템 (itemId 사용)</summary>
    SpecificItem,
    /// <summary>랜덤 건물</summary>
    RandomBuilding,
    /// <summary>모든 건물</summary>
    AllBuildings,
    /// <summary>전역 인벤토리</summary>
    GlobalInventory,
    /// <summary>전역 (작업 속도 등)</summary>
    Global
}
