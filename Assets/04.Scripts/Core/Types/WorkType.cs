/// <summary>
/// 작업 종류 열거형.
/// 직원의 작업 할당, 우선순위 설정, 작업 능력 판단에 사용됩니다.
/// </summary>
public enum WorkType
{
    /// <summary>작업 없음 (기본값)</summary>
    None,
    /// <summary>채광</summary>
    Mining,
    /// <summary>벌목</summary>
    Chopping,
    /// <summary>연구</summary>
    Research,
    /// <summary>제작</summary>
    Crafting,
    /// <summary>원예 (수확)</summary>
    Gardening,
    /// <summary>운반</summary>
    Hauling,
    /// <summary>건설</summary>
    Building,
    /// <summary>철거</summary>
    Demolish,
    /// <summary>세척 (방에 고인 침식 제거)</summary>
    Cleaning,
    /// <summary>휴식</summary>
    Resting,
    /// <summary>식사</summary>
    Eating
}

/// <summary>
/// 작업 종류의 기본(내재) 우선순위 — 단일 출처.
///
/// 용도:
///   1) 직원 작업 우선순위의 초기값 (EmployeeWork.InitializeWorkPriorities)
///   2) 직원이 설정한 우선순위가 동률일 때의 작업 종류 간 타이브레이크
///      (WorkSystemManager.GetCandidateOrders)
///
/// 낮은 값 = 먼저 수행. 순서를 바꾸면 두 용도 모두에 일괄 반영됩니다.
/// </summary>
public static class WorkTypeDefaults
{
    /// <summary>자동 픽업 작업의 기본 우선순위 순서 (앞에 있을수록 먼저).</summary>
    public static readonly WorkType[] BaseOrder =
    {
        WorkType.Mining,
        WorkType.Chopping,
        WorkType.Crafting,
        WorkType.Research,
        WorkType.Gardening,
        WorkType.Cleaning,
        WorkType.Hauling,
        WorkType.Building,
        WorkType.Demolish,
    };

    /// <summary>기본 우선순위 값 (1부터). BaseOrder에 없는 타입은 99.</summary>
    public static int GetBasePriority(WorkType type)
    {
        for (int i = 0; i < BaseOrder.Length; i++)
        {
            if (BaseOrder[i] == type) return i + 1;
        }
        return 99;
    }
}
