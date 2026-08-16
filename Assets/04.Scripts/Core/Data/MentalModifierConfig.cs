using UnityEngine;

/// <summary>
/// 정신력 변동 기준값(SO). 숫자만 보관 — 적용은 EmployeeStatsController가 담당.
///
/// 정신력은 기본값(EmployeeData.baseMental) 기준으로 오르내리며, 여기 값들은
/// 상황별로 붙는 모디파이어의 크기와 지속 시간을 정한다.
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

    [Tooltip("탈진(피로 0) 상태일 때 정신력에 붙는 페널티. 재우면 즉시 사라집니다.")]
    public float exhaustionPenalty = -20f;

    #endregion

    #region 시간형 기본값

    [Header("시간형 — 지속 시간이 지나면 사라진다")]
    [Tooltip("출처를 지정하지 않은 정신력 변동(이벤트 등)의 기본 지속 시간(초).")]
    [Min(1f)]
    public float defaultDuration = 120f;

    [Tooltip("오락을 즐겼을 때 붙는 보너스의 지속 시간(초).")]
    [Min(1f)]
    public float recreationDuration = 180f;

    [Tooltip("오락 보너스가 누적될 수 있는 상한. 오락 시설에 오래 있어도 이 이상은 오르지 않습니다.")]
    [Min(0f)]
    public float recreationMaxBonus = 40f;

    [Tooltip("동료의 감정 폭발을 목격했을 때 붙는 페널티의 지속 시간(초).")]
    [Min(1f)]
    public float outburstDuration = 90f;

    #endregion

    #region 조회

    /// <summary>모디파이어 키에 대응하는 기본 지속 시간(초).</summary>
    public float GetDuration(string reasonKey) => reasonKey switch
    {
        MentalReason.RECREATION => recreationDuration,
        MentalReason.OUTBURST   => outburstDuration,
        _                       => defaultDuration,
    };

    #endregion
}
