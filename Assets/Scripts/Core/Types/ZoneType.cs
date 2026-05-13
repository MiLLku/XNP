/// <summary>
/// 구역 용도 유형.
/// 직원의 스케줄 활동과 매핑되어 사용됩니다.
///
/// 매핑:
///   Sleep 스케줄       → ZoneType.Sleep 구역의 침대
///   Recreation 스케줄  → ZoneType.Recreation 구역의 오락 시설
///   Wash 스케줄        → ZoneType.Wash 구역의 세척 시설
///   Work 스케줄        → ZoneType.Work 구역 (작업 범위 제한)
/// </summary>
public enum ZoneType
{
    /// <summary>취침 구역 (침대가 있는 방)</summary>
    Sleep,

    /// <summary>오락 구역 (오락 시설이 있는 공간)</summary>
    Recreation,

    /// <summary>세척 구역 (샤워/세척 시설)</summary>
    Wash,

    /// <summary>작업 구역 (작업 범위를 제한)</summary>
    Work,

    /// <summary>저장 구역 (자원 보관)</summary>
    Storage,

    /// <summary>제한 구역 (출입 금지)</summary>
    Restricted
}
