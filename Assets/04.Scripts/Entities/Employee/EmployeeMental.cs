using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 직원 정신 이상 컴포넌트 — 정신 이상 발생 판정의 <b>단일 진입점</b>입니다.
///
/// 역할 분담:
///   • <b>정신 수치</b> → 정신 이상이 <b>발생할 확률</b>을 결정한다.
///   • <b>침식 수치</b> → 발생한 정신 이상이 <b>'침식 계열'일 확률</b>을 높인다. 발생 여부에는 관여하지 않는다.
///   • <b>임계점</b> → 직원마다 다르다. 공통 기본값을 개인 저항 배율로 나눠 보정한다.
///
/// 판정 흐름 (림월드식):
///   1. checkIntervalSeconds(기본 2.5초)마다 한 번만 검사한다.
///   2. 정신 비율이 실효 임계점 아래면 후보가 된다.
///      실효 임계점 = 기본 임계점 / 저항배율.
///      저항배율 = abnormalResistMult(특성·스킬) × 재미계수 × 피로계수.
///      → 저항이 높으면 임계점이 낮아져 더 낮은 정신까지 버티고,
///        재미·수면 관리가 무너지면 저항이 깎여 임계점이 올라가 더 일찍 터진다.
///   3. 발생 확률은 평균 발생 간격(MTB)에서 환산한다: p = 1 - exp(-Δt / MTB).
///      임계점 아래로 깊이 내려갈수록 MTB가 짧아진다.
///   4. 발생이 확정되면 계열을 고른다:
///      침식비율 = (침식 / erosionFullLevel) × erosionWeightMultiplier
///      → 이 확률로 침식 계열, 나머지는 일반 계열. 고른 계열에 후보가 없으면 반대 계열로 폴백한다.
///
/// 침식 계열 정신 이상은 AbnormalBehaviorRegistry의 구현체를 그대로 실행합니다
/// (구 EmployeeErosionController의 단계별 이상행동 롤을 이쪽으로 통합 — 판정은 이제 여기 한 곳뿐).
///
/// <b>분류는 계열 2가지가 전부다.</b> 경미·중간·심각 같은 심각도 등급은 두지 않는다 —
/// "얼마나 심한가"는 침식 수치가 계열 확률로 이미 표현하고 있고, 등급을 겹쳐 두면
/// 같은 축을 두 번 재는 셈이라 임계점·MTB·후보 풀이 전부 3벌로 늘어나기만 한다.
/// 정신이 낮을수록 자주 터지는 기울기는 depthMtbFactor 하나가 담당한다.
///
/// <b>정신차림</b> — 정신 이상이 끝나면 큰 폭의 정신력 버프(시간형 모디파이어)가 붙어
/// 한동안 다시 터지지 않는다. 짧은 재판정 유예(breakGraceSeconds)가 "숨 돌릴 틈"이라면
/// 이쪽은 "당분간 안전한 구간"이다. 이 구간을 노리고 <b>침식 위험 작업 직전에 일부러
/// 일반 계열 정신 이상을 터뜨리는</b> 운영이 성립한다 — 상세는 ApplyComposure 주석.
/// </summary>
public class EmployeeMental : MonoBehaviour
{
    #region 상수 (Config 미할당 시 사용하는 기본값)

    private const float DEFAULT_CHECK_INTERVAL   = 2.5f;
    private const float DEFAULT_BREAK_GRACE      = 40f;
    private const float DEFAULT_BREAK_THRESHOLD  = 0.50f;
    private const float DEFAULT_MTB_DAYS         = 0.75f;
    private const float DEFAULT_DEPTH_MTB_FACTOR = 4f;
    private const float DEFAULT_COMPOSURE_BONUS  = 50f;
    private const float DEFAULT_COMPOSURE_TIME   = 1000f;  // 1 게임일 (림월드 '카타르시스'와 동일)
    private const float DEFAULT_EROSION_FULL     = 200f;
    private const float DEFAULT_EROSION_WEIGHT   = 1f;
    private const float DEFAULT_EROSION_COOLDOWN = 75f;

    /// <summary>DayCycle이 없을 때 사용할 게임 1일 길이 (초)</summary>
    private const float FALLBACK_DAY_LENGTH = 600f;

    /// <summary>감정 폭발 영향 반경 (타일)</summary>
    private const float OUTBURST_RADIUS = 5f;

    /// <summary>감정 폭발이 주변 직원에게 주는 정신력 피해</summary>
    private const float OUTBURST_MENTAL_DAMAGE = 5f;

    #endregion

    #region 정신 이상 풀

    /// <summary>
    /// 일반 계열 — 침식과 무관하게 정신력만으로 발생한다.
    /// 태업 수준의 시간 손실이라 기지가 부서지거나 동료가 다치지는 않는다.
    /// </summary>
    private static readonly MentalEventType[] NORMAL_POOL =
    {
        MentalEventType.WorkSlowdown,
        MentalEventType.RefuseWork,
        MentalEventType.Wander,
        MentalEventType.EmotionalOutburst,
    };

    /// <summary>
    /// 침식 계열 — 침식 수치가 높을수록 이쪽에서 뽑힌다.
    /// AbnormalBehaviorRegistry에 실제 등록된 구현체만 후보가 되므로(FilterRegistered),
    /// 미구현 타입을 여기 적어두어도 안전합니다.
    /// </summary>
    private static readonly List<AbnormalBehaviorType> EROSION_POOL = new List<AbnormalBehaviorType>
    {
        AbnormalBehaviorType.IgnoreCommand,
        AbnormalBehaviorType.RandomMove,
        AbnormalBehaviorType.WorkStop,
        AbnormalBehaviorType.FriendlyAttack,
        AbnormalBehaviorType.ErosionOutburst,
        AbnormalBehaviorType.AttackBuilding,

        // 미구현 — 클래스를 만들어 레지스트리에 등록하면 자동으로 후보가 된다
        AbnormalBehaviorType.IgnoreCommandEnhanced,
        AbnormalBehaviorType.MoveTowardEnemy,
        AbnormalBehaviorType.FriendlyAttackEnhanced,
        AbnormalBehaviorType.Flee,
        AbnormalBehaviorType.ErosionTrailExplosion,
    };

    /// <summary>일반 계열 지속 시간 (초)</summary>
    private static readonly Dictionary<MentalEventType, float> EVENT_DURATIONS = new Dictionary<MentalEventType, float>
    {
        { MentalEventType.WorkSlowdown,     50f },
        { MentalEventType.RefuseWork,       35f },
        { MentalEventType.Wander,           25f },
        { MentalEventType.EmotionalOutburst, 10f },
    };

    /// <summary>일반 계열 재발생 대기 시간 (초)</summary>
    private static readonly Dictionary<MentalEventType, float> EVENT_COOLDOWNS = new Dictionary<MentalEventType, float>
    {
        { MentalEventType.WorkSlowdown,     100f },
        { MentalEventType.RefuseWork,        75f },
        { MentalEventType.Wander,            65f },
        { MentalEventType.EmotionalOutburst, 150f },
    };

    #endregion

    #region 필드

    /// <summary>다음 발생 판정까지 남은 시간</summary>
    private float checkTimer;

    /// <summary>정신 이상 종료 후 재판정 유예 남은 시간</summary>
    private float graceTimer;

    /// <summary>활성 정신 이상 목록 (일반·침식 계열 공용)</summary>
    private List<ActiveMentalEvent> activeMentalEvents = new List<ActiveMentalEvent>();

    /// <summary>일반 계열 쿨다운 타이머</summary>
    private Dictionary<MentalEventType, float> normalCooldowns = new Dictionary<MentalEventType, float>();

    /// <summary>침식 계열 쿨다운 타이머</summary>
    private Dictionary<AbnormalBehaviorType, float> erosionCooldowns = new Dictionary<AbnormalBehaviorType, float>();

    /// <summary>현재 작업 속도 보정 (1.0 = 정상)</summary>
    private float activeSpeedModifier = 1f;

    /// <summary>현재 작업 거부 상태</summary>
    private bool isRefusingWork = false;

    /// <summary>침식 계열 '명령 무시'로 새 작업 배정이 차단된 상태</summary>
    private bool isBlockingWorkAssignment = false;

    // 컴포넌트 참조
    private Employee employee;
    private EmployeeStatsController statsController;
    private EmployeeMovement movement;

    #endregion

    #region 프로퍼티

    /// <summary>현재 활성 속도 보정 (정신 이상에 의한)</summary>
    public float ActiveSpeedModifier => activeSpeedModifier;

    /// <summary>현재 작업 거부 중인지 여부 (일반 계열 RefuseWork 또는 침식 계열 명령 무시)</summary>
    public bool IsRefusingWork => isRefusingWork || isBlockingWorkAssignment;

    /// <summary>침식 계열 정신 이상을 수행 중인지 여부</summary>
    public bool IsPerformingErosionBreak => activeMentalEvents.Any(e => e.IsErosionKind);

    /// <summary>활성 정신 이상 수</summary>
    public int ActiveEventCount => activeMentalEvents.Count;

    /// <summary>기준값 SO (미할당이면 null → 코드 기본값 사용)</summary>
    private static MentalBreakConfig Cfg
        => EmployeeManager.instance != null ? EmployeeManager.instance.MentalBreakConfig : null;

    #endregion

    #region 초기화

    void Awake()
    {
        employee = GetComponent<Employee>();
        statsController = GetComponent<EmployeeStatsController>();
        movement = GetComponent<EmployeeMovement>();

        checkTimer = CheckInterval;
    }

    #endregion

    #region 업데이트

    void Update()
    {
        if (employee == null || employee.State == EmployeeState.Dead) return;

        float dt = Time.deltaTime;

        UpdateActiveMentalEvents(dt);
        UpdateCooldowns(dt);

        if (graceTimer > 0f)
        {
            graceTimer -= dt;
            return;
        }

        // 주기적 판정 — 림월드처럼 매 프레임이 아니라 고정 간격으로만 굴린다
        checkTimer -= dt;
        if (checkTimer > 0f) return;

        float interval = CheckInterval;
        checkTimer = interval;
        EvaluateMentalState(interval);
    }

    #endregion

    #region 발생 판정

    /// <summary>
    /// 정신 수치를 기준으로 정신 이상 발생 여부를 판정합니다.
    /// </summary>
    /// <param name="interval">직전 판정 이후 경과 시간 (MTB → 확률 환산에 사용)</param>
    private void EvaluateMentalState(float interval)
    {
        if (statsController == null) return;

        // 디버그: 정신 이상 차단
        if (DebugManager.IsBlocked(DebugFlag.MentalBreak)) return;

        // 이미 정신 이상이 진행 중이면 새로 굴리지 않는다
        if (activeMentalEvents.Count > 0) return;

        EmployeeStats stats = statsController.Stats;
        if (stats.maxMental <= 0f) return;

        float mentalRatio = stats.mental / stats.maxMental;

        // 1) 임계점 판정 — 정신 비율이 실효 임계점 아래일 때만 후보가 된다
        float threshold = GetEffectiveThreshold();
        if (mentalRatio >= threshold) return;

        // 2) 발생 확률 — MTB(평균 발생 간격)를 확률로 환산
        if (!RollMentalBreak(mentalRatio, threshold, interval)) return;

        // 3) 계열 결정 — 침식 수치가 높을수록 침식 계열이 뽑힌다
        TriggerMentalBreak();
    }

    /// <summary>
    /// 평균 발생 간격(MTB)을 이번 판정 구간의 확률로 환산해 굴립니다.
    /// p = 1 - exp(-Δt / MTB) — 지수 분포라 판정 주기를 바꿔도 장기 발생 빈도는 유지됩니다.
    /// </summary>
    private bool RollMentalBreak(float mentalRatio, float threshold, float interval)
    {
        float mtbDays = MtbDays;
        if (mtbDays <= 0f) return false;

        // 임계점 아래로 얼마나 깊이 내려갔는지 (0 = 임계점 바로 아래, 1 = 정신 0)
        float depth = threshold > 0f ? Mathf.Clamp01((threshold - mentalRatio) / threshold) : 0f;
        float mtbSeconds = mtbDays * DayLengthSeconds / (1f + DepthMtbFactor * depth);
        if (mtbSeconds <= 0f) return true;

        float chance = 1f - Mathf.Exp(-interval / mtbSeconds);
        return Random.value < chance;
    }

    /// <summary>
    /// 발생이 확정된 정신 이상의 계열과 종류를 고르고 적용합니다.
    /// </summary>
    private void TriggerMentalBreak()
    {
        // 디버그: 침식 계열 차단 시 계열 추첨을 건너뛰고 일반 계열만 사용한다
        bool blockErosionKind = DebugManager.IsBlocked(DebugFlag.ErosionKind);
        bool preferErosion = !blockErosionKind && Random.value < GetErosionKindChance();

        // 선택한 계열에 후보가 없으면 반대 계열로 폴백한다
        if (preferErosion)
        {
            if (TryApplyErosionBreak()) return;
            TryApplyNormalBreak();
        }
        else
        {
            if (TryApplyNormalBreak()) return;
            if (!blockErosionKind) TryApplyErosionBreak();
        }
    }

    /// <summary>
    /// 침식 계열이 선택될 확률. 침식 비율에 비례하며 발생 확률 자체와는 무관합니다.
    /// </summary>
    public float GetErosionKindChance()
    {
        if (statsController == null) return 0f;

        var cfg = Cfg;
        float fullLevel = cfg != null ? cfg.erosionFullLevel : DEFAULT_EROSION_FULL;
        float weight    = cfg != null ? cfg.erosionWeightMultiplier : DEFAULT_EROSION_WEIGHT;
        if (fullLevel <= 0f) return 0f;

        return Mathf.Clamp01(statsController.ErosionLevel / fullLevel * weight);
    }

    #endregion

    #region 임계점

    /// <summary>
    /// 이 직원의 정신 이상 저항 배율.
    /// 특성·스킬의 abnormalResistMult에 재미·피로 계수를 곱해 누적합니다.
    /// 1보다 크면 저항(임계점이 내려감), 1보다 작으면 취약(임계점이 올라감).
    /// </summary>
    public float GetBreakResistance()
    {
        if (statsController == null) return 1f;

        float resist = statsController.CachedAbnormalResistMult
                     * statsController.GetFunErosionFactor()
                     * statsController.GetFatigueErosionFactor();

        return resist > 0f ? resist : 1f;
    }

    /// <summary>
    /// 이 직원의 실효 임계점(정신 비율)을 반환합니다.
    /// 저항이 높을수록 임계점이 낮아져 더 낮은 정신까지 버팁니다.
    /// </summary>
    public float GetEffectiveThreshold()
    {
        var cfg = Cfg;
        float baseThreshold = cfg != null ? cfg.breakThreshold : DEFAULT_BREAK_THRESHOLD;
        float resist = GetBreakResistance();

        return resist > 0f ? Mathf.Clamp01(baseThreshold / resist) : baseThreshold;
    }

    /// <summary>평균 발생 간격(게임일). 심각도 등급 없이 하나만 쓰고, 기울기는 depthMtbFactor가 만든다.</summary>
    private static float MtbDays
    {
        get
        {
            var cfg = Cfg;
            return cfg != null ? Mathf.Max(0.01f, cfg.mtbDays) : DEFAULT_MTB_DAYS;
        }
    }

    private static float CheckInterval
    {
        get
        {
            var cfg = Cfg;
            return cfg != null ? Mathf.Max(0.1f, cfg.checkIntervalSeconds) : DEFAULT_CHECK_INTERVAL;
        }
    }

    private static float BreakGrace
    {
        get
        {
            var cfg = Cfg;
            return cfg != null ? cfg.breakGraceSeconds : DEFAULT_BREAK_GRACE;
        }
    }

    private static float DepthMtbFactor
    {
        get
        {
            var cfg = Cfg;
            return cfg != null ? cfg.depthMtbFactor : DEFAULT_DEPTH_MTB_FACTOR;
        }
    }

    private static float ErosionCooldown
    {
        get
        {
            var cfg = Cfg;
            return cfg != null ? cfg.erosionEventCooldown : DEFAULT_EROSION_COOLDOWN;
        }
    }

    private static float ComposureBonus
    {
        get
        {
            var cfg = Cfg;
            return cfg != null ? cfg.composureBonus : DEFAULT_COMPOSURE_BONUS;
        }
    }

    private static float ComposureDuration
    {
        get
        {
            var cfg = Cfg;
            return cfg != null ? cfg.composureDurationSeconds : DEFAULT_COMPOSURE_TIME;
        }
    }

    /// <summary>
    /// 정신 이상이 끝난 직원에게 '정신차림' 버프를 겁니다.
    ///
    /// 재판정 유예(breakGraceSeconds)가 몇십 초짜리 "숨 돌릴 틈"이라면, 이 버프는
    /// 정신력 자체를 크게 끌어올려 <b>한동안 임계점 근처에도 가지 않게</b> 만드는 안전 구간이다.
    /// 시간형 모디파이어라 지속 시간이 지나면 원래 정신력으로 돌아온다.
    ///
    /// <b>의도한 운영 — 위험 작업 전에 일부러 터뜨리기</b>
    ///   침식 계열이 뽑힐 확률은 그 시점의 침식 수치가 정한다(침식 ÷ 200). 그래서
    ///   <b>침식이 낮을 때</b> 정신 이상을 맞으면 거의 확실히 일반 계열(태업 수준)로 끝나고,
    ///   그 대가로 정신차림 버프를 얻는다. 플레이어는 이걸 역이용할 수 있다:
    ///     ① 깊은 채광·제놉스 취급처럼 침식이 크게 오를 작업을 앞두고
    ///     ② 침식이 아직 깨끗한 상태에서 정신력을 일부러 떨어뜨려(오락·수면 방치) 일반 계열을 유도한 뒤
    ///     ③ 정신차림이 붙은 구간에 위험 작업을 몰아넣는다
    ///   버프가 없으면 침식이 오른 뒤에 정신이 꺾여 <b>침식 계열</b>(건물 파괴·동료 공격·침식 폭주)이
    ///   터질 수 있다. 즉 "안전할 때 미리 한 번 무너뜨려 두는" 선택이 성립한다.
    ///   이것이 정신 이상을 순수한 페널티가 아니라 <b>관리 가능한 자원</b>으로 만드는 지점이다.
    ///
    /// <b>지속 시간을 300초로 잡은 근거</b> (게임 1일 = 600초, 1시간 = 25초)
    ///   • 기본 스케줄의 작업 블록은 오전 7~11시·오후 13~17시로 각각 5 게임시간(125초)이다.
    ///     300초면 근무 블록 하나를 이동 시간까지 포함해 통째로 덮고도 남는다 —
    ///     "위험 작업 한 탕"에 맞는 길이다.
    ///   • 오락 버프(180초)보다 길다. 정신 이상을 한 번 겪는 대가를 치른 만큼
    ///     시설로 얻는 안정보다는 나아야 한다.
    ///   • 평균 발생 간격(450초)보다는 짧다. 버프가 MTB보다 길면 한 번 터뜨린 뒤
    ///     영구히 안전해져 판정 자체가 무의미해진다.
    ///   모든 타이머는 Time.deltaTime 기반이라 배속(1~3x)에 그대로 비례한다 —
    ///   3배속에서는 현실 100초다.
    /// </summary>
    private void ApplyComposure()
    {
        if (employee == null || statsController == null) return;

        float bonus = ComposureBonus;
        if (bonus <= 0f) return;

        // 누산이 아니라 재설정 — 연속으로 터져도 버프가 겹쳐 쌓이지 않게 한다
        statsController.RemoveMentalModifier(MentalReason.COMPOSURE);
        statsController.ModifyMental(bonus, MentalReason.COMPOSURE, "정신을 차림", ComposureDuration);

        PushBreakLetter("정신차림",
                        $"{employee.DisplayName}이(가) 정신을 차렸습니다. 약 {ComposureDuration:F0}초 동안은 다시 무너지지 않습니다 — 침식 위험 작업을 몰아넣기 좋은 구간입니다.",
                        LetterType.Positive, false);

        Debug.Log($"[Mental] {employee.DisplayName}: 정신차림 +{bonus:F0} ({ComposureDuration:F0}초)");
    }

    private static float DayLengthSeconds
        => DayCycle.instance != null ? DayCycle.instance.DayLengthInSeconds : FALLBACK_DAY_LENGTH;

    #endregion

    #region 정신 이상 적용 — 일반 계열

    /// <summary>
    /// 일반 계열 정신 이상을 하나 골라 적용합니다. 후보가 없으면 false.
    /// </summary>
    private bool TryApplyNormalBreak()
    {
        var available = NORMAL_POOL.Where(e => !IsOnCooldown(e)).ToArray();
        if (available.Length == 0) return false;

        ApplyNormalEvent(available[Random.Range(0, available.Length)]);
        return true;
    }

    private bool IsOnCooldown(MentalEventType type)
        => normalCooldowns.TryGetValue(type, out float remaining) && remaining > 0f;

    private void ApplyNormalEvent(MentalEventType type)
    {
        float duration = EVENT_DURATIONS.TryGetValue(type, out float d) ? d : 10f;
        float cooldown = EVENT_COOLDOWNS.TryGetValue(type, out float c) ? c : 60f;

        switch (type)
        {
            case MentalEventType.WorkSlowdown:
                activeSpeedModifier = 0.5f;
                Debug.Log($"[Mental] {employee.DisplayName}: 작업 속도 감소 ({duration}초)");
                break;

            case MentalEventType.RefuseWork:
                isRefusingWork = true;
                employee.CancelWork();
                Debug.Log($"[Mental] {employee.DisplayName}: 작업 거부 ({duration}초)");
                break;

            case MentalEventType.Wander:
                employee.CancelWork();
                WanderToRandomPosition();
                Debug.Log($"[Mental] {employee.DisplayName}: 방황 시작");
                break;

            case MentalEventType.EmotionalOutburst:
                AffectNearbyEmployees(OUTBURST_RADIUS, -OUTBURST_MENTAL_DAMAGE);
                Debug.Log($"[Mental] {employee.DisplayName}: 감정 폭발!");
                break;
        }

        activeMentalEvents.Add(new ActiveMentalEvent
        {
            type = type,
            abnormalType = AbnormalBehaviorType.None,
            remainingTime = duration,
            cooldownRemaining = cooldown
        });

        normalCooldowns[type] = cooldown;

        PushBreakLetter(BreakLabel(type), NormalBreakBody(type, duration), LetterType.Neutral, false);
    }

    #endregion

    #region 정신 이상 적용 — 침식 계열

    /// <summary>
    /// 침식 계열 정신 이상을 하나 골라 적용합니다. 후보가 없으면 false.
    /// AbnormalBehaviorRegistry에 실제 등록된 구현체만 후보가 됩니다.
    /// </summary>
    private bool TryApplyErosionBreak()
    {
        var registered = AbnormalBehaviorRegistry.FilterRegistered(EROSION_POOL);
        if (registered.Count == 0) return false;

        // 쿨다운 중이 아니고 실행 가능한 것만
        var candidates = registered
            .Where(t => !IsOnCooldown(t))
            .Select(AbnormalBehaviorRegistry.Get)
            .Where(b => b != null && b.CanExecute(employee))
            .ToList();

        if (candidates.Count == 0) return false;

        ApplyErosionEvent(candidates[Random.Range(0, candidates.Count)]);
        return true;
    }

    private bool IsOnCooldown(AbnormalBehaviorType type)
        => erosionCooldowns.TryGetValue(type, out float remaining) && remaining > 0f;

    private void ApplyErosionEvent(IAbnormalBehavior behavior)
    {
        float duration = behavior.Execute(employee);
        float cooldown = ErosionCooldown;

        if (IsCommandIgnoreKind(behavior.BehaviorType))
            isBlockingWorkAssignment = true;

        activeMentalEvents.Add(new ActiveMentalEvent
        {
            type = MentalEventType.None,
            abnormalType = behavior.BehaviorType,
            remainingTime = duration,
            cooldownRemaining = cooldown
        });

        erosionCooldowns[behavior.BehaviorType] = cooldown;

        bool destructive = IsDestructiveKind(behavior.BehaviorType);
        PushBreakLetter(BreakLabel(behavior.BehaviorType),
                        ErosionBreakBody(behavior.BehaviorType, duration),
                        LetterType.Threat, destructive);

        Debug.Log($"[Mental] {employee.DisplayName}: 침식 계열 정신 이상 — {behavior.BehaviorType} ({duration:F0}초)");
    }

    /// <summary>
    /// 지속되는 동안 새 작업 배정을 막아야 하는 이상 행동인지.
    /// 종료 처리와 세이브 복원도 이 판정을 함께 쓴다.
    /// </summary>
    private static bool IsCommandIgnoreKind(AbnormalBehaviorType type)
        => type == AbnormalBehaviorType.IgnoreCommand ||
           type == AbnormalBehaviorType.IgnoreCommandEnhanced ||
           type == AbnormalBehaviorType.WorkStop;

    #endregion

    #region 플레이어 알림

    /// <summary>
    /// 정신 이상 발생·종료를 레터로 알립니다.
    ///
    /// 그동안 정신 이상은 콘솔 로그로만 남아서, 기지가 부서지거나 동료가 맞고 있어도
    /// 플레이어가 알아채지 못했다. 손실이 큰 침식 계열 3종(침식 폭주·건물 파괴·동료 공격)은
    /// 침공과 같은 등급으로 취급해 <b>확인할 때까지 일시정지</b>한다.
    /// </summary>
    private void PushBreakLetter(string label, string body, LetterType type, bool pause)
    {
        if (employee == null) return;

        NotificationManager.instance?.PushLetter(new Letter
        {
            title = $"{label} — {employee.DisplayName}",
            body = body,
            type = type,
            pauseUntilRead = pause
        });

        if (pause) TimeManager.instance?.ForcePause();
    }

    /// <summary>손실이 커서 플레이어가 즉시 개입해야 하는 침식 계열인지.</summary>
    private static bool IsDestructiveKind(AbnormalBehaviorType type)
        => type == AbnormalBehaviorType.ErosionOutburst
        || type == AbnormalBehaviorType.AttackBuilding
        || type == AbnormalBehaviorType.FriendlyAttack;

    private static string BreakLabel(MentalEventType type) => type switch
    {
        MentalEventType.WorkSlowdown      => "작업 둔화",
        MentalEventType.RefuseWork        => "작업 거부",
        MentalEventType.Wander            => "방황",
        MentalEventType.EmotionalOutburst => "감정 폭발",
        _                                 => "정신 이상",
    };

    private static string BreakLabel(AbnormalBehaviorType type) => type switch
    {
        AbnormalBehaviorType.IgnoreCommand   => "명령 무시",
        AbnormalBehaviorType.RandomMove      => "무작위 이동",
        AbnormalBehaviorType.WorkStop        => "작업 중단",
        AbnormalBehaviorType.FriendlyAttack  => "동료 공격",
        AbnormalBehaviorType.ErosionOutburst => "침식 폭주",
        AbnormalBehaviorType.AttackBuilding  => "건물 파괴 충동",
        _                                    => "침식 이상 행동",
    };

    private string NormalBreakBody(MentalEventType type, float duration) => type switch
    {
        MentalEventType.WorkSlowdown      => $"{employee.DisplayName}의 작업 속도가 절반으로 떨어졌습니다. ({duration:F0}초)",
        MentalEventType.RefuseWork        => $"{employee.DisplayName}이(가) 일을 놓고 새 작업도 받지 않습니다. ({duration:F0}초)",
        MentalEventType.Wander            => $"{employee.DisplayName}이(가) 일을 멈추고 정처 없이 돌아다닙니다. ({duration:F0}초)",
        MentalEventType.EmotionalOutburst => $"{employee.DisplayName}이(가) 감정을 터뜨려 주변 동료의 정신력이 깎였습니다.",
        _                                 => $"{employee.DisplayName}의 정신이 흔들리고 있습니다. ({duration:F0}초)",
    };

    private string ErosionBreakBody(AbnormalBehaviorType type, float duration) => type switch
    {
        AbnormalBehaviorType.ErosionOutburst => $"{employee.DisplayName}이(가) 그 자리에 멈춰 주변에 침식을 흩뿌립니다. 소집도 되지 않습니다 — 동료를 물리세요. ({duration:F0}초)",
        AbnormalBehaviorType.AttackBuilding  => $"{employee.DisplayName}이(가) 주변 건물을 부수기 시작했습니다. ({duration:F0}초)",
        AbnormalBehaviorType.FriendlyAttack  => $"{employee.DisplayName}이(가) 동료를 공격합니다. ({duration:F0}초)",
        AbnormalBehaviorType.WorkStop        => $"{employee.DisplayName}이(가) 하던 작업을 망가뜨리고 손을 놓았습니다 — 진행도가 깎였습니다. ({duration:F0}초)",
        AbnormalBehaviorType.IgnoreCommand   => $"{employee.DisplayName}이(가) 명령을 듣지 않습니다. ({duration:F0}초)",
        AbnormalBehaviorType.RandomMove      => $"{employee.DisplayName}이(가) 제멋대로 움직입니다. ({duration:F0}초)",
        _                                    => $"{employee.DisplayName}에게 침식 계열 이상 행동이 나타났습니다. ({duration:F0}초)",
    };

    #endregion

    #region 활성 정신 이상 관리

    /// <summary>
    /// 활성 정신 이상의 시간을 갱신하고 만료된 항목을 제거합니다.
    /// </summary>
    private void UpdateActiveMentalEvents(float deltaTime)
    {
        bool anyEnded = false;

        for (int i = activeMentalEvents.Count - 1; i >= 0; i--)
        {
            activeMentalEvents[i].remainingTime -= deltaTime;

            if (activeMentalEvents[i].remainingTime > 0f)
            {
                // 지속형 침식 이상 행동(제자리 침식·건물 공격·동료 공격)을 구동한다.
                // 단발형 구현체는 Tick이 비어 있으므로 그냥 지나간다.
                if (activeMentalEvents[i].IsErosionKind)
                    AbnormalBehaviorRegistry.Get(activeMentalEvents[i].abnormalType)?.Tick(employee, deltaTime);

                continue;
            }

            RemoveMentalEventEffect(activeMentalEvents[i]);
            activeMentalEvents.RemoveAt(i);
            anyEnded = true;
        }

        // 마지막 정신 이상이 끝나면 재판정 유예 + '정신차림' 버프를 건다
        if (anyEnded && activeMentalEvents.Count == 0)
        {
            graceTimer = BreakGrace;
            ApplyComposure();
        }
    }

    /// <summary>
    /// 정신 이상 종료 시 효과를 제거합니다.
    /// </summary>
    private void RemoveMentalEventEffect(ActiveMentalEvent evt)
    {
        if (evt.IsErosionKind)
        {
            AbnormalBehaviorRegistry.Get(evt.abnormalType)?.OnEnd(employee);

            if (IsCommandIgnoreKind(evt.abnormalType) &&
                !activeMentalEvents.Any(e => e != evt && IsCommandIgnoreKind(e.abnormalType) && e.remainingTime > 0f))
            {
                isBlockingWorkAssignment = false;
            }

            Debug.Log($"[Mental] {employee?.DisplayName}: {evt.abnormalType} 정신 이상 종료");
            return;
        }

        switch (evt.type)
        {
            case MentalEventType.WorkSlowdown:
                if (!activeMentalEvents.Any(e => e != evt && e.type == MentalEventType.WorkSlowdown && e.remainingTime > 0f))
                    activeSpeedModifier = 1f;
                break;

            case MentalEventType.RefuseWork:
                if (!activeMentalEvents.Any(e => e != evt && e.type == MentalEventType.RefuseWork && e.remainingTime > 0f))
                    isRefusingWork = false;
                break;
        }

        Debug.Log($"[Mental] {employee?.DisplayName}: {evt.type} 정신 이상 종료");
    }

    /// <summary>
    /// 쿨다운 타이머를 감소시킵니다.
    /// </summary>
    private void UpdateCooldowns(float deltaTime)
    {
        var normalKeys = new List<MentalEventType>(normalCooldowns.Keys);
        foreach (var key in normalKeys)
        {
            normalCooldowns[key] -= deltaTime;
            if (normalCooldowns[key] <= 0f) normalCooldowns.Remove(key);
        }

        var erosionKeys = new List<AbnormalBehaviorType>(erosionCooldowns.Keys);
        foreach (var key in erosionKeys)
        {
            erosionCooldowns[key] -= deltaTime;
            if (erosionCooldowns[key] <= 0f) erosionCooldowns.Remove(key);
        }
    }

    #endregion

    #region 헬퍼

    /// <summary>
    /// 랜덤 위치로 이동합니다 (방황).
    /// </summary>
    private void WanderToRandomPosition()
    {
        if (movement == null) return;

        Vector2Int footTile = movement.GetFootTile();
        int randomX = footTile.x + Random.Range(-5, 6);
        int randomY = footTile.y + Random.Range(-2, 3);

        randomX = Mathf.Clamp(randomX, 0, GameMap.MAP_WIDTH - 1);
        randomY = Mathf.Clamp(randomY, 0, GameMap.MAP_HEIGHT - 1);

        // 정신 이상 상태의 방황 — 배정 구역을 무시한다 (이상 행동과 동일 규칙).
        Vector3 wanderTarget = new Vector3(randomX + 0.5f, randomY, 0);
        movement.MoveTo(wanderTarget);
    }

    /// <summary>
    /// 주변 직원의 정신력에 영향을 줍니다 (감정 폭발).
    /// </summary>
    private void AffectNearbyEmployees(float radius, float mentalDamage)
    {
        if (EmployeeManager.instance == null) return;

        Vector3 myPos = transform.position;

        foreach (var emp in EmployeeManager.instance.AllEmployees)
        {
            if (emp == null || emp == employee || emp.State == EmployeeState.Dead) continue;

            float dist = Vector2.Distance(myPos, emp.transform.position);
            if (dist <= radius)
            {
                emp.ModifyMental(mentalDamage, MentalReason.OUTBURST, "동료의 감정 폭발을 목격함");
                Debug.Log($"[Mental] {emp.DisplayName} 감정 폭발 영향: 정신력 {mentalDamage}");
            }
        }
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 현재 활성 속도 보정을 반환합니다.
    /// EmployeeWork에서 GetWorkSpeed() 계산 시 사용합니다.
    /// </summary>
    public float GetActiveSpeedModifier()
    {
        return activeSpeedModifier;
    }

    /// <summary>
    /// 모든 활성 정신 이상을 강제 종료합니다.
    /// </summary>
    public void ClearAllEvents()
    {
        for (int i = activeMentalEvents.Count - 1; i >= 0; i--)
        {
            var evt = activeMentalEvents[i];
            activeMentalEvents.RemoveAt(i);
            RemoveMentalEventEffect(evt);
        }

        activeMentalEvents.Clear();
        activeSpeedModifier = 1f;
        isRefusingWork = false;
        isBlockingWorkAssignment = false;
    }

    #endregion

    #region 저장/복원

    /// <summary>
    /// 저장 데이터에 정신 이상 정보를 기록합니다.
    /// </summary>
    public void PopulateSaveData(EmployeeSaveData data)
    {
        if (data.activeMentalEvents == null)
            data.activeMentalEvents = new List<MentalEventSaveData>();

        data.activeMentalEvents.Clear();

        foreach (var evt in activeMentalEvents)
        {
            data.activeMentalEvents.Add(new MentalEventSaveData
            {
                eventType = (int)evt.type,
                abnormalType = (int)evt.abnormalType,
                remainingTime = evt.remainingTime,
                cooldownRemaining = evt.cooldownRemaining
            });
        }

        data.mentalBreakGraceRemaining = graceTimer;
    }

    /// <summary>
    /// 저장 데이터에서 정신 이상을 복원합니다.
    /// </summary>
    public void RestoreFromSaveData(EmployeeSaveData data)
    {
        activeMentalEvents.Clear();
        normalCooldowns.Clear();
        erosionCooldowns.Clear();
        activeSpeedModifier = 1f;
        isRefusingWork = false;
        isBlockingWorkAssignment = false;
        graceTimer = data.mentalBreakGraceRemaining;
        checkTimer = CheckInterval;

        if (data.activeMentalEvents == null) return;

        foreach (var saved in data.activeMentalEvents)
        {
            var type = (MentalEventType)saved.eventType;
            var abnormalType = (AbnormalBehaviorType)saved.abnormalType;

            activeMentalEvents.Add(new ActiveMentalEvent
            {
                type = type,
                abnormalType = abnormalType,
                remainingTime = saved.remainingTime,
                cooldownRemaining = saved.cooldownRemaining
            });

            // 효과 재적용 — 침식 계열은 Execute를 다시 돌리지 않고 차단 플래그만 되살린다
            if (abnormalType != AbnormalBehaviorType.None)
            {
                if (IsCommandIgnoreKind(abnormalType)) isBlockingWorkAssignment = true;
                if (saved.cooldownRemaining > 0f) erosionCooldowns[abnormalType] = saved.cooldownRemaining;
                continue;
            }

            switch (type)
            {
                case MentalEventType.WorkSlowdown:
                    activeSpeedModifier = 0.5f;
                    break;
                case MentalEventType.RefuseWork:
                    isRefusingWork = true;
                    break;
            }

            if (saved.cooldownRemaining > 0f)
                normalCooldowns[type] = saved.cooldownRemaining;
        }
    }

    #endregion
}
