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
///   2. 정신 비율이 실효 임계점 아래면 해당 심각도가 후보가 된다 (가장 심각한 단계 우선).
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
/// </summary>
public class EmployeeMental : MonoBehaviour
{
    #region 상수 (Config 미할당 시 사용하는 기본값)

    private const float DEFAULT_CHECK_INTERVAL   = 2.5f;
    private const float DEFAULT_BREAK_GRACE      = 30f;
    private const float DEFAULT_MINOR_THRESHOLD  = 0.50f;
    private const float DEFAULT_MAJOR_THRESHOLD  = 0.30f;
    private const float DEFAULT_EXTREME_THRESHOLD = 0.15f;
    private const float DEFAULT_MINOR_MTB_DAYS   = 0.75f;
    private const float DEFAULT_MAJOR_MTB_DAYS   = 0.35f;
    private const float DEFAULT_EXTREME_MTB_DAYS = 0.15f;
    private const float DEFAULT_DEPTH_MTB_FACTOR = 1f;
    private const float DEFAULT_EROSION_FULL     = 200f;
    private const float DEFAULT_EROSION_WEIGHT   = 1f;
    private const float DEFAULT_EROSION_COOLDOWN = 45f;

    /// <summary>DayCycle이 없을 때 사용할 게임 1일 길이 (초)</summary>
    private const float FALLBACK_DAY_LENGTH = 600f;

    /// <summary>감정 폭발 영향 반경 (타일)</summary>
    private const float OUTBURST_RADIUS = 5f;

    /// <summary>감정 폭발이 주변 직원에게 주는 정신력 피해</summary>
    private const float OUTBURST_MENTAL_DAMAGE = 5f;

    #endregion

    #region 정신 이상 풀

    /// <summary>심각도가 높은 순서 (판정은 가장 심각한 단계부터 매칭)</summary>
    private static readonly MentalSeverity[] SEVERITIES_DESC =
    {
        MentalSeverity.High, MentalSeverity.Medium, MentalSeverity.Low
    };

    /// <summary>일반 계열 — 침식과 무관하게 정신력만으로 발생하는 정신 이상</summary>
    private static readonly Dictionary<MentalSeverity, MentalEventType[]> NORMAL_POOLS = new Dictionary<MentalSeverity, MentalEventType[]>
    {
        { MentalSeverity.Low,    new[] { MentalEventType.WorkSlowdown } },
        { MentalSeverity.Medium, new[] { MentalEventType.WorkSlowdown, MentalEventType.RefuseWork, MentalEventType.Wander } },
        { MentalSeverity.High,   new[] { MentalEventType.RefuseWork, MentalEventType.Wander, MentalEventType.EmotionalOutburst } },
    };

    /// <summary>
    /// 침식 계열 — 침식 수치가 높을수록 이쪽에서 뽑힌다.
    /// AbnormalBehaviorRegistry에 실제 등록된 구현체만 후보가 되므로(FilterRegistered),
    /// 미구현 타입을 여기 적어두어도 안전합니다.
    /// </summary>
    private static readonly Dictionary<MentalSeverity, List<AbnormalBehaviorType>> EROSION_POOLS = new Dictionary<MentalSeverity, List<AbnormalBehaviorType>>
    {
        { MentalSeverity.Low, new List<AbnormalBehaviorType>
            { AbnormalBehaviorType.IgnoreCommand, AbnormalBehaviorType.RandomMove } },

        { MentalSeverity.Medium, new List<AbnormalBehaviorType>
            { AbnormalBehaviorType.RandomMove, AbnormalBehaviorType.WorkStop,
              AbnormalBehaviorType.FriendlyAttack, AbnormalBehaviorType.IgnoreCommandEnhanced } },

        { MentalSeverity.High, new List<AbnormalBehaviorType>
            { AbnormalBehaviorType.IgnoreCommandEnhanced, AbnormalBehaviorType.MoveTowardEnemy,
              AbnormalBehaviorType.FriendlyAttackEnhanced, AbnormalBehaviorType.Flee,
              AbnormalBehaviorType.ErosionTrailExplosion } },
    };

    /// <summary>일반 계열 지속 시간 (초)</summary>
    private static readonly Dictionary<MentalEventType, float> EVENT_DURATIONS = new Dictionary<MentalEventType, float>
    {
        { MentalEventType.WorkSlowdown,     30f },
        { MentalEventType.RefuseWork,       20f },
        { MentalEventType.Wander,           15f },
        { MentalEventType.EmotionalOutburst, 5f },
    };

    /// <summary>일반 계열 재발생 대기 시간 (초)</summary>
    private static readonly Dictionary<MentalEventType, float> EVENT_COOLDOWNS = new Dictionary<MentalEventType, float>
    {
        { MentalEventType.WorkSlowdown,      60f },
        { MentalEventType.RefuseWork,        45f },
        { MentalEventType.Wander,            40f },
        { MentalEventType.EmotionalOutburst, 90f },
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

        // 이미 정신 이상이 진행 중이면 새로 굴리지 않는다
        if (activeMentalEvents.Count > 0) return;

        EmployeeStats stats = statsController.Stats;
        if (stats.maxMental <= 0f) return;

        float mentalRatio = stats.mental / stats.maxMental;

        // 1) 심각도 결정 — 가장 심각한 단계부터 매칭
        float resist = GetBreakResistance();
        MentalSeverity severity = MentalSeverity.Normal;
        float matchedThreshold = 0f;

        foreach (var candidate in SEVERITIES_DESC)
        {
            float threshold = GetEffectiveThreshold(candidate, resist);
            if (mentalRatio < threshold)
            {
                severity = candidate;
                matchedThreshold = threshold;
                break;
            }
        }

        if (severity == MentalSeverity.Normal) return;

        // 2) 발생 확률 — MTB(평균 발생 간격)를 확률로 환산
        if (!RollMentalBreak(severity, mentalRatio, matchedThreshold, interval)) return;

        // 3) 계열 결정 — 침식 수치가 높을수록 침식 계열이 뽑힌다
        TriggerMentalBreak(severity);
    }

    /// <summary>
    /// 평균 발생 간격(MTB)을 이번 판정 구간의 확률로 환산해 굴립니다.
    /// p = 1 - exp(-Δt / MTB) — 지수 분포라 판정 주기를 바꿔도 장기 발생 빈도는 유지됩니다.
    /// </summary>
    private bool RollMentalBreak(MentalSeverity severity, float mentalRatio, float threshold, float interval)
    {
        float mtbDays = GetMtbDays(severity);
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
    private void TriggerMentalBreak(MentalSeverity severity)
    {
        bool preferErosion = Random.value < GetErosionKindChance();

        // 선택한 계열에 후보가 없으면 반대 계열로 폴백한다
        if (preferErosion)
        {
            if (TryApplyErosionBreak(severity)) return;
            TryApplyNormalBreak(severity);
        }
        else
        {
            if (TryApplyNormalBreak(severity)) return;
            TryApplyErosionBreak(severity);
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
    public float GetEffectiveThreshold(MentalSeverity severity)
        => GetEffectiveThreshold(severity, GetBreakResistance());

    private float GetEffectiveThreshold(MentalSeverity severity, float resist)
    {
        var cfg = Cfg;
        float baseThreshold = cfg != null ? cfg.GetThreshold(severity) : severity switch
        {
            MentalSeverity.Low    => DEFAULT_MINOR_THRESHOLD,
            MentalSeverity.Medium => DEFAULT_MAJOR_THRESHOLD,
            MentalSeverity.High   => DEFAULT_EXTREME_THRESHOLD,
            _                     => 0f,
        };

        return resist > 0f ? Mathf.Clamp01(baseThreshold / resist) : baseThreshold;
    }

    private float GetMtbDays(MentalSeverity severity)
    {
        var cfg = Cfg;
        if (cfg != null) return cfg.GetMtbDays(severity);

        return severity switch
        {
            MentalSeverity.Low    => DEFAULT_MINOR_MTB_DAYS,
            MentalSeverity.Medium => DEFAULT_MAJOR_MTB_DAYS,
            MentalSeverity.High   => DEFAULT_EXTREME_MTB_DAYS,
            _                     => float.MaxValue,
        };
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

    private static float DayLengthSeconds
        => DayCycle.instance != null ? DayCycle.instance.DayLengthInSeconds : FALLBACK_DAY_LENGTH;

    #endregion

    #region 정신 이상 적용 — 일반 계열

    /// <summary>
    /// 일반 계열 정신 이상을 하나 골라 적용합니다. 후보가 없으면 false.
    /// </summary>
    private bool TryApplyNormalBreak(MentalSeverity severity)
    {
        if (!NORMAL_POOLS.TryGetValue(severity, out var pool)) return false;

        var available = pool.Where(e => !IsOnCooldown(e)).ToArray();
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
    }

    #endregion

    #region 정신 이상 적용 — 침식 계열

    /// <summary>
    /// 침식 계열 정신 이상을 하나 골라 적용합니다. 후보가 없으면 false.
    /// AbnormalBehaviorRegistry에 실제 등록된 구현체만 후보가 됩니다.
    /// </summary>
    private bool TryApplyErosionBreak(MentalSeverity severity)
    {
        if (!EROSION_POOLS.TryGetValue(severity, out var pool)) return false;

        var registered = AbnormalBehaviorRegistry.FilterRegistered(pool);
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

        Debug.Log($"[Mental] {employee.DisplayName}: 침식 계열 정신 이상 — {behavior.BehaviorType} ({duration:F0}초)");
    }

    private static bool IsCommandIgnoreKind(AbnormalBehaviorType type)
        => type == AbnormalBehaviorType.IgnoreCommand ||
           type == AbnormalBehaviorType.IgnoreCommandEnhanced;

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
            if (activeMentalEvents[i].remainingTime > 0f) continue;

            RemoveMentalEventEffect(activeMentalEvents[i]);
            activeMentalEvents.RemoveAt(i);
            anyEnded = true;
        }

        // 마지막 정신 이상이 끝나면 재판정 유예를 건다
        if (anyEnded && activeMentalEvents.Count == 0)
            graceTimer = BreakGrace;
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
