/// <summary>
/// 정신 이벤트 타입.
/// 정신력 수치가 낮을 때 확률적으로 발생하는 이벤트 종류입니다.
/// </summary>
public enum MentalEventType
{
    /// <summary>없음</summary>
    None,

    /// <summary>작업 속도 감소 (일시적)</summary>
    WorkSlowdown,

    /// <summary>작업 거부 (새 작업 할당 불가)</summary>
    RefuseWork,

    /// <summary>방황 (작업 취소 + 랜덤 이동)</summary>
    Wander,

    /// <summary>감정 폭발 (주변 직원 정신력 감소)</summary>
    EmotionalOutburst,

    /// <summary>정신 붕괴 (정신력 0에서만)</summary>
    MentalBreak
}

/// <summary>
/// 정신 이벤트 심각도 등급.
/// 정신력 비율에 따라 결정됩니다.
/// </summary>
public enum MentalSeverity
{
    /// <summary>정상 (이벤트 없음)</summary>
    Normal,

    /// <summary>경미 (mental < 50%)</summary>
    Low,

    /// <summary>중간 (mental < 30%)</summary>
    Medium,

    /// <summary>심각 (mental < 15%)</summary>
    High
}
