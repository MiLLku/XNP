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
    /// <summary>휴식</summary>
    Resting,
    /// <summary>식사</summary>
    Eating
}
