/// <summary>
/// UI 패널 종류 열거형.
/// UIManager에서 패널을 식별하고 토글하는 데 사용됩니다.
/// </summary>
public enum UIPanelType
{
    /// <summary>패널 없음 (기본값)</summary>
    None,
    /// <summary>상호작용 모드 표시 패널</summary>
    InteractionMode,
    /// <summary>상단 자원 표시 및 인벤토리</summary>
    ResourceInventory,
    /// <summary>작업 할당 패널</summary>
    WorkAssignment,
    /// <summary>화면 효과 오버레이</summary>
    ScreenOverlay,
    /// <summary>건설 메뉴</summary>
    ConstructionUI,
    /// <summary>생산 건물 레시피 UI</summary>
    ProductionUI,
    /// <summary>연구 작업대 상태/제어 UI</summary>
    ResearchWorkbenchUI,

    /// <summary>직원 스킬 트리 패널</summary>
    SkillTreeUI,

    /// <summary>직원 채용 패널</summary>
    HiringUI,

    /// <summary>연구 트리 패널</summary>
    ResearchTreeUI,

    /// <summary>레터(메시지 로그) 상세 팝업</summary>
    LetterDetail,

    /// <summary>직원 작업 일정(스케줄) 편집 패널</summary>
    ScheduleUI,

    /// <summary>직원 관리 패널 (상태·장비·필수 소지)</summary>
    EmployeeUI,

    /// <summary>개발용 디버그 패널 (차단 스위치·즉시 실행·자원 지급)</summary>
    DebugUI,

    /// <summary>냉난방기 목표 온도 설정 패널</summary>
    ClimateControlUI,

    /// <summary>아이템 정보 오버레이 (설명·파종조건·파종 버튼)</summary>
    ItemInfo,

    /// <summary>재배 창 (밭·수경재배기 칸별 작물 선택)</summary>
    CropPlotUI,

    /// <summary>침식 배양기 목표 침식 설정</summary>
    ErosionIncubatorUI,

    /// <summary>생산 계획 창 (스토브 등 — 계획 목록·반복·재료 필터)</summary>
    ProductionPlanUI,

    /// <summary>침대 주인 배정 창</summary>
    BedAssignUI,

    /// <summary>단련장 — 직원 레벨업 창</summary>
    LevelUpUI,
}
