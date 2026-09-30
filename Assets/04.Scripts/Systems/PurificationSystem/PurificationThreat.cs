using UnityEngine;

/// <summary>
/// 정화 가동 중 위협 강화 규칙 — 튜닝값을 한 곳에 모읍니다.
///
/// 가동 중에는 (기획 확정)
///   - 이벤트 대부분이 위협·습격 이벤트로 바뀐다   → <see cref="WeightMultiplier"/>
///   - 이벤트가 점점 잦아진다                      → <see cref="IntervalScale"/>
///   - 습격이 날이 갈수록 강해진다                  → <see cref="RaidMultiplierScale"/>
///   - 건물을 노리는 적은 장치를 우선 노린다         → <see cref="TargetDistanceScale"/>
/// 가동 중이 아니면 모든 값이 중립(1)입니다.
/// </summary>
public static class PurificationThreat
{
    #region 튜닝값

    /// <summary>가동 중 위협 분류 이벤트의 가중치 배수</summary>
    private const float THREAT_WEIGHT_MULT = 6f;

    /// <summary>가동 중 일상 분류(직원·이야기·자원·방문) 이벤트의 가중치 배수</summary>
    private const float CALM_WEIGHT_MULT = 0.15f;

    /// <summary>이벤트 간격 배수 — 가동 직후 → 완료 직전</summary>
    private const float INTERVAL_SCALE_START = 0.4f;
    private const float INTERVAL_SCALE_END = 0.2f;

    /// <summary>습격 스폰 배수 가산 — 경과 1일당</summary>
    private const float RAID_SCALE_PER_DAY = 0.15f;

    /// <summary>
    /// 가동 중인 장치까지의 거리 배수. 0이면 거리와 무관하게 항상 최우선 —
    /// 바로 옆 바닥·벽보다도 장치를 노린다 (도달 불가로 제외된 동안만 다른 대상을 고른다)
    /// </summary>
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
    public static float IntervalScale
    {
        get
        {
            var d = Active;
            return d == null ? 1f : Mathf.Lerp(INTERVAL_SCALE_START, INTERVAL_SCALE_END, d.Progress);
        }
    }

    /// <summary>습격 스폰 배수에 곱할 값 — 가동 경과일에 비례</summary>
    public static float RaidMultiplierScale
    {
        get
        {
            var d = Active;
            return d == null ? 1f : 1f + RAID_SCALE_PER_DAY * d.ElapsedDays;
        }
    }

    /// <summary>
    /// 적이 건물까지의 거리를 비교할 때 곱할 값. 가동 중인 장치면 최우선으로 노리게 한다.
    /// </summary>
    public static float TargetDistanceScale(Building building)
    {
        var d = Active;
        return d != null && building != null && building.gameObject == d.gameObject
            ? DEVICE_TARGET_DISTANCE_SCALE
            : 1f;
    }

    #endregion
}
