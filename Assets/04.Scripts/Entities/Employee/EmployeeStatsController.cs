using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 직원 스탯/욕구 관리 컴포넌트.
/// 4개 스탯(체력, 허기, 피로, 정신력)의 업데이트, 특성 보정, 위험 상태 체크를 담당합니다.
///
/// 욕구 흐름:
///   - 허기: 매 프레임 감소 (hungerDecayRate × 특성 보정)
///   - 피로: 작업 중 증가, 휴식 중 회복
///   - 기아(허기 0): 체력 감소 + 정신력 페널티 모디파이어
///   - 탈진(피로 0): 정신력 페널티 모디파이어
///   - 체력 0 → Dead, 정신력 0 → MentalBreak
///
/// <b>정신력은 직접 가감하는 값이 아니다 (2026-07-29 개편).</b>
///   정신력 = clamp(기본값(EmployeeData.baseMental) + Σ활성 모디파이어, 0, 최대치)
///   변동은 영구적이지 않고 반드시 원상복구된다 —
///   상태형(굶주림·탈진)은 <b>상황을 해결하면</b>, 시간형(오락·감정 폭발)은 <b>효과가 끝나면</b> 사라진다.
///   외부에서는 ModifyMental(일시형) / SetConditionalMental(상태형)으로만 건드린다.
/// </summary>
public class EmployeeStatsController : MonoBehaviour
{
    #region 상수

    /// <summary>초기 배고픔/피로 값</summary>
    private const float INITIAL_NEEDS_VALUE = 100f;

    /// <summary>피로 < 20% 일 때 작업 속도 감소율</summary>
    private const float SEVERE_FATIGUE_SPEED = 0.5f;

    /// <summary>피로 < 50% 일 때 작업 속도 감소율</summary>
    private const float MODERATE_FATIGUE_SPEED = 0.75f;

    /// <summary>휴식 중 피로 회복 속도 (포인트/초)</summary>
    private const float REST_FATIGUE_RECOVERY = 10f;

    /// <summary>굶주림 시 체력 감소 속도 (포인트/초)</summary>
    private const float STARVATION_HEALTH_DECAY = 1f;

    // ── 정신력 모디파이어 기본값 (MentalModifierConfig 미할당 시 사용) ──
    private const float DEFAULT_STARVATION_PENALTY  = -25f;
    private const float DEFAULT_EXHAUSTION_PENALTY  = -20f;
    private const float DEFAULT_MODIFIER_DURATION   = 120f;
    private const float DEFAULT_RECREATION_MAX      = 40f;

    /// <summary>EmployeeData.baseMental이 0 이하일 때 쓰는 기본 정신력</summary>
    private const float FALLBACK_BASE_MENTAL = 50f;

    #endregion

    #region 필드

    /// <summary>현재 스탯</summary>
    [SerializeField] private EmployeeStats currentStats;

    /// <summary>현재 욕구</summary>
    [SerializeField] private EmployeeNeeds currentNeeds;

    // ── 곱연산 배율 (특성·스킬의 같은 배율을 곱해서 누적, 1.0 = 변화 없음) ──
    /// <summary>최대 체력 배율</summary>
    private float cachedHealthMult = 1f;

    /// <summary>최대 정신력 배율</summary>
    private float cachedMentalMult = 1f;

    /// <summary>받는 물리 피해 배율</summary>
    private float cachedDamageTakenMult = 1f;

    /// <summary>받는 침식 피해 배율</summary>
    private float cachedErosionDamageMult = 1f;

    /// <summary>정신력 감소 속도 배율</summary>
    private float cachedMentalDecayMult = 1f;

    /// <summary>이상행동 발동 임계점 배율 (높을수록 저항)</summary>
    private float cachedAbnormalResistMult = 1f;

    /// <summary>이동 속도 배율</summary>
    private float cachedMoveSpeedMult = 1f;

    // ── 전투 배율 (전투 능력치의 기본값은 무기가 갖고, 이 배율이 조정한다) ──
    /// <summary>근접 데미지 배율</summary>
    private float cachedMeleeDamageMult = 1f;

    /// <summary>원거리 데미지 배율</summary>
    private float cachedRangedDamageMult = 1f;

    /// <summary>명중률 배율</summary>
    private float cachedAccuracyMult = 1f;

    /// <summary>공격 간격 배율 (낮을수록 빠름)</summary>
    private float cachedAttackIntervalMult = 1f;

    /// <summary>무기 사거리 가감 (flat, 타일)</summary>
    private float cachedAttackRangeBonus = 0f;

    /// <summary>방어 관통력 가산 (flat, 0~1)</summary>
    private float cachedPenetrationBonus = 0f;

    // ── 가산·flat 보정 (1.0 또는 0에서 누적) ──
    /// <summary>특성 효과: 전체 작업 속도 보정 (가산, 1.0 기준)</summary>
    private float cachedWorkSpeedModifier = 1f;

    /// <summary>특성 효과: 배고픔 감소 속도 보정 (가산, 1.0 기준)</summary>
    private float cachedHungerRateModifier = 1f;

    /// <summary>특성 효과: 피로 증가 속도 보정 (가산, 1.0 기준)</summary>
    private float cachedFatigueRateModifier = 1f;

    /// <summary>특성/스킬 효과: 침식 오라 무시 수치 (flat)</summary>
    private float cachedErosionIgnoreBonus = 0f;

    /// <summary>특성/스킬 효과: 기술 상승 속도 보정 (가산, 1.0 기준)</summary>
    private float cachedSkillGainRateModifier = 1f;

    /// <summary>장비 스탯 보정 (슬롯별)</summary>
    private Dictionary<EquipmentSlot, EquipmentStatModifier> equipmentModifiers = new Dictionary<EquipmentSlot, EquipmentStatModifier>();

    /// <summary>이 직원의 기본 정신력 (모든 모디파이어가 없을 때 수렴하는 값)</summary>
    [SerializeField] private float baseMental = FALLBACK_BASE_MENTAL;

    /// <summary>활성 정신력 모디파이어 목록</summary>
    [SerializeField] private List<MentalModifier> mentalModifiers = new List<MentalModifier>();

    /// <summary>코디네이터 참조</summary>
    private Employee employee;

    /// <summary>스킬 상태 참조 (스킬 부여 특성 효과 읽기)</summary>
    private EmployeeSkillState skillState;

    #endregion

    #region 이벤트

    public delegate void StatsChangedDelegate(EmployeeStats stats);
    public event StatsChangedDelegate OnStatsChanged;

    public delegate void NeedsChangedDelegate(EmployeeNeeds needs);
    public event NeedsChangedDelegate OnNeedsChanged;

    #endregion

    #region 프로퍼티

    /// <summary>현재 스탯</summary>
    public EmployeeStats Stats => currentStats;

    /// <summary>현재 욕구</summary>
    public EmployeeNeeds Needs => currentNeeds;

    /// <summary>캐시된 글로벌 작업 속도 보정</summary>
    public float CachedWorkSpeedModifier => cachedWorkSpeedModifier;

    /// <summary>캐시된 받는 물리 피해 배율 (0.8 = 20% 감소)</summary>
    public float CachedDamageTakenMult => cachedDamageTakenMult;

    /// <summary>캐시된 받는 침식 피해 배율</summary>
    public float CachedErosionDamageMult => cachedErosionDamageMult;

    /// <summary>캐시된 정신력 감소 속도 배율</summary>
    public float CachedMentalDecayMult => cachedMentalDecayMult;

    /// <summary>캐시된 이상행동 발동 임계점 배율 (높을수록 저항)</summary>
    public float CachedAbnormalResistMult => cachedAbnormalResistMult;

    /// <summary>캐시된 침식 오라 무시 수치 (flat)</summary>
    public float CachedErosionIgnoreBonus => cachedErosionIgnoreBonus;

    /// <summary>캐시된 이동 속도 배율 (1.0 = 정상)</summary>
    public float CachedMoveSpeedMult => cachedMoveSpeedMult;

    /// <summary>캐시된 근접 데미지 배율</summary>
    public float CachedMeleeDamageMult => cachedMeleeDamageMult;

    /// <summary>캐시된 원거리 데미지 배율</summary>
    public float CachedRangedDamageMult => cachedRangedDamageMult;

    /// <summary>캐시된 명중률 배율</summary>
    public float CachedAccuracyMult => cachedAccuracyMult;

    /// <summary>캐시된 공격 간격 배율 (낮을수록 빠름)</summary>
    public float CachedAttackIntervalMult => cachedAttackIntervalMult;

    /// <summary>캐시된 무기 사거리 가감 (타일)</summary>
    public float CachedAttackRangeBonus => cachedAttackRangeBonus;

    /// <summary>캐시된 방어 관통력 가산 (0~1)</summary>
    public float CachedPenetrationBonus => cachedPenetrationBonus;

    /// <summary>캐시된 기술 상승 속도 보정 (1.0 = 정상)</summary>
    public float CachedSkillGainRateModifier => cachedSkillGainRateModifier;

    /// <summary>이 직원의 기본 정신력 (모디파이어가 전부 사라지면 여기로 돌아온다)</summary>
    public float BaseMental => baseMental;

    /// <summary>활성 정신력 모디파이어 목록 (UI 표시용, 읽기 전용)</summary>
    public IReadOnlyList<MentalModifier> MentalModifiers => mentalModifiers;

    /// <summary>정신력 모디파이어 기준값 (미할당이면 null → 코드 기본값)</summary>
    private static MentalModifierConfig MentalCfg
        => EmployeeManager.instance != null ? EmployeeManager.instance.MentalModifierConfig : null;

    #endregion

    #region 초기화

    void Awake()
    {
        employee = GetComponent<Employee>();
        skillState = GetComponent<EmployeeSkillState>();
    }

    /// <summary>
    /// 템플릿 데이터로 스탯/욕구를 초기화합니다.
    /// </summary>
    /// <param name="data">직원 템플릿 데이터</param>
    public void Initialize(EmployeeData data)
    {
        CalculateTraitModifiers(data);

        // 기본 정신력 — 모디파이어가 전부 없을 때 수렴하는 값. 최대 정신력은 상한 역할.
        baseMental = data.baseMental > 0f
            ? data.baseMental * cachedMentalMult
            : FALLBACK_BASE_MENTAL * cachedMentalMult;
        mentalModifiers.Clear();

        currentStats = new EmployeeStats
        {
            health = Mathf.RoundToInt(data.maxHealth * cachedHealthMult),
            maxHealth = Mathf.RoundToInt(data.maxHealth * cachedHealthMult),
            maxMental = Mathf.RoundToInt(data.maxMental * cachedMentalMult)
        };

        currentNeeds = new EmployeeNeeds
        {
            hunger = INITIAL_NEEDS_VALUE,
            fatigue = INITIAL_NEEDS_VALUE,
            fun = INITIAL_NEEDS_VALUE
        };

        RecalculateMental();
    }

    #endregion

    #region 업데이트

    void Update()
    {
        if (employee == null || employee.State == EmployeeState.Dead) return;

        UpdateNeeds(Time.deltaTime);
        CheckCriticalNeeds();
    }

    /// <summary>
    /// 매 프레임 욕구(배고픔, 피로)를 갱신하고 파생 효과를 적용합니다.
    /// </summary>
    private void UpdateNeeds(float deltaTime)
    {
        EmployeeData data = employee.Data;
        if (data == null) return;

        // 허기 감소
        float hungerDecay = data.hungerDecayRate * cachedHungerRateModifier;
        currentNeeds.hunger -= hungerDecay * deltaTime;
        currentNeeds.hunger = Mathf.Clamp(currentNeeds.hunger, 0f, 100f);

        // 피로: 작업 중 증가, 휴식 중 회복
        if (employee.State == EmployeeState.Working)
        {
            float fatigueIncrease = data.fatigueIncreaseRate * cachedFatigueRateModifier;
            currentNeeds.fatigue -= fatigueIncrease * deltaTime;
            currentNeeds.fatigue = Mathf.Clamp(currentNeeds.fatigue, 0f, 100f);
        }
        else if (employee.State == EmployeeState.Resting)
        {
            currentNeeds.fatigue += REST_FATIGUE_RECOVERY * deltaTime;
            currentNeeds.fatigue = Mathf.Clamp(currentNeeds.fatigue, 0f, 100f);
        }

        // 재미: 오락으로만 차오르고, 그 외에는 항상 일정하게 감소한다.
        // (기준점 50은 수렴 지점이 아니라 정신 이상 임계점 계산의 기준일 뿐이다)
        FunConfig funCfg = EmployeeManager.instance?.FunConfig;
        if (funCfg != null)
        {
            currentNeeds.fun -= funCfg.decayPerSecond * deltaTime;
            currentNeeds.fun = Mathf.Clamp(currentNeeds.fun, 0f, 100f);
        }

        // 기아: 체력은 지속 감소(생존 위협), 정신력은 상태형 모디파이어 — 먹이면 즉시 원상복구된다
        if (currentNeeds.hunger <= 0f)
        {
            currentStats.health -= STARVATION_HEALTH_DECAY * deltaTime;
        }
        UpdateNeedPenalty(MentalReason.STARVATION, "굶주림",
            StarvationPenalty, currentNeeds.hunger <= 0f);

        // 탈진: 정신력 상태형 모디파이어 — 재우면 즉시 원상복구된다
        UpdateNeedPenalty(MentalReason.EXHAUSTION, "탈진",
            ExhaustionPenalty, currentNeeds.fatigue <= 0f);

        // 시간형 모디파이어 소멸 처리
        TickMentalModifiers(deltaTime);

        // 클램프 (정신력은 RecalculateMental이 담당)
        currentStats.health = Mathf.Clamp(currentStats.health, 0f, currentStats.maxHealth);
        RecalculateMental();

        OnNeedsChanged?.Invoke(currentNeeds);
        OnStatsChanged?.Invoke(currentStats);
    }

    /// <summary>
    /// 욕구 바닥 상태에 따른 상태형 정신력 페널티를 갱신합니다.
    /// 페널티 크기에는 특성의 정신력 감소 배율(mentalDecayMult)이 곱해집니다.
    /// </summary>
    private void UpdateNeedPenalty(string key, string displayName, float basePenalty, bool active)
    {
        SetConditionalMental(key, displayName, basePenalty * cachedMentalDecayMult, active);
    }

    /// <summary>
    /// 위험 상태를 확인합니다 (체력 0 → Dead, 정신력 0 → MentalBreak).
    /// </summary>
    private void CheckCriticalNeeds()
    {
        if (currentStats.health <= 0f)
        {
            employee.SetState(EmployeeState.Dead);

            // 사망 레터 — Update가 Dead 상태에서 조기 반환하므로 1회만 발행된다
            NotificationManager.instance?.PushLetter(new Letter
            {
                title = "직원 사망",
                body = $"{employee.DisplayName}이(가) 사망했습니다.",
                type = LetterType.Threat,
                pauseUntilRead = true
            });
            TimeManager.instance?.ForcePause();
            return;
        }

        if (currentStats.mental <= 0f)
        {
            employee.SetState(EmployeeState.MentalBreak);
            return;
        }
    }

    #endregion

    #region 특성 효과

    /// <summary>
    /// 모든 특성의 효과를 계산하여 캐시합니다.
    /// EmployeeData 특성 + 해제된 스킬 부여 특성 모두 포함합니다.
    /// </summary>
    public void CalculateTraitModifiers(EmployeeData data)
    {
        // 배율 (곱연산 기준값 1.0)
        cachedHealthMult           = 1f;
        cachedMentalMult           = 1f;
        cachedDamageTakenMult      = 1f;
        cachedErosionDamageMult    = 1f;
        cachedMentalDecayMult      = 1f;
        cachedAbnormalResistMult   = 1f;
        cachedMoveSpeedMult        = 1f;
        cachedMeleeDamageMult      = 1f;
        cachedRangedDamageMult     = 1f;
        cachedAccuracyMult         = 1f;
        cachedAttackIntervalMult   = 1f;
        cachedAttackRangeBonus     = 0f;
        cachedPenetrationBonus     = 0f;
        // 가산·flat (1.0 또는 0 기준)
        cachedWorkSpeedModifier        = 1f;
        cachedHungerRateModifier       = 1f;
        cachedFatigueRateModifier      = 1f;
        cachedErosionIgnoreBonus       = 0f;
        cachedSkillGainRateModifier    = 1f;

        // 연구 침식 저항 — 모든 침식 경로(오라·자연·피격)에 일괄 적용된다.
        // '제놉스 관리' 연구 라인이 여기서 실효를 갖는다. 0.9 하한으로 완전 무효화는 막는다.
        var researchTree = ResearchTreeManager.instance;
        if (researchTree != null)
        {
            float resist = researchTree.GetStatBonus(ResearchStatType.ErosionResistanceBonus);
            cachedErosionDamageMult *= Mathf.Clamp(1f - resist, 0.1f, 1f);
        }

        // EmployeeData 내장 특성
        if (data?.traits != null)
        {
            foreach (var trait in data.traits)
            {
                if (trait == null) continue;
                ApplyTraitEffects(trait.effects);
            }
        }

        // 해제된 스킬 부여 특성
        if (skillState != null)
        {
            ApplyTraitEffects(skillState.GetActiveTraitEffects());
        }
    }

    /// <summary>
    /// 스킬 해제 등으로 특성 효과가 변경됐을 때 재계산합니다.
    /// EmployeeSkillState.Unlock()에서 호출됩니다.
    /// </summary>
    public void RecalculateModifiers()
    {
        CalculateTraitModifiers(employee?.Data);
    }

    private void ApplyTraitEffects(TraitEffects fx)
    {
        // 배율: 곱연산 누적
        cachedHealthMult         *= fx.healthMult;
        cachedMentalMult         *= fx.mentalMult;
        cachedDamageTakenMult    *= fx.damageTakenMult;
        cachedErosionDamageMult  *= fx.erosionDamageMult;
        cachedMentalDecayMult    *= fx.mentalDecayMult;
        cachedAbnormalResistMult *= fx.abnormalResistMult;
        cachedMoveSpeedMult      *= fx.moveSpeedMult;
        cachedMeleeDamageMult    *= fx.meleeDamageMult;
        cachedRangedDamageMult   *= fx.rangedDamageMult;
        cachedAccuracyMult       *= fx.accuracyMult;
        cachedAttackIntervalMult *= fx.attackIntervalMult;

        // 가산·flat: 기존 방식 유지
        cachedAttackRangeBonus   += fx.attackRangeBonus;
        cachedPenetrationBonus   += fx.penetrationBonus;
        cachedWorkSpeedModifier     += fx.globalWorkSpeedModifier / 100f;
        cachedHungerRateModifier    += fx.hungerRateModifier / 100f;
        cachedFatigueRateModifier   += fx.fatigueRateModifier / 100f;
        cachedErosionIgnoreBonus    += fx.erosionIgnoreBonus;
        cachedSkillGainRateModifier += fx.skillGainRateModifier / 100f;
    }

    /// <summary>
    /// 특성에 의한 데미지 보정 계수를 반환합니다 (가산 %, 근접·원거리 공통).
    /// 무기 데미지에 곱해집니다 — 직원 자체가 공격력을 갖지는 않습니다.
    /// </summary>
    public float GetTraitDamageModifier()
    {
        float modifier = 0f;
        EmployeeData data = employee?.Data;
        if (data?.traits == null) return modifier;

        foreach (var trait in data.traits)
        {
            if (trait != null)
            {
                modifier += trait.effects.attackModifier / 100f;
            }
        }
        return modifier;
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 식사하여 허기를 회복합니다.
    /// </summary>
    public void Eat(float nutritionValue)
    {
        currentNeeds.hunger += nutritionValue;
        currentNeeds.hunger = Mathf.Clamp(currentNeeds.hunger, 0f, 100f);
        OnNeedsChanged?.Invoke(currentNeeds);
    }

    /// <summary>체력 수정</summary>
    public void ModifyHealth(float amount)
    {
        currentStats.health += amount;
        currentStats.health = Mathf.Clamp(currentStats.health, 0f, currentStats.maxHealth);
        OnStatsChanged?.Invoke(currentStats);
    }

    /// <summary>
    /// 정신력을 일시적으로 변동시킵니다 (시간형 모디파이어).
    /// 같은 reasonKey로 반복 호출하면 값이 누산되고 지속 시간이 갱신됩니다 —
    /// 오락 시설처럼 매 프레임 조금씩 올리는 경우를 자연스럽게 처리하기 위함입니다.
    /// 지속 시간이 지나면 <b>원래 정신력으로 돌아옵니다.</b>
    /// </summary>
    /// <param name="amount">가감량 (양수 = 기분 좋아짐)</param>
    /// <param name="reasonKey">중복 방지 키 (MentalReason 상수 권장)</param>
    /// <param name="displayName">UI 표시용 이름</param>
    /// <param name="duration">지속 시간(초). 0 이하면 Config의 키별 기본값 사용</param>
    public void ModifyMental(float amount, string reasonKey = MentalReason.GENERIC,
                             string displayName = "일시적인 기분", float duration = 0f)
    {
        if (Mathf.Approximately(amount, 0f)) return;

        var cfg = MentalCfg;
        float dur = duration > 0f
            ? duration
            : (cfg != null ? cfg.GetDuration(reasonKey) : DEFAULT_MODIFIER_DURATION);

        var existing = FindModifier(reasonKey);
        if (existing != null)
        {
            existing.value += amount;
            existing.remainingTime = dur;
            existing.displayName = displayName;
        }
        else
        {
            mentalModifiers.Add(new MentalModifier
            {
                reasonKey = reasonKey,
                displayName = displayName,
                value = amount,
                remainingTime = dur
            });
            existing = mentalModifiers[mentalModifiers.Count - 1];
        }

        // 오락 보너스는 무한 누적되지 않도록 상한을 건다
        if (reasonKey == MentalReason.RECREATION)
        {
            float cap = cfg != null ? cfg.recreationMaxBonus : DEFAULT_RECREATION_MAX;
            existing.value = Mathf.Min(existing.value, cap);
        }

        RecalculateMental();
        OnStatsChanged?.Invoke(currentStats);
    }

    /// <summary>
    /// 조건이 참인 동안만 유지되는 상태형 정신력 모디파이어를 설정합니다.
    /// active가 false가 되면 즉시 제거되어 <b>정신력이 원상복구</b>됩니다.
    /// 굶주림·탈진처럼 "상황을 해결하면 회복되는" 페널티에 사용합니다.
    /// </summary>
    public void SetConditionalMental(string reasonKey, string displayName, float value, bool active)
    {
        var existing = FindModifier(reasonKey);

        if (!active)
        {
            if (existing != null)
            {
                mentalModifiers.Remove(existing);
                RecalculateMental();
                OnStatsChanged?.Invoke(currentStats);
            }
            return;
        }

        if (existing == null)
        {
            mentalModifiers.Add(new MentalModifier
            {
                reasonKey = reasonKey,
                displayName = displayName,
                value = value,
                remainingTime = -1f      // 음수 = 상태형
            });
        }
        else if (!Mathf.Approximately(existing.value, value))
        {
            existing.value = value;
            existing.remainingTime = -1f;
        }
        else
        {
            return; // 변화 없음
        }

        RecalculateMental();
        OnStatsChanged?.Invoke(currentStats);
    }

    /// <summary>특정 정신력 모디파이어를 즉시 제거합니다.</summary>
    public void RemoveMentalModifier(string reasonKey)
    {
        var existing = FindModifier(reasonKey);
        if (existing == null) return;

        mentalModifiers.Remove(existing);
        RecalculateMental();
        OnStatsChanged?.Invoke(currentStats);
    }

    /// <summary>
    /// 현재 정신력을 재계산합니다: clamp(기본값 + Σ모디파이어, 0, 최대치).
    /// </summary>
    private void RecalculateMental()
    {
        float sum = 0f;
        for (int i = 0; i < mentalModifiers.Count; i++)
            sum += mentalModifiers[i].value;

        currentStats.mental = Mathf.Clamp(baseMental + sum, 0f, currentStats.maxMental);
    }

    /// <summary>시간형 모디파이어의 남은 시간을 줄이고 만료된 것을 제거합니다.</summary>
    private void TickMentalModifiers(float deltaTime)
    {
        bool changed = false;

        for (int i = mentalModifiers.Count - 1; i >= 0; i--)
        {
            var m = mentalModifiers[i];
            if (m.IsConditional) continue;   // 상태형은 시간으로 사라지지 않는다

            m.remainingTime -= deltaTime;
            if (m.remainingTime <= 0f)
            {
                mentalModifiers.RemoveAt(i);
                changed = true;
            }
        }

        if (changed) RecalculateMental();
    }

    private MentalModifier FindModifier(string reasonKey)
    {
        for (int i = 0; i < mentalModifiers.Count; i++)
            if (mentalModifiers[i].reasonKey == reasonKey) return mentalModifiers[i];
        return null;
    }

    private static float StarvationPenalty
    {
        get
        {
            var cfg = MentalCfg;
            return cfg != null ? cfg.starvationPenalty : DEFAULT_STARVATION_PENALTY;
        }
    }

    private static float ExhaustionPenalty
    {
        get
        {
            var cfg = MentalCfg;
            return cfg != null ? cfg.exhaustionPenalty : DEFAULT_EXHAUSTION_PENALTY;
        }
    }

    /// <summary>배고픔 수정</summary>
    public void ModifyHunger(float amount)
    {
        currentNeeds.hunger += amount;
        currentNeeds.hunger = Mathf.Clamp(currentNeeds.hunger, 0f, 100f);
        OnNeedsChanged?.Invoke(currentNeeds);
    }

    /// <summary>피로도 수정</summary>
    public void ModifyFatigue(float amount)
    {
        currentNeeds.fatigue += amount;
        currentNeeds.fatigue = Mathf.Clamp(currentNeeds.fatigue, 0f, 100f);
        OnNeedsChanged?.Invoke(currentNeeds);
    }

    /// <summary>재미 수정 (오락 시설 이용·약물 복용·이벤트 등)</summary>
    public void ModifyFun(float amount)
    {
        currentNeeds.fun += amount;
        currentNeeds.fun = Mathf.Clamp(currentNeeds.fun, 0f, 100f);
        OnNeedsChanged?.Invoke(currentNeeds);
    }

    /// <summary>침식 수치를 설정합니다. 0 = 회복, 양수 = 제노프스 등에 의한 침식 적용.</summary>
    public void SetErosion(float level)
    {
        currentStats.erosionLevel = Mathf.Max(0f, level);
        OnStatsChanged?.Invoke(currentStats);
    }

    /// <summary>현재 침식 수치</summary>
    public float ErosionLevel => currentStats.erosionLevel;

    /// <summary>
    /// 피로도에 따른 작업 속도 감소율을 반환합니다.
    /// 피로 20% 미만: 50%, 50% 미만: 75%, 이상: 100%.
    /// </summary>
    public float GetFatigueModifier()
    {
        if (currentNeeds.fatigue < 20f) return SEVERE_FATIGUE_SPEED;
        if (currentNeeds.fatigue < 50f) return MODERATE_FATIGUE_SPEED;
        return 1f;
    }

    /// <summary>
    /// 재미에 따른 정신 이상 저항 배율을 반환합니다 (구간형).
    /// 재미가 낮으면 1.0 미만 → 정신 이상 임계점이 올라가 더 일찍 터진다(취약).
    /// EmployeeMental.GetBreakResistance에서 abnormalResistMult에 곱해집니다.
    ///
    /// 재미는 이 역할만 갖는다 — 작업 속도에는 관여하지 않는다.
    /// </summary>
    public float GetFunErosionFactor()
    {
        FunConfig cfg = EmployeeManager.instance?.FunConfig;
        if (cfg == null) return 1f;

        // 연속형 — 기준점(50)에서 멀어진 만큼 선형으로 반영된다.
        // 기준점 위면 1.0 초과(잘 버팀), 아래면 1.0 미만(취약).
        float factor = 1f + (currentNeeds.fun - cfg.baseline) * cfg.resistPerFunPoint;
        return Mathf.Clamp(factor, cfg.minResistFactor, cfg.maxResistFactor);
    }

    /// <summary>
    /// 피로(수면 부족)에 따른 정신 이상 저항 배율을 반환합니다 (구간형).
    /// 수면 관리가 무너져도 임계점이 올라간다 — 재미(GetFunErosionFactor)와 곱연산으로 누적.
    /// 정신력이 같아도 욕구 관리 실패만으로 정신 이상 위험이 생기는 설계.
    /// </summary>
    public float GetFatigueErosionFactor()
    {
        FunConfig cfg = EmployeeManager.instance?.FunConfig;
        if (cfg == null) return 1f;

        if (currentNeeds.fatigue < cfg.fatigueSevereThreshold) return cfg.fatigueSevereFactor;
        if (currentNeeds.fatigue < cfg.fatigueVulnerableThreshold) return cfg.fatigueVulnerableFactor;
        return 1f;
    }

    /// <summary>
    /// 최대 스탯을 직접 증가시킵니다 (레벨업용).
    /// </summary>
    public void IncreaseMaxStats(int healthGain, int mentalGain)
    {
        currentStats.maxHealth += healthGain;
        currentStats.health = currentStats.maxHealth; // 레벨업 시 체력 회복
        currentStats.maxMental += mentalGain;

        // 정신력은 만점으로 채우지 않는다 — 기본값 + 모디파이어 결과가 곧 현재 정신력이므로,
        // 최대치가 올라간 만큼 기본값도 함께 올려 상대적 위치를 유지한다.
        baseMental += mentalGain;
        RecalculateMental();

        OnStatsChanged?.Invoke(currentStats);
    }

    #endregion

    #region 장비 보정

    /// <summary>
    /// 장비 슬롯에 대한 스탯 보정을 추가합니다.
    /// </summary>
    public void AddEquipmentModifier(EquipmentSlot slot, EquipmentStatModifier modifier)
    {
        // 기존 보정 제거 후 추가
        RemoveEquipmentModifier(slot);
        equipmentModifiers[slot] = modifier;
        ApplyEquipmentModifiers();
    }

    /// <summary>
    /// 장비 슬롯의 스탯 보정을 제거합니다.
    /// </summary>
    public void RemoveEquipmentModifier(EquipmentSlot slot)
    {
        if (!equipmentModifiers.ContainsKey(slot)) return;
        equipmentModifiers.Remove(slot);
        ApplyEquipmentModifiers();
    }

    /// <summary>
    /// 모든 장비 보정을 합산하여 스탯에 적용합니다.
    /// </summary>
    private void ApplyEquipmentModifiers()
    {
        // 기본 스탯에서 재계산
        EmployeeData data = employee?.Data;
        if (data == null) return;

        // 연구 전역 보너스(비율) — '직원 성장' 연구 라인이 여기서 실효를 갖는다
        var rt = ResearchTreeManager.instance;
        float researchHealthBonus = rt != null ? rt.GetStatBonus(ResearchStatType.EmployeeMaxHealthBonus) : 0f;

        float baseMaxHealth = data.maxHealth * cachedHealthMult * (1f + researchHealthBonus);
        float baseMaxMental = data.maxMental * cachedMentalMult;

        // 장비 절대값 보정 합산 (공격력은 더 이상 직원 스탯이 아니므로 여기서 다루지 않는다 —
        // 장비의 damageBonus는 EmployeeCombat이 무기 데미지에 직접 더한다)
        float equipHealthMod = 0f;
        float equipMentalMod = 0f;
        float equipWorkSpeedMod = 0f;
        float equipHungerRateMod = 0f;
        float equipFatigueRateMod = 0f;

        foreach (var kvp in equipmentModifiers)
        {
            equipHealthMod += kvp.Value.maxHealthModifier;
            equipMentalMod += kvp.Value.maxMentalModifier;
            equipWorkSpeedMod += kvp.Value.workSpeedModifier;
            equipHungerRateMod += kvp.Value.hungerRateModifier;
            equipFatigueRateMod += kvp.Value.fatigueRateModifier;
        }

        // 최대치 갱신 (체력/정신력은 절대값 가감)
        currentStats.maxHealth = Mathf.Max(1, baseMaxHealth + equipHealthMod);
        currentStats.maxMental = Mathf.Max(1, baseMaxMental + equipMentalMod);

        // 현재 체력이 새 최대치를 초과하지 않도록 클램프 (정신력은 RecalculateMental이 담당)
        currentStats.health = Mathf.Clamp(currentStats.health, 0f, currentStats.maxHealth);
        RecalculateMental();

        // 퍼센트 보정 — 특성/스킬 전부 재계산 후 장비 보정 합산
        CalculateTraitModifiers(data);

        cachedWorkSpeedModifier   += equipWorkSpeedMod / 100f;
        cachedHungerRateModifier  += equipHungerRateMod / 100f;
        cachedFatigueRateModifier += equipFatigueRateMod / 100f;

        OnStatsChanged?.Invoke(currentStats);
    }

    #endregion

    #region 저장/복원

    /// <summary>
    /// 저장 데이터에 스탯/욕구 정보를 기록합니다.
    /// </summary>
    public void PopulateSaveData(EmployeeSaveData data)
    {
        data.maxHealth = (int)currentStats.maxHealth;
        data.currentHealth = (int)currentStats.health;
        data.maxMental = (int)currentStats.maxMental;
        data.currentMental = (int)currentStats.mental;

        data.hunger = currentNeeds.hunger;
        data.fatigue = currentNeeds.fatigue;
        data.fun = currentNeeds.fun;

        // 정신력은 파생값이므로 기본값 + 모디파이어를 저장한다 (currentMental은 표시용 스냅샷)
        data.baseMental = baseMental;
        data.mentalModifiers = new List<MentalModifierSaveData>();
        foreach (var m in mentalModifiers)
        {
            data.mentalModifiers.Add(new MentalModifierSaveData
            {
                reasonKey = m.reasonKey,
                displayName = m.displayName,
                value = m.value,
                remainingTime = m.remainingTime
            });
        }
    }

    /// <summary>
    /// 저장 데이터에서 스탯/욕구를 복원합니다.
    /// </summary>
    public void RestoreFromSaveData(EmployeeSaveData data)
    {
        currentStats = new EmployeeStats
        {
            maxHealth = data.maxHealth,
            health = data.currentHealth,
            maxMental = data.maxMental,
            mental = data.currentMental
        };

        currentNeeds = new EmployeeNeeds
        {
            hunger = data.hunger,
            fatigue = data.fatigue,
            fun = data.fun
        };

        // 특성 보정 캐시 재계산
        Employee emp = GetComponent<Employee>();
        if (emp?.Data != null)
        {
            CalculateTraitModifiers(emp.Data);
        }

        // 정신력 복원 — 기본값 + 모디파이어에서 재계산한다 (currentMental은 신뢰하지 않음)
        baseMental = data.baseMental > 0f
            ? data.baseMental
            : (emp?.Data != null && emp.Data.baseMental > 0f
                ? emp.Data.baseMental * cachedMentalMult
                : FALLBACK_BASE_MENTAL * cachedMentalMult);

        mentalModifiers.Clear();
        if (data.mentalModifiers != null)
        {
            foreach (var m in data.mentalModifiers)
            {
                if (m == null || string.IsNullOrEmpty(m.reasonKey)) continue;
                mentalModifiers.Add(new MentalModifier
                {
                    reasonKey = m.reasonKey,
                    displayName = m.displayName,
                    value = m.value,
                    remainingTime = m.remainingTime
                });
            }
        }

        RecalculateMental();
    }

    #endregion
}
