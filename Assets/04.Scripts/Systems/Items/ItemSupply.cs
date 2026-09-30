using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 건물이 소모하는 재료의 내부 재고 + 자동 보급 (수경재배기·침식 배양기의 침식 결정체).
///
/// 재고가 <see cref="refillBelow"/> 아래로 떨어지면 창고에서 예약 → 운반 작업(WithdrawOrder)을 걸어
/// 직원이 가져옵니다. 발전기 연료 보급(PowerProducer)과 같은 흐름입니다.
///
/// 컴포넌트가 아닌 이유: Building은 확장 세이브(IBuildingExtraSerializable)를 건물당 하나만 쓰므로,
/// 소유 건물이 자기 저장 데이터 안에 <see cref="SaveState"/>를 함께 담습니다.
/// </summary>
[System.Serializable]
public class ItemSupply : IMaterialReceiver
{
    private const float CHECK_INTERVAL = 2f;

    [Tooltip("소모 재료")]
    public ItemData item;

    [Tooltip("내부에 쌓아 둘 수 있는 최대 개수")]
    [Min(1)] public int capacity = 5;

    [Tooltip("재고가 이 값 미만이면 보급 요청")]
    [Min(1)] public int refillBelow = 2;

    [Tooltip("보급 운반 작업 우선순위")]
    public int priority = 3;

    [System.NonSerialized] private MonoBehaviour owner;
    [System.NonSerialized] private WorkOrder order;
    [System.NonSerialized] private int reservationId = -1;
    [System.NonSerialized] private int incoming;
    [System.NonSerialized] private float nextCheck;

    [SerializeField, HideInInspector] private int stock;

    /// <summary>현재 내부 재고</summary>
    public int Stock => stock;

    /// <summary>소유 건물 연결 (Awake에서 호출)</summary>
    public void Bind(MonoBehaviour owner) => this.owner = owner;

    /// <summary>주기적으로 부르면 필요할 때 보급을 요청합니다.</summary>
    public void Tick()
    {
        if (owner == null || item == null || Time.time < nextCheck) return;
        nextCheck = Time.time + CHECK_INTERVAL;

        if (order != null || stock + incoming >= refillBelow) return;
        if (InventoryManager.instance == null || WorkSystemManager.instance == null) return;

        int want = capacity - stock - incoming;
        int amount = Mathf.Min(want, InventoryManager.instance.GetWorldAvailable(item));
        if (amount < 1) return; // 재고 없음 — 다음 체크에 재시도

        int id = InventoryManager.instance.TryReserve(new List<ResourceCost> { new ResourceCost { item = item, amount = amount } });
        if (id < 0) return;

        reservationId = id;
        incoming = amount;
        order = WorkSystemManager.instance.CreateWorkOrder($"{item.itemName} 보급: {owner.name}", WorkType.Hauling, 0, priority);
        order.AddTarget(new WithdrawOrder(new MaterialRequest(item, amount, this, id)));
    }

    /// <summary>재고에서 꺼내 씁니다. 모자라면 아무것도 빼지 않고 false.</summary>
    public bool TryConsume(int amount = 1)
    {
        if (stock < amount) return false;
        stock -= amount;
        return true;
    }

    /// <summary>건물이 사라질 때 — 진행 중 보급 취소 (예약 해제)</summary>
    public void Cancel()
    {
        if (order != null && WorkSystemManager.instance != null)
            WorkSystemManager.instance.RemoveWorkOrder(order, isCancellation: true);
        order = null;
        ReleaseReservation();
        incoming = 0;
    }

    #region IMaterialReceiver

    public Vector3 GetDeliveryPosition() => owner != null ? owner.transform.position + new Vector3(0.5f, 0f, 0f) : Vector3.zero;

    public bool IsRequestStillValid() => owner != null && incoming > 0;

    public void OnMaterialDelivered(ItemData itemData, int amount)
    {
        stock = Mathf.Min(capacity, stock + amount);
        incoming = 0;
        ReleaseReservation(); // 출고 시점에 이미 창고에서 빠졌다 (CraftingTable·발전기와 같은 규칙)
        if (order != null && WorkSystemManager.instance != null)
            WorkSystemManager.instance.RemoveWorkOrder(order, isCancellation: false);
        order = null;
    }

    public void OnMaterialRequestFailed(ItemData itemData, int amount)
    {
        // 일시적 실패일 수 있다 — 작업은 큐에 남아 다른 직원이 재시도
    }

    #endregion

    #region 세이브

    /// <summary>저장할 상태</summary>
    [System.Serializable]
    public struct SaveState
    {
        public int stock;
        /// <summary>저장 시점에 진행 중이던 보급의 예약 ID — 로드하면 해제하고 다시 요청한다</summary>
        public int pendingReservationId;
    }

    public SaveState Capture() => new SaveState { stock = stock, pendingReservationId = reservationId };

    /// <summary>
    /// 복원. 인벤토리가 예약을 ID째로 복원하므로, 끊긴 보급의 예약은 여기서 해제해야 새지 않는다.
    /// 보급은 다음 Tick에서 새로 요청된다.
    /// </summary>
    public void Restore(SaveState s)
    {
        stock = Mathf.Clamp(s.stock, 0, capacity);
        if (s.pendingReservationId >= 0 && InventoryManager.instance != null)
            InventoryManager.instance.CancelReservation(s.pendingReservationId);
        order = null;
        reservationId = -1;
        incoming = 0;
    }

    #endregion

    private void ReleaseReservation()
    {
        if (reservationId >= 0 && InventoryManager.instance != null)
            InventoryManager.instance.CancelReservation(reservationId);
        reservationId = -1;
    }
}
