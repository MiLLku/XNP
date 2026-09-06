using UnityEngine;

/// <summary>
/// 정신력 변동 기준값(SO). 숫자만 보관 — 적용은 EmployeeStatsController가 담당.
///
/// 정신력은 기본값(EmployeeData.baseMental) 기준으로 오르내리며, 여기 값들은
/// 상황별로 붙는 모디파이어의 크기와 지속 시간을 정한다.
///
/// 욕구 3종(허기·피로·재미)은 모두 이 경로로만 정신 이상에 영향을 준다 —
/// 욕구가 정신 이상 판정(임계점)을 직접 건드리는 경로는 없다. 재미 쪽 수치는 FunConfig에 있다.
/// 미할당 시 EmployeeStatsController의 동일한 코드 상수로 동작합니다.
///
/// 참조 경로: EmployeeManager.MentalModifierConfig
/// 메뉴: StampSystem ▶ Mental Modifier Config
/// </summary>
[CreateAssetMenu(fileName = "MentalModifierConfig", menuName = "StampSystem/Mental Modifier Config")]
public class MentalModifierConfig : ScriptableObject
{
    #region 상태형 페널티 (조건이 해소되면 자동으로 사라짐)

    [Header("상태형 페널티 — 상황을 해결하면 원상복구된다")]
    [Tooltip("굶주림(배고픔 0) 상태일 때 정신력에 붙는 페널티. 먹이면 즉시 사라집니다.")]
    public float starvationPenalty = -25f;

    [Tooltip("탈진(피로 0) 상태일 때 정신력에 붙는 페널티. 재우면 즉시 사라집니다.\n" +
             "수면 부족 사다리의 마지막 칸입니다.")]
    public float exhaustionPenalty = -20f;

    #endregion

    #region 수면 부족 사다리 (탈진 이전 단계)

    [Header("수면 부족 — 탈진에 닿기 전부터 정신력이 깎인다")]
    [Tooltip("피로가 이 수치 미만이면 '수면 부족' 페널티가 붙습니다.\n" +
             "탈진(0)만으로는 수면 관리 실패가 너무 늦게 드러나기 때문에 앞단에 두는 칸입니다.")]
    [Range(0f, 100f)]
    public float sleepDeprivedThreshold = 30f;

    [Tooltip("수면 부족 1단계 정신력 페널티.")]
    public float sleepDeprivedPenalty = -8f;

    [Tooltip("피로가 이 수치 미만이면 '심한 수면 부족'으로 올라섭니다.")]
    [Range(0f, 100f)]
    public float severeSleepDeprivedThreshold = 10f;

    [Tooltip("수면 부족 2단계 정신력 페널티.")]
    public float severeSleepDeprivedPenalty = -15f;

    #endregion

    #region 시간형 기본값

    [Header("시간형 — 지속 시간이 지나면 사라진다")]
    [Tooltip("출처를 지정하지 않은 정신력 변동(이벤트 등)의 기본 지속 시간(초).")]
    [Min(1f)]
    public float defaultDuration = 200f;

    [Tooltip("동료의 감정 폭발을 목격했을 때 붙는 페널티의 지속 시간(초).")]
    [Min(1f)]
    public float outburstDuration = 150f;

    #endregion

    #region 조회

    /// <summary>모디파이어 키에 대응하는 기본 지속 시간(초).</summary>
    public float GetDuration(string reasonKey) => reasonKey switch
    {
        MentalReason.OUTBURST   => outburstDuration,
        _                       => defaultDuration,
    };

    #endregion
}
