using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 생산 건물 추상 클래스
/// 제재소, 대장간, 연금술 작업대 등 모든 생산 건물의 기본 클래스
///
/// 제작 흐름:
///   1. 플레이어가 건물 클릭 → ProductionUI 표시
///   2. 레시피 선택 → OnProductionOrderCreated → 자원 예약 (ReserveMaterials)
///   3. 직원 할당 → StartProduction → CraftingOrder 생성
///   4. 작업 완료 → OnCraftingComplete → 예약 소모 + 결과물 지급
///   5. 작업 취소 → CancelProduction → 예약 해제 (자원 손실 없음)
/// </summary>
[RequireComponent(typeof(Collider2D))]
public abstract class ProductionBuilding : MonoBehaviour, IBuildingFunction, IMaterialReceiver, IBuildingExtraSerializable
{
    #region 필드 및 설정

    [Header("제작 설정")]
    [Tooltip("이 건물에서 만들 수 있는 레시피 목록")]
    [SerializeField] protected List<CraftingRecipe> availableRecipes;

    [Header("작업 위치")]
    [Tooltip("직원이 작업할 위치 (빈 값이면 건물 중앙)")]
    [SerializeField] protected Transform workPosition;

    [Header("시각 효과 (선택사항)")]
    [SerializeField] protected GameObject workingEffectPrefab;
    [SerializeField] protected Vector3 effectOffset = new Vector3(0, 0.5f, 0);

    [Header("오디오 (선택사항)")]
    [SerializeField] protected SoundData workingSound;

    /// <summary>현재 진행 중인 제작 주문</summary>
    protected CraftingOrder _currentOrder;

    /// <summary>직원 할당 대기 중인 레시피</summary>
    protected CraftingRecipe _pendingRecipe;

    /// <summary>직원 할당 대기 중인 제작 수량</summary>
    protected int _pendingAmount;

    /// <summary>대기 중 레시피의 자원 예약 ID (-1이면 예약 없음)</summary>
    protected int _pendingReservationId = -1;

    /// <summary>작업 시각 효과 인스턴스</summary>
    protected GameObject _workingEffectInstance;

    /// <summary>Building 컴포넌트 캐시</summary>
    protected Building _building;

    /// <summary>이 생산 시설의 건물 (부속 판정용)</summary>
    public Building HostBuilding => _building;

    /// <summary>전력 소비 컴포넌트 캐시 (없으면 전력 제약 없음)</summary>
    protected PowerConsumer _powerConsumer;

    #endregion

    #region 프로퍼티

    /// <summary>현재 작업 중인지 여부 (IBuildingFunction)</summary>
    public bool IsOperating => _currentOrder != null && !_currentOrder.IsCompleted();

    /// <summary>이 건물에서 제작 가능한 레시피 목록</summary>
    public List<CraftingRecipe> AvailableRecipes => availableRecipes;

    /// <summary>현재 진행 중인 제작 주문</summary>
    public CraftingOrder CurrentOrder => _currentOrder;

    /// <summary>직원이 작업할 월드 좌표</summary>
    public Vector3 WorkPosition => workPosition != null ? workPosition.position : transform.position;

    /// <summary>직원 배정 대기 중인 주문이 있는지 여부</summary>
    public bool HasPendingOrder => _pendingRecipe != null;

    /// <summary>진행 중인 주문이 있는지 여부</summary>
    public bool HasActiveOrder => _currentOrder != null;

    /// <summary>
    /// 전력 소비 건물인데 현재 정전 상태인지 여부.
    /// PowerConsumer가 없으면(전력 미사용 건물) 항상 false를 반환합니다.
    /// CraftingOrder가 작업 진행 중 정전 시 진행도 누적을 멈추는 데 사용합니다.
    /// </summary>
    public bool IsUnpowered()
    {
        return _powerConsumer != null && !_powerConsumer.IsPowered;
    }

    #endregion

    #region 초기화 및 정리

    protected virtual void Awake()
    {
        _building = GetComponent<Building>();
        _powerConsumer = GetComponent<PowerConsumer>();

        if (workPosition == null)
        {
            CreateDefaultWorkPosition();
        }
    }

    /// <summary>
    /// 기본 작업 위치 생성 (건물 중앙).
    /// workPosition이 지정되지 않았을 때 자동으로 호출됩니다.
    /// </summary>
    private void CreateDefaultWorkPosition()
    {
        GameObject workPosObj = new GameObject("WorkPosition_Auto");
        workPosObj.transform.SetParent(transform);

        if (_building != null && _building.buildingData != null)
        {
            Vector2Int size = _building.buildingData.size;
            workPosObj.transform.localPosition = new Vector3(size.x * 0.5f, 0, 0);
        }
        else
        {
            workPosObj.transform.localPosition = Vector3.zero;
        }

        workPosition = workPosObj.transform;
        Debug.Log($"[{GetBuildingName()}] 자동 작업 위치 생성: {workPosition.position}");
    }

    protected virtual void Start()
    {
        ValidateRecipes();
    }

    protected virtual void OnDestroy()
    {
        if (_currentOrder != null || _pendingRecipe != null)
        {
            CancelProduction();
        }

        if (_workingEffectInstance != null)
        {
            Destroy(_workingEffectInstance);
        }
    }

    /// <summary>
    /// 레시피 목록 유효성 검사 (null 레시피 제거)
    /// </summary>
    private void ValidateRecipes()
    {
        if (availableRecipes == null || availableRecipes.Count == 0)
        {
            Debug.LogWarning($"[{GetBuildingName()}] {name}에 사용 가능한 레시피가 없습니다!");
        }

        availableRecipes?.RemoveAll(r => r == null);
    }

    #endregion

    #region 클릭 및 UI 연동

    private void OnMouseDown()
    {
        // UI 위 클릭은 월드로 흘리지 않는다 (OnMouseDown은 EventSystem을 거치지 않는다)
        if (UIManager.PointerOverUI) return;

        if (_building != null && !_building.IsFunctional)
        {
            Debug.LogWarning($"[{GetBuildingName()}] 건물이 비활성화 상태입니다. 기반을 복구하세요.");
            ShowDisabledMessage();
            return;
        }

        // 대기 중이거나 진행 중이면 직원 할당 패널, 아니면 생산 UI
        if (_pendingRecipe != null || _currentOrder != null)
        {
            OpenWorkAssignmentPanel();
        }
        else
        {
            OpenProductionUI();
        }
    }

    /// <summary>
    /// 생산 UI를 엽니다 (레시피 선택).
    /// </summary>
    protected virtual void OpenProductionUI()
    {
        if (availableRecipes == null || availableRecipes.Count == 0)
        {
            Debug.LogWarning($"[{GetBuildingName()}] 제작 가능한 레시피가 없습니다.");
            return;
        }

        var productionUI = UIManager.instance.GetPanel<ProductionUI>(UIPanelType.ProductionUI);
        if (productionUI != null)
        {
            productionUI.Setup(availableRecipes, OnProductionOrderCreated, this);
            UIManager.instance.ShowPanel(UIPanelType.ProductionUI);
        }
        else
        {
            Debug.LogError($"[{GetBuildingName()}] ProductionUI를 찾을 수 없습니다!");
        }
    }

    /// <summary>
    /// 직원 할당 패널을 엽니다.
    /// 대기 중인 주문이 있으면 해당 주문을, 없으면 진행 중 주문을 사용합니다.
    /// </summary>
    protected virtual void OpenWorkAssignmentPanel()
    {
        CraftingRecipe targetRecipe = _pendingRecipe ?? _currentOrder?.recipe;
        int targetAmount = _pendingRecipe != null ? _pendingAmount : (_currentOrder?.craftAmount ?? 1);

        if (targetRecipe == null)
        {
            Debug.LogWarning($"[{GetBuildingName()}] 할당할 Order가 없습니다.");
            return;
        }

        WorkOrder tempOrder = new WorkOrder
        {
            orderName = $"{targetRecipe.outputItem.itemName} 제작",
            workType = WorkType.Crafting,
            maxAssignedWorkers = 1
        };

        var assignmentPanel = UIManager.instance.GetPanel<WorkAssignmentPanel>(UIPanelType.WorkAssignment);
        if (assignmentPanel != null)
        {
            assignmentPanel.Setup(
                tempOrder,
                OnWorkerAssigned,
                OnWorkerAssignmentClosed,
                OnWorkerAssignmentCancelled
            );

            UIManager.instance.ShowPanel(UIPanelType.WorkAssignment);
        }
        else
        {
            Debug.LogError($"[{GetBuildingName()}] WorkAssignmentPanel을 찾을 수 없습니다!");
        }
    }

    protected virtual void ShowDisabledMessage()
    {
        Debug.Log($"[{GetBuildingName()}] 이 건물은 현재 사용할 수 없습니다. 기반을 복구하세요.");
        // TODO [UI]: 비활성화 상태 UI 메시지 표시
    }

    #endregion

    #region 생산 흐름 (주문 생성 → 직원 할당 → 시작)

    /// <summary>
    /// ProductionUI에서 레시피와 수량이 확정되었을 때 호출됩니다.
    /// 자원을 예약하고 직원 배정을 대기합니다.
    /// </summary>
    /// <param name="recipe">선택된 레시피</param>
    /// <param name="amount">제작 수량</param>
    /// <param name="worker">할당된 직원 (null이면 추후 배정)</param>
    protected virtual void OnProductionOrderCreated(CraftingRecipe recipe, int amount, Employee worker)
    {
        if (recipe == null)
        {
            Debug.LogError($"[{GetBuildingName()}] 레시피가 null입니다.");
            return;
        }

        // 장비는 보관소에 자리가 있어야 만든다 (재료를 쓰기 전에 막는다)
        if (!EquipmentStorageManager.HasRoomForCraft(recipe.outputItem, amount * recipe.outputAmount))
        {
            Debug.LogWarning($"[{GetBuildingName()}] 장비 보관소에 자리가 없어 {recipe.outputItem?.itemName} 제작을 시작하지 못했습니다.");
            return;
        }

        // 재료 예약 (즉시 소모하지 않고 예약만 걸어둠)
        int reservationId = ReserveMaterials(recipe, amount);
        if (reservationId < 0)
        {
            Debug.LogError($"[{GetBuildingName()}] 재료 예약 실패");
            return;
        }

        Debug.Log($"[{GetBuildingName()}] 재료 예약 완료 (#{reservationId}). {recipe.outputItem.itemName} x{amount} Order 생성.");

        // Pending 상태로 저장 (직원 할당 대기)
        _pendingRecipe = recipe;
        _pendingAmount = amount;
        _pendingReservationId = reservationId;

        // 재료가 있으면 먼저 직원이 창고·바닥 더미에서 가져온다 — 다 오면 제작 시작
        if (BeginMaterialHaul(worker)) return;

        // 직원이 이미 선택되어 넘어온 경우 바로 시작
        if (worker != null)
        {
            StartProduction(worker);
        }
    }

    #region 재료 운반 (주문 → 운반 → 제작)

    private readonly Dictionary<ItemData, int> _haulPending = new Dictionary<ItemData, int>();
    private readonly Dictionary<ItemData, int> _haulDelivered = new Dictionary<ItemData, int>();
    private WorkOrder _haulOrder;
    private Employee _haulWorker;

    /// <summary>재료가 아직 다 오지 않은 주문이 있는지</summary>
    public bool AwaitingMaterials => _pendingRecipe != null && _haulPending.Count > 0;

    /// <summary>운반 중이면 배정 직원만 기억하고 true — 재료가 다 오면 그 직원이 시작한다.</summary>
    protected bool DeferWhileHauling(Employee worker)
    {
        if (!AwaitingMaterials) return false;
        if (worker != null) _haulWorker = worker;
        return true;
    }

    private bool BeginMaterialHaul(Employee worker)
    {
        _haulPending.Clear();
        _haulDelivered.Clear();
        foreach (var cost in _pendingRecipe.requiredMaterials)
            if (cost.item != null && cost.amount > 0)
                _haulPending[cost.item] = (_haulPending.TryGetValue(cost.item, out int n) ? n : 0) + cost.amount * _pendingAmount;
        if (_haulPending.Count == 0) return false;

        _haulWorker = worker;
        CreateHaulOrder();
        return true;
    }

    private void CreateHaulOrder()
    {
        if (WorkSystemManager.instance == null) return;
        _haulOrder = WorkSystemManager.instance.CreateWorkOrder($"생산 재료 운반: {_pendingRecipe.outputItem.itemName}", WorkType.Hauling, maxWorkers: 3, priority: 3);
        foreach (var kv in _haulPending)
            _haulOrder.AddTarget(new WithdrawOrder(new MaterialRequest(kv.Key, kv.Value, this, _pendingReservationId)));
    }

    /// <summary>운반 작업·대기 재료를 정리하고, 이미 도착한 재료는 건물 앞에 떨군다.</summary>
    private void AbortMaterialHaul()
    {
        if (_haulOrder != null && WorkSystemManager.instance != null)
            WorkSystemManager.instance.RemoveWorkOrder(_haulOrder, isCancellation: true);
        _haulOrder = null;
        if (_haulDelivered.Count > 0) SpawnRefund(_haulDelivered);
        _haulPending.Clear();
        _haulDelivered.Clear();
        _haulWorker = null;
    }

    private void SpawnRefund(Dictionary<ItemData, int> items)
    {
        if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;
        var size = _building != null && _building.buildingData != null ? _building.buildingData.size : Vector2Int.one;
        ItemRefundHelper.SpawnRefunds(new Dictionary<ItemData, int>(items), transform.position, size);
    }

    /// <summary>건물 옆에 설 수 있는 칸 — 차단 건물(제련소 등)은 작업 위치가 발판이 아니라 운반 태스크가 도달 불가로 걸러진다</summary>
    public Vector3 GetDeliveryPosition()
    {
        var reach = ReachabilityMap.Current;
        if (reach == null || _building == null || _building.buildingData == null) return WorkPosition;
        int x0 = Mathf.FloorToInt(transform.position.x), y0 = Mathf.FloorToInt(transform.position.y);
        Vector2Int size = _building.buildingData.size;
        var work = WorkPosition;
        var candidates = new[] { new Vector2Int(Mathf.FloorToInt(work.x), Mathf.FloorToInt(work.y)), new Vector2Int(x0 - 1, y0), new Vector2Int(x0 + size.x, y0), new Vector2Int(x0, y0 + size.y), new Vector2Int(x0 + size.x / 2, y0) };
        foreach (var c in candidates)
            if (reach.CanStandAtPosition(c)) return new Vector3(c.x + 0.5f, c.y, 0f);
        return WorkPosition;
    }

    public bool IsRequestStillValid() => this != null && (_building == null || _building.IsFunctional) && AwaitingMaterials;

    public void OnMaterialDelivered(ItemData itemData, int amount)
    {
        if (itemData == null || amount <= 0 || !AwaitingMaterials) return;
        _haulDelivered[itemData] = (_haulDelivered.TryGetValue(itemData, out int d) ? d : 0) + amount;
        if (_haulPending.TryGetValue(itemData, out int left))
        {
            left -= amount;
            if (left <= 0) _haulPending.Remove(itemData); else _haulPending[itemData] = left;
        }
        if (_haulPending.Count == 0) FinishMaterialHaul();
    }

    public void OnMaterialRequestFailed(ItemData itemData, int amount)
    {
        // 일시 실패(경로·낙하 등) — 태스크가 큐로 돌아가 다시 시도된다 (작업대·청사진과 같은 정책)
        Debug.LogWarning($"[{GetBuildingName()}] 재료 운반 실패: {itemData?.itemName}×{amount} — 재시도 대기");
    }

    /// <summary>재료가 다 왔다 — 실물은 이미 창고·바닥에서 빠졌으니 예약만 풀고(완료 시 차감 안 함) 제작을 시작</summary>
    private void FinishMaterialHaul()
    {
        if (_pendingReservationId >= 0 && InventoryManager.instance != null)
            InventoryManager.instance.CancelReservation(_pendingReservationId);
        _pendingReservationId = -1;
        if (_haulOrder != null && WorkSystemManager.instance != null)
            WorkSystemManager.instance.RemoveWorkOrder(_haulOrder, isCancellation: false);
        _haulOrder = null;
        _haulDelivered.Clear();
        StartAfterHaulAsync().Forget();
    }

    // 인계 중인 운반 흐름(WithdrawWorkAsync) 안에서 직원 작업을 바꾸지 않도록 한 프레임 뒤에 시작
    private async UniTaskVoid StartAfterHaulAsync()
    {
        await UniTask.Yield(GameLoop.Frame, this.GetCancellationTokenOnDestroy());
        var worker = _haulWorker;
        _haulWorker = null;
        if (_pendingRecipe == null || worker == null || worker.State == EmployeeState.Dead) return; // 직원 배정 대기로 남는다
        StartProduction(worker);
    }

    /// <summary>운반으로 들여온 재료로 진행 중인 주문(예약 없음)을 취소하면 재료를 돌려준다.</summary>
    private void RefundHauledOrder(CraftingOrder order)
    {
        if (order == null || order.recipe == null || order.ReservationId >= 0) return;
        var items = new Dictionary<ItemData, int>();
        foreach (var cost in order.recipe.requiredMaterials)
            if (cost.item != null && cost.amount > 0)
                items[cost.item] = (items.TryGetValue(cost.item, out int n) ? n : 0) + cost.amount * order.craftAmount;
        if (items.Count > 0) SpawnRefund(items);
    }

    [System.Serializable]
    private class PendingSave
    {
        public int recipeId = -1;
        public int amount;
        public int reservationId = -1;
        public int workerId;
        public List<ItemStackSaveData> pending = new List<ItemStackSaveData>();
        public List<ItemStackSaveData> delivered = new List<ItemStackSaveData>();

        // 진행 중 제작 — CraftingOrder는 작업 시스템에 등록되지 않아 여기서 직접 저장한다
        public int activeRecipeId = -1;
        public int activeAmount;
        public float activeProgress;
        public int activeReservationId = -1;
        public int activeWorkerId;
    }

    /// <summary>배정·운반 대기 주문과 진행 중 제작을 저장</summary>
    public string SerializeExtra()
    {
        if (_pendingRecipe == null && _currentOrder == null) return "";
        var s = new PendingSave();
        if (_pendingRecipe != null)
        {
            s.recipeId = _pendingRecipe.recipeID; s.amount = _pendingAmount; s.reservationId = _pendingReservationId;
            s.workerId = _haulWorker != null ? _haulWorker.InstanceId : 0;
            foreach (var kv in _haulPending) s.pending.Add(new ItemStackSaveData { itemId = kv.Key.itemID, amount = kv.Value });
            foreach (var kv in _haulDelivered) s.delivered.Add(new ItemStackSaveData { itemId = kv.Key.itemID, amount = kv.Value });
        }
        if (_currentOrder != null && _currentOrder.recipe != null)
        {
            s.activeRecipeId = _currentOrder.recipe.recipeID; s.activeAmount = _currentOrder.craftAmount;
            s.activeProgress = _currentOrder.CraftingProgress; s.activeReservationId = _currentOrder.ReservationId;
            s.activeWorkerId = _currentOrder.assignedWorker != null ? _currentOrder.assignedWorker.InstanceId : 0;
        }
        return JsonUtility.ToJson(s);
    }

    public void DeserializeExtra(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var s = JsonUtility.FromJson<PendingSave>(json);
        var db = GameDatabase.Instance;
        if (s == null || db == null) return;
        var recipe = db.GetRecipeData(s.recipeId);
        if (recipe != null)
        {
            _pendingRecipe = recipe;
            _pendingAmount = s.amount;
            _pendingReservationId = s.reservationId;
            _haulPending.Clear();
            _haulDelivered.Clear();
            foreach (var it in s.pending) { var item = db.GetItemData(it.itemId); if (item != null && it.amount > 0) _haulPending[item] = it.amount; }
            foreach (var it in s.delivered) { var item = db.GetItemData(it.itemId); if (item != null && it.amount > 0) _haulDelivered[item] = it.amount; }
        }
        var activeRecipe = s.activeRecipeId >= 0 ? db.GetRecipeData(s.activeRecipeId) : null;
        RestoreAfterLoadAsync(s.workerId, activeRecipe, s).Forget();
    }

    // 건물(30)이 작업 시스템(60)보다 먼저 복원되고, 작업 시스템 복원이 모든 작업물을 지운다 → 로드가 끝난 뒤 다시 건다
    // 배정 직원도 직원(50)이 복원된 뒤에야 찾을 수 있다
    private async UniTaskVoid RestoreAfterLoadAsync(int workerId, CraftingRecipe activeRecipe, PendingSave s)
    {
        await UniTask.WaitUntil(() => SaveManager.instance == null || !SaveManager.instance.IsLoading, GameLoop.Frame, this.GetCancellationTokenOnDestroy());
        if (activeRecipe != null) RestoreActiveOrder(activeRecipe, s);
        if (_pendingRecipe == null) return;
        var worker = workerId > 0 ? RuntimeIDRegistry.instance?.ResolveComponent<Employee>(workerId) : null;
        if (_currentOrder != null) worker = null; // 진행 중 제작이 끝나야 다음 주문을 시작한다 — 배정 대기로 둔다
        if (AwaitingMaterials)
        {
            if (worker != null) _haulWorker = worker;
            if (_haulOrder == null) CreateHaulOrder();
        }
        else if (worker != null && worker.State != EmployeeState.Dead) StartProduction(worker);
    }

    /// <summary>진행 중이던 제작을 같은 진행도로 다시 건다. 담당이 없으면(사망 등) 재료가 들어온 배정 대기 주문으로 둔다.</summary>
    private void RestoreActiveOrder(CraftingRecipe recipe, PendingSave s)
    {
        var worker = s.activeWorkerId > 0 ? RuntimeIDRegistry.instance?.ResolveComponent<Employee>(s.activeWorkerId) : null;
        if (worker == null || worker.State == EmployeeState.Dead)
        {
            // shortcut: 담당이 없으면 진행도는 버린다 — 재배정 때 처음부터. 아까우면 진행도도 대기 주문에 실을 것
            if (_pendingRecipe != null) return; // 대기 주문이 이미 있으면 그대로 (진행 중 몫은 잃는다)
            _pendingRecipe = recipe;
            _pendingAmount = s.activeAmount;
            _pendingReservationId = s.activeReservationId;
            return;
        }
        _currentOrder = new CraftingOrder(recipe, s.activeAmount, this, worker, s.activeReservationId);
        _currentOrder.RestoreProgress(s.activeProgress);
        worker.AssignCraftingWork(_currentOrder, GetDeliveryPosition());
        OnProductionStarted();
        ResumeCraftingLoopAsync().Forget();
    }

    #endregion

    /// <summary>
    /// 재료를 예약합니다 (즉시 소모 대신 예약만 걸어둠).
    /// </summary>
    /// <param name="recipe">제작 레시피</param>
    /// <param name="amount">제작 수량 (재료는 수량에 비례하여 계산)</param>
    /// <returns>예약 ID (성공 시 양수, 실패 시 -1)</returns>
    protected virtual int ReserveMaterials(CraftingRecipe recipe, int amount)
    {
        if (recipe == null || recipe.requiredMaterials == null) return -1;

        // 수량을 반영한 비용 목록 생성
        var scaledCosts = new List<ResourceCost>();
        foreach (var cost in recipe.requiredMaterials)
        {
            scaledCosts.Add(new ResourceCost { item = cost.item, amount = cost.amount * amount });
        }

        if (InventoryManager.instance == null)
        {
            Debug.LogError($"[{GetBuildingName()}] InventoryManager가 없습니다.");
            return -1;
        }

        int reservationId = InventoryManager.instance.TryReserve(scaledCosts);
        if (reservationId < 0)
        {
            Debug.LogError($"[{GetBuildingName()}] 재료 예약 실패: 자원 부족");
        }
        return reservationId;
    }

    /// <summary>
    /// 직원이 할당되었을 때 호출됩니다.
    /// </summary>
    /// <param name="worker">할당된 직원</param>
    protected virtual void OnWorkerAssigned(Employee worker)
    {
        if (worker == null)
        {
            Debug.LogWarning($"[{GetBuildingName()}] 직원이 null입니다.");
            return;
        }

        UIManager.instance.HidePanel(UIPanelType.WorkAssignment);

        if (DeferWhileHauling(worker)) return;

        if (_pendingRecipe != null)
        {
            StartProduction(worker);
        }
        else if (_currentOrder != null)
        {
            ReassignWorker(worker);
        }
    }

    /// <summary>
    /// 제작을 시작합니다 (직원 할당 완료 후).
    /// Pending 상태의 레시피로 CraftingOrder를 생성합니다.
    /// </summary>
    /// <param name="worker">작업을 수행할 직원</param>
    protected virtual void StartProduction(Employee worker)
    {
        if (_pendingRecipe == null || worker == null)
        {
            Debug.LogError($"[{GetBuildingName()}] Pending 레시피 또는 직원이 null입니다.");
            return;
        }

        // CraftingOrder 생성 (예약 ID 전달)
        _currentOrder = new CraftingOrder(_pendingRecipe, _pendingAmount, this, worker, _pendingReservationId);

        Debug.Log($"[{GetBuildingName()}] {worker.Data.employeeName}이(가) {_pendingRecipe.outputItem.itemName} x{_pendingAmount} 제작을 시작합니다. (예약 #{_pendingReservationId})");

        // 작업자에게 작업 할당
        worker.AssignCraftingWork(_currentOrder, GetDeliveryPosition()); // 차단 건물(제련소 등)은 작업 위치가 건물 속이라 옆 칸에서 일한다

        // Pending 상태 초기화
        _pendingRecipe = null;
        _pendingAmount = 0;
        _pendingReservationId = -1;

        // 하위 클래스 훅
        OnProductionStarted();
        ResumeCraftingLoopAsync().Forget();
    }

    private bool _resumeLoopRunning;

    // 제작 주문은 작업 시스템에 등록되지 않아, 담당이 잠·식사 등으로 작업이 끊기면 아무도 다시 집지 않는다
    // → 담당이 한가해지면(Idle·소집 아님·할 일 없음) 이어서 하게 한다
    private async UniTaskVoid ResumeCraftingLoopAsync()
    {
        if (_resumeLoopRunning) return;
        _resumeLoopRunning = true;
        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            while (_currentOrder != null && !_currentOrder.IsCompleted())
            {
                await UniTask.Delay(2000, DelayType.DeltaTime, GameLoop.Frame, ct);
                var order = _currentOrder;
                var w = order != null ? order.assignedWorker : null;
                // 제작을 끈 직원을 다시 보내면 도착 즉시 취소되는 왕복을 끝없이 반복한다
                if (w == null || w.State != EmployeeState.Idle || w.IsDrafted || !w.CanPerformWork(WorkType.Crafting)) continue;
                var work = w.GetComponent<EmployeeWork>();
                if (work == null || work.CurrentWorkTarget != null) continue;
                w.AssignCraftingWork(order, GetDeliveryPosition());
            }
        }
        finally { _resumeLoopRunning = false; }
    }

    /// <summary>
    /// 진행 중인 작업에 새 직원을 재할당합니다.
    /// </summary>
    /// <param name="newWorker">새로 할당할 직원</param>
    protected virtual void ReassignWorker(Employee newWorker)
    {
        if (_currentOrder == null || newWorker == null)
        {
            Debug.LogWarning($"[{GetBuildingName()}] 재할당할 Order 또는 직원이 없습니다.");
            return;
        }

        // 기존 직원 작업 취소
        if (_currentOrder.assignedWorker != null)
        {
            _currentOrder.assignedWorker.CancelWork();
        }

        // 새 직원 할당
        _currentOrder.assignedWorker = newWorker;
        newWorker.AssignCraftingWork(_currentOrder, GetDeliveryPosition());

        Debug.Log($"[{GetBuildingName()}] {newWorker.Data.employeeName}에게 작업을 재할당했습니다.");
    }

    protected virtual void OnWorkerAssignmentClosed()
    {
        UIManager.instance.HidePanel(UIPanelType.WorkAssignment);
    }

    protected virtual void OnWorkerAssignmentCancelled()
    {
        UIManager.instance.HidePanel(UIPanelType.WorkAssignment);
        Debug.Log($"[{GetBuildingName()}] 작업자 배정이 취소되었습니다.");
    }

    #endregion

    #region 제작 완료/취소

    /// <summary>
    /// 제작 완료 시 CraftingOrder에서 호출됩니다.
    /// 예약된 자원을 실제로 소모하고, 결과물을 인벤토리에 추가합니다.
    /// </summary>
    /// <param name="recipe">완료된 레시피</param>
    /// <param name="amount">제작 수량</param>
    /// <param name="reservationId">자원 예약 ID</param>
    public virtual void OnCraftingComplete(CraftingRecipe recipe, int amount, int reservationId)
    {
        if (recipe == null) return;

        // 예약된 자원을 실제로 소모
        if (reservationId >= 0 && InventoryManager.instance != null)
        {
            InventoryManager.instance.ConsumeReservation(reservationId);
        }

        int totalOutput = recipe.outputAmount * amount;
        StoreCraftOutput(recipe.outputItem, totalOutput, reservationId);

        _currentOrder = null;
        StopWorkingEffect();

        // 하위 클래스 훅
        OnProductionCompleted(recipe, amount);
    }

    /// <summary>
    /// 완성품을 어디에 둘지 결정합니다.
    ///
    /// 우선순위:
    ///   1. 장비 → 장비 보관소 풀 (인스턴스 단위 관리라 운반 대상이 아님)
    ///   2. BuildingOutputBuffer가 붙어 있으면 → 건물에 보관, 직원이 창고로 운반
    ///   3. 버퍼가 없으면 → 기존대로 전역 인벤토리 직행 (구 프리팹 호환)
    ///
    /// 버퍼가 가득 차 못 담은 몫은 잃지 않도록 인벤토리로 보냅니다.
    /// </summary>
    protected void StoreCraftOutput(ItemData output, int totalOutput, int reservationId)
    {
        if (output == null || totalOutput <= 0) return;

        // 장비 품질 = 만든 직원의 제작 적성 + 이 건물의 단계 + 같은 방 부속
        float qualityScore = EquipmentQualities.Score(_currentOrder != null ? _currentOrder.assignedWorker : null, _building);
        if (EquipmentStorageManager.TryAddCraftOutput(output, totalOutput, qualityScore))
        {
            Debug.Log($"[{GetBuildingName()}] 제작 완료! {output.itemName} x{totalOutput} → 장비 보관소 (예약 #{reservationId} 소모)");
            return;
        }

        var buffer = GetComponent<BuildingOutputBuffer>();
        int stored = buffer != null ? buffer.TryStore(output, totalOutput) : 0;
        int overflow = totalOutput - stored;

        if (overflow > 0) InventoryManager.instance?.AddItem(output, overflow);

        if (stored > 0)
            Debug.Log($"[{GetBuildingName()}] 제작 완료! {output.itemName} x{stored} 보관 — 운반 대기 (예약 #{reservationId} 소모)");
        if (overflow > 0)
            Debug.Log($"[{GetBuildingName()}] 보관함 여유 없음 — {output.itemName} x{overflow}은(는) 인벤토리로 보냈습니다.");
    }

    /// <summary>
    /// 제작을 취소합니다.
    /// 예약 시스템: 작업 시작 전이면 예약만 취소 (자원 손실 없음).
    /// </summary>
    public virtual void CancelProduction()
    {
        // Pending Order 취소 (직원 배정 전 → 예약 취소만 하면 됨)
        if (_pendingRecipe != null)
        {
            Debug.Log($"[{GetBuildingName()}] Pending Order 취소: {_pendingRecipe.outputItem.itemName}");

            if (AwaitingMaterials) AbortMaterialHaul();
            else if (_pendingReservationId < 0 && _pendingRecipe.requiredMaterials != null)
            {
                // 재료가 이미 운반돼 들어온 뒤 배정 대기 중이던 주문
                var items = new Dictionary<ItemData, int>();
                foreach (var cost in _pendingRecipe.requiredMaterials)
                    if (cost.item != null && cost.amount > 0) items[cost.item] = (items.TryGetValue(cost.item, out int n) ? n : 0) + cost.amount * _pendingAmount;
                if (items.Count > 0) SpawnRefund(items);
            }

            if (_pendingReservationId >= 0 && InventoryManager.instance != null)
            {
                InventoryManager.instance.CancelReservation(_pendingReservationId);
            }

            _pendingRecipe = null;
            _pendingAmount = 0;
            _pendingReservationId = -1;
            // pending과 active 둘 다 있을 수 있으므로 early return하지 않고 계속 진행
        }

        // 진행 중인 Order 취소
        if (_currentOrder == null) return;

        Debug.Log($"[{GetBuildingName()}] 제작 Order 취소: {_currentOrder.orderName}");

        // CraftingOrder의 예약 취소 처리 (운반으로 들여온 재료면 돌려준다)
        RefundHauledOrder(_currentOrder);
        _currentOrder.CancelReservation();

        // 작업자 작업 취소
        if (_currentOrder.assignedWorker != null)
        {
            _currentOrder.assignedWorker.CancelWork();
        }

        _currentOrder.Cancel();
        _currentOrder = null;

        StopWorkingEffect();

        // 하위 클래스 훅
        OnProductionCancelled();
    }

    #endregion

    #region 시각/사운드 효과

    /// <summary>
    /// 작업 시작 시 시각 효과를 표시합니다.
    /// </summary>
    public virtual void StartWorkingEffect()
    {
        if (workingEffectPrefab == null) return;

        if (_workingEffectInstance == null)
        {
            _workingEffectInstance = Instantiate(workingEffectPrefab, transform.position + effectOffset, Quaternion.identity, transform);
        }
    }

    /// <summary>
    /// 작업 종료 시 시각 효과를 제거합니다.
    /// </summary>
    public virtual void StopWorkingEffect()
    {
        if (_workingEffectInstance != null)
        {
            Destroy(_workingEffectInstance);
            _workingEffectInstance = null;
        }
    }

    #endregion

    #region IBuildingFunction 인터페이스

    /// <summary>
    /// 건물이 비활성화될 때 호출됩니다 (기반 파괴 등).
    /// 진행 중인 모든 제작을 취소합니다.
    /// </summary>
    public virtual void OnBuildingDisabled()
    {
        if (_currentOrder != null || _pendingRecipe != null)
        {
            CancelProduction();
            Debug.Log($"[{GetBuildingName()}] 건물이 비활성화되어 제작이 취소되었습니다.");
        }
    }

    /// <summary>
    /// 건물이 다시 활성화될 때 호출됩니다.
    /// </summary>
    public virtual void OnBuildingEnabled()
    {
        Debug.Log($"[{GetBuildingName()}] 건물이 다시 활성화되었습니다.");
    }

    #endregion

    #region 하위 클래스 훅 메서드

    /// <summary>
    /// 제작 시작 시 호출됩니다.
    /// 기본 구현: 시각 효과를 시작합니다.
    /// </summary>
    protected virtual void OnProductionStarted()
    {
        StartWorkingEffect();
    }

    /// <summary>
    /// 제작 완료 시 호출됩니다.
    /// 하위 클래스에서 추가 처리 (보너스 생산, 파티클 정리 등)를 구현합니다.
    /// </summary>
    /// <param name="recipe">완료된 레시피</param>
    /// <param name="amount">제작 수량</param>
    protected virtual void OnProductionCompleted(CraftingRecipe recipe, int amount)
    {
        // 하위 클래스에서 오버라이드
    }

    /// <summary>
    /// 제작 취소 시 호출됩니다.
    /// 하위 클래스에서 추가 처리 (파티클 정리, 상태 리셋 등)를 구현합니다.
    /// </summary>
    protected virtual void OnProductionCancelled()
    {
        // 하위 클래스에서 오버라이드
    }

    /// <summary>
    /// 건물 이름을 반환합니다.
    /// 하위 클래스에서 오버라이드하여 로그에 사용될 이름을 지정합니다.
    /// </summary>
    /// <returns>건물 이름 문자열</returns>
    protected virtual string GetBuildingName()
    {
        return this.GetType().Name;
    }

    #endregion
}
