using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 직원 작업 시스템 컴포넌트.
/// 작업 할당/실행/완료/취소, 우선순위 관리, 동적 비자격 리스트를 담당합니다.
///
/// 작업 흐름:
///   AssignWork → FindWorkablePosition → MoveTo → StartWork → PerformWork → CompleteWork
///
/// 비자격 시스템:
///   기본 능력(canXxx) + 우선순위(enabled) + 동적 비자격 리스트 + 정신이벤트(RefuseWork)
///   → 모두 통과해야 CanPerformWork() = true
/// </summary>
public class EmployeeWork : MonoBehaviour
{
    #region 상수

    /// <summary>직원 높이 (타일 단위)</summary>
    private const int EMPLOYEE_HEIGHT = 2;

    /// <summary>작업 우선순위 기본 최대값 (미할당)</summary>
    private const int DEFAULT_MAX_PRIORITY = 999;

    #endregion

    #region 필드

    [Header("작업 능력 (성장으로 변경 가능)")]
    [SerializeField] private WorkAbilities runtimeAbilities;

    [Header("작업 우선순위")]
    [SerializeField] private List<WorkPriority> workPriorities;

    [Header("디버그")]
    [SerializeField] private bool showDebugInfo = true;

    /// <summary>현재 작업 대상</summary>
    private IWorkTarget currentWorkTarget;

    /// <summary>현재 작업 주문</summary>
    private WorkOrder currentWorkOrder;

    /// <summary>현재 작업 타입</summary>
    private WorkType currentWork = WorkType.None;

    /// <summary>현재 연구 중인 작업대 (연구 작업 종료/취소 시 알림용)</summary>
    private ResearchWorkbench currentResearchBench;

    /// <summary>현재 작업 진행도 (0~1)</summary>
    private float workProgress = 0f;

    /// <summary>진행 중인 작업 실행의 취소원 (null이면 실행 중인 작업 없음)</summary>
    private CancellationTokenSource workCts;

    /// <summary>동적 비자격 리스트</summary>
    [SerializeField] private List<DisqualificationEntry> disqualifications = new List<DisqualificationEntry>();

    /// <summary>
    /// 개인 식량 소지 슬롯 (운반 작업의 임시 carryPile과 별개의 영구 슬롯).
    /// 직원은 식량을 한 종류씩 소지하다가 배고프면 꺼내 먹습니다.
    /// </summary>
    private ItemData _heldFood;
    private int _heldFoodCount;

    /// <summary>개인 소지 약물 슬롯 (식량과 동일 패턴, 한 종류만)</summary>
    private ItemData _heldDrug;
    private int _heldDrugCount;

    /// <summary>필수 소지 설정 — 자유시간에 이 개수까지 미리 챙겨둔다 (직원 관리창에서 조정)</summary>
    private int desiredFoodCount = 1;
    private int desiredDrugCount = 0;

    // 컴포넌트 참조
    private Employee employee;
    private EmployeeStatsController statsController;
    private EmployeeMovement movement;
    private EmployeeMental mental;
    private EmployeeGrowth growth;
    private EmployeeErosionController erosionController;

    #endregion

    #region 프로퍼티

    /// <summary>현재 수행 중인 작업 타입</summary>
    public WorkType CurrentWork => currentWork;

    /// <summary>현재 작업 진행도 (0~1)</summary>
    public float WorkProgress => workProgress;

    /// <summary>작업 할당 가능 여부 (Idle 상태일 때)</summary>
    public bool IsAvailableForWork => employee != null && employee.State == EmployeeState.Idle;

    /// <summary>현재 작업 주문</summary>
    public WorkOrder CurrentWorkOrder => currentWorkOrder;

    /// <summary>현재 작업 대상</summary>
    public IWorkTarget CurrentWorkTarget => currentWorkTarget;

    /// <summary>
    /// 현재 운반 작업(Hauling 또는 Withdraw)을 진행 중인지의 근사 표시.
    /// 정확한 carryPile 추적은 별도 작업으로 분리됩니다.
    /// 저장 시점에 이 값이 true면 자재 일부가 손실될 수 있음을 알리는 용도.
    /// </summary>
    public bool IsCarryingMaterials => currentWork == WorkType.Hauling && currentWorkTarget != null;

    /// <summary>작업 능력 (런타임 값 우선, 없으면 템플릿 복사)</summary>
    public WorkAbilities Abilities
    {
        get
        {
            if (runtimeAbilities == null && employee?.Data?.abilities != null)
            {
                runtimeAbilities = CopyAbilities(employee.Data.abilities);
            }
            return runtimeAbilities ?? new WorkAbilities();
        }
    }

    #endregion

    #region 초기화

    void Awake()
    {
        employee = GetComponent<Employee>();
        statsController = GetComponent<EmployeeStatsController>();
        movement = GetComponent<EmployeeMovement>();
        mental = GetComponent<EmployeeMental>();
        growth = GetComponent<EmployeeGrowth>();
        erosionController = GetComponent<EmployeeErosionController>();
    }

    /// <summary>
    /// 작업 시스템을 초기화합니다.
    /// </summary>
    public void Initialize(EmployeeData data)
    {
        runtimeAbilities = CopyAbilities(data?.abilities);
        InitializeWorkPriorities();
        disqualifications.Clear();

        // 초기 결격 작업 적용 (EmployeeData에 지정된 경우)
        if (data?.initialDisqualifications != null)
        {
            foreach (WorkType wt in data.initialDisqualifications)
            {
                disqualifications.Add(new DisqualificationEntry
                {
                    workType = wt,
                    reason   = "초기 결격"
                });
            }
        }
    }

    /// <summary>
    /// WorkAbilities 복사 (런타임 수정용)
    /// </summary>
private WorkAbilities CopyAbilities(WorkAbilities source)
    {
        if (source == null) return new WorkAbilities();

        return new WorkAbilities
        {
            canMine = source.canMine,
            canChop = source.canChop,
            canResearch = source.canResearch,
            canCraft = source.canCraft,
            canGarden = source.canGarden,
            canBuild = source.canBuild,
            canHaul = source.canHaul,
            canDemolish = source.canDemolish,
            miningSpeed = source.miningSpeed,
            choppingSpeed = source.choppingSpeed,
            researchSpeed = source.researchSpeed,
            craftingSpeed = source.craftingSpeed,
            gardeningSpeed = source.gardeningSpeed,
            buildingSpeed = source.buildingSpeed,
            haulingSpeed = source.haulingSpeed,
            demolishSpeed = source.demolishSpeed,
            baseCarryCapacity = source.baseCarryCapacity
        };
    }

    /// <summary>
    /// 작업 우선순위를 기본값으로 초기화합니다 (WorkTypeDefaults.BaseOrder가 단일 출처).
    /// </summary>
    private void InitializeWorkPriorities()
    {
        workPriorities = new List<WorkPriority>();
        foreach (WorkType type in WorkTypeDefaults.BaseOrder)
        {
            workPriorities.Add(new WorkPriority
            {
                workType = type,
                priority = WorkTypeDefaults.GetBasePriority(type),
                enabled  = true
            });
        }
    }

    #endregion

    #region 비자격 시스템

    /// <summary>
    /// 동적 비자격을 추가합니다 (특성/이벤트/부상 등).
    /// </summary>
    /// <param name="workType">비자격 작업 타입</param>
    /// <param name="reason">비자격 사유 (UI 표시용)</param>
    public void AddDisqualification(WorkType workType, string reason = "")
    {
        if (IsDisqualified(workType)) return;

        disqualifications.Add(new DisqualificationEntry { workType = workType, reason = reason });
        Debug.Log($"[Work] {employee?.DisplayName}: {workType} 비자격 추가 ({reason})");

        // 현재 해당 타입 작업 중이면 취소
        if (currentWork == workType)
        {
            CancelWork();
        }
    }

    /// <summary>
    /// 동적 비자격을 제거합니다.
    /// </summary>
    public void RemoveDisqualification(WorkType workType)
    {
        int removed = disqualifications.RemoveAll(d => d.workType == workType);
        if (removed > 0)
        {
            Debug.Log($"[Work] {employee?.DisplayName}: {workType} 비자격 해제");
        }
    }

    /// <summary>
    /// 특정 작업 타입이 비자격 상태인지 확인합니다.
    /// </summary>
    public bool IsDisqualified(WorkType workType)
    {
        return disqualifications.Any(d => d.workType == workType);
    }

    /// <summary>
    /// 현재 비자격 목록을 반환합니다.
    /// </summary>
    public IReadOnlyList<DisqualificationEntry> GetDisqualifications()
    {
        return disqualifications.AsReadOnly();
    }

    #endregion

    #region 작업 능력 판단

    /// <summary>
    /// 특정 작업을 수행할 수 있는지 확인합니다.
    /// 기본 능력 + 우선순위 활성 + 비자격 아님 + 정신이벤트 거부 아님
    /// </summary>
    public bool CanPerformWork(WorkType type)
    {
        // 1. 기본 능력 체크
        WorkAbilities abilities = Abilities;
        if (abilities == null || !abilities.CanPerformWork(type)) return false;

        // 2. 우선순위에서 비활성
        var priority = workPriorities?.FirstOrDefault(w => w.workType == type);
        if (priority != null && !priority.enabled) return false;

        // 3. 동적 비자격
        if (IsDisqualified(type)) return false;

        // 4. 정신 이벤트로 작업 거부 중
        if (mental != null && mental.IsRefusingWork) return false;

        return true;
    }

    /// <summary>
    /// 특정 작업 타입의 작업 속도를 반환합니다.
    /// 기본속도 × 특성보정 × 피로보정 × 글로벌보정 × 정신이상보정 × 침식보정 × 연구보정
    ///
    /// 재미는 여기에 관여하지 않는다 — 재미의 역할은 정신 이상 임계점 조절뿐이다.
    /// </summary>
    public float GetWorkSpeed(WorkType type)
    {
        if (!CanPerformWork(type)) return 0f;

        WorkAbilities abilities = Abilities;
        float baseSpeed = abilities.GetWorkSpeed(type);
        float traitModifier = GetTraitWorkSpeedModifier(type);
        float fatigueModifier = statsController != null ? statsController.GetFatigueModifier() : 1f;
        float globalModifier = statsController != null ? statsController.CachedWorkSpeedModifier : 1f;
        float mentalModifier = mental != null ? mental.GetActiveSpeedModifier() : 1f;
        float erosionModifier = erosionController != null ? erosionController.WorkSpeedModifier : 1f;
        float researchModifier = 1f + GetResearchWorkSpeedBonus(type);

        return baseSpeed * traitModifier * fatigueModifier * globalModifier * mentalModifier * erosionModifier * researchModifier;
    }

    /// <summary>
    /// 작업 종류에 해당하는 연구 속도 보너스(비율)를 반환합니다.
    /// 연구 노드의 ResearchStatBonusEffect가 여기서 실제 효과를 갖습니다.
    /// </summary>
    private float GetResearchWorkSpeedBonus(WorkType type)
    {
        var rt = ResearchTreeManager.instance;
        if (rt == null) return 0f;

        switch (type)
        {
            case WorkType.Mining:
                return rt.GetStatBonus(ResearchStatType.MiningSpeedBonus);
            case WorkType.Building:
                return rt.GetStatBonus(ResearchStatType.ConstructionSpeedBonus);
            case WorkType.Crafting:
                return rt.GetStatBonus(ResearchStatType.CraftingSpeedBonus);
            case WorkType.Chopping:
            case WorkType.Gardening:
                return rt.GetStatBonus(ResearchStatType.HarvestSpeedBonus);
            default:
                return 0f;
        }
    }

/// <summary>
    /// 현재 직원의 실제 운반 용량을 반환합니다.
    /// base × (1 + trait% / 100) + growth bonus, 최소 1 보장.
    /// </summary>
    /// <returns>운반 가능한 DroppedItem 최대 개수</returns>
    public int GetCarryCapacity()
    {
        WorkAbilities abilities = Abilities;
        int baseCap = Mathf.Max(1, abilities != null ? abilities.baseCarryCapacity : 5);

        // 특성 보정 (% 합산)
        float traitPercent = 0f;
        EmployeeData data = employee?.Data;
        if (data?.traits != null)
        {
            foreach (var trait in data.traits)
            {
                if (trait?.effects != null)
                    traitPercent += trait.effects.carryCapacityModifier;
            }
        }

        float withTrait = baseCap * (1f + traitPercent / 100f);
        int growthBonus = growth != null ? growth.CarryCapacityBonus : 0;
        int total = Mathf.RoundToInt(withTrait) + growthBonus;

        return Mathf.Max(1, total);
    }

    #endregion

    #region 식량 소지

    /// <summary>식량을 소지 중인지 여부.</summary>
    public bool HasFood => _heldFood != null && _heldFoodCount > 0;

    /// <summary>현재 소지 중인 식량 종류 (없으면 null).</summary>
    public ItemData HeldFood => _heldFood;

    /// <summary>현재 소지 중인 식량 개수.</summary>
    public int HeldFoodCount => _heldFoodCount;

    /// <summary>
    /// 식량을 소지 슬롯에 추가합니다 (한 종류만 누적). 음식이 아니거나 다른 종류면 실패.
    /// </summary>
    /// <summary>소지 약물 보유 여부</summary>
    public bool HasDrug => _heldDrug != null && _heldDrugCount > 0;

    /// <summary>소지 약물 종류</summary>
    public ItemData HeldDrug => _heldDrug;

    /// <summary>소지 약물 개수</summary>
    public int HeldDrugCount => _heldDrugCount;

    /// <summary>필수 소지 식량 개수 (직원 관리창에서 설정, 0~5)</summary>
    public int DesiredFoodCount
    {
        get => desiredFoodCount;
        set => desiredFoodCount = Mathf.Clamp(value, 0, 5);
    }

    /// <summary>필수 소지 약물 개수 (직원 관리창에서 설정, 0~5)</summary>
    public int DesiredDrugCount
    {
        get => desiredDrugCount;
        set => desiredDrugCount = Mathf.Clamp(value, 0, 5);
    }

    /// <summary>약물을 개인 슬롯에 보관합니다 (한 종류만).</summary>
    public bool StoreDrug(ItemData drug, int count)
    {
        if (drug == null || !drug.isDrug || count <= 0) return false;
        if (_heldDrug != null && _heldDrug != drug) return false;

        _heldDrug = drug;
        _heldDrugCount += count;
        return true;
    }

    /// <summary>소지 약물 1개를 소비하고 재미 회복량을 반환합니다 (없으면 0).</summary>
    public int ConsumeOneDrug()
    {
        if (!HasDrug) return 0;

        int funValue = _heldDrug.funValue;
        _heldDrugCount--;
        if (_heldDrugCount <= 0) _heldDrug = null;
        return funValue;
    }

    public bool StoreFood(ItemData food, int count)
    {
        if (food == null || !food.isFood || count <= 0) return false;
        if (_heldFood != null && _heldFood != food) return false; // 한 종류만 소지

        _heldFood = food;
        _heldFoodCount += count;
        return true;
    }

    /// <summary>
    /// 소지 식량 1개를 소비하고 영양값을 반환합니다. 소지분이 없으면 0.
    /// </summary>
    public int ConsumeOneFood()
    {
        if (!HasFood) return 0;

        int nutrition = _heldFood.nutrition;
        _heldFoodCount--;
        if (_heldFoodCount <= 0) _heldFood = null;
        return nutrition;
    }


    /// <summary>
    /// 특성에 의한 작업 타입별 속도 보정을 반환합니다.
    /// </summary>
    private float GetTraitWorkSpeedModifier(WorkType type)
    {
        float modifier = 1f;
        EmployeeData data = employee?.Data;
        if (data?.traits == null) return modifier;

        foreach (var trait in data.traits)
        {
            if (trait == null || trait.effects.workSpeedMultipliers == null) continue;

            var specific = trait.effects.workSpeedMultipliers.FirstOrDefault(m => m.workType == type);
            if (specific.workType == type)
            {
                modifier *= specific.multiplier; // 곱연산 배율
            }
        }

        return modifier;
    }

    #endregion

    #region 작업 할당 및 실행

    /// <summary>
    /// 직원의 현재 발 위치 타일 좌표를 반환합니다.
    /// </summary>
    public Vector3Int GetFootTile()
    {
        return new Vector3Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y),
            0
        );
    }

    /// <summary>
    /// WorkManager로부터 작업물과 구체적인 작업 대상을 할당받습니다.
    /// </summary>
    public void AssignWork(WorkOrder workOrder, IWorkTarget target)
    {
        if (target == null || workOrder == null ||
            employee.State == EmployeeState.Dead || employee.State == EmployeeState.MentalBreak)
            return;

        if (currentWorkTarget != null)
        {
            CancelWork();
        }

        // ── 운반 작업은 별도 2단계 비동기 흐름으로 처리 ─────────────────────
        if (target is WithdrawOrder withdrawOrder)
        {
            AssignWithdrawWork(workOrder, withdrawOrder);
            return;
        }

        if (target is HaulOrder haulOrder)
        {
            AssignHaulWork(workOrder, haulOrder);
            return;
        }
        // ────────────────────────────────────────────────────────────────────

        currentWorkOrder = workOrder;
        currentWorkTarget = target;

        if (showDebugInfo)
        {
            Debug.Log($"[Work] {employee.DisplayName}에게 작업 할당: {target.GetWorkType()} at {target.GetWorkPosition()}");
        }

        Vector3 targetPos = target.GetWorkPosition();
        Vector3Int targetTilePos = new Vector3Int(
            Mathf.FloorToInt(targetPos.x),
            Mathf.FloorToInt(targetPos.y),
            0
        );

        // 건설 등 다중 셀 건물은 footprint(size) 전체를 기준으로 작업 범위/위치를 판정한다.
        Vector2Int buildingSize = Vector2Int.one;
        if (target is BuildOrder buildOrderForSize && buildOrderForSize.buildingData != null)
            buildingSize = buildOrderForSize.buildingData.size;

        Vector3Int currentFootTile = GetFootTile();

        bool inRange = IsPositionInWorkRange(targetTilePos, buildingSize);

        // 사정거리 안이더라도 현재 위치가 이동 차단 타일(청사진·건물) 안이면 즉시 작업 불가.
        // 그대로 StartWork를 호출하면 건설 완료 시 건물 내부에 갇히므로 안전한 위치로 이동 후 작업.
        //
        // 추가: 직원의 발/몸통이 건설 대상 타일의 footprint와 겹쳐도 즉시 작업 불가.
        // 이유: 청사진은 blocksMovement=false 이므로 IsCurrentPositionBlocked()가 false를 반환하지만,
        //       건설 완료 시 해당 위치에 건물이 스폰되어 직원이 그 안에 파묻히는 버그가 발생합니다.
        if (inRange && !IsCurrentPositionBlocked() && !IsStandingInsideBuildingFootprint(targetTilePos, target))
        {
            StartWork(target);
        }
        else
        {
            if (movement != null)
            {
                Vector3 workPosition = FindWorkablePositionForTarget(targetTilePos, buildingSize);

                if (workPosition == Vector3.zero)
                {
                    Debug.Log($"[Work] {employee.DisplayName}: 작업 가능한 위치 없음 (target={targetTilePos}), 작업 취소 후 재대기");
                    CancelWork();
                    return;
                }

                employee.SetState(EmployeeState.Moving);

                bool isRetrying = false;

                Action<Vector2Int> onLandedHandler = null;
                onLandedHandler = (landedTile) =>
                {
                    movement.OnLanded -= onLandedHandler;

                    if (isRetrying || currentWorkTarget != target)
                        return;

                    isRetrying = true;

                    if (IsPositionInWorkRange(targetTilePos, buildingSize))
                    {
                        StartWork(target);
                    }
                    else
                    {
                        Vector3 newWorkPosition = FindWorkablePositionForTarget(targetTilePos, buildingSize);
                        if (newWorkPosition != Vector3.zero)
                        {
                            isRetrying = false;
                            movement.OnLanded += onLandedHandler;

                            movement.MoveTo(newWorkPosition,
                                onComplete: () =>
                                {
                                    movement.OnLanded -= onLandedHandler;
                                    if (IsPositionInWorkRange(targetTilePos, buildingSize))
                                        StartWork(target);
                                    else
                                    {
                                        Debug.LogWarning($"[Work] {employee.DisplayName}: 재이동 후에도 작업 범위 밖, 작업 취소");
                                        CancelWork();
                                    }
                                },
                                onFailed: () =>
                                {
                                    Debug.LogWarning($"[Work] {employee.DisplayName}: 재이동 실패, Idle 전환 " +
                                                     $"(target={targetTilePos}, retryPos={newWorkPosition})");
                                    employee.SetState(EmployeeState.Idle);
                                }
                            );
                        }
                        else
                        {
                            Debug.LogWarning($"[Work] {employee.DisplayName}: 착지 후 새 작업 위치 없음, 작업 취소");
                            CancelWork();
                        }
                    }
                };

                movement.OnLanded += onLandedHandler;

                movement.MoveTo(workPosition,
                    onComplete: () =>
                    {
                        movement.OnLanded -= onLandedHandler;

                        if (IsPositionInWorkRange(targetTilePos, buildingSize))
                            StartWork(target);
                        else
                        {
                            Debug.LogWarning($"[Work] {employee.DisplayName}: 도착 후 작업 범위 밖, 취소 " +
                                             $"(foot={GetFootTile()}, target={targetTilePos})");
                            CancelWork();
                        }
                    },
                    onFailed: () =>
                    {
                        Debug.LogWarning($"[Work] {employee.DisplayName}: 이동 실패 - 경로 없음 또는 차단됨 " +
                                         $"(start={GetFootTile()}, dest={workPosition}, target={targetTilePos})");

                        // fall(낙하)로 인한 일시 중단은 onLanded 핸들러가 자동으로 재이동 처리하므로
                        // 작업을 취소하면 안 됨 (취소하면 사다리·낙하 등이 모두 깨짐).
                        // 진짜 경로 없음(non-fall)인 경우만 CancelWork → 직원 프리징 방지.
                        if (movement.IsFalling)
                            return;

                        movement.OnLanded -= onLandedHandler;
                        CancelWork();
                    }
                );
            }
            else
            {
                Debug.LogWarning($"[Work] {employee.DisplayName}: movement 컴포넌트 없음, 작업 취소");
                CancelWork();
            }
        }
    }

    /// <summary>
    /// 제작 작업을 할당합니다 (생산 건물용).
    /// </summary>
    public void AssignCraftingWork(CraftingOrder craftingOrder, Vector3 workPosition)
    {
        if (craftingOrder == null) return;

        if (employee.State == EmployeeState.Dead || employee.State == EmployeeState.MentalBreak)
            return;

        if (currentWorkTarget != null)
        {
            CancelWork();
        }

        currentWorkOrder = craftingOrder;
        currentWorkTarget = craftingOrder;
        currentWork = WorkType.Crafting;

        if (movement != null)
        {
            employee.SetState(EmployeeState.Moving);

            movement.MoveTo(workPosition,
                onComplete: () =>
                {
                    if (currentWorkTarget == craftingOrder)
                        StartCraftingWork(craftingOrder);
                },
                onFailed: () =>
                {
                    CancelWork();
                }
            );
        }
        else
        {
            StartCraftingWork(craftingOrder);
        }
    }

    /// <summary>
    /// 운반 작업을 할당합니다.
    /// Phase 1: 아이템 위치로 이동 → 픽업
    /// Phase 2: 가장 가까운 창고로 이동 → 배달
    /// 창고가 없으면 인벤토리에 직접 추가.
    /// </summary>
    private void AssignHaulWork(WorkOrder workOrder, HaulOrder haulOrder)
    {
        if (!haulOrder.IsWorkAvailable())
        {
            employee.SetState(EmployeeState.Idle);
            return;
        }

        currentWorkOrder  = workOrder;
        currentWorkTarget = haulOrder;
        currentWork       = WorkType.Hauling;

        // 아이템 예약 (다른 직원이 중복 할당하지 않도록)
        haulOrder.item?.Claim();

        employee.SetState(EmployeeState.Moving);
        HaulWorkAsync(workOrder, haulOrder, RestartWorkTask()).Forget();
    }

    /// <summary>
    /// 2단계 운반 흐름.
    /// Phase 1: 아이템 위치로 이동 → 픽업 (아이템 파괴)
    /// Phase 2: 창고로 이동 → 배달
    /// </summary>
    private async UniTaskVoid HaulWorkAsync(WorkOrder order, HaulOrder haulOrder, CancellationToken ct)
    {
        const float MULTIPICK_RADIUS = 6f;

        DroppedItem item = haulOrder.item;

        // ── Phase 1: 첫 아이템 위치로 이동 ─────────────────────────────────────
        if (item == null || !item.isActiveAndEnabled)
        {
            if (showDebugInfo) Debug.Log($"[Work] {employee.DisplayName}: Haul 아이템이 이미 없음, 취소");
            CancelWork();
            return;
        }

        Vector3 itemWorldPos = item.transform.position;
        Vector3 pickupPos    = new Vector3(
            Mathf.FloorToInt(itemWorldPos.x) + 0.5f,
            Mathf.FloorToInt(itemWorldPos.y),
            0f
        );

        bool reachedItem = false;
        bool moveFailed  = false;

        movement.MoveTo(pickupPos,
            onComplete: () => reachedItem = true,
            onFailed:   () => moveFailed  = true
        );

        await UniTask.WaitUntil(() => reachedItem || moveFailed
                                        || item == null || !item.isActiveAndEnabled, GameLoop.Frame, ct);

        if (moveFailed || item == null || !item.isActiveAndEnabled)
        {
            if (showDebugInfo) Debug.Log($"[Work] {employee.DisplayName}: 아이템 위치 이동 실패 또는 아이템 소멸");
            haulOrder.item?.Unclaim();
            CancelWork();
            return;
        }

        // 첫 픽업: 캐리 더미에 추가
        var carryPile = new Dictionary<ItemData, int>();
        AddToCarryPile(carryPile, item.itemData, item.quantity);

        haulOrder.CompleteWork(employee);
        item.Remove();
        haulOrder.item = null;

        if (showDebugInfo) Debug.Log($"[Work] {employee.DisplayName}: 1차 픽업 → {SummarizePile(carryPile)}");

        // ── Phase 1.5: carryCapacity까지 인접 아이템 추가 픽업 ───────────────
        int capacity = GetCarryCapacity();
        int remainingSlots = capacity - 1;

        while (remainingSlots > 0 && DroppedItemManager.instance != null)
        {
            Vector2 fromPos = transform.position;
            var nearby = DroppedItemManager.instance.GetNearbyAvailableItems(
                fromPos, MULTIPICK_RADIUS, 1, null);

            if (nearby == null || nearby.Count == 0) break;

            DroppedItem next = nearby[0];
            if (next == null || !next.IsAvailable) break;

            next.Claim();

            Vector3 nextPickupPos = new Vector3(
                Mathf.FloorToInt(next.transform.position.x) + 0.5f,
                Mathf.FloorToInt(next.transform.position.y),
                0f
            );

            bool reachedNext = false;
            bool nextFailed  = false;
            employee.SetState(EmployeeState.Moving);
            movement.MoveTo(nextPickupPos,
                onComplete: () => reachedNext = true,
                onFailed:   () => nextFailed  = true
            );

            await UniTask.WaitUntil(() => reachedNext || nextFailed
                                            || next == null || !next.isActiveAndEnabled, GameLoop.Frame, ct);

            if (nextFailed || next == null || !next.isActiveAndEnabled)
            {
                next?.Unclaim();
                break;
            }

            AddToCarryPile(carryPile, next.itemData, next.quantity);
            next.Remove();
            remainingSlots--;

            if (showDebugInfo)
                Debug.Log($"[Work] {employee.DisplayName}: 추가 픽업, 남은 슬롯 {remainingSlots} → {SummarizePile(carryPile)}");
        }

        // ── Phase 2: 창고로 이동 → 배달 ─────────────────────────────────────
        Vector2Int footTile = new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y)
        );

        Stockpile stockpile = StockpileManager.instance?.GetNearestStockpile(footTile);

        if (stockpile != null)
        {
            Vector3 depositPos = stockpile.GetDepositPosition();

            bool reachedStock = false;
            moveFailed        = false;

            employee.SetState(EmployeeState.Moving);
            movement.MoveTo(depositPos,
                onComplete: () => reachedStock = true,
                onFailed:   () => moveFailed   = true
            );

            await UniTask.WaitUntil(() => reachedStock || moveFailed, GameLoop.Frame, ct);

            if (!moveFailed)
            {
                foreach (var kv in carryPile)
                    stockpile.Deposit(kv.Key, kv.Value);

                if (showDebugInfo)
                    Debug.Log($"[Work] {employee.DisplayName}: 창고 배달 완료 → {SummarizePile(carryPile)}");
            }
            else
            {
                // 창고 이동 실패 → 인벤토리 폴백
                foreach (var kv in carryPile)
                    InventoryManager.instance?.AddItem(kv.Key, kv.Value);
                if (showDebugInfo) Debug.Log($"[Work] {employee.DisplayName}: 창고 이동 실패 → 인벤토리 폴백");
            }
        }
        else
        {
            // 창고 없음 → 인벤토리에 직접 추가
            foreach (var kv in carryPile)
                InventoryManager.instance?.AddItem(kv.Key, kv.Value);
            if (showDebugInfo) Debug.Log($"[Work] {employee.DisplayName}: 창고 없음 → 인벤토리 직접 추가");
        }

        // 작업 완료 처리
        IWorkTarget completedTarget = currentWorkTarget;
        WorkOrder   completedOrder  = currentWorkOrder;

        currentWorkTarget    = null;
        currentWorkOrder     = null;
        currentWork          = WorkType.None;
        workProgress         = 0f;
        CancelWorkTask();

        if (WorkSystemManager.instance != null && completedTarget != null && completedOrder != null)
            WorkSystemManager.instance.OnWorkerCompletedTarget(employee, completedTarget, completedOrder);
        else
            employee.SetState(EmployeeState.Idle);
    }

    /// <summary>
    /// 출고 작업을 할당합니다.
    /// Phase 1: 가장 가까운 자재 보유 Stockpile로 이동 → 출고
    /// Phase 2: 사용처(IMaterialReceiver)로 이동 → 인계
    /// </summary>
    private void AssignWithdrawWork(WorkOrder workOrder, WithdrawOrder withdrawOrder)
    {
        if (!withdrawOrder.IsWorkAvailable())
        {
            employee.SetState(EmployeeState.Idle);
            return;
        }

        currentWorkOrder  = workOrder;
        currentWorkTarget = withdrawOrder;
        currentWork       = WorkType.Hauling;

        employee.SetState(EmployeeState.Moving);
        WithdrawWorkAsync(workOrder, withdrawOrder, RestartWorkTask()).Forget();
    }

    /// <summary>
    /// 2단계 출고 흐름.
    /// Phase 1: 자재 보유한 가장 가까운 Stockpile로 이동 → Withdraw
    /// Phase 2: receiver.GetDeliveryPosition()으로 이동 → OnMaterialDelivered 호출
    /// </summary>
    private async UniTaskVoid WithdrawWorkAsync(WorkOrder order, WithdrawOrder withdrawOrder, CancellationToken ct)
    {
        var request = withdrawOrder.request;
        if (request == null || request.itemData == null || request.amount <= 0)
        {
            CancelWork();
            return;
        }

        // ── Phase 1: 자재 보유 Stockpile 검색 → 이동 → 출고 ─────────────────
        // 도착하기 전에 다른 직원이 자재를 가져갔으면, 현재 위치 기준으로 다른 창고를
        // 재탐색해 실제로 이동한 뒤 다시 출고합니다 (창고별 개별 저장소 구조에서도 안전).
        const int MAX_SOURCE_ATTEMPTS = 3;

        Stockpile source = null;
        bool withdrawn   = false;
        bool moveFailed  = false;

        for (int attempt = 0; attempt < MAX_SOURCE_ATTEMPTS && !withdrawn; attempt++)
        {
            Vector2Int footTile = new Vector2Int(
                Mathf.FloorToInt(transform.position.x),
                Mathf.FloorToInt(transform.position.y)
            );

            source = StockpileManager.instance?.GetNearestStockpileWith(
                footTile, request.itemData, request.amount);

            if (source == null) break;

            Vector3 sourcePos = source.GetDepositPosition();

            bool reachedSource = false;
            moveFailed = false;
            movement.MoveTo(sourcePos,
                onComplete: () => reachedSource = true,
                onFailed:   () => moveFailed    = true
            );

            await UniTask.WaitUntil(() => reachedSource || moveFailed
                                             || !request.receiver.IsRequestStillValid(), GameLoop.Frame, ct);

            if (moveFailed || !request.receiver.IsRequestStillValid())
            {
                request.receiver?.OnMaterialRequestFailed(request.itemData, request.amount);
                CancelWork();
                return;
            }

            // 도착 후 출고 — 이동하는 사이 자재가 소진됐으면 다음 후보 창고로 재시도
            withdrawn = source.Withdraw(request.itemData, request.amount);

            if (!withdrawn && showDebugInfo)
                Debug.Log($"[Work] {employee.DisplayName}: {source.name} 출고 실패(도착 전 소진) — " +
                          $"다른 창고 재탐색 ({attempt + 1}/{MAX_SOURCE_ATTEMPTS})");
        }

        if (!withdrawn)
        {
            if (showDebugInfo)
                Debug.Log($"[Work] {employee.DisplayName}: 자재 {request.itemData.itemName}×{request.amount} 보유한 창고 없음");
            request.receiver?.OnMaterialRequestFailed(request.itemData, request.amount);
            CancelWork();
            return;
        }

        var carryPile = new Dictionary<ItemData, int>();
        AddToCarryPile(carryPile, request.itemData, request.amount);

        // ── Phase 1.5: 같은 자재의 다른 pending WithdrawOrder를 capacity까지 추가 픽업 ──
        // 한 번에 여러 사이트의 같은 자재 운반을 묶어 효율 ↑ (직원 carry capacity 활용)
        int capacity = GetCarryCapacity();
        int remainingCapacity = capacity - request.amount;
        var additionalRequests = new List<(WithdrawOrder wo, WorkTask task, WorkOrder workOrder)>();

        if (remainingCapacity > 0 && WorkSystemManager.instance != null)
        {
            foreach (var wo in WorkSystemManager.instance.AllOrders.ToList())
            {
                if (remainingCapacity <= 0) break;
                if (wo == order) continue;
                if (wo.workType != WorkType.Hauling) continue;
                if (!wo.isActive || wo.isPaused) continue;
                if (wo.taskQueue == null) continue;

                foreach (var t in wo.taskQueue.PendingTasks.ToList())
                {
                    if (remainingCapacity <= 0) break;
                    if (!(t.target is WithdrawOrder otherWO)) continue;
                    var otherReq = otherWO.request;
                    if (otherReq == null) continue;
                    if (otherReq.itemData != request.itemData) continue;
                    if (otherReq.amount > remainingCapacity) continue;
                    if (!otherWO.IsWorkAvailable()) continue;
                    if (!source.HasItem(otherReq.itemData, otherReq.amount)) continue;

                    // task를 직원에게 lock (다른 직원 픽업 방지) → 성공해야 출고
                    if (!wo.taskQueue.TryReserveForWorker(t, employee)) continue;

                    if (source.Withdraw(otherReq.itemData, otherReq.amount))
                    {
                        AddToCarryPile(carryPile, otherReq.itemData, otherReq.amount);
                        additionalRequests.Add((otherWO, t, wo));
                        remainingCapacity -= otherReq.amount;

                        if (showDebugInfo)
                            Debug.Log($"[Work] {employee.DisplayName}: 추가 픽업 {otherReq.itemData.itemName}×{otherReq.amount} " +
                                      $"(남은 capacity={remainingCapacity})");
                    }
                    else
                    {
                        // 출고 실패 → reserve 롤백 (task를 pending으로 되돌림)
                        t.Unassign();
                    }
                }
            }
        }

        if (showDebugInfo)
            Debug.Log($"[Work] {employee.DisplayName}: 출고 완료 → {SummarizePile(carryPile)} " +
                      $"(추가 사이트 {additionalRequests.Count}곳 포함)");

        // ── Phase 2: 첫 사용처로 이동 → 인계 ──────────────────────────────────
        if (!request.receiver.IsRequestStillValid())
        {
            // 운반 중 사용처가 무효화됨 → 자재 환불 (가장 가까운 창고에 반납)
            ReturnCarryToStorage(carryPile, "사용처 무효화");
            request.receiver?.OnMaterialRequestFailed(request.itemData, request.amount);
            CancelAdditionalReserves(additionalRequests, refundAll: false);
            FinishWithdrawWork();
            return;
        }

        Vector3 deliveryPos = request.receiver.GetDeliveryPosition();
        Vector3Int deliveryTile = new Vector3Int(
            Mathf.FloorToInt(deliveryPos.x), Mathf.FloorToInt(deliveryPos.y), 0);

        // 이미 work range 안이면 이동 생략하고 바로 인계
        bool inRange = IsPositionInWorkRange(deliveryTile);
        bool reachedDelivery = inRange;
        moveFailed = false;

        if (!inRange)
        {
            employee.SetState(EmployeeState.Moving);
            movement.MoveTo(deliveryPos,
                onComplete: () => reachedDelivery = true,
                onFailed:   () => moveFailed      = true
            );

            // 도착 OR work range 진입 OR 실패 OR receiver 무효화
            await UniTask.WaitUntil(() =>
                reachedDelivery || moveFailed
                || IsPositionInWorkRange(deliveryTile)
                || !request.receiver.IsRequestStillValid(), GameLoop.Frame, ct);

            // 이동 중 work range에 진입했으면 즉시 멈춤 (불필요한 이동/fall 방지)
            if (!reachedDelivery && IsPositionInWorkRange(deliveryTile))
            {
                movement.StopMoving();
                employee.SetState(EmployeeState.Idle);
            }
        }

        bool canDeliverNow = !moveFailed && request.receiver.IsRequestStillValid()
                             && (reachedDelivery || IsPositionInWorkRange(deliveryTile));
        if (!canDeliverNow)
        {
            ReturnCarryToStorage(carryPile, moveFailed ? "사용처 이동 실패" : "사용처 무효화");
            request.receiver?.OnMaterialRequestFailed(request.itemData, request.amount);
            CancelAdditionalReserves(additionalRequests, refundAll: false);
            FinishWithdrawWork();
            return;
        }

        // 첫 인계
        request.receiver.OnMaterialDelivered(request.itemData, request.amount);
        withdrawOrder.CompleteWork(employee);
        carryPile.Remove(request.itemData); // 같은 자재의 추가 분량은 그대로 유지될 수 있도록 카운트만 차감
        if (additionalRequests.Count > 0)
        {
            // 추가 분량 carryPile에 다시 등록 (자재 종류는 같음, 추가 총량)
            int leftover = 0;
            foreach (var (otherWO, _, _) in additionalRequests)
                if (otherWO.request != null) leftover += otherWO.request.amount;
            if (leftover > 0) AddToCarryPile(carryPile, request.itemData, leftover);
        }

        if (showDebugInfo)
            Debug.Log($"[Work] {employee.DisplayName}: 자재 인계 완료 → {request.itemData.itemName}×{request.amount}");

        // ── Phase 2.5: 추가 사용처들 순회 → 인계 ──────────────────────────────
        foreach (var (otherWO, otherTask, otherOrder) in additionalRequests)
        {
            var req = otherWO.request;
            if (req == null) { otherOrder.taskQueue.CancelTask(otherTask); continue; }

            if (!req.receiver.IsRequestStillValid())
            {
                ReturnSingleItem(req.itemData, req.amount, "추가 사용처 무효화");
                req.receiver?.OnMaterialRequestFailed(req.itemData, req.amount);
                otherOrder.taskQueue.CancelTask(otherTask);
                RemoveFromCarryPile(carryPile, req.itemData, req.amount);
                continue;
            }

            Vector3 nextPos = req.receiver.GetDeliveryPosition();
            Vector3Int nextTile = new Vector3Int(Mathf.FloorToInt(nextPos.x), Mathf.FloorToInt(nextPos.y), 0);

            bool nextInRange = IsPositionInWorkRange(nextTile);
            bool reached = nextInRange;
            bool failed = false;

            if (!nextInRange)
            {
                employee.SetState(EmployeeState.Moving);
                movement.MoveTo(nextPos,
                    onComplete: () => reached = true,
                    onFailed:   () => failed = true);

                await UniTask.WaitUntil(() =>
                    reached || failed
                    || IsPositionInWorkRange(nextTile)
                    || !req.receiver.IsRequestStillValid(), GameLoop.Frame, ct);

                if (!reached && IsPositionInWorkRange(nextTile))
                {
                    movement.StopMoving();
                    employee.SetState(EmployeeState.Idle);
                }
            }

            bool canDeliverNext = !failed && req.receiver.IsRequestStillValid()
                                  && (reached || IsPositionInWorkRange(nextTile));
            if (!canDeliverNext)
            {
                ReturnSingleItem(req.itemData, req.amount, failed ? "추가 사용처 이동 실패" : "추가 사용처 무효화");
                req.receiver?.OnMaterialRequestFailed(req.itemData, req.amount);
                otherOrder.taskQueue.CancelTask(otherTask);
                RemoveFromCarryPile(carryPile, req.itemData, req.amount);
                continue;
            }

            // 인계 + task 완료 (WorkSystemManager에 등록 안 됐으므로 직접 CompleteTask)
            req.receiver.OnMaterialDelivered(req.itemData, req.amount);
            otherOrder.taskQueue.CompleteTask(otherTask);
            RemoveFromCarryPile(carryPile, req.itemData, req.amount);

            if (showDebugInfo)
                Debug.Log($"[Work] {employee.DisplayName}: 추가 인계 완료 → {req.itemData.itemName}×{req.amount}");
        }

        FinishWithdrawWork();
    }

    /// <summary>운반 실패/취소 시 추가 reserve된 task들을 정리합니다.</summary>
    private void CancelAdditionalReserves(
        List<(WithdrawOrder wo, WorkTask task, WorkOrder workOrder)> additionals, bool refundAll)
    {
        foreach (var (otherWO, otherTask, otherOrder) in additionals)
        {
            var req = otherWO?.request;
            if (req != null)
            {
                if (refundAll)
                    ReturnSingleItem(req.itemData, req.amount, "묶음 운반 취소");
                req.receiver?.OnMaterialRequestFailed(req.itemData, req.amount);
            }
            otherOrder.taskQueue.CancelTask(otherTask);
        }
    }

    /// <summary>단일 아이템을 가까운 창고/인벤토리로 반납합니다.</summary>
    private void ReturnSingleItem(ItemData item, int amount, string reason)
    {
        if (item == null || amount <= 0) return;
        var tmp = new Dictionary<ItemData, int> { [item] = amount };
        ReturnCarryToStorage(tmp, reason);
    }

    /// <summary>carryPile에서 특정 아이템 일정량을 차감합니다.</summary>
    private static void RemoveFromCarryPile(Dictionary<ItemData, int> pile, ItemData data, int qty)
    {
        if (pile == null || data == null || qty <= 0) return;
        if (!pile.TryGetValue(data, out int cur)) return;
        cur -= qty;
        if (cur <= 0) pile.Remove(data);
        else          pile[data] = cur;
    }

    /// <summary>WithdrawWorkAsync 종료 시 공통 정리/통보.</summary>
    private void FinishWithdrawWork()
    {
        IWorkTarget completedTarget = currentWorkTarget;
        WorkOrder   completedOrder  = currentWorkOrder;

        currentWorkTarget    = null;
        currentWorkOrder     = null;
        currentWork          = WorkType.None;
        workProgress         = 0f;
        CancelWorkTask();

        if (WorkSystemManager.instance != null && completedTarget != null && completedOrder != null)
            WorkSystemManager.instance.OnWorkerCompletedTarget(employee, completedTarget, completedOrder);
        else
            employee.SetState(EmployeeState.Idle);
    }

    /// <summary>운반 중 실패 시 carryPile을 가장 가까운 창고/인벤토리로 반납합니다.</summary>
    private void ReturnCarryToStorage(Dictionary<ItemData, int> carryPile, string reason)
    {
        if (carryPile == null || carryPile.Count == 0) return;

        Vector2Int footTile = new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y)
        );

        Stockpile fallback = StockpileManager.instance?.GetNearestStockpile(footTile);
        foreach (var kv in carryPile)
        {
            bool ok = fallback != null && fallback.Deposit(kv.Key, kv.Value);
            if (!ok) InventoryManager.instance?.AddItem(kv.Key, kv.Value);
        }

        if (showDebugInfo)
            Debug.Log($"[Work] {employee.DisplayName}: 자재 반납({reason}) → {SummarizePile(carryPile)}");
    }

    /// <summary>같은 ItemData는 합산해서 캐리 더미에 추가합니다.</summary>
    private static void AddToCarryPile(Dictionary<ItemData, int> pile, ItemData data, int qty)
    {
        if (pile == null || data == null || qty <= 0) return;
        if (pile.TryGetValue(data, out int existing))
            pile[data] = existing + qty;
        else
            pile[data] = qty;
    }

    /// <summary>디버그 로그용 캐리 더미 요약 문자열.</summary>
    private static string SummarizePile(Dictionary<ItemData, int> pile)
    {
        if (pile == null || pile.Count == 0) return "(empty)";
        var sb = new System.Text.StringBuilder();
        bool first = true;
        foreach (var kv in pile)
        {
            if (!first) sb.Append(", ");
            sb.Append(kv.Key?.itemName ?? "?").Append(" × ").Append(kv.Value);
            first = false;
        }
        return sb.ToString();
    }

    /// <summary>
    /// 연구 작업을 할당합니다 (연구 작업대 전용).
    /// 직원이 workPosition으로 이동 후 연구 작업을 시작합니다.
    /// </summary>
    /// <param name="bench">연구할 작업대</param>
    /// <param name="workPos">이동할 작업 위치</param>
    public void AssignResearchWork(ResearchWorkbench bench, Vector3 workPos)
    {
        if (bench == null) return;
        if (employee.State == EmployeeState.Dead || employee.State == EmployeeState.MentalBreak) return;

        if (currentWorkTarget != null)
        {
            CancelWork();
        }
        // 연구 벤치는 IWorkTarget을 사용하지 않으므로 별도 필드에 저장
        currentResearchBench = bench;
        currentWork = WorkType.Research;

        if (movement != null)
        {
            employee.SetState(EmployeeState.Moving);

            movement.MoveTo(workPos,
                onComplete: () =>
                {
                    if (currentResearchBench == bench)
                        StartResearchWork(bench);
                },
                onFailed: () =>
                {
                    CancelWork();
                }
            );
        }
        else
        {
            StartResearchWork(bench);
        }
    }

    private void StartResearchWork(ResearchWorkbench bench)
    {
        if (bench == null || !bench.IsWorkAvailable())
        {
            // 도착했는데 벤치가 이미 중단됨
            currentResearchBench = null;
            currentWork = WorkType.None;
            employee.SetState(EmployeeState.Idle);
            return;
        }

        employee.SetState(EmployeeState.Working);
        currentWork = WorkType.Research;
        workProgress = 0f;

        ResearchWorkAsync(bench, RestartWorkTask()).Forget();
    }

    private async UniTaskVoid ResearchWorkAsync(ResearchWorkbench bench, CancellationToken ct)
    {
        float visualTimer = 0f;
        const float VISUAL_CYCLE = 10f;   // 진행 바 1사이클 시간 (초)
        float xpAccumulator = 0f;
        const float XP_INTERVAL = 5f;     // XP 지급 주기 (초)

        while (bench != null &&
               bench.IsWorkAvailable() &&
               employee.State == EmployeeState.Working)
        {
            float speed = GetWorkSpeed(WorkType.Research);
            if (speed <= 0f)
            {
                Debug.LogWarning($"[EmployeeWork] {employee.DisplayName}: 연구 속도 0 이하, 연구 중단");
                break;
            }

            // 연구 포인트 누적
            bench.OnResearchTick(speed, Time.deltaTime);

            // 경험치 주기적 지급
            xpAccumulator += Time.deltaTime;
            if (xpAccumulator >= XP_INTERVAL)
            {
                if (growth != null) growth.GainExperience(Mathf.CeilToInt(XP_INTERVAL * speed));
                xpAccumulator -= XP_INTERVAL;
            }

            // 시각적 진행도 (무한 반복 사이클)
            visualTimer += Time.deltaTime;
            workProgress = (visualTimer % VISUAL_CYCLE) / VISUAL_CYCLE;

            await UniTask.Yield(GameLoop.Frame, ct);
        }

        // 작업이 자연 종료될 때 벤치에 알림
        if (currentResearchBench != null)
        {
            currentResearchBench.OnWorkerLeft();
            currentResearchBench = null;
        }

        currentWork = WorkType.None;
        workProgress = 0f;
        CancelWorkTask();
        employee.SetState(EmployeeState.Idle);
    }

    private void StartWork(IWorkTarget target)
    {
        if (target == null || !target.IsWorkAvailable())
        {
            CompleteWork();
            return;
        }

        employee.SetState(EmployeeState.Working);
        currentWork = target.GetWorkType();

        float speed = GetWorkSpeed(currentWork);
        if (speed <= 0f)
        {
            Debug.LogWarning($"[Work] {employee.DisplayName}: 작업 속도 0 이하, 작업 취소");
            CancelWork();
            return;
        }

        // 진행도가 대상에 누적되는 작업(건설·철거)은 전용 루프를 탄다 —
        // 중단해도 진행이 남고 다른 직원이 이어받을 수 있다.
        if (target is IProgressiveWork progressive)
        {
            PerformProgressiveWorkAsync(target, progressive, RestartWorkTask()).Forget();
            return;
        }

        float workTime = target.GetWorkTime() / speed;
        PerformWorkAsync(target, workTime, RestartWorkTask()).Forget();
    }

    /// <summary>
    /// 작업량 누적형 작업 루프 (건설·철거).
    ///
    /// 매 프레임 직원의 현재 작업 속도만큼 작업량을 대상에 넣는다. 속도는 특성·피로·정신 이상·
    /// 침식·연구가 곱해진 값이라 유능한 직원일수록 같은 시간에 더 많이 진행시킨다.
    /// 중단 시 지금까지 넣은 작업량은 대상에 그대로 남는다.
    /// </summary>
    private async UniTaskVoid PerformProgressiveWorkAsync(IWorkTarget target, IProgressiveWork progressive, CancellationToken ct)
    {
        float total = Mathf.Max(0.01f, progressive.GetWorkAmount());
        workProgress = Mathf.Clamp01(progressive.GetAccumulatedWork() / total);

        while (progressive.GetAccumulatedWork() < total)
        {
            if (employee.State != EmployeeState.Working || !target.IsWorkAvailable())
            {
                Debug.Log($"[Work] {employee.DisplayName}: {target.GetWorkType()} 중단 — " +
                          $"진행도 {progressive.GetAccumulatedWork():F1}/{total:F1}는 대상에 보존됨");
                CancelWork();
                return;
            }

            float speed = GetWorkSpeed(currentWork);
            if (speed <= 0f)
            {
                Debug.LogWarning($"[Work] {employee.DisplayName}: 작업 속도 0 이하, 작업 취소");
                CancelWork();
                return;
            }

            progressive.AddWork(speed * Time.deltaTime);
            workProgress = Mathf.Clamp01(progressive.GetAccumulatedWork() / total);

            await UniTask.Yield(GameLoop.Frame, ct);
        }

        // 작업 적성 경험치 — 총 작업량에 비례
        growth?.GainWorkExperience(target.GetWorkType(), Mathf.Max(1, Mathf.RoundToInt(total)));

        CompleteWork();
    }

    private async UniTaskVoid PerformWorkAsync(IWorkTarget target, float workTime, CancellationToken ct)
    {
        workProgress = 0f;

        while (workProgress < 1f)
        {
            workProgress += Time.deltaTime / workTime;

            if (employee.State != EmployeeState.Working || !target.IsWorkAvailable())
            {
                // 작업이 중단된 사유 명확히 로깅
                string reason = (employee.State != EmployeeState.Working)
                    ? $"State 변경됨 (현재={employee.State})"
                    : "Target.IsWorkAvailable=false (이미 처리되었거나 무효화됨)";
                Debug.LogWarning($"[Work] {employee.DisplayName}: 작업 중단 → {reason} " +
                                 $"(progress={workProgress:F2}, target={target.GetWorkType()}@{target.GetWorkPosition()})");
                CancelWork();
                return;
            }

            await UniTask.Yield(GameLoop.Frame, ct);
        }

        // 작업 적성 경험치 — 해당 작업을 실제로 끝냈을 때만 지급된다(스킬 해금 조건).
        // 오래 걸리는 작업일수록 많이 주도록 소요 시간에 비례시킨다.
        growth?.GainWorkExperience(target.GetWorkType(), Mathf.Max(1, Mathf.RoundToInt(workTime)));

        // 주의: target.CompleteWork(부수효과)는 여기서 직접 호출하지 않는다.
        // CompleteWork() → WSM.OnWorkerCompletedTarget → order.CompleteTask → WorkTask.Complete가
        // 단일 호출 지점이다. (과거 직접 호출+체인 호출의 이중 실행으로 벌목 이중드롭·철거 자원
        // 이중반환이 발생했음 — IWorkTarget 구현체가 가드를 빼먹어도 안전하도록 호출을 단일화)
        CompleteWork();
    }

    private void StartCraftingWork(CraftingOrder craftingOrder)
    {
        if (craftingOrder == null || !craftingOrder.IsWorkAvailable())
        {
            CancelWork();
            return;
        }

        employee.SetState(EmployeeState.Working);
        currentWork = WorkType.Crafting;
        workProgress = 0f;

        craftingOrder.StartWorking();
        CraftingWorkAsync(craftingOrder, RestartWorkTask()).Forget();
    }

    private async UniTaskVoid CraftingWorkAsync(CraftingOrder craftingOrder, CancellationToken ct)
    {
        float workSpeed = GetWorkSpeed(WorkType.Crafting);
        if (workSpeed <= 0f)
        {
            CancelWork();
            return;
        }

        while (craftingOrder != null && craftingOrder.IsWorkAvailable())
        {
            float deltaTime = Time.deltaTime * workSpeed;
            craftingOrder.UpdateProgress(deltaTime);
            workProgress = craftingOrder.CraftingProgress;

            if (craftingOrder.CraftingProgress >= 1f)
            {
                int expGain = Mathf.CeilToInt(craftingOrder.GetWorkTime() * 2f);
                if (growth != null) growth.GainExperience(expGain);

                currentWorkTarget = null;
                currentWorkOrder = null;
                currentWork = WorkType.None;
                workProgress = 0f;
                CancelWorkTask();

                employee.SetState(EmployeeState.Idle);
                return;
            }

            await UniTask.Yield(GameLoop.Frame, ct);
        }

        CancelWork();
    }

    private void CompleteWork()
    {
        if (WorkSystemManager.instance != null && currentWorkTarget != null && currentWorkOrder != null)
        {
            IWorkTarget completedTarget = currentWorkTarget;
            WorkOrder completedOrder = currentWorkOrder;

            currentWorkTarget = null;
            currentWork = WorkType.None;
            workProgress = 0f;
            CancelWorkTask();

            WorkSystemManager.instance.OnWorkerCompletedTarget(employee, completedTarget, completedOrder);

            if (currentWorkTarget == null)
            {
                currentWorkOrder = null;
                employee.SetState(EmployeeState.Idle);
            }
        }
        else
        {
            currentWorkTarget = null;
            currentWorkOrder = null;
            currentWork = WorkType.None;
            workProgress = 0f;
            CancelWorkTask();
            employee.SetState(EmployeeState.Idle);
        }
    }

    private void OnDestroy()
    {
        CancelWorkTask();
    }

    /// <summary>
    /// 진행 중인 작업 실행을 끊고 새 취소 토큰을 발급합니다.
    /// 직원이 파괴되면 함께 취소되도록 파괴 토큰에 묶습니다.
    /// </summary>
    private CancellationToken RestartWorkTask()
    {
        CancelWorkTask();
        workCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        return workCts.Token;
    }

    /// <summary>진행 중인 작업 실행을 취소합니다.</summary>
    private void CancelWorkTask()
    {
        if (workCts == null) return;

        workCts.Cancel();
        workCts.Dispose();
        workCts = null;
    }

    /// <summary>
    /// 현재 작업을 취소합니다.
    /// </summary>
    public void CancelWork()
    {
        CancelWorkTask();

        // 연구 작업대는 IWorkTarget이 아니므로 별도로 알림
        if (currentResearchBench != null)
        {
            currentResearchBench.OnWorkerLeft();
            currentResearchBench = null;
        }

        if (currentWorkTarget != null)
        {
            currentWorkTarget.CancelWork(employee);

            if (WorkSystemManager.instance != null)
            {
                WorkSystemManager.instance.OnWorkerCancelledWork(employee);
            }

            currentWorkTarget = null;
            currentWorkOrder = null;
        }

        currentWork = WorkType.None;
        workProgress = 0f;

        if (movement != null)
        {
            movement.StopMoving();
        }

        employee.SetState(EmployeeState.Idle);
    }

    #endregion

    #region 작업 우선순위

    public void SetWorkPriority(WorkType type, int priority, bool enabled)
    {
        var work = workPriorities?.FirstOrDefault(w => w.workType == type);
        if (work != null)
        {
            work.priority = priority;
            work.enabled = enabled;
        }
    }

    public List<WorkType> GetEnabledWorkTypes()
    {
        if (workPriorities == null)
            InitializeWorkPriorities();

        return workPriorities
            .Where(w => w.enabled && CanPerformWork(w.workType))
            .OrderBy(w => w.priority)
            .Select(w => w.workType)
            .ToList();
    }

    public int GetWorkPriority(WorkType type)
    {
        var work = workPriorities?.FirstOrDefault(w => w.workType == type);
        return work != null ? work.priority : DEFAULT_MAX_PRIORITY;
    }

    #endregion

    #region 작업 범위 / 시야

    /// <summary>
    /// 특정 위치가 현재 직원의 작업 범위 내에 있는지 확인합니다.
    /// </summary>
    public bool IsPositionInWorkRange(Vector3Int targetPosition, Vector2Int buildingSize = default)
    {
        Vector3Int standingTile = GetFootTile();

        int sx = Mathf.Max(1, buildingSize.x);
        int sy = Mathf.Max(1, buildingSize.y);

        // 건물 footprint의 각 셀에 대해 인접(좌우 1칸, 위 2칸/아래 1칸) + 시야를 검사한다.
        // 큰 건물은 기준점 한 점이 아니라 footprint 전체를 기준으로 작업 범위를 판정한다.
        for (int i = 0; i < sx; i++)
        {
            for (int j = 0; j < sy; j++)
            {
                Vector3Int cell = new Vector3Int(targetPosition.x + i, targetPosition.y + j, 0);
                int dx = Mathf.Abs(cell.x - standingTile.x);
                int dy = cell.y - standingTile.y;
                if (dx <= 1 && dy >= -1 && dy <= 2 && HasLineOfSight(standingTile, cell))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 직원의 작업 가능 범위 타일 목록을 반환합니다.
    /// </summary>
    public List<Vector3Int> GetWorkableRange()
    {
        List<Vector3Int> workablePositions = new List<Vector3Int>();
        Vector3Int footPosition = GetFootTile();

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 2; dy++)
            {
                Vector3Int targetPos = footPosition + new Vector3Int(dx, dy, 0);

                if (targetPos.x >= 0 && targetPos.x < GameMap.MAP_WIDTH &&
                    targetPos.y >= 0 && targetPos.y < GameMap.MAP_HEIGHT)
                {
                    workablePositions.Add(targetPos);
                }
            }
        }

        return workablePositions;
    }

    private bool HasLineOfSight(Vector3Int from, Vector3Int to)
    {
        GameMap gameMap = MapGenerator.instance?.GameMapInstance;
        if (gameMap == null) return true;

        int footY = from.y;
        int bodyY2 = from.y + 2;

        if (from.x == to.x)
        {
            if (to.y > bodyY2)
            {
                for (int y = bodyY2 + 1; y < to.y; y++)
                {
                    if (IsSolidTile(gameMap, from.x, y)) return false;
                }
            }
            else if (to.y < footY)
            {
                if (IsSolidTile(gameMap, from.x, footY)) return false;

                for (int y = footY - 1; y > to.y; y--)
                {
                    if (IsSolidTile(gameMap, from.x, y)) return false;
                }
            }
        }
        else
        {
            int targetX = to.x;

            if (to.y >= footY && to.y <= bodyY2) return true;

            if (to.y > bodyY2)
            {
                for (int y = bodyY2 + 1; y < to.y; y++)
                {
                    if (IsSolidTile(gameMap, targetX, y)) return false;
                }
            }
            else if (to.y < footY)
            {
                for (int y = footY; y > to.y; y--)
                {
                    if (IsSolidTile(gameMap, targetX, y)) return false;
                }
            }
        }

        return true;
    }

    private bool IsSolidTile(GameMap gameMap, int x, int y)
    {
        if (x < 0 || x >= GameMap.MAP_WIDTH || y < 0 || y >= GameMap.MAP_HEIGHT)
            return false;
        return gameMap.TileGrid[x, y] != 0;
    }

    /// <summary>
    /// 현재 직원의 발·몸통 위치에 이동 차단 타일(청사진·완공 건물)이 있는지 확인합니다.
    /// 이 상태에서 그대로 작업을 시작하면 건설 완료 후 건물 내부에 갇힐 수 있으므로,
    /// AssignWork에서 즉시 StartWork를 건너뛰고 안전한 위치로 이동하도록 강제합니다.
    /// </summary>
    private bool IsCurrentPositionBlocked()
    {
        GameMap gameMap = MapGenerator.instance?.GameMapInstance;
        if (gameMap == null) return false;

        Vector3Int foot = GetFootTile();

        if (foot.x < 0 || foot.x >= GameMap.MAP_WIDTH ||
            foot.y < 0 || foot.y >= GameMap.MAP_HEIGHT)
            return false;

        // 발 타일이 이동 차단이면 직접 막힌 것 (청사진·벽 안)
        if (gameMap.DoesTileBlockMovement(foot.x, foot.y)) return true;

        // 몸통 타일(foot.y + 1)이 이동 차단이면 건물·청사진 안에 서 있는 것
        if (foot.y + 1 < GameMap.MAP_HEIGHT &&
            gameMap.DoesTileBlockMovement(foot.x, foot.y + 1)) return true;

        return false;
    }

    /// <summary>
    /// 직원의 발 또는 몸통이 건설 대상 건물의 footprint 안에 있는지 확인합니다.
    ///
    /// 청사진은 blocksMovement=false로 등록되므로 IsCurrentPositionBlocked()가 잡지 못합니다.
    /// 그러나 건설이 완료되면 해당 위치에 실제 건물이 스폰되기 때문에,
    /// 직원이 footprint 안에서 작업을 시작하면 완공 순간 건물 내부에 파묻히는 버그가 발생합니다.
    /// 이를 방지하기 위해 겹치는 경우 반드시 이동 후 작업하도록 강제합니다.
    /// </summary>
    private bool IsStandingInsideBuildingFootprint(Vector3Int targetTilePos, IWorkTarget target)
    {
        // BuildOrder에서 건물 크기 추출 (기본 1×1)
        Vector2Int size = Vector2Int.one;
        if (target is BuildOrder buildOrder && buildOrder.buildingData != null)
            size = buildOrder.buildingData.size;

        Vector3Int foot = GetFootTile();

        // X축 겹침: 발이 footprint X 범위 안에 있는지
        bool xInside = foot.x >= targetTilePos.x && foot.x < targetTilePos.x + size.x;
        if (!xInside) return false;

        // Y축 겹침: 발(foot.y) 또는 몸통(foot.y + 1) 중 하나라도 footprint Y 범위 안이면 겹침
        bool footYInside = foot.y >= targetTilePos.y && foot.y < targetTilePos.y + size.y;

        // 발이 footprint 안에 있을 때만 막는다(완공 시 직원이 건물 내부에 파묻히는 것을 방지).
        // 몸통만 겹치는 경우(건물 바로 아래/위에서 작업)는 허용한다 — 완공 시 발이 footprint 밖이라 안전.
        return footYInside;
    }

    #endregion

    #region 작업 위치 탐색

    private struct WorkPositionCandidate
    {
        public Vector2Int position;
        public float distance;
        public int heightDiff;
        public int fallSafety;
        public int verticalPriority;
    }

    /// <summary>
    /// 경로 질의용 길찾기 인스턴스.
    /// EmployeeMovement가 가진 것을 재사용하고, 없을 때만 만들어 캐시합니다.
    /// (예전에는 FindWorkablePositionForTarget 호출마다 new 했습니다.)
    /// </summary>
    private TilePathfinder _fallbackPathfinder;

    private TilePathfinder GetPathfinder(GameMap gameMap)
    {
        if (movement != null && movement.Pathfinder != null) return movement.Pathfinder;

        if (_fallbackPathfinder == null && gameMap != null)
            _fallbackPathfinder = new TilePathfinder(gameMap);

        return _fallbackPathfinder;
    }

    /// <summary>
    /// 작업 대상 근처에서 실제로 작업할 수 있는 위치를 찾습니다.
    /// </summary>
    public Vector3 FindWorkablePositionForTarget(Vector3Int targetTilePos, Vector2Int buildingSize = default)
    {
        Vector3Int currentFootTile = GetFootTile();
        Vector2Int startPos = new Vector2Int(currentFootTile.x, currentFootTile.y);

        GameMap gameMap = MapGenerator.instance != null
            ? MapGenerator.instance.GameMapInstance
            : null;
        TilePathfinder pathfinder = GetPathfinder(gameMap);

        if (pathfinder == null || gameMap == null)
        {
            Debug.LogWarning($"[Work] {employee?.DisplayName}: FindWorkablePosition 실패 - " +
                             $"pathfinder={pathfinder != null}, gameMap={gameMap != null}");
            return Vector3.zero;
        }

        // 디버그 카운터: 어느 단계에서 후보가 걸러지는지 추적
        int rejectedOutOfBounds = 0;
        int rejectedOutOfRange = 0;
        int rejectedFootBlocked = 0;
        int rejectedBodyBlocked = 0;
        int rejectedNoGround = 0;
        int rejectedSamePos = 0;
        int rejectedNoPath = 0;

        List<WorkPositionCandidate> candidates = new List<WorkPositionCandidate>();

        int sx = Mathf.Max(1, buildingSize.x);
        int sy = Mathf.Max(1, buildingSize.y);
        bool multiCell = sx > 1 || sy > 1;

        // 작업 위치 후보 생성
        List<Vector2Int> candidatePositions = new List<Vector2Int>();
        if (multiCell)
        {
            // 다중 셀 건물(건설 등): footprint 둘레 — 바로 아래 행(우선) → 좌/우 열 → 위 행
            for (int i = 0; i < sx; i++) candidatePositions.Add(new Vector2Int(targetTilePos.x + i, targetTilePos.y - 1));
            for (int j = 0; j < sy; j++)
            {
                candidatePositions.Add(new Vector2Int(targetTilePos.x - 1, targetTilePos.y + j));
                candidatePositions.Add(new Vector2Int(targetTilePos.x + sx, targetTilePos.y + j));
            }
            for (int i = 0; i < sx; i++) candidatePositions.Add(new Vector2Int(targetTilePos.x + i, targetTilePos.y + sy));
        }
        else
        {
            // 단일 셀(채광·벌목 등): 기존 방식 — 대상 주변 좌우 1칸 × 상하 범위
            int[] dyOrder = { 1, 2, 3, 0, -1, -2, -3 };
            int[] dxOrder = { 1, -1, 0 };
            foreach (int dy in dyOrder)
                foreach (int dx in dxOrder)
                    candidatePositions.Add(new Vector2Int(targetTilePos.x + dx, targetTilePos.y + dy));
        }

        foreach (var candidatePos in candidatePositions)
        {
            if (candidatePos.x < 0 || candidatePos.x >= GameMap.MAP_WIDTH ||
                candidatePos.y < 0 || candidatePos.y >= GameMap.MAP_HEIGHT)
            { rejectedOutOfBounds++; continue; }

            // 단일 셀만 작업 범위 제한(대상 좌우 1칸·상하 범위). 다중 셀은 둘레 후보라 인접 보장.
            if (!multiCell)
            {
                int workDx = Mathf.Abs(targetTilePos.x - candidatePos.x);
                int workDy = targetTilePos.y - candidatePos.y;
                if (workDx > 1 || workDy < -1 || workDy > 2)
                { rejectedOutOfRange++; continue; }
            }

            // 발 위치가 건설 대상 footprint 안이면 막힘 판정을 예외 처리한다
            // (청사진은 아직 미완성이므로 직원이 그 위/아래/안에 겹쳐 서서 작업할 수 있어야 함).
            bool footInFootprint = multiCell &&
                candidatePos.x >= targetTilePos.x && candidatePos.x < targetTilePos.x + sx &&
                candidatePos.y >= targetTilePos.y && candidatePos.y < targetTilePos.y + sy;
            if (!footInFootprint)
            {
                int footTileId = gameMap.TileGrid[candidatePos.x, candidatePos.y];
                if (footTileId != 0) { rejectedFootBlocked++; continue; }
                if (gameMap.DoesTileBlockMovement(candidatePos.x, candidatePos.y))
                { rejectedFootBlocked++; continue; }
            }

            // 몸통 위치(발+1)도 footprint 안이면 예외 — 건물 바로 아래에서 작업 시 몸통 겹침 허용.
            if (candidatePos.y + 1 < GameMap.MAP_HEIGHT)
            {
                int bodyX = candidatePos.x, bodyY = candidatePos.y + 1;
                bool bodyInFootprint = multiCell &&
                    bodyX >= targetTilePos.x && bodyX < targetTilePos.x + sx &&
                    bodyY >= targetTilePos.y && bodyY < targetTilePos.y + sy;
                if (!bodyInFootprint)
                {
                    int bodyTileId = gameMap.TileGrid[bodyX, bodyY];
                    if (bodyTileId != 0) { rejectedBodyBlocked++; continue; }
                    if (gameMap.DoesTileBlockMovement(bodyX, bodyY))
                    { rejectedBodyBlocked++; continue; }
                }
            }

            int groundY = candidatePos.y - 1;
            if (groundY < 0) { rejectedNoGround++; continue; }

            int groundTileId = gameMap.TileGrid[candidatePos.x, groundY];
            bool hasFloorTile = FloorTile.HasFloorTileAt(new Vector2Int(candidatePos.x, groundY));
            // IsFloorSupport는 완공된 바닥 건물만 true — 건설 예정지(blueprint)는 false.
            bool hasConstructedFloor = gameMap.IsFloorSupport(candidatePos.x, groundY);
            bool hasGround = groundTileId != 0 || hasFloorTile || hasConstructedFloor;

            if (!hasGround)
            {
                FloorTile ladder = FloorTile.GetFloorTileAt(new Vector2Int(candidatePos.x, groundY));
                FloorTile currentLadder = FloorTile.GetFloorTileAt(candidatePos);
                bool hasLadder = (ladder != null && ladder.AllowsVerticalMovement()) ||
                                 (currentLadder != null && currentLadder.AllowsVerticalMovement());
                if (!hasLadder) { rejectedNoGround++; continue; }
            }

            if (candidatePos == startPos) { rejectedSamePos++; continue; }

            // 경로 검증은 여기서 하지 않는다 — 정렬 후 상위 후보부터 확인한다(아래).
            float distance = Vector2Int.Distance(startPos, candidatePos);
            int heightDiff = Mathf.Abs(candidatePos.y - currentFootTile.y);

            bool willFallAfterMining = (candidatePos.x == targetTilePos.x) &&
                                       (candidatePos.y > targetTilePos.y) &&
                                       (candidatePos.y == targetTilePos.y + 1);
            int fallSafety = willFallAfterMining ? 1 : 0;
            int verticalPriority = (candidatePos.y > targetTilePos.y) ? 0 : 1;

            candidates.Add(new WorkPositionCandidate
            {
                position = candidatePos,
                distance = distance,
                heightDiff = heightDiff,
                fallSafety = fallSafety,
                verticalPriority = verticalPriority
            });
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning($"[Work] {employee?.DisplayName}: 작업 위치 후보 없음 (target={targetTilePos}, start={startPos}). " +
                             $"기각 사유: 범위밖={rejectedOutOfBounds}, 작업범위밖={rejectedOutOfRange}, " +
                             $"발막힘={rejectedFootBlocked}, 몸막힘={rejectedBodyBlocked}, " +
                             $"바닥없음={rejectedNoGround}, 동일위치={rejectedSamePos}");
            return Vector3.zero;
        }

        // 경로 없이 계산 가능한 기준으로 먼저 정렬한 뒤, 앞에서부터 경로를 확인하고
        // 첫 번째로 도달 가능한 후보를 채택한다.
        // (예전에는 모든 후보에 A*를 돌린 뒤 정렬했다 — 최대 21회 → 보통 1~2회)
        var sortedCandidates = candidates
            .OrderBy(c => c.fallSafety)
            .ThenBy(c => c.verticalPriority)
            .ThenBy(c => c.heightDiff)
            .ThenBy(c => c.distance)
            .ToList();

        var reachability = ReachabilityMap.Current;

        foreach (var candidate in sortedCandidates)
        {
            // O(1) 선판정으로 다른 섬에 있는 후보를 A* 없이 걸러낸다.
            if (reachability != null &&
                !reachability.IsReachable(startPos, candidate.position, PathOptions.Default))
            { rejectedNoPath++; continue; }

            // 실제 이동과 같은 정책으로 검증해야 한다 — 여기서 통과한 위치를
            // 이어서 movement.MoveTo가 거부하면 직원이 그 자리에서 멈춘다.
            List<Vector2Int> path = pathfinder.FindPath(startPos, candidate.position, PathOptions.Default);
            if (path == null || path.Count == 0) { rejectedNoPath++; continue; }

            if (showDebugInfo)
                Debug.Log($"[Work] {employee?.DisplayName}: 작업 위치 선정 완료. " +
                          $"후보={candidates.Count}개, 경로탐색={rejectedNoPath + 1}회, " +
                          $"선택={candidate.position} (target={targetTilePos})");

            return new Vector3(candidate.position.x + 0.5f, candidate.position.y, 0);
        }

        Debug.LogWarning($"[Work] {employee?.DisplayName}: 작업 위치 후보는 있으나 전부 도달 불가 " +
                         $"(target={targetTilePos}, start={startPos}, 후보={candidates.Count}개)");
        return Vector3.zero;
    }

    #endregion

    #region 저장/복원

    /// <summary>
    /// 저장 데이터에 작업 정보를 기록합니다.
    /// </summary>
    public void PopulateSaveData(EmployeeSaveData data)
    {
        data.abilities = WorkAbilitiesSaveData.FromWorkAbilities(runtimeAbilities);
        data.assignedWorkOrderId = currentWorkOrder != null ? currentWorkOrder.orderId : -1;

        // 작업 우선순위
        data.workPriorities = new List<WorkPrioritySaveData>();
        if (workPriorities != null)
        {
            foreach (var wp in workPriorities)
            {
                data.workPriorities.Add(new WorkPrioritySaveData
                {
                    workType = (int)wp.workType,
                    priority = wp.priority,
                    enabled = wp.enabled
                });
            }
        }

        // 비자격 목록
        data.disqualifiedWorkTypes = new List<int>();
        foreach (var dq in disqualifications)
        {
            data.disqualifiedWorkTypes.Add((int)dq.workType);
        }

        // 소지 식량
        data.heldFoodItemId = _heldFood != null ? _heldFood.itemID : 0;
        data.heldFoodCount  = _heldFoodCount;
        data.heldDrugItemId = _heldDrug != null ? _heldDrug.itemID : 0;
        data.heldDrugCount  = _heldDrugCount;
        data.desiredFoodCount = desiredFoodCount;
        data.desiredDrugCount = desiredDrugCount;
    }

    /// <summary>
    /// 저장 데이터에서 작업 정보를 복원합니다.
    /// </summary>
    public void RestoreFromSaveData(EmployeeSaveData data)
    {
        if (data.abilities != null)
        {
            runtimeAbilities = data.abilities.ToWorkAbilities();
        }

        if (data.workPriorities != null && data.workPriorities.Count > 0)
        {
            workPriorities = new List<WorkPriority>();
            foreach (var wp in data.workPriorities)
            {
                workPriorities.Add(new WorkPriority
                {
                    workType = (WorkType)wp.workType,
                    priority = wp.priority,
                    enabled = wp.enabled
                });
            }
        }
        else
        {
            // 이전 저장 데이터 호환: workPriorities 없으면 기본값으로 초기화
            InitializeWorkPriorities();
        }

        // 비자격 복원
        disqualifications.Clear();
        if (data.disqualifiedWorkTypes != null)
        {
            foreach (var wt in data.disqualifiedWorkTypes)
            {
                disqualifications.Add(new DisqualificationEntry
                {
                    workType = (WorkType)wt,
                    reason = "저장 데이터 복원"
                });
            }
        }

        // 소지 식량 복원
        _heldDrug = null;
        _heldDrugCount = 0;
        if (data.heldDrugItemId != 0 && data.heldDrugCount > 0)
        {
            var drug = GameDatabase.Instance?.GetItemData(data.heldDrugItemId);
            if (drug != null)
            {
                _heldDrug = drug;
                _heldDrugCount = data.heldDrugCount;
            }
        }
        desiredFoodCount = Mathf.Clamp(data.desiredFoodCount, 0, 5);
        desiredDrugCount = Mathf.Clamp(data.desiredDrugCount, 0, 5);

        _heldFood = null;
        _heldFoodCount = 0;
        if (data.heldFoodItemId != 0 && data.heldFoodCount > 0)
        {
            var food = GameDatabase.Instance?.GetItemData(data.heldFoodItemId);
            if (food != null)
            {
                _heldFood = food;
                _heldFoodCount = data.heldFoodCount;
            }
        }
    }

    #endregion
}

/// <summary>
/// 작업 비자격 항목.
/// </summary>
[System.Serializable]
public struct DisqualificationEntry
{
    /// <summary>비자격 작업 타입</summary>
    public WorkType workType;

    /// <summary>비자격 사유 (UI 표시용)</summary>
    public string reason;
}
