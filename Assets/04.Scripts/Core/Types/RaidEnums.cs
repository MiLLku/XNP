/// <summary>
/// 레이드 진행 상태 열거형.
/// </summary>
public enum RaidState
{
    /// <summary>레이드 없음 (평상시)</summary>
    None = 0,

    /// <summary>레이드 예고 — 접근 연출 중</summary>
    Approaching = 1,

    /// <summary>레이드 진행 중</summary>
    InProgress = 2,

    /// <summary>레이드 철수 중 — 남은 개체 도주</summary>
    Retreating = 3,

    /// <summary>레이드 완료 (전멸 또는 철수 완료)</summary>
    Completed = 4
}

/// <summary>
/// 레이드 난이도 열거형.
/// RaidData에서 분류 및 선택 조건에 사용합니다.
/// </summary>
public enum RaidDifficulty
{
    Easy    = 0,
    Normal  = 1,
    Hard    = 2,
    Extreme = 3
}
