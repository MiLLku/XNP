using System;
using System.Collections;
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

    // ── 실패 백오프 ─────────────────────────────────────────────────────
    // 목적지에 갈 수 없을 때(경로 없음 등) 행동이 곧바로 Idle로 되돌아오는데,
    // OnStateChanged가 재평가 타이머를 0.1초로 당기므로 그대로 두면
    // "실패 → 즉시 재시도 → 실패"가 초당 10회 반복되며 A*를 낭비한다.
    // 실패가 이어질수록 재평가를 미뤄 이 루프를 끊는다.

    /// <summary>연속 실패 횟수 (성공적으로 무언가를 시작하면 0으로 초기화).</summary>
    private int consecutiveFailures;

    /// <summary>이 시각까지는 재평가를 미룬다.</summary>
    private float failureBackoffUntil;

    /// <summary>실패 1회당 늘어나는 대기 시간 (초).</summary>
    private const float FAILURE_BACKOFF_STEP = 2f;

    /// <summary>실패 백오프 상한 (초).</summary>
    private const float FAILURE_BACKOFF_MAX = 30f;

    [Header("디버그")]
    [SerializeField] private bool showDebugLogs = false;

    /// <summary>현재 수행 중인 스케줄 활동</summary>
    private ScheduleActivity currentExecutingActivity = ScheduleActivity.Anything;

    /// <summary>자유 시간 욕구 재확인 타이머</summary>
    private float needsCheckTimer;

    /// <summary>진행 중인 오락 회복 코루틴 (null = 오락 중 아님)</summary>
    private Coroutine recreationRoutine;

    // 컴포넌트 참조
    private Employee employee;
    private EmployeeMovement movement;
    private EmployeeSchedule schedule;
    private EmployeeDraft draft;
    private EmployeeZoneAssignment zoneAssignment;
    private EmployeeStatsController statsController;

    #endregion

    #region 초기화

    void Awake()
    {
        employee        = GetComponent<Employee>();
        movement        = GetComponent<EmployeeMovement>();
        schedule        = GetComponent<EmployeeSchedule>();
        draft           = GetComponent<EmployeeDraft>();
        zoneAssignment  = GetComponent<EmployeeZoneAssignment>();
        statsController = GetComponent<EmployeeStatsController>();
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
        // 무언가를 실제로 시작했다면 그동안의 실패는 없던 일로 한다.
        if (newState == EmployeeState.Working ||
            newState == EmployeeState.Resting ||
            newState == EmployeeState.Eating)
        {
            consecutiveFailures = 0;
            failureBackoffUntil = 0f;
            return;
        }

        if (newState == EmployeeState.Idle)
        {
            // Idle 진입 즉시 재평가 (짧은 지연으로 동일 프레임 재할당 충돌 회피).
            // 단, 직전에 실패했다면 백오프가 끝날 때까지 미룬다.
            float delay = Mathf.Max(0.1f, failureBackoffUntil - Time.time);

            workReevaluateTimer = delay;
            needsCheckTimer = delay;   // Anything 모드(자유 시간)에서도 동일 적용
        }
    }

    /// <summary>
    /// 이동/행동 실패 처리 — 백오프를 걸고 Idle로 되돌립니다.
    /// EmployeeAI가 거는 모든 MoveTo의 onFailed는 이 메서드를 써야 합니다.
    /// (직접 SetState(Idle)만 하면 재평가가 0.1초 뒤에 다시 돌아 실패 루프가 됩니다.)
    /// </summary>
    private void OnActionFailed()
    {
        consecutiveFailures++;
        failureBackoffUntil = Time.time +
            Mathf.Min(FAILURE_BACKOFF_STEP * consecutiveFailures, FAILURE_BACKOFF_MAX);

        if (showDebugLogs)
            Debug.Log($"[AI] {employee.DisplayName}: 행동 실패 {consecutiveFailures}회 연속 → " +
                      $"{failureBackoffUntil - Time.time:F1}초 후 재평가");

        employee.SetState(EmployeeState.Idle);
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
                return HasFacility(FacilityTag.Bed);

            case ScheduleActivity.Recreation:
            {
                // 재미와 정신력 둘 다 충분하면 오락 불필요
                FunConfig funCfg = EmployeeManager.instance?.FunConfig;
                float funTarget = funCfg != null ? funCfg.recreationTargetFun : 90f;
                bool funFull    = employee.Needs.fun >= funTarget;
                bool mentalFull = employee.Stats.mental >= employee.Stats.maxMental * MENTAL_FULL_RATIO;
                if (funFull && mentalFull) return false;

                // 오락거리: 시설 또는 약물(창고 복용) 중 하나라도 있으면 수행 가능
                return HasFacility(FacilityTag.Recreation) ||
                       IsDrugAvailable();
            }

            case ScheduleActivity.Wash:
                if (employee.ErosionLevel <= EROSION_LOW_THRESHOLD) return false;
                return HasFacility(FacilityTag.WashStation);

            case ScheduleActivity.Work:
                return true;

            case ScheduleActivity.Anything:
                return true;

            default:
                return true;
        }
    }

    /// <summary>
    /// 해당 시설이 맵에 하나라도 있는지 확인합니다.
    ///
    /// 배정 구역 안에 없어도 전체 탐색으로 폴백하므로(FindNearestFacility),
    /// 여기서 구역으로 걸러내면 직원이 아무것도 못 하고 멈춥니다.
    /// 구역 우선 선택은 실제 이동 시점(MoveToFacility)에서 처리합니다.
    /// </summary>
    private bool HasFacility(string tag)
    {
        var objs = FindWithTagCached(tag);
        return objs.Length > 0 && objs.Any(o => o != null);
    }

    #endregion

    #region 활동 실행

    private void ExecuteActivity(ScheduleActivity activity)
    {
        // 오락 진행 중 다른 활동으로 전환하면 오락을 중단
        if (activity != ScheduleActivity.Recreation)
            StopRecreation();

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

            int workZoneId = zoneAssignment != null ? zoneAssignment.AssignedZoneId : -1;

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

    /// <summary>
    /// 오락거리를 알아서 찾아 재미를 회복합니다.
    /// 선택: 우선순위(IFunSource.Priority / FunConfig.drugPriority) 높은 순 → 동률이면 거리순.
    ///   - 오락 시설: 이동 → 이용(초당 재미/정신력 회복) → 목표치 도달 또는 활동 전환 시 종료
    ///   - 약물: 창고로 이동 → 복용 → 즉시 회복 (추후 '정책' 시스템으로 개인 소지 확장 예정)
    /// </summary>
    private void ExecuteRecreation()
    {
        if (recreationRoutine != null) return; // 이미 오락 중

        // 1. 최선 시설 후보 (구역 필터 + 사용 가능 + 우선순위/거리 정렬)
        RecreationFacility bestFacility = SelectBestRecreationFacility();

        // 2. 약물 후보와 우선순위 비교
        FunConfig funCfg = EmployeeManager.instance?.FunConfig;
        int drugPriority = funCfg != null ? funCfg.drugPriority : -10;
        bool drugAvailable = IsDrugAvailable();

        if (bestFacility != null && (!drugAvailable || bestFacility.Priority >= drugPriority))
        {
            GoUseRecreationFacility(bestFacility);
        }
        else if (drugAvailable)
        {
            GoTakeDrug();
        }
        // 둘 다 없으면 아무것도 안 함 (CanExecuteActivity가 걸러 Anything으로 대체됨)
    }

    /// <summary>
    /// 사용 가능한 오락 시설 중 최선(우선순위 → 거리)을 반환합니다.
    /// 오락 구역이 할당돼 있으면 구역 내 시설만 후보로 삼습니다.
    /// </summary>
    private RecreationFacility SelectBestRecreationFacility()
    {
        var objs = FindWithTagCached(FacilityTag.Recreation);
        if (objs.Length == 0) return null;

        // 구역 필터 준비 (배정 구역이 있으면 그 안의 시설을 우선)
        Zone zone = zoneAssignment != null ? zoneAssignment.AssignedZone : null;

        RecreationFacility best = null;
        float bestDist = float.MaxValue;

        foreach (var obj in objs)
        {
            if (obj == null) continue;

            var facility = obj.GetComponent<RecreationFacility>();
            if (facility == null || !facility.CanUse(employee)) continue;

            // 구역 할당 시 구역 내 시설만
            if (zone != null)
            {
                var tile = new Vector2Int(
                    Mathf.FloorToInt(obj.transform.position.x),
                    Mathf.FloorToInt(obj.transform.position.y));
                if (!zone.ContainsTile(tile)) continue;
            }

            float dist = Vector2.Distance(transform.position, obj.transform.position);

            // 우선순위 높은 순 → 동률이면 가까운 순
            if (best == null ||
                facility.Priority > best.Priority ||
                (facility.Priority == best.Priority && dist < bestDist))
            {
                best = facility;
                bestDist = dist;
            }
        }

        return best;
    }

    /// <summary>시설로 이동해 오락 이용을 시작합니다.</summary>
    private void GoUseRecreationFacility(RecreationFacility facility)
    {
        if (movement == null || facility == null) return;

        CancelCurrentAction();
        employee.SetState(EmployeeState.Moving);

        // 구역 배정 시 구역 내 경로만 허용 (MoveToFacility와 동일 규칙)
        PathOptions pathOpts = zoneAssignment != null ? zoneAssignment.GetPathOptions() : null;

        Action onArrive = () =>
        {
            // 이동 중 활동이 바뀌었으면 이용하지 않음
            if (currentExecutingActivity != ScheduleActivity.Recreation &&
                currentExecutingActivity != ScheduleActivity.Anything)
            {
                employee.SetState(EmployeeState.Idle);
                return;
            }
            recreationRoutine = StartCoroutine(RecreationTick(facility));
        };
        Action onFailed = OnActionFailed;

        if (pathOpts != null)
            movement.MoveTo(facility.transform.position, pathOpts, onComplete: onArrive, onFailed: onFailed);
        else
            movement.MoveTo(facility.transform.position, onComplete: onArrive, onFailed: onFailed);
    }

    /// <summary>
    /// 시설 이용 루프 — 초당 재미/정신력을 회복하고,
    /// 목표치 도달·시설 사용 불가·상태 변화 시 종료합니다.
    /// </summary>
    private IEnumerator RecreationTick(RecreationFacility facility)
    {
        employee.SetState(EmployeeState.Resting);

        FunConfig funCfg = EmployeeManager.instance?.FunConfig;
        float target = funCfg != null ? funCfg.recreationTargetFun : 90f;

        if (showDebugLogs)
            Debug.Log($"[AI] {employee.DisplayName}: 오락 시작 ({facility.name}, 재미 {employee.Needs.fun:F0} → 목표 {target:F0})");

        while (employee != null &&
               employee.State == EmployeeState.Resting &&
               facility != null && facility.IsOperating &&
               employee.Needs.fun < target)
        {
            employee.ModifyFun(facility.FunPerSecond * Time.deltaTime);
            // 오락으로 오른 정신력은 영구적이지 않다 — 일정 시간 뒤 원래대로 돌아간다
            statsController?.ModifyMental(facility.MentalPerSecond * Time.deltaTime,
                MentalReason.RECREATION, "오락을 즐김");
            yield return null;
        }

        if (showDebugLogs && employee != null)
            Debug.Log($"[AI] {employee.DisplayName}: 오락 종료 (재미 {employee.Needs.fun:F0})");

        recreationRoutine = null;
        if (employee != null && employee.State == EmployeeState.Resting)
            employee.SetState(EmployeeState.Idle);
    }

    /// <summary>진행 중인 오락을 중단합니다 (활동 전환 등).</summary>
    private void StopRecreation()
    {
        if (recreationRoutine == null) return;

        StopCoroutine(recreationRoutine);
        recreationRoutine = null;

        if (employee != null && employee.State == EmployeeState.Resting)
            employee.SetState(EmployeeState.Idle);
    }

    /// <summary>약물 복용이 가능한 상태인지 (개인 소지분 또는 창고 재고).</summary>
    private bool IsDrugAvailable()
    {
        var work = employee.GetComponent<EmployeeWork>();
        if (work != null && work.HasDrug) return true;

        return InventoryManager.instance != null &&
               InventoryManager.instance.HasAnyDrug() &&
               StockpileManager.instance != null;
    }

    /// <summary>
    /// 창고로 이동해 약물 1개를 복용합니다 (즉시 재미 회복).
    /// 추후 '정책' 시스템 도입 시: 여기서 복용 대신 소지 슬롯에 넣는 분기가 추가될 예정
    /// (식량의 GoToStockpileForFood(eatAfterStocking) 패턴 참고).
    /// </summary>
    private void GoTakeDrug()
    {
        // 개인 소지분이 있으면 이동 없이 즉시 복용 ('정책' 소지 시스템)
        var heldWork = employee.GetComponent<EmployeeWork>();
        if (heldWork != null && heldWork.HasDrug)
        {
            int funValue = heldWork.ConsumeOneDrug();
            if (funValue > 0)
            {
                employee.ModifyFun(funValue);
                if (showDebugLogs)
                    Debug.Log($"[AI] {employee.DisplayName}: 소지 약물 복용 (재미 +{funValue} → {employee.Needs.fun:F0})");
            }
            return;
        }

        if (movement == null || StockpileManager.instance == null) return;

        Vector2Int footTile = new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y));

        Stockpile target = StockpileManager.instance.GetNearestStockpile(footTile);
        if (target == null) return;

        CancelCurrentAction();
        employee.SetState(EmployeeState.Moving);

        movement.MoveTo(target.GetDepositPosition(),
            onComplete: () =>
            {
                ItemData drug = InventoryManager.instance?.TakeAnyDrug(1);
                if (drug != null)
                {
                    employee.ModifyFun(drug.funValue);
                    if (showDebugLogs)
                        Debug.Log($"[AI] {employee.DisplayName}: 약물 복용 ({drug.itemName}, 재미 +{drug.funValue} → {employee.Needs.fun:F0})");
                }
                employee.SetState(EmployeeState.Idle);
            },
            onFailed: OnActionFailed);
    }

    // ─── Wash ───

    private void ExecuteWash()
    {
        CancelCurrentAction();
        MoveToFacility(ScheduleActivity.Wash, FacilityTag.WashStation, () =>
        {
            employee.ErosionController?.ClearErosion();
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
            HasFacility(FacilityTag.Bed))
        {
            ExecuteSleep();
            return;
        }

        // 3. 정신력
        if (employee.Stats.mental < employee.Stats.maxMental * FREE_MENTAL_RATIO &&
            employee.State != EmployeeState.Resting &&
            HasFacility(FacilityTag.Recreation))
        {
            ExecuteRecreation();
            return;
        }

        // 4. 침식
        if (employee.ErosionLevel > FREE_EROSION_THRESHOLD &&
            HasFacility(FacilityTag.WashStation))
        {
            ExecuteWash();
            return;
        }

        // 4.3 재미 — 낮으면 스스로 오락거리를 찾는다 (시설 또는 약물)
        FunConfig freeFunCfg = EmployeeManager.instance?.FunConfig;
        if (freeFunCfg != null &&
            employee.Needs.fun < freeFunCfg.freeTimeFunThreshold &&
            employee.State != EmployeeState.Resting &&
            recreationRoutine == null &&
            CanExecuteActivity(ScheduleActivity.Recreation))
        {
            ExecuteRecreation();
            return;
        }

        // 4.5 필수 소지 설정만큼 미리 챙겨두기 (유도) — 식량·약물
        if (TryStockUpFood()) return;
        if (TryStockUpDrug()) return;

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
        if (work == null) return false;
        // 필수 소지 설정(직원 관리창)만큼 채워져 있으면 스킵
        if (work.DesiredFoodCount <= 0 || work.HeldFoodCount >= work.DesiredFoodCount) return false;

        return GoToStockpileForFood(work, eatAfterStocking: false);
    }

    /// <summary>
    /// 약물 필수 소지 설정만큼 미리 챙겨두는 '유도' (식량과 동일 패턴).
    /// </summary>
    private bool TryStockUpDrug()
    {
        var work = employee.GetComponent<EmployeeWork>();
        if (work == null) return false;
        if (work.DesiredDrugCount <= 0 || work.HeldDrugCount >= work.DesiredDrugCount) return false;

        if (InventoryManager.instance == null || !InventoryManager.instance.HasAnyDrug()) return false;
        if (StockpileManager.instance == null || movement == null) return false;

        Vector2Int footTile = new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y));

        Stockpile target = StockpileManager.instance.GetNearestStockpile(footTile);
        if (target == null) return false;

        CancelCurrentAction();
        employee.SetState(EmployeeState.Moving);

        movement.MoveTo(target.GetDepositPosition(),
            onComplete: () =>
            {
                ItemData drug = InventoryManager.instance?.TakeAnyDrug(1);
                if (drug != null) work.StoreDrug(drug, 1);
                employee.SetState(EmployeeState.Idle);
            },
            onFailed: OnActionFailed);

        return true;
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
            onFailed: OnActionFailed);

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
        StopRecreation(); // 오락 이용 중이었다면 중단 (아니면 no-op)

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
            target   = zoneAssignment.FindNearestFacility(facilityTag, transform.position);
            pathOpts = zoneAssignment.GetPathOptions();
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
                onFailed:   OnActionFailed);
        }
        else
        {
            movement.MoveTo(target.transform.position,
                onComplete: onArrive,
                onFailed:   OnActionFailed);
        }
    }

    #endregion

    #region 공개 API

    public void SetAutonomousBehavior(bool enabled)
    {
        enableAutonomousBehavior = enabled;
    }

    /// <summary>
    /// 장비 장착/해제 지시 (직원 관리창에서 호출).
    /// 하던 행동을 중단하고 가장 가까운 장비 보관소로 이동해 처리합니다.
    /// </summary>
    /// <param name="slot">대상 슬롯</param>
    /// <param name="poolInstanceId">장착할 보관소 풀 인스턴스 ID (0 = 해제만)</param>
    public void RequestEquipChange(EquipmentSlot slot, int poolInstanceId)
    {
        var mgr = EquipmentStorageManager.instance;
        if (mgr == null || movement == null) return;
        if (employee.State == EmployeeState.Dead) return;

        var armory = mgr.GetNearestArmory(transform.position);
        if (armory == null)
        {
            Debug.LogWarning($"[AI] {employee.DisplayName}: 장비 보관소가 없어 장착 지시를 수행할 수 없습니다.");
            return;
        }

        CancelCurrentAction();
        employee.SetState(EmployeeState.Moving);

        movement.MoveTo(armory.transform.position,
            onComplete: () =>
            {
                var equipment = employee.GetComponent<EmployeeEquipment>();
                if (equipment == null) { employee.SetState(EmployeeState.Idle); return; }

                // 기존 장비 반납 (풀로)
                var old = equipment.UnequipToInstance(slot);
                if (old != null) mgr.ReturnInstance(old);

                // 새 장비 장착 (풀에서 꺼내기 — 그새 사라졌으면 취소)
                if (poolInstanceId > 0)
                {
                    var inst = mgr.TakeInstance(poolInstanceId);
                    if (inst != null && !equipment.EquipInstance(slot, inst))
                        mgr.ReturnInstance(inst); // 슬롯 불일치 등 실패 시 반납
                }

                employee.SetState(EmployeeState.Idle);
            },
            onFailed: OnActionFailed);
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
