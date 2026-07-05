/// <summary>
/// 경고(상시 배너) 심각도. UI 배경색 매핑에 사용한다.
/// </summary>
public enum AlertSeverity
{
    /// <summary>정보 (회색)</summary>
    Info,
    /// <summary>주의 (주황) — 예: 침식 50% 이상</summary>
    Caution,
    /// <summary>위험 (빨강) — 예: 침식 70% 이상</summary>
    Critical
}
