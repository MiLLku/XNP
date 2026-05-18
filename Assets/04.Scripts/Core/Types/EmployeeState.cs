/// <summary>
/// 직원 상태 열거형.
/// 직원의 현재 행동/상태를 나타냅니다.
/// </summary>
public enum EmployeeState
{
    /// <summary>대기 중 (작업 없음)</summary>
    Idle,

    /// <summary>목적지로 이동 중</summary>
    Moving,

    /// <summary>작업 수행 중</summary>
    Working,

    /// <summary>휴식 중</summary>
    Resting,

    /// <summary>식사 중</summary>
    Eating,

    /// <summary>소집 상태 (플레이어 직접 조작)</summary>
    Drafted,

    /// <summary>정신 붕괴 상태</summary>
    MentalBreak,

    /// <summary>사망</summary>
    Dead
}
