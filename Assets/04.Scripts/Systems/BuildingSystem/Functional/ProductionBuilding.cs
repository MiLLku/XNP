using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
public abstract class ProductionBuilding : MonoBehaviour, IBuildingFunction
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
    [SerializeField] protected AudioClip workingSound;

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

    #endregion

    #region 초기화 및 정리

    protected virtual void Awake()
    {
        _building = GetComponent<Building>();

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

        // 직원이 이미 선택되어 넘어온 경우 바로 시작
        if (worker != null)
        {
            StartProduction(worker);
        }
    }

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
        worker.AssignCraftingWork(_currentOrder, workPosition.position);

        // Pending 상태 초기화
        _pendingRecipe = null;
        _pendingAmount = 0;
        _pendingReservationId = -1;

        // 하위 클래스 훅
        OnProductionStarted();
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
        newWorker.AssignCraftingWork(_currentOrder, workPosition.position);

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

        // 제작 결과물 인벤토리에 추가
        int totalOutput = recipe.outputAmount * amount;
        if (InventoryManager.instance != null)
        {
            InventoryManager.instance.AddItem(recipe.outputItem, totalOutput);
        }

        Debug.Log($"[{GetBuildingName()}] 제작 완료! {recipe.outputItem.itemName} x{totalOutput}이(가) 인벤토리에 추가되었습니다. (예약 #{reservationId} 소모)");

        _currentOrder = null;
        StopWorkingEffect();

        // 하위 클래스 훅
        OnProductionCompleted(recipe, amount);
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

        // CraftingOrder의 예약 취소 처리
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
