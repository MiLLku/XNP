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

    #region 전투 배율 (곱연산)

    // 전투 능력치의 기본값은 '무기'가 갖는다. 특성은 그 값을 배율로 조정한다.

    [Header("전투 배율 (1.0 = 변화 없음)")]
    [Tooltip("근접 데미지 배율")]
    public float meleeDamageMult = 1f;

    [Tooltip("원거리 데미지 배율")]
    public float rangedDamageMult = 1f;

    [Tooltip("명중률 배율 (근접·원거리 공통)")]
    public float accuracyMult = 1f;

    [Tooltip("공격 간격 배율 — 낮을수록 빠르다 (0.8 = 20% 빠름). 근접·원거리 공통")]
    public float attackIntervalMult = 1f;

    [Tooltip("근접 공격 간격 배율 (싸움꾼 0.83 = 20% 빠름)")]
    public float meleeAttackIntervalMult = 1f;

    [Tooltip("원거리 공격 간격 배율")]
    public float rangedAttackIntervalMult = 1f;

    [Tooltip("근접 명중률 배율")]
    public float meleeAccuracyMult = 1f;

    [Tooltip("원거리(사격) 명중률 배율")]
    public float rangedAccuracyMult = 1f;

    [Tooltip("사격 조준 시간 배율 (총기광 0.5 = 50% 짧음)")]
    public float aimTimeMult = 1f;

    #endregion

    #region 전투 특수 (flat) — 아주 특수한 특성만 사용

    [Header("전투 특수 (flat, 특수 특성 전용)")]
    [Tooltip("무기 사거리 가감 (타일). 예: +2 = 더 멀리 공격")]
    public float attackRangeBonus = 0f;

    [Tooltip("방어 관통력 가산 (0~1). 무기 관통력에 더해집니다.")]
    [Range(0f, 1f)]
    public float penetrationBonus = 0f;

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

    #region 무드 (flat)

    [Header("무드 (flat)")]
    [Tooltip("본인에게 늘 붙는 무드. 예: 우울함 -5, 긍정적 +3, 낙천적 +5")]
    public float selfMood = 0f;

    [Tooltip("다른 모든 직원에게 붙는 무드. 예: 음침함 -1, 친절함 +1 — 그런 직원이 여럿이면 쌓인다")]
    public float moodToOthers = 0f;

    [Tooltip("다른 직원 때문에 생기는 무드를 받지 않음 (사이코패스) — 요리사의 요리, 음침함·친절함, 동료의 감정 폭발 등")]
    public bool ignoreColleagueMood = false;

    [Tooltip("침식 1당 오르는 무드 (침식 선호). 0이면 효과 없음")]
    public float moodPerErosion = 0f;

    [Tooltip("침식 무드 상한")]
    public float erosionMoodMax = 0f;

    [Tooltip("침식이 없을 때의 무드 (침식 선호 -10)")]
    public float moodWhenNoErosion = 0f;

    [Tooltip("침식 무드 표시 문구")]
    public string erosionMoodLabel = "";

    #endregion

    #region 정신 이상 계열·수확·생성 보너스

    [Header("정신 이상")]
    [Tooltip("폭력적 — 정신 이상이 폭력 계열로만 나온다 (지금은 침식 이상 중 폭력 계열만 고름. 일반 폭력 이상은 추후 추가)")]
    public bool violent = false;

    [Header("수확")]
    [Tooltip("채광·채집 때 1개를 더 떨굴 확률 (0~1). 장비 추가 산출 확률과 더해진다")]
    [Range(0f, 1f)] public float bonusYieldChance = 0f;

    [Header("생성 시 숙련 보너스 (작업 수치 최대치 밖에서 더함)")]
    public WorkLevelBonus[] workLevelBonuses;

    [Tooltip("생성 시 격투 숙련 +")]
    public int meleeLevelBonus = 0;

    [Tooltip("생성 시 사격 숙련 +")]
    public int rangedLevelBonus = 0;

    [Tooltip("요리사 — 이 직원이 조리한 요리는 요리사 요리(먹으면 추가 기분)로 나온다")]
    public bool chefDishes = false;

    [Header("전투 제한")]
    [Tooltip("근접 무기 착용 불가 (총기광)")]
    public bool cannotEquipMelee = false;

    [Tooltip("원거리 무기 착용 불가 (싸움꾼)")]
    public bool cannotEquipRanged = false;

    [Tooltip("전투 결격 (비폭력주의) — 피해를 주는 행동을 전혀 하지 않는다. 소집해서 이동은 가능")]
    public bool nonViolent = false;

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
