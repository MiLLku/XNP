using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 직원 행동 결정기.
///
/// 결정 우선순위:
///   0. Dead         → 아무것도 안 함
///   1. MentalBreak  → EmployeeMental이 처리
///   2. Drafted      → EmployeeDraft이 처리
///   3. 스케줄 활동  → 현재 시간대에 맞는 행동
///   4. 수행 불가 시 → Anything(자유 시간)으로 대체
///
/// 이벤트 기반 설계:
///   - DayCycle.OnHourChanged  → 스케줄 재평가 (시간 전환 시 1회)
///   - 자유 시간 욕구 감시     → needsCheckInterval마다 소주기 확인
///   - Update 폴링 제거        → CPU 부담 대폭 감소
/// </summary>
public class EmployeeAI : MonoBehaviour
{
    #region 상수

    private const float FREE_FATIGUE_THRESHOLD  = 40f;
    private const float FREE_MENTAL_RATIO        = 0.5f;
    private const float FREE_EROSION_THRESHOLD   = 30f;
    private const float FREE_HUNGER_THRESHOLD    = 50f;
    private const float FATIGUE_FULL_THRESHOLD   = 90f;
    private const float HUNGER_FULL_THRESHOLD    = 80f;
    private const float EROSION_LOW_THRESHOLD    = 5f;
    private const float MENTAL_FULL_RATIO        = 0.8f;

    /// <summary>자유 시간 중 욕구 재확인 간격 (초). 스케줄 체크와 분리.</summary>
    private const float NEEDS_CHECK_INTERVAL = 8f;

    #endregion

    #region 정적 캐시 (FacilityRegistry 도입 전 임시 유지)

    private static readonly Dictionary<string, (GameObject[] objs, float time)> _tagCache
        = new Dictionary<string, (GameObject[], float)>();
    private const float TAG_CACHE_DURATION = 5f;

    private static GameObject[] FindWithTagCached(string tag)
    {
        float now = Time.time;
        if (_tagCache.TryGetValue(tag, out var entry) && now - entry.time < TAG_CACHE_DURATION)
            return entry.objs;

        var result = GameObject.FindGameObjectsWithTag(tag);
        _tagCache[tag] = (result, now);
        return result;
    }

    /// <summary>특정 태그 캐시를 즉시 무효화합니다 (시설 생성/파괴 시 호출).</summary>
    public static void InvalidateTagCache(string tag) => _tagCache.Remove(tag);

    /// <summary>모든 태그 캐시를 초기화합니다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearStaticCache() => _tagCache.Clear();

    #endregion

    #region 필드

    [Header("AI 설정")]
    [SerializeField] private bool enableAutonomousBehavior = true;

    [Tooltip("스케줄이 Work일 때 자동 픽업 작업(채광/건설/벌목/운반/철거/원예)을 자동으로 가져올지 여부. " +
             "전용 할당 작업(연구/제작)은 항상 플레이어가 명시적으로 등록한 직원만 수행합니다. " +
             "기본 true: 직원이 우선순위에 맞춰 자유롭게 자동 픽업 작업을 수행합니다.")]
    [SerializeField] private bool autoAssignWork = true;

    /// <summary>Idle 상태 재평가 간격 (초). 작업 완료 후 새 작업 탐색 주기.</summary>
    private const float WORK_REEVALUATE_INTERVAL = 2f;
    private float workReevaluateTimer;

    [Header("디버그")]
    [SerializeField] private bool showDebugLogs = false;

    /// <summary>현재 수행 중인 스케줄 활동</summary>
    private ScheduleActivity currentExecutingActivity = ScheduleActivity.Anything;

    /// <summary>자유 시간 욕구 재확인 타이머</summary>
    private float needsCheckTimer;

    // 컴포넌트 참조
    private Employee employee;
    private EmployeeMovement movement;
    private EmployeeSchedule schedule;
    private EmployeeDraft draft;
    private EmployeeZoneAssignment zoneAssignment;

    #endregion

    #region 초기화

    void Awake()
    {
        employee       = GetComponent<Employee>();
        movement       = GetComponent<EmployeeMovement>();
        schedule       = GetComponent<EmployeeSchedule>();
        draft          = GetComponent<EmployeeDraft>();
        zoneAssignment = GetComponent<EmployeeZoneAssignment>();
    }

    void OnEnable()
    {
        if (DayCycle.instance != null)
            DayCycle.instance.OnHourChanged += OnHourChanged;

        if (employee != null)
            employee.OnStateChanged += OnEmployeeStateChanged;
    }

    void OnDisable()
    {
        if (DayCycle.instance != null)
            DayCycle.instance.OnHourChanged -= OnHourChanged;

        if (employee != null)
            employee.OnStateChanged -= OnEmployeeStateChanged;
    }

    void Start()
    {
        // DayCycle이 Start 이전에 존재하지 않을 수 있으므로 Start에서도 구독
        if (DayCycle.instance != null)
        {
            DayCycle.instance.OnHourChanged -= OnHourChanged; // 중복 방지
            DayCycle.instance.OnHourChanged += OnHourChanged;
        }

        // 초기 결정 (게임 시작 시 첫 행동 부여)
        needsCheckTimer = NEEDS_CHECK_INTERVAL;
        workReevaluateTimer = WORK_REEVALUATE_INTERVAL;

        MakeDecision();
    }

    void OnDestroy()
    {
        if (DayCycle.instance != null)
            DayCycle.instance.OnHourChanged -= OnHourChanged;

        if (employee != null)
            employee.OnStateChanged -= OnEmployeeStateChanged;
    }

    /// <summary>
    /// 직원 상태 변화 핸들러.
    /// Working → Idle 등 작업 종료 직후 빠르게 다음 작업을 탐색하기 위해
    /// 재평가 타이머를 짧게 리셋합니다 (Update에서 처리).
    /// </summary>
    private void OnEmployeeStateChanged(EmployeeState newState)
    {
        if (newState == EmployeeState.Idle)
        {
            // Idle 진입 즉시 재평가 (짧은 지연으로 동일 프레임 재할당 충돌 회피)
            workReevaluateTimer = 0.1f;
            needsCheckTimer = 0.1f;   // Anything 모드(자유 시간)에서도 즉시 재평가
        }
    }

    #endregion

    #region 업데이트 — 욕구 감시 전용

    void Update()
    {
        if (!enableAutonomousBehavior || employee == null) return;
        if (employee.State == EmployeeState.Dead)          return;
        if (employee.State == EmployeeState.MentalBreak)   return;
        if (draft != null && draft.IsDrafted)              return;

        // 자유 시간 중일 때만 욕구 소주기 재확인
        if (currentExecutingActivity == ScheduleActivity.Anything)
        {
            needsCheckTimer -= Time.deltaTime;
            if (needsCheckTimer <= 0f)
            {
                needsCheckTimer = NEEDS_CHECK_INTERVAL;
                if (!IsActivelyBusy())
                    ExecuteFreeTime();
            }
        }
        // Work 스케줄 중 Idle 상태이면 주기적으로 작업 재탐색
        // (작업 완료/취소 후 빠르게 다음 작업을 가져오기 위함)
        else if (currentExecutingActivity == ScheduleActivity.Work &&
                 employee.State == EmployeeState.Idle)
        {
            workReevaluateTimer -= Time.deltaTime;
            if (workReevaluateTimer <= 0f)
            {
                workReevaluateTimer = WORK_REEVALUATE_INTERVAL;
                ExecuteWork();
            }
        }
    }

    #endregion

    #region 시간 이벤트 핸들러

    /// <summary>
    /// DayCycle.OnHourChanged 이벤트 핸들러.
    /// 시간이 바뀔 때마다 스케줄을 재평가합니다.
    /// </summary>
    private void OnHourChanged(int newHour)
    {
        if (!enableAutonomousBehavior || employee == null) return;
        if (employee.State == EmployeeState.Dead)          return;
        if (employee.State == EmployeeState.MentalBreak)   return;
        if (draft != null && draft.IsDrafted)              return;

        MakeDecision();
    }

    #endregion

    #region 행동 결정

    private void MakeDecision()
    {
        // 스케줄 활동 결정
        ScheduleActivity scheduledActivity = schedule != null
            ? schedule.GetCurrentActivity()
            : ScheduleActivity.Anything;

        // 수행 가능 여부 확인 → 불가 시 Anything 대체
        ScheduleActivity actualActivity = CanExecuteActivity(scheduledActivity)
            ? scheduledActivity
            : ScheduleActivity.Anything;

        if (showDebugLogs && DayCycle.instance != null)
        {
            string sub = actualActivity != scheduledActivity ? $" → 대체={actualActivity}" : "";
            Debug.Log($"[AI] {employee.DisplayName}: {DayCycle.instance.CurrentHour}시 스케줄={scheduledActivity}{sub}");
        }

        // 동일 활동 중이고 실제로 진행 중이면 유지
        if (actualActivity == currentExecutingActivity && IsActivelyBusy())
            return;

        needsCheckTimer = NEEDS_CHECK_INTERVAL;
        ExecuteActivity(actualActivity);
    }

    private bool IsActivelyBusy()
    {
        return employee.State == EmployeeState.Moving  ||
               employee.State == EmployeeState.Working ||
               employee.State == EmployeeState.Eating  ||
               employee.State == EmployeeState.Resting;
    }

    /// <summary>
    /// 스케줄 활동을 수행할 수 있는지 확인합니다.
    /// 구역이 할당된 경우 구역 내 시설만 확인합니다.
    /// </summary>
    private bool CanExecuteActivity(ScheduleActivity activity)
    {
        switch (activity)
        {
            case ScheduleActivity.Sleep:
                if (employee.Needs.fatigue >= FATIGUE_FULL_THRESHOLD) return false;
                return HasFacilityForActivity(FacilityTag.Bed, ZoneType.Sleep);

            case ScheduleActivity.Recreation:
                if (employee.Stats.mental >= employee.Stats.maxMental * MENTAL_FULL_RATIO) return false;
                return HasFacilityForActivity(FacilityTag.Recreation, ZoneType.Recreation);

            case ScheduleActivity.Wash:
                if (employee.ErosionLevel <= EROSION_LOW_THRESHOLD) return false;
                return HasFacilityForActivity(FacilityTag.WashStation, ZoneType.Wash);

            case ScheduleActivity.Work:
                return true;

            case ScheduleActivity.Anything:
                return true;

            default:
                return true;
        }
    }

    /// <summary>
    /// 구역이 할당된 경우 구역 내 시설만, 미할당이면 전체 탐색합니다.
    /// </summary>
    private bool HasFacilityForActivity(string tag, ZoneType zoneType)
    {
        int assignedZoneId = zoneAssignment != null
            ? zoneAssignment.GetAssignedZoneId(zoneType)
            : -1;

        if (assignedZoneId >= 0 && ZoneManager.instance != null)
        {
            Zone zone = ZoneManager.instance.GetZone(assignedZoneId);
            if (zone == null) return false;

            var facilities = FindWithTagCached(tag);
            return facilities.Any(f => f != null && zone.ContainsTile(
                new Vector2Int(
                    Mathf.FloorToInt(f.transform.position.x),
                    Mathf.FloorToInt(f.transform.position.y))));
        }

        // 구역 미할당: 전체 탐색
        var objs = FindWithTagCached(tag);
        return objs.Length > 0 && objs.Any(o => o != null);
    }

    #endregion

    #region 활동 실행

    private void ExecuteActivity(ScheduleActivity activity)
    {
        currentExecutingActivity = activity;

        switch (activity)
        {
            case ScheduleActivity.Work:       ExecuteWork();       break;
            case ScheduleActivity.Sleep:      ExecuteSleep();      break;
            case ScheduleActivity.Recreation: ExecuteRecreation(); break;
            case ScheduleActivity.Wash:       ExecuteWash();       break;
            case ScheduleActivity.Anything:   ExecuteFreeTime();   break;
        }
    }

    // ─── Work ───

    private void ExecuteWork()
    {
        if (employee.State == EmployeeState.Working ||
            employee.State == EmployeeState.Moving)
        {
            if (showDebugLogs)
                Debug.Log($"[AI] {employee.DisplayName}: ExecuteWork 스킵 (state={employee.State})");
            return;
        }

        if (employee.State == EmployeeState.Idle)
        {
            // ★ 자동 할당이 비활성화된 경우 플레이어 수동 할당만 허용
            if (!autoAssignWork)
            {
                if (showDebugLogs)
                    Debug.Log($"[AI] {employee.DisplayName}: 자동 작업 할당 비활성화 (autoAssignWork=false). " +
                              $"수동 할당 대기 중.");
                return;
            }

            int workZoneId = zoneAssignment != null
                ? zoneAssignment.GetAssignedZoneId(ZoneType.Work)
                : -1;

            if (showDebugLogs)
                Debug.Log($"[AI] {employee.DisplayName}: 자동 작업 할당 시도 (workZoneId={workZoneId})");

            bool assigned = WorkSystemManager.instance?.TryAssignWorkToEmployee(employee, workZoneId) ?? false;

            if (showDebugLogs)
                Debug.Log($"[AI] {employee.DisplayName}: 자동 작업 할당 결과={assigned}");
        }
    }

    // ─── Sleep ───

    private void ExecuteSleep()
    {
        if (employee.State == EmployeeState.Resting) return;

        CancelCurrentAction();
        MoveToFacility(ScheduleActivity.Sleep, FacilityTag.Bed, () =>
            employee.SetState(EmployeeState.Resting));
    }

    // ─── Recreation ───

    private void ExecuteRecreation()
    {
        if (employee.State == EmployeeState.Resting) return;

        CancelCurrentAction();
        MoveToFacility(ScheduleActivity.Recreation, FacilityTag.Recreation, () =>
            employee.SetState(EmployeeState.Resting));
    }

    // ─── Wash ───

    private void ExecuteWash()
    {
        CancelCurrentAction();
        MoveToFacility(ScheduleActivity.Wash, FacilityTag.WashStation, () =>
        {
            employee.SetErosion(0f);
            employee.SetState(EmployeeState.Idle);
        });
    }

    // ─── Free Time ───

    private void ExecuteFreeTime()
    {
        // 1. 배고픔 (최우선) — 소지 식량으로 먹거나, 없으면 창고에서 챙겨 먹는다.
        if (employee.Needs.hunger < FREE_HUNGER_THRESHOLD &&
            employee.State != EmployeeState.Eating)
        {
            if (HandleHunger()) return;
        }

        // 2. 피로
        if (employee.Needs.fatigue < FREE_FATIGUE_THRESHOLD &&
            employee.State != EmployeeState.Resting &&
            HasFacilityForActivity(FacilityTag.Bed, ZoneType.Sleep))
        {
            ExecuteSleep();
            return;
        }

        // 3. 정신력
        if (employee.Stats.mental < employee.Stats.maxMental * FREE_MENTAL_RATIO &&
            employee.State != EmployeeState.Resting &&
            HasFacilityForActivity(FacilityTag.Recreation, ZoneType.Recreation))
        {
            ExecuteRecreation();
            return;
        }

        // 4. 침식
        if (employee.ErosionLevel > FREE_EROSION_THRESHOLD &&
            HasFacilityForActivity(FacilityTag.WashStation, ZoneType.Wash))
        {
            ExecuteWash();
            return;
        }

        // 4.5 식량 미소지 시 미리 챙겨두기 (유도) — 작업 전 식량 1개 확보
        if (TryStockUpFood()) return;

        // 5. 작업
        ExecuteWork();
    }

    #endregion

    #region 배고픔

    /// <summary>
    /// 배고픔 처리. 소지 식량이 있으면 그 자리에서 먹고,
    /// 없으면 가장 가까운 창고로 이동해 음식 1개를 챙긴 뒤 먹습니다.
    /// </summary>
    /// <returns>식사 행동을 시작했으면 true (다른 자유시간 행동을 막음).</returns>
    private bool HandleHunger()
    {
        var work = employee.GetComponent<EmployeeWork>();
        if (work == null) return false;

        // 1. 소지 식량이 있으면 즉시 먹기 (이동 불필요)
        if (work.HasFood)
        {
            EatHeldFood(work);
            return true;
        }

        // 2. 소지분이 없으면 창고로 이동해 챙긴 뒤 먹는다.
        return GoToStockpileForFood(work, eatAfterStocking: true);
    }

    /// <summary>
    /// 식량 미소지 시 자유시간에 미리 1개 챙겨두는 '유도'. 작업 전에 호출합니다.
    /// 평소에 식량을 확보해 두면 작업 중 배고파질 때 창고 왕복 없이 즉시 먹을 수 있습니다.
    /// </summary>
    /// <returns>챙기러 가는 행동을 시작했으면 true.</returns>
    private bool TryStockUpFood()
    {
        var work = employee.GetComponent<EmployeeWork>();
        if (work == null || work.HasFood) return false; // 이미 소지 중이면 스킵

        return GoToStockpileForFood(work, eatAfterStocking: false);
    }

    /// <summary>
    /// 가장 가까운 창고로 이동해 음식 1개를 소지 슬롯에 챙깁니다.
    /// 현재는 전역 단일 저장소 구조라 '음식이 인벤토리에 있으면 가장 가까운 아무 창고'로 갑니다.
    /// (창고별 개별 저장소로 확장하면 '음식 보유 창고 찾기'로 교체)
    /// </summary>
    /// <param name="work">대상 직원의 작업 컴포넌트</param>
    /// <param name="eatAfterStocking">true면 챙긴 직후 바로 섭취(배고픔 처리), false면 소지만(유도)</param>
    /// <returns>이동 행동을 시작했으면 true.</returns>
    private bool GoToStockpileForFood(EmployeeWork work, bool eatAfterStocking)
    {
        if (InventoryManager.instance == null || !InventoryManager.instance.HasAnyFood())
            return false; // 먹을 음식이 없음 (창고/식량 확보 필요)

        if (StockpileManager.instance == null) return false;

        Vector2Int footTile = new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y));

        Stockpile target = StockpileManager.instance.GetNearestStockpile(footTile);
        if (target == null) return false; // 창고 없음

        CancelCurrentAction();
        employee.SetState(EmployeeState.Moving);

        movement.MoveTo(target.GetDepositPosition(),
            onComplete: () =>
            {
                // 도착 후 창고(전역 저장소)에서 음식 1개를 꺼내 소지
                ItemData food = InventoryManager.instance.TakeAnyFood(1);
                if (food != null && work.StoreFood(food, 1))
                {
                    if (eatAfterStocking) EatHeldFood(work);
                    else                  employee.SetState(EmployeeState.Idle);
                }
                else
                {
                    employee.SetState(EmployeeState.Idle);
                }
            },
            onFailed: () => employee.SetState(EmployeeState.Idle));

        return true;
    }

    /// <summary>소지 식량 1개를 소비해 배고픔을 회복합니다.</summary>
    private void EatHeldFood(EmployeeWork work)
    {
        int nutrition = work.ConsumeOneFood();
        if (nutrition <= 0)
        {
            employee.SetState(EmployeeState.Idle);
            return;
        }

        employee.Eat(nutrition);
        employee.SetState(EmployeeState.Idle);

        if (showDebugLogs)
            Debug.Log($"[AI] {employee.DisplayName}: 식사 (회복 +{nutrition}, 배고픔 {employee.Needs.hunger:F0}%, 남은 식량 {work.HeldFoodCount})");
    }

    #endregion

    #region 유틸리티

    private void CancelCurrentAction()
    {
        if (employee.State == EmployeeState.Working)
            employee.CancelWork();

        if (employee.State == EmployeeState.Moving && movement != null)
            movement.StopMoving();
    }

    /// <summary>
    /// 스케줄 활동에 맞는 시설로 이동 후 콜백을 실행합니다.
    /// 구역이 할당됐으면 구역 내 시설 우선 탐색 + 구역 내 경로만 허용.
    /// 구역 미할당이면 전체 맵에서 가장 가까운 시설 탐색.
    /// </summary>
    private void MoveToFacility(ScheduleActivity activity, string facilityTag, Action onArrive)
    {
        GameObject target  = null;
        PathOptions pathOpts = null;

        if (zoneAssignment != null)
        {
            target = zoneAssignment.FindNearestFacility(activity, facilityTag, transform.position);

            int zoneId = zoneAssignment.GetAssignedZoneId(
                EmployeeZoneAssignment.GetZoneTypeForActivity(activity));
            if (zoneId >= 0)
                pathOpts = PathOptions.ForZone(zoneId);
        }

        if (target == null)
        {
            var objects = FindWithTagCached(facilityTag);
            target = objects
                .Where(o => o != null)
                .OrderBy(o => Vector2.Distance(transform.position, o.transform.position))
                .FirstOrDefault();
        }

        if (target == null || movement == null) return;

        if (pathOpts != null)
        {
            movement.MoveTo(target.transform.position, pathOpts,
                onComplete: onArrive,
                onFailed:   () => employee.SetState(EmployeeState.Idle));
        }
        else
        {
            movement.MoveTo(target.transform.position,
                onComplete: onArrive,
                onFailed:   () => employee.SetState(EmployeeState.Idle));
        }
    }

    #endregion

    #region 공개 API

    public void SetAutonomousBehavior(bool enabled)
    {
        enableAutonomousBehavior = enabled;
    }

    /// <summary>외부(스케줄 변경 등)에서 즉시 재결정을 요청합니다.</summary>
    public void ForceReevaluate()
    {
        if (employee == null || employee.State == EmployeeState.Dead) return;
        MakeDecision();
    }

    public ScheduleActivity CurrentExecutingActivity => currentExecutingActivity;

    #endregion

    #region 디버그

    [ContextMenu("Print AI Status")]
    public void PrintAIStatus()
    {
        if (employee == null) { Debug.Log("[AI] Employee 없음"); return; }

        Debug.Log($"=== {employee.DisplayName} AI 상태 ===");
        Debug.Log($"소집: {(draft?.IsDrafted == true ? "소집중" : "해제")}");
        Debug.Log($"스케줄: {schedule?.GetCurrentActivity()} → 실행중: {currentExecutingActivity}");
        Debug.Log($"배고픔: {employee.Needs.hunger:F0}%  피로: {employee.Needs.fatigue:F0}%");
        Debug.Log($"정신력: {employee.Stats.mental:F0}/{employee.Stats.maxMental}  침식: {employee.ErosionLevel:F0}");
        Debug.Log($"상태: {employee.State}");
    }

    #endregion
}
