using UnityEngine;

/// <summary>
/// 정화 가동 중 위협 강화 규칙 — 튜닝값을 한 곳에 모읍니다 (임시 수치).
///
/// 가동 중에는
///   - 습격 점수가 날이 갈수록 커진다 (ThreatScore 총점 × <see cref="ThreatMultiplier"/>) — 위협 단계는 그대로(기지 가치 문턱)
///   - 자동 습격 간격이 짧아진다 (RaidManager × <see cref="RaidIntervalScale"/>)
///   - 랜덤 이벤트가 위협 분류로 쏠리고 잦아진다 (<see cref="WeightMultiplier"/>, <see cref="EventIntervalScale"/>)
///   - 건물을 노리는 침식 사수는 가동 중인 장치를 우선 노린다 (<see cref="TargetDistanceScale"/>)
/// 가동 중이 아니면 모든 값이 중립(1)입니다.
/// </summary>
public static class PurificationThreat
{
    #region 튜닝값

    /// <summary>가동 중 위협 분류 이벤트의 가중치 배수</summary>
    private const float THREAT_WEIGHT_MULT = 6f;

    /// <summary>가동 중 일상 분류(직원·이야기·자원·방문) 이벤트의 가중치 배수</summary>
    private const float CALM_WEIGHT_MULT = 0.15f;

    /// <summary>랜덤 이벤트 간격 배수 — 가동 직후 → 완료 직전</summary>
    private const float EVENT_INTERVAL_START = 0.4f;
    private const float EVENT_INTERVAL_END = 0.2f;

    /// <summary>자동 습격 간격 배수 — 가동 직후 → 완료 직전 (기본 0.7~1.5일 → 끝에는 0.35~0.75일)</summary>
    private const float RAID_INTERVAL_START = 0.8f;
    private const float RAID_INTERVAL_END = 0.5f;

    /// <summary>습격 점수 배수 가산 — 가동 경과 1일당 (14일째 약 ×3.1)</summary>
    private const float THREAT_PER_DAY = 0.15f;

    /// <summary>가동 중인 장치까지의 거리 배수. 0이면 거리와 무관하게 건물 중 최우선 (도달 불가로 제외된 동안만 다른 대상)</summary>
    private const float DEVICE_TARGET_DISTANCE_SCALE = 0f;

    #endregion

    #region 조회

    /// <summary>가동 중인 장치 (없으면 null)</summary>
    private static PurificationDevice Active
        => PurificationManager.instance != null ? PurificationManager.instance.ActiveDevice : null;

    public static bool IsActive => Active != null;

    /// <summary>위협 분류인지 — 가동 중 이 분류들로 이벤트가 쏠린다</summary>
    public static bool IsThreatCategory(EventCategory category)
        => category == EventCategory.Invasion
        || category == EventCategory.Disaster
        || category == EventCategory.XenopsAppearance;

    /// <summary>이벤트 선택 가중치 배수. 날씨 같은 지속형은 그대로 둔다.</summary>
    public static float WeightMultiplier(EventCategory category)
    {
        if (!IsActive) return 1f;
        if (IsThreatCategory(category)) return THREAT_WEIGHT_MULT;
        if (category == EventCategory.Persistent) return 1f;
        return CALM_WEIGHT_MULT;
    }

    /// <summary>랜덤 이벤트 간격 배수 — 정화가 진행될수록 짧아진다</summary>
    public static float EventIntervalScale
    {
        get
        {
            var d = Active;
            return d == null ? 1f : Mathf.Lerp(EVENT_INTERVAL_START, EVENT_INTERVAL_END, d.Progress);
        }
    }

    /// <summary>자동 습격 간격 배수 — 정화가 진행될수록 짧아진다</summary>
    public static float RaidIntervalScale
    {
        get
        {
            var d = Active;
            return d == null ? 1f : Mathf.Lerp(RAID_INTERVAL_START, RAID_INTERVAL_END, d.Progress);
        }
    }

    /// <summary>습격 점수에 곱할 값 — 가동 경과일에 비례</summary>
    public static float ThreatMultiplier
    {
        get
        {
            var d = Active;
            return d == null ? 1f : 1f + THREAT_PER_DAY * d.ElapsedDays;
        }
    }

    /// <summary>적이 건물까지의 거리를 비교할 때 곱할 값. 가동 중인 장치면 최우선으로 노리게 한다.</summary>
    public static float TargetDistanceScale(Building building)
    {
        var d = Active;
        return d != null && building != null && building.gameObject == d.gameObject
            ? DEVICE_TARGET_DISTANCE_SCALE
            : 1f;
    }

    #endregion
}
