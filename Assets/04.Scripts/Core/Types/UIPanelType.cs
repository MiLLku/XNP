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
}
