using UnityEngine;
using System.Collections;

/// <summary>
/// 제작 작업 명령
/// 생산 건물에서 아이템을 제작하는 작업을 관리
/// WorkOrder를 상속하고 IWorkTarget을 구현하여 WorkTask 시스템과 연동
///
/// 생명주기:
///   생성 → StartWorking() → UpdateProgress() 반복 → CompleteWork() 또는 CancelWork()
/// </summary>
[System.Serializable]
public class CraftingOrder : WorkOrder, IWorkTarget
{
    #region 필드

    [Header("제작 정보")]
    /// <summary>제작할 레시피</summary>
    public CraftingRecipe recipe;

    /// <summary>제작 개수</summary>
    public int craftAmount;

    /// <summary>작업이 수행되는 생산 건물</summary>
    public ProductionBuilding building;

    /// <summary>할당된 직원</summary>
    public Employee assignedWorker;

    [Header("진행 상태")]
    private float _craftingProgress = 0f;   // 제작 진행도 (0~1)
    private float _totalTime;               // 총 제작 시간 (초)
    private bool _isWorking = false;        // 현재 작업 중 여부
    private Vector3 _workPosition;          // 작업 위치 (월드 좌표)

    /// <summary>
    /// InventoryManager에서 발급받은 자원 예약 ID
    /// -1이면 예약 없음. 작업 완료 시 ConsumeReservation으로 소모됨
    /// </summary>
    private int _reservationId = -1;

    #endregion

    #region 생성자

    /// <summary>
    /// CraftingOrder를 생성
    /// WorkTask를 자동으로 생성하고 직원을 등록
    /// </summary>
    /// <param name="recipe">제작할 레시피</param>
    /// <param name="amount">제작 수량</param>
    /// <param name="productionBuilding">작업 건물</param>
    /// <param name="worker">할당할 직원</param>
    /// <param name="reservationId">자원 예약 ID (-1이면 예약 없음)</param>
    public CraftingOrder(CraftingRecipe recipe, int amount, ProductionBuilding productionBuilding, Employee worker, int reservationId = -1)
    {
        this.recipe = recipe;
        this.craftAmount = amount;
        this.building = productionBuilding;
        this.assignedWorker = worker;
        this._reservationId = reservationId;

        // WorkOrder 기본 설정
        this.workType = WorkType.Crafting;
        this.orderName = $"{recipe.outputItem.itemName} 제작";
        this.priority = 5;
        this.maxAssignedWorkers = 1;
        this.isActive = true;
        this.isPaused = false;

        // 총 제작 시간 계산
        this._totalTime = recipe.craftingTime * amount;
        this._workPosition = productionBuilding.WorkPosition;

        // WorkTask 생성 (this를 IWorkTarget으로 전달)
        WorkTask task = new WorkTask(this, priority);
        taskQueue.Enqueue(task);

        // 직원을 작업물에 등록
        AssignWorker(worker);

        Debug.Log($"[CraftingOrder] 생성됨: {orderName}, 총 시간: {_totalTime}초, 예약: #{reservationId}");
    }

    /// <summary>로드 전용 기본 생성자 (WorkOrder 기본 초기화만 수행)</summary>
    private CraftingOrder() : base() { }

    /// <summary>
    /// 세이브 데이터로부터 CraftingOrder를 복원합니다.
    /// </summary>
    public static CraftingOrder CreateForRestore(
        WorkOrderSaveData orderData, CraftingTargetData craftData,
        CraftingRecipe recipe, ProductionBuilding building)
    {
        var order = new CraftingOrder();
        order.recipe = recipe;
        order.craftAmount = craftData.craftAmount;
        order.building = building;
        order._reservationId = craftData.reservationId;
        order._craftingProgress = craftData.craftingProgress;
        order._totalTime = recipe.craftingTime * craftData.craftAmount;
        order._workPosition = building != null ? building.WorkPosition : Vector3.zero;

        // WorkOrder 필드 복원
        order.orderId = orderData.orderId;
        order.orderName = orderData.orderName;
        order.workType = (WorkType)orderData.workType;
        order.priority = orderData.priority;
        order.createdTime = orderData.createdTime;
        order.maxAssignedWorkers = orderData.maxWorkers;
        order.isActive = orderData.isActive;
        order.isPaused = orderData.isPaused;

        // WorkTask 생성 (this를 IWorkTarget으로)
        WorkTask task = new WorkTask(order, order.priority);
        order.taskQueue.Enqueue(task);

        return order;
    }

    #endregion

    #region 프로퍼티

    /// <summary>자원 예약 ID (-1이면 예약 없음)</summary>
    public int ReservationId => _reservationId;

    /// <summary>제작 진행도 (0~1)</summary>
    public float CraftingProgress => _craftingProgress;

    /// <summary>현재 작업 중 여부</summary>
    public bool IsWorking => _isWorking;

    /// <summary>남은 제작 시간 (초)</summary>
    public float RemainingTime => _totalTime * (1f - _craftingProgress);

    #endregion

    #region IWorkTarget 인터페이스 구현

    /// <summary>
    /// 작업 위치를 반환
    /// </summary>
    public Vector3 GetWorkPosition()
    {
        return _workPosition;
    }

    /// <summary>
    /// 작업 타입을 반환
    /// </summary>
    public WorkType GetWorkType()
    {
        return WorkType.Crafting;
    }

    /// <summary>
    /// 총 작업 시간을 반환 (초 기준)
    /// </summary>
    public float GetWorkTime()
    {
        return _totalTime;
    }

    /// <summary>
    /// 작업 가능 여부를 반환
    /// 활성 상태이고 일시정지가 아니며 건물과 직원이 유효해야함
    /// </summary>
    public bool IsWorkAvailable()
    {
        return isActive && !isPaused && building != null && assignedWorker != null;
    }

    /// <summary>
    /// 작업 완료 처리
    /// 할당된 직원만 완료할 수 있으며, 건물에 제작 완료를 알림
    /// </summary>
    /// <param name="worker">작업을 완료한 직원</param>
    public void CompleteWork(Employee worker)
    {
        if (worker != assignedWorker)
        {
            Debug.LogWarning($"[CraftingOrder] 다른 직원이 작업을 완료하려 했습니다: {worker.Data.employeeName}");
            return;
        }

        Debug.Log($"[CraftingOrder] 작업 완료: {orderName} (예약 #{_reservationId} 소모)");

        _isWorking = false;
        _craftingProgress = 1f;

        // 건물에 제작 완료 알림 (예약 ID 전달하여 자원 실제 소모)
        if (building != null)
        {
            building.OnCraftingComplete(recipe, craftAmount, _reservationId);
            _reservationId = -1;
        }

        // WorkOrder 완료 처리
        if (taskQueue.AssignedTasks.Count > 0)
        {
            CompleteTask(taskQueue.AssignedTasks[0]);
        }
    }

    /// <summary>
    /// 작업 취소 처리
    /// </summary>
    /// <param name="worker">작업을 취소한 직원</param>
    public void CancelWork(Employee worker)
    {
        Debug.Log($"[CraftingOrder] 작업 취소: {orderName}");
        _isWorking = false;
    }

    #endregion

    #region 자원 예약

    /// <summary>
    /// 자원 예약을 취소 (제작 취소 시 호출)
    /// 예약만 해제되고 실제 자원은 손실되지 않습니다.
    /// </summary>
    public void CancelReservation()
    {
        if (_reservationId >= 0 && InventoryManager.instance != null)
        {
            InventoryManager.instance.CancelReservation(_reservationId);
            Debug.Log($"[CraftingOrder] 예약 #{_reservationId} 취소됨: {orderName}");
            _reservationId = -1;
        }
    }

    /// <summary>
    /// 작업 취소 시 자원 예약도 해제합니다.
    /// </summary>
    public new void Cancel()
    {
        CancelReservation();
        _isWorking = false;
        if (building != null)
        {
            building.StopWorkingEffect();
        }
        base.Cancel();
    }

    #endregion

    #region 제작 진행 관리

    /// <summary>
    /// 작업을 시작
    /// 건물의 작업 시각 효과도 함께 시작
    /// </summary>
    public void StartWorking()
    {
        if (_isWorking)
        {
            Debug.LogWarning("[CraftingOrder] 이미 작업 중입니다.");
            return;
        }

        _isWorking = true;
        Debug.Log($"[CraftingOrder] 작업 시작: {orderName}");

        if (building != null)
        {
            building.StartWorkingEffect();
        }
    }

    /// <summary>
    /// 매 프레임 진행도를 업데이트 (Employee.Update에서 호출)
    /// 진행도가 100%에 도달하면 자동으로 CompleteWork가 호출
    /// </summary>
    /// <param name="deltaTime">경과 시간 (초)</param>
    public void UpdateProgress(float deltaTime)
    {
        if (!_isWorking || _totalTime <= 0)
        {
            return;
        }

        _craftingProgress += deltaTime / _totalTime;

        if (_craftingProgress >= 1f)
        {
            _craftingProgress = 1f;
            if (assignedWorker != null)
            {
                CompleteWork(assignedWorker);
            }
            else
            {
                Debug.LogWarning("[CraftingOrder] 진행도 100% 도달했으나 assignedWorker가 null입니다.");
                _isWorking = false;
            }
        }
    }

    #endregion

    #region 디버그

    /// <summary>
    /// 디버그 정보를 문자열로 반환
    /// </summary>
    /// <returns>기본 WorkOrder 정보 + 제작 상세 정보</returns>
    public new string GetDebugInfo()
    {
        string baseInfo = base.GetDebugInfo();
        string craftingInfo = $" | Crafting: {recipe?.outputItem.itemName ?? "null"} x{craftAmount} | " +
                              $"Progress: {_craftingProgress * 100:F0}% | Working: {_isWorking}";

        return baseInfo + craftingInfo;
    }

    #endregion
}
