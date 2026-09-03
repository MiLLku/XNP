using System.Collections.Generic;

/// <summary>
/// 게임 전역 메시지 카탈로그.
///
/// 매니저 간 · 매니저→UI 통신에 쓰이는 메시지 타입을 한곳에 모읍니다.
/// "무엇이 언제 발행되는가"를 이 파일 하나로 훑을 수 있게 하는 것이 목적이며,
/// 새 메시지를 추가하면 GameMessageBus.Registry.cs에도 반드시 등록해야 합니다.
///
/// 규칙:
///   - 메시지는 읽기 전용 struct — 발행 후 수신 측이 내용을 바꿀 수 없습니다.
///   - 이름은 "일어난 일"의 과거형 (…Changed / …Spawned / …Completed).
///   - 개체 내부 통신(직원 상태 변화, 작업 지시 완료 등)은 메시지로 만들지 않습니다.
///     구독자가 이미 그 개체를 참조하고 있으므로 일반 C# 이벤트가 더 적합합니다.
/// </summary>

#region 시간 — DayCycle / TimeManager

/// <summary>게임 내 시각(시)이 바뀜</summary>
public readonly struct HourChangedMessage
{
    /// <summary>바뀐 시각 (0~23)</summary>
    public readonly int hour;

    public HourChangedMessage(int hour) => this.hour = hour;
}

/// <summary>날짜가 넘어감</summary>
public readonly struct NewDayMessage
{
    /// <summary>새 날짜</summary>
    public readonly int day;

    public NewDayMessage(int day) => this.day = day;
}

/// <summary>게임 배속이 바뀜</summary>
public readonly struct GameSpeedChangedMessage
{
    /// <summary>바뀐 배속</summary>
    public readonly int speed;

    public GameSpeedChangedMessage(int speed) => this.speed = speed;
}

/// <summary>일시정지 상태가 바뀜</summary>
public readonly struct GamePauseStateChangedMessage
{
    /// <summary>true면 정지 상태</summary>
    public readonly bool isPaused;

    public GamePauseStateChangedMessage(bool isPaused) => this.isPaused = isPaused;
}

#endregion

#region 직원 — EmployeeManager / ErosionManager

/// <summary>직원이 스폰됨</summary>
public readonly struct EmployeeSpawnedMessage
{
    public readonly Employee employee;

    public EmployeeSpawnedMessage(Employee employee) => this.employee = employee;
}

/// <summary>직원이 제거됨 (사망·해고)</summary>
public readonly struct EmployeeRemovedMessage
{
    public readonly Employee employee;

    public EmployeeRemovedMessage(Employee employee) => this.employee = employee;
}

/// <summary>직원이 침식으로 변이함</summary>
public readonly struct EmployeeMutatedMessage
{
    public readonly Employee employee;

    public EmployeeMutatedMessage(Employee employee) => this.employee = employee;
}

#endregion

#region 상호작용 — InteractionManager

/// <summary>플레이어 상호작용 모드가 바뀜</summary>
public readonly struct InteractionModeChangedMessage
{
    public readonly InteractionManager.InteractMode mode;

    public InteractionModeChangedMessage(InteractionManager.InteractMode mode) => this.mode = mode;
}

/// <summary>직원 선택이 바뀜</summary>
public readonly struct EmployeeSelectionChangedMessage
{
    /// <summary>선택된 직원 (null = 선택 해제)</summary>
    public readonly Employee employee;

    public EmployeeSelectionChangedMessage(Employee employee) => this.employee = employee;
}

/// <summary>구역 편집 대상이 바뀜</summary>
public readonly struct EditingZoneChangedMessage
{
    /// <summary>편집 중인 구역 ID (-1 = 없음)</summary>
    public readonly int zoneId;

    public EditingZoneChangedMessage(int zoneId) => this.zoneId = zoneId;
}

#endregion

#region 인벤토리 — InventoryManager / EquipmentStorageManager

/// <summary>글로벌 인벤토리 수량이 바뀜</summary>
public readonly struct InventoryChangedMessage
{
    /// <summary>변경된 아이템 (null = 특정 아이템이 아닌 전체 갱신)</summary>
    public readonly ItemData item;

    /// <summary>증감량 (음수 = 감소, 전체 갱신 시 0)</summary>
    public readonly int changeAmount;

    public InventoryChangedMessage(ItemData item, int changeAmount)
    {
        this.item = item;
        this.changeAmount = changeAmount;
    }

    /// <summary>특정 아이템을 지목하지 않는 전체 갱신 알림</summary>
    public static InventoryChangedMessage Refresh => new InventoryChangedMessage(null, 0);
}

/// <summary>장비 보관소 재고가 바뀜</summary>
public readonly struct EquipmentPoolChangedMessage
{
}

#endregion

#region 건설 — ConstructionManager

/// <summary>건물 배치 모드가 켜지거나 꺼짐</summary>
public readonly struct BuildingPlacementModeChangedMessage
{
    /// <summary>true면 배치 모드 진입</summary>
    public readonly bool isPlacing;

    /// <summary>배치하려는 건물 (해제 시 null)</summary>
    public readonly BuildingData building;

    public BuildingPlacementModeChangedMessage(bool isPlacing, BuildingData building)
    {
        this.isPlacing = isPlacing;
        this.building = building;
    }
}

/// <summary>건설 현장이 생성됨</summary>
public readonly struct ConstructionSiteCreatedMessage
{
    public readonly ConstructionSite site;

    public ConstructionSiteCreatedMessage(ConstructionSite site) => this.site = site;
}

/// <summary>건설 현장이 완공됨</summary>
public readonly struct ConstructionSiteCompletedMessage
{
    public readonly ConstructionSite site;

    public ConstructionSiteCompletedMessage(ConstructionSite site) => this.site = site;
}

/// <summary>건설 현장이 취소됨</summary>
public readonly struct ConstructionSiteCancelledMessage
{
    public readonly ConstructionSite site;

    public ConstructionSiteCancelledMessage(ConstructionSite site) => this.site = site;
}

#endregion

#region 방·구역 — RoomManager / ZoneManager

/// <summary>방 구획이 다시 계산됨</summary>
public readonly struct RoomsRebuiltMessage
{
}

/// <summary>구역이 생성됨</summary>
public readonly struct ZoneCreatedMessage
{
    public readonly Zone zone;

    public ZoneCreatedMessage(Zone zone) => this.zone = zone;
}

/// <summary>구역이 삭제됨</summary>
public readonly struct ZoneDeletedMessage
{
    public readonly int zoneId;

    public ZoneDeletedMessage(int zoneId) => this.zoneId = zoneId;
}

/// <summary>구역에 포함된 타일이 바뀜</summary>
public readonly struct ZoneTilesChangedMessage
{
    public readonly int zoneId;

    public ZoneTilesChangedMessage(int zoneId) => this.zoneId = zoneId;
}

#endregion

#region 이벤트 — EventManager

/// <summary>일회성 게임 이벤트가 발생함</summary>
public readonly struct GameEventTriggeredMessage
{
    public readonly EventData eventData;

    public GameEventTriggeredMessage(EventData eventData) => this.eventData = eventData;
}

/// <summary>지속형 이벤트가 시작됨 (한파·폭염 등)</summary>
public readonly struct PersistentEventStartedMessage
{
    public readonly EventData eventData;

    public PersistentEventStartedMessage(EventData eventData) => this.eventData = eventData;
}

/// <summary>지속형 이벤트가 끝남</summary>
public readonly struct PersistentEventEndedMessage
{
    public readonly EventData eventData;

    public PersistentEventEndedMessage(EventData eventData) => this.eventData = eventData;
}

/// <summary>플레이어가 이벤트 선택지를 고름</summary>
public readonly struct GameEventChoiceMadeMessage
{
    public readonly EventData eventData;
    public readonly EventChoice choice;

    public GameEventChoiceMadeMessage(EventData eventData, EventChoice choice)
    {
        this.eventData = eventData;
        this.choice = choice;
    }
}

#endregion

#region 알림 — NotificationManager

/// <summary>편지가 도착함</summary>
public readonly struct LetterAddedMessage
{
    public readonly Letter letter;

    public LetterAddedMessage(Letter letter) => this.letter = letter;
}

/// <summary>편지가 목록에서 사라짐 (읽음·만료)</summary>
public readonly struct LetterRemovedMessage
{
    public readonly Letter letter;

    public LetterRemovedMessage(Letter letter) => this.letter = letter;
}

/// <summary>경고 배너 목록이 갱신됨</summary>
public readonly struct AlertsRefreshedMessage
{
    /// <summary>
    /// 현재 활성 경고 목록.
    /// NotificationManager가 소유한 리스트를 그대로 전달하므로 수신 측은 읽기만 해야 합니다.
    /// </summary>
    public readonly List<AlertReport> alerts;

    public AlertsRefreshedMessage(List<AlertReport> alerts) => this.alerts = alerts;
}

#endregion

#region 습격 — RaidManager

/// <summary>습격이 시작됨</summary>
public readonly struct RaidStartedMessage
{
    public readonly RaidData raid;

    public RaidStartedMessage(RaidData raid) => this.raid = raid;
}

/// <summary>습격의 한 웨이브가 시작됨</summary>
public readonly struct RaidWaveStartedMessage
{
    /// <summary>웨이브 인덱스 (0부터)</summary>
    public readonly int waveIndex;

    public RaidWaveStartedMessage(int waveIndex) => this.waveIndex = waveIndex;
}

/// <summary>습격이 종료됨</summary>
public readonly struct RaidCompletedMessage
{
    public readonly RaidData raid;

    public RaidCompletedMessage(RaidData raid) => this.raid = raid;
}

#endregion

#region 연구 — ResearchManager / ResearchTreeManager

/// <summary>보유 연구 포인트가 바뀜</summary>
public readonly struct ResearchPointsChangedMessage
{
    public readonly float totalPoints;

    public ResearchPointsChangedMessage(float totalPoints) => this.totalPoints = totalPoints;
}

/// <summary>연구가 시작됨</summary>
public readonly struct ResearchStartedMessage
{
    public readonly ResearchNodeData node;

    public ResearchStartedMessage(ResearchNodeData node) => this.node = node;
}

/// <summary>연구가 완료됨</summary>
public readonly struct ResearchCompletedMessage
{
    public readonly ResearchNodeData node;

    public ResearchCompletedMessage(ResearchNodeData node) => this.node = node;
}

/// <summary>연구가 취소됨</summary>
public readonly struct ResearchCancelledMessage
{
    public readonly ResearchNodeData node;

    public ResearchCancelledMessage(ResearchNodeData node) => this.node = node;
}

/// <summary>진행 중인 연구의 진척도가 바뀜</summary>
public readonly struct ResearchProgressChangedMessage
{
    /// <summary>현재 누적 진척</summary>
    public readonly float current;

    /// <summary>완료에 필요한 총량</summary>
    public readonly float required;

    public ResearchProgressChangedMessage(float current, float required)
    {
        this.current = current;
        this.required = required;
    }
}

/// <summary>연구 노드의 상태가 바뀜 (잠김/해금/완료 등)</summary>
public readonly struct ResearchNodeStateChangedMessage
{
    public readonly string nodeId;
    public readonly ResearchNodeState state;

    public ResearchNodeStateChangedMessage(string nodeId, ResearchNodeState state)
    {
        this.nodeId = nodeId;
        this.state = state;
    }
}

/// <summary>연구로 건물이 해금됨</summary>
public readonly struct BuildingUnlockedMessage
{
    public readonly BuildingData building;

    public BuildingUnlockedMessage(BuildingData building) => this.building = building;
}

/// <summary>연구로 제작법이 해금됨</summary>
public readonly struct RecipeUnlockedMessage
{
    public readonly CraftingRecipe recipe;

    public RecipeUnlockedMessage(CraftingRecipe recipe) => this.recipe = recipe;
}

#endregion

#region 스킬 — SkillPointManager

/// <summary>전역 스킬 포인트 상한이 올라감</summary>
public readonly struct SkillPointCapIncreasedMessage
{
    /// <summary>늘어난 뒤의 전역 보너스 포인트</summary>
    public readonly int globalBonusPoints;

    public SkillPointCapIncreasedMessage(int globalBonusPoints) => this.globalBonusPoints = globalBonusPoints;
}

#endregion

#region 디버그 — DebugManager

/// <summary>디버그 차단 플래그가 바뀜</summary>
public readonly struct DebugFlagsChangedMessage
{
}

#endregion
