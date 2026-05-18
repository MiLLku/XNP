/// <summary>
/// 스케줄 시간대에 수행할 활동 유형.
/// 직원의 24시간 스케줄에서 각 시간대마다 하나씩 할당됩니다.
/// </summary>
public enum ScheduleActivity
{
    /// <summary>작업 (WorkSystemManager가 할당한 작업 수행)</summary>
    Work,

    /// <summary>취침 (Sleep 구역의 침대로 이동 → 피로 회복)</summary>
    Sleep,

    /// <summary>오락 (Recreation 구역의 시설로 이동 → 정신력 회복)</summary>
    Recreation,

    /// <summary>세척 (Wash 구역의 시설로 이동 → 침식 제거)</summary>
    Wash,

    /// <summary>자유 시간 (욕구 상태에 따라 AI가 자동 판단)</summary>
    Anything
}
