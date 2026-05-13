using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 정신 이벤트 시스템 컴포넌트.
/// 정신력 임계값에 따라 확률 기반으로 정신 이벤트를 발생시킵니다.
///
/// 임계값별 평가:
///   mental ≥ 50%: 이벤트 없음
///   mental &lt; 50%: Low (10%) → WorkSlowdown
///   mental &lt; 30%: Medium (20%) → WorkSlowdown, RefuseWork, Wander
///   mental &lt; 15%: High (40%) → RefuseWork, Wander, EmotionalOutburst
///   mental ≤ 0%: MentalBreak (StatsController에서 확정 처리)
///
/// 타이머 기반: 5~10초 간격으로 평가 (매 프레임 아님)
/// </summary>
public class EmployeeMental : MonoBehaviour
{
    #region 상수

    /// <summary>평가 간격 (초)</summary>
    private const float EVALUATION_INTERVAL = 7f;

    /// <summary>감정 폭발 영향 반경 (타일)</summary>
    private const float OUTBURST_RADIUS = 5f;

    /// <summary>감정 폭발 정신력 감소량</summary>
    private const float OUTBURST_MENTAL_DAMAGE = 5f;

    #endregion

    #region 임계값 설정

    /// <summary>정신력 임계값 정의</summary>
    private struct MentalThreshold
    {
        public float ratio;
        public MentalSeverity severity;
        public float baseChance;
    }

    private static readonly MentalThreshold[] THRESHOLDS = new MentalThreshold[]
    {
        new MentalThreshold { ratio = 0.50f, severity = MentalSeverity.Low,    baseChance = 0.10f },
        new MentalThreshold { ratio = 0.30f, severity = MentalSeverity.Medium, baseChance = 0.20f },
        new MentalThreshold { ratio = 0.15f, severity = MentalSeverity.High,   baseChance = 0.40f },
    };

    /// <summary>심각도별 이벤트 풀</summary>
    private static readonly Dictionary<MentalSeverity, MentalEventType[]> EVENT_POOLS = new Dictionary<MentalSeverity, MentalEventType[]>
    {
        { MentalSeverity.Low,    new[] { MentalEventType.WorkSlowdown } },
        { MentalSeverity.Medium, new[] { MentalEventType.WorkSlowdown, MentalEventType.RefuseWork, MentalEventType.Wander } },
        { MentalSeverity.High,   new[] { MentalEventType.RefuseWork, MentalEventType.Wander, MentalEventType.EmotionalOutburst } },
    };

    /// <summary>이벤트 타입별 지속 시간 (초)</summary>
    private static readonly Dictionary<MentalEventType, float> EVENT_DURATIONS = new Dictionary<MentalEventType, float>
    {
        { MentalEventType.WorkSlowdown,     30f },
        { MentalEventType.RefuseWork,       20f },
        { MentalEventType.Wander,           15f },
        { MentalEventType.EmotionalOutburst, 5f },
    };

    /// <summary>이벤트 타입별 쿨다운 (초)</summary>
    private static readonly Dictionary<MentalEventType, float> EVENT_COOLDOWNS = new Dictionary<MentalEventType, float>
    {
        { MentalEventType.WorkSlowdown,      60f },
        { MentalEventType.RefuseWork,        45f },
        { MentalEventType.Wander,            40f },
        { MentalEventType.EmotionalOutburst, 90f },
    };

    #endregion

    #region 필드

    /// <summary>마지막 평가 시각</summary>
    private float lastEvaluationTime;

    /// <summary>활성 정신 이벤트 목록</summary>
    private List<ActiveMentalEvent> activeMentalEvents = new List<ActiveMentalEvent>();

    /// <summary>이벤트 타입별 쿨다운 타이머</summary>
    private Dictionary<MentalEventType, float> cooldownTimers = new Dictionary<MentalEventType, float>();

    /// <summary>현재 작업 속도 보정 (1.0 = 정상)</summary>
    private float activeSpeedModifier = 1f;

    /// <summary>현재 작업 거부 상태</summary>
    private bool isRefusingWork = false;

    // 컴포넌트 참조
    private Employee employee;
    private EmployeeStatsController statsController;
    private EmployeeMovement movement;

    #endregion

    #region 프로퍼티

    /// <summary>현재 활성 속도 보정 (정신 이벤트에 의한)</summary>
    public float ActiveSpeedModifier => activeSpeedModifier;

    /// <summary>현재 작업 거부 중인지 여부</summary>
    public bool IsRefusingWork => isRefusingWork;

    /// <summary>활성 이벤트 수</summary>
    public int ActiveEventCount => activeMentalEvents.Count;

    #endregion

    #region 초기화

    void Awake()
    {
        employee = GetComponent<Employee>();
        statsController = GetComponent<EmployeeStatsController>();
        movement = GetComponent<EmployeeMovement>();
    }

    #endregion

    #region 업데이트

    void Update()
    {
        if (employee == null || employee.State == EmployeeState.Dead) return;

        // 활성 이벤트 시간 업데이트
        UpdateActiveMentalEvents(Time.deltaTime);

        // 쿨다운 감소
        UpdateCooldowns(Time.deltaTime);

        // 주기적 평가
        if (Time.time - lastEvaluationTime >= EVALUATION_INTERVAL)
        {
            lastEvaluationTime = Time.time;
            EvaluateMentalState();
        }
    }

    #endregion

    #region 이벤트 평가

    /// <summary>
    /// 현재 정신력 상태를 평가하고 필요 시 이벤트를 발생시킵니다.
    /// </summary>
    private void EvaluateMentalState()
    {
        if (statsController == null) return;

        EmployeeStats stats = statsController.Stats;
        if (stats.maxMental <= 0f) return;

        float mentalRatio = stats.mental / stats.maxMental;

        // 가장 심각한 해당 임계값 찾기
        MentalThreshold? matchedThreshold = null;
        foreach (var threshold in THRESHOLDS)
        {
            if (mentalRatio < threshold.ratio)
            {
                // 더 심각한 임계값이 있으면 그것을 사용
                if (!matchedThreshold.HasValue || threshold.ratio < matchedThreshold.Value.ratio)
                {
                    matchedThreshold = threshold;
                }
            }
        }

        if (!matchedThreshold.HasValue) return;

        // 확률 판정
        if (Random.value > matchedThreshold.Value.baseChance) return;

        // 해당 심각도의 이벤트 풀에서 랜덤 선택
        if (!EVENT_POOLS.TryGetValue(matchedThreshold.Value.severity, out var pool)) return;

        // 쿨다운 중이 아닌 이벤트만 후보
        var availableEvents = pool.Where(e => !IsOnCooldown(e)).ToArray();
        if (availableEvents.Length == 0) return;

        MentalEventType selectedEvent = availableEvents[Random.Range(0, availableEvents.Length)];
        ApplyMentalEvent(selectedEvent);
    }

    /// <summary>
    /// 특정 이벤트 타입이 쿨다운 중인지 확인합니다.
    /// </summary>
    private bool IsOnCooldown(MentalEventType type)
    {
        return cooldownTimers.TryGetValue(type, out float remaining) && remaining > 0f;
    }

    #endregion

    #region 이벤트 적용

    /// <summary>
    /// 정신 이벤트를 적용합니다.
    /// </summary>
    private void ApplyMentalEvent(MentalEventType type)
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
            remainingTime = duration,
            cooldownRemaining = cooldown
        });

        // 쿨다운 시작
        cooldownTimers[type] = cooldown;
    }

    /// <summary>
    /// 이벤트 종료 시 효과를 제거합니다.
    /// </summary>
    private void RemoveMentalEventEffect(MentalEventType type)
    {
        switch (type)
        {
            case MentalEventType.WorkSlowdown:
                // 다른 WorkSlowdown이 활성 상태인지 확인
                bool hasOtherSlowdown = activeMentalEvents.Any(e =>
                    e.type == MentalEventType.WorkSlowdown && e.remainingTime > 0f);
                if (!hasOtherSlowdown)
                {
                    activeSpeedModifier = 1f;
                }
                break;

            case MentalEventType.RefuseWork:
                bool hasOtherRefuse = activeMentalEvents.Any(e =>
                    e.type == MentalEventType.RefuseWork && e.remainingTime > 0f);
                if (!hasOtherRefuse)
                {
                    isRefusingWork = false;
                }
                break;
        }

        Debug.Log($"[Mental] {employee?.DisplayName}: {type} 이벤트 종료");
    }

    #endregion

    #region 활성 이벤트 관리

    /// <summary>
    /// 활성 이벤트의 시간을 업데이트하고 만료된 이벤트를 제거합니다.
    /// </summary>
    private void UpdateActiveMentalEvents(float deltaTime)
    {
        for (int i = activeMentalEvents.Count - 1; i >= 0; i--)
        {
            activeMentalEvents[i].remainingTime -= deltaTime;

            if (activeMentalEvents[i].remainingTime <= 0f)
            {
                RemoveMentalEventEffect(activeMentalEvents[i].type);
                activeMentalEvents.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// 쿨다운 타이머를 감소시킵니다.
    /// </summary>
    private void UpdateCooldowns(float deltaTime)
    {
        var keys = new List<MentalEventType>(cooldownTimers.Keys);
        foreach (var key in keys)
        {
            cooldownTimers[key] -= deltaTime;
            if (cooldownTimers[key] <= 0f)
            {
                cooldownTimers.Remove(key);
            }
        }
    }

    #endregion

    #region 헬퍼

    /// <summary>
    /// 랜덤 위치로 이동합니다 (방황 이벤트).
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
                emp.ModifyMental(mentalDamage);
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
    /// 모든 활성 이벤트를 강제 종료합니다.
    /// </summary>
    public void ClearAllEvents()
    {
        foreach (var evt in activeMentalEvents)
        {
            RemoveMentalEventEffect(evt.type);
        }
        activeMentalEvents.Clear();
        activeSpeedModifier = 1f;
        isRefusingWork = false;
    }

    #endregion

    #region 저장/복원

    /// <summary>
    /// 저장 데이터에 정신 이벤트 정보를 기록합니다.
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
                remainingTime = evt.remainingTime,
                cooldownRemaining = evt.cooldownRemaining
            });
        }
    }

    /// <summary>
    /// 저장 데이터에서 정신 이벤트를 복원합니다.
    /// </summary>
    public void RestoreFromSaveData(EmployeeSaveData data)
    {
        activeMentalEvents.Clear();
        cooldownTimers.Clear();
        activeSpeedModifier = 1f;
        isRefusingWork = false;

        if (data.activeMentalEvents == null) return;

        foreach (var saved in data.activeMentalEvents)
        {
            var type = (MentalEventType)saved.eventType;

            activeMentalEvents.Add(new ActiveMentalEvent
            {
                type = type,
                remainingTime = saved.remainingTime,
                cooldownRemaining = saved.cooldownRemaining
            });

            // 효과 재적용
            switch (type)
            {
                case MentalEventType.WorkSlowdown:
                    activeSpeedModifier = 0.5f;
                    break;
                case MentalEventType.RefuseWork:
                    isRefusingWork = true;
                    break;
            }

            // 쿨다운 복원
            if (saved.cooldownRemaining > 0f)
            {
                cooldownTimers[type] = saved.cooldownRemaining;
            }
        }
    }

    #endregion
}
