using UnityEngine;

/// <summary>
/// 직원 특성의 효과 데이터.
///
/// 두 종류의 보정이 있습니다:
///   • 배율(Mult) — 곱연산. 같은 직원의 여러 특성·스킬이 서로 곱해집니다. 1.0 = 변화 없음.
///                  (예: 0.8 × 0.9 = 0.72 → 누적해도 음수가 되지 않고 안전하게 수렴)
///   • 가산(Modifier, %) / flat — 1.0 또는 0에서 더해집니다 (기존 방식 유지).
/// </summary>
[System.Serializable]
public class TraitEffects
{
    #region 스탯·생존 배율 (곱연산, 1.0 = 변화 없음)

    [Header("스탯·생존 배율 (1.0 = 변화 없음)")]
    [Tooltip("최대 체력 배율")]
    public float healthMult = 1f;

    [Tooltip("최대 정신력 배율")]
    public float mentalMult = 1f;

    [Tooltip("받는 물리 피해 배율 (0.8 = 20% 감소)")]
    public float damageTakenMult = 1f;

    [Tooltip("받는 침식 피해 배율 (0.8 = 침식 20% 덜 쌓임)")]
    public float erosionDamageMult = 1f;

    [Tooltip("정신력 감소 속도 배율 (기아·탈진 시 정신 깎이는 속도)")]
    public float mentalDecayMult = 1f;

    [Tooltip("이상행동 발동 임계점 배율 (1보다 크면 더 높은 침식까지 견딤 = 저항)")]
    public float abnormalResistMult = 1f;

    [Tooltip("이동 속도 배율")]
    public float moveSpeedMult = 1f;

    #endregion

    #region 특정 작업 속도 배율 (곱연산)

    [Header("특정 작업 속도 배율 (1.0 = 변화 없음)")]
    public WorkSpeedMultiplier[] workSpeedMultipliers;

    #endregion

    #region 가산·flat 보정 (기존 방식 유지)

    [Header("공격력 (가산 %)")]
    [Range(-50, 50)]
    public float attackModifier = 0;

    [Header("전체 작업 속도 (가산 %)")]
    [Range(-50, 100)]
    public float globalWorkSpeedModifier = 0;

    [Header("욕구 (가산 %)")]
    [Tooltip("배고픔 감소 속도 보정 (%)")]
    [Range(-50, 50)]
    public float hungerRateModifier = 0;

    [Tooltip("피로 증가 속도 보정 (%)")]
    [Range(-50, 50)]
    public float fatigueRateModifier = 0;

    [Header("운반 (가산 %)")]
    [Tooltip("운반 용량 보정 (%). 20 = 운반 가능 개수 +20% (반올림)")]
    [Range(-50, 100)]
    public float carryCapacityModifier = 0f;

    [Header("성장 (가산 %)")]
    [Tooltip("기술 상승 속도 보정 (%). 20 = 20% 증가")]
    [Range(-50, 100)]
    public float skillGainRateModifier = 0f;

    [Header("침식 (flat)")]
    [Tooltip("침식 오라 무시 수치 (flat 누적, HostileErosionAura 체크에 합산)")]
    [Min(0)]
    public float erosionIgnoreBonus = 0f;

    [Tooltip("경계 태세 반경 가감 (타일, flat). 예: +2 = 더 넓게 경계")]
    public float guardRangeBonus = 0f;

    #endregion

    #region 특수 효과 (플래그)

    [Header("특수 효과")]
    [Tooltip("야간 작업 가능")]
    public bool canWorkAtNight = false;

    [Tooltip("비 오는 날 작업 불가")]
    public bool cannotWorkInRain = false;

    [Tooltip("혼자 작업시 효율 증가")]
    public bool lonewolfBonus = false;

    [Tooltip("팀 작업시 효율 증가")]
    public bool teamworkBonus = false;

    #endregion
}
