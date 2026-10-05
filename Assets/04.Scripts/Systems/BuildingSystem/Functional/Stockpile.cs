using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 창고 건물 컴포넌트 — Building과 함께 사용합니다 (IBuildingFunction 구현).
///
/// 역할:
///   - 직원이 운반한 재료를 저장소(IItemStorage)에 위임 저장합니다.
///   - 저장된 재료를 출고(Withdraw)할 수 있는 접근점을 제공합니다.
///   - StockpileManager에 등록하여 가장 가까운 창고 조회를 지원합니다.
///
/// 저장소 위임:
///   현재 모든 Stockpile은 InventoryManager(IItemStorage) 단일 전역 저장소를 공유합니다.
///   향후 LocalStorage 구현체로 교체하면 창고별 분리 저장이 가능합니다.
///   linkedStorage는 인스펙터에서 직접 지정하지 않고 Awake/Start에서 해결합니다.
///
/// 사용 방법:
///   1. Building 컴포넌트와 함께 프리팹에 붙입니다.
///   2. BuildingData의 blocksMovement = false (직원이 입장 가능)로 설정합니다.
///   3. depositOffset으로 직원이 배달/출고하러 오는 정확한 위치를 조정합니다.
/// </summary>
[RequireComponent(typeof(Building))]
public class Stockpile : MonoBehaviour, IBuildingFunction, IMaterialSource, IStorageView
{
    #region 설정

    [Header("창고 설정")]
    [Tooltip("직원이 배달/출고하러 올 위치 오프셋 (건물 좌측 하단 기준)")]
    [SerializeField] private Vector2 depositOffset = new Vector2(0.5f, 0f);

    [Tooltip("보관함 창에 보이는 칸 수 (아이템 종류 하나가 한 칸, 수량 제한 없음). 16칸이 한 쪽입니다.")]
    [Min(1)] [SerializeField] private int slotCount = 16;

    [Header("디버그")]
    [SerializeField] private bool showDebugInfo = false;

    #endregion

    #region 내부 상태

    private Building     _building;
    private IItemStorage _linkedStorage;
    private bool         _registered = false;

    #endregion

    #region 프로퍼티

    /// <summary>창고가 정상 동작 중인지 여부.</summary>
    public bool IsOperational => _building != null && _building.IsFunctional;

    // IBuildingFunction
    public bool IsOperating => IsOperational;

    /// <summary>이 창고가 위임하는 저장소(현재는 전역 InventoryManager).</summary>
    public IItemStorage LinkedStorage => _linkedStorage;

    #endregion

    #region 초기화

    void Awake()
    {
        _building = GetComponent<Building>();
    }

    void Start()
    {
        ResolveLinkedStorage();
        StockpileManager.instance?.Register(this);
        _registered = true;
    }

    void OnDestroy()
    {
        if (_registered)
            StockpileManager.instance?.Unregister(this);
    }

    /// <summary>
    /// linkedStorage를 결정합니다. 현재는 전역 InventoryManager를 사용하지만,
    /// 향후 LocalStorage 구현체로 교체할 수 있습니다.
    /// </summary>
    private void ResolveLinkedStorage()
    {
        // 현재: 모든 창고가 전역 인벤토리를 공유
        _linkedStorage = InventoryManager.instance;

        if (_linkedStorage == null)
            Debug.LogWarning($"[Stockpile] {name}: linkedStorage 해결 실패 — InventoryManager가 없습니다.");
    }

    #endregion

    #region IBuildingFunction

    public void OnBuildingDisabled()
    {
        if (showDebugInfo)
            Debug.Log($"[Stockpile] {gameObject.name} 파손 — 입출고 비활성");
    }

    public void OnBuildingEnabled()
    {
        if (showDebugInfo)
            Debug.Log($"[Stockpile] {gameObject.name} 복구 — 입출고 활성");
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 직원이 아이템을 배달/출고하는 월드 위치를 반환합니다.
    /// </summary>
    public Vector3 GetDepositPosition()
    {
        return transform.position + new Vector3(depositOffset.x, depositOffset.y, 0f);
    }

    #region IMaterialSource
    // 창고는 전역 인벤토리의 물리적 접근점입니다. 다른 소스(건물 산출물·바닥 더미)와
    // 같은 인터페이스로 묶여야 직원이 "가장 가까운 자재 보유 지점"을 한 번에 고를 수 있습니다.

    public bool IsSourceAvailable => IsOperational && _linkedStorage != null;

    public Vector3 GetWithdrawPosition() => GetDepositPosition();

    public int GetStoredAmount(ItemData item)
        => item != null && _linkedStorage != null ? _linkedStorage.GetItemCount(item) : 0;

    #endregion

    /// <summary>
    /// 아이템을 창고에 저장합니다 (위임된 저장소로 전달).
    /// </summary>
    /// <param name="data">저장할 아이템 데이터</param>
    /// <param name="qty">수량</param>
    /// <returns>저장 성공 여부</returns>
    public bool Deposit(ItemData data, int qty)
    {
        if (data == null || qty <= 0) return false;
        if (!IsOperational)
        {
            if (showDebugInfo)
                Debug.LogWarning($"[Stockpile] {name}: 비활성 상태에서 Deposit 시도");
            return false;
        }

        if (_linkedStorage == null)
        {
            Debug.LogWarning($"[Stockpile] {name}: linkedStorage가 없어 입고 실패: {data.itemName} × {qty}");
            return false;
        }

        bool ok = _linkedStorage.AddItem(data, qty);
        if (ok && showDebugInfo)
            Debug.Log($"[Stockpile] {name} 입고: {data.itemName} × {qty}");
        return ok;
    }

    /// <summary>
    /// 위임된 저장소에 해당 아이템이 충분히 있는지 확인합니다.
    /// </summary>
    public bool HasItem(ItemData data, int qty = 1)
    {
        if (data == null || qty <= 0) return false;
        if (_linkedStorage == null) return false;
        return _linkedStorage.HasItem(data, qty);
    }

    /// <summary>
    /// 창고를 클릭하면 보관함 창을 엽니다.
    /// (BoxCollider2D가 있어야 OnMouseDown이 동작합니다.)
    /// </summary>
    void OnMouseDown()
    {
        // UI 위 클릭은 월드로 흘리지 않는다 (OnMouseDown은 EventSystem을 거치지 않는다)
        if (UIManager.PointerOverUI) return;
        if (!IsOperational) return;

        StoragePanel.Show(this);
    }

    #region IStorageView — 보관함 창

    public int SlotCount => slotCount;

    /// <summary>
    /// 이 창고의 칸에 든 아이템을 채웁니다.
    ///
    /// 재고는 전역 인벤토리 하나라 창고마다 내용물을 따로 기록하지 않습니다. 대신 아이템 종류를 번호순으로 늘어놓고
    /// <b>등록된 창고 순서대로 칸 수만큼 채운다</b>고 보고 이 창고 몫을 잘라 보여 줍니다.
    /// 전체 칸을 넘는 종류는 마지막 창고에 '칸 초과'로 이어 붙입니다 — 어디에도 안 보이는 재고가 없도록.
    /// </summary>
    public void CollectSlots(List<StorageSlot> into)
    {
        var inventory = InventoryManager.instance;
        if (inventory == null) return;

        var items = inventory.GetAllItems();
        items.RemoveAll(pair => pair.Key == null || pair.Value <= 0);
        items.Sort((a, b) => a.Key.itemID.CompareTo(b.Key.itemID));

        int offset = 0;
        bool isLast = true;
        var all = StockpileManager.instance != null ? StockpileManager.instance.All : null;
        if (all != null)
        {
            bool passedSelf = false;
            foreach (var other in all)
            {
                if (other == null || !other.IsOperational) continue;
                if (other == this) { passedSelf = true; continue; }
                if (passedSelf) isLast = false; else offset += other.SlotCount;
            }
        }

        int end = isLast ? items.Count : Mathf.Min(items.Count, offset + slotCount);
        for (int i = offset; i < end; i++)
            into.Add(new StorageSlot { item = items[i].Key, count = items[i].Value });
    }

    #endregion

    /// <summary>
    /// 아이템을 창고에서 출고합니다 (위임된 저장소에서 제거).
    /// 출고 흐름: 직원이 사용처(작업대 등)의 자재 요청을 받아 Stockpile에 와서 호출합니다.
    /// </summary>
    /// <param name="data">출고할 아이템 데이터</param>
    /// <param name="qty">수량</param>
    /// <returns>출고 성공 여부 (수량 부족 시 false)</returns>
    public bool Withdraw(ItemData data, int qty)
    {
        if (data == null || qty <= 0) return false;
        if (!IsOperational)
        {
            if (showDebugInfo)
                Debug.LogWarning($"[Stockpile] {name}: 비활성 상태에서 Withdraw 시도");
            return false;
        }

        if (_linkedStorage == null)
        {
            Debug.LogWarning($"[Stockpile] {name}: linkedStorage가 없어 출고 실패: {data.itemName} × {qty}");
            return false;
        }

        bool ok = _linkedStorage.RemoveItem(data, qty);
        if (ok && showDebugInfo)
            Debug.Log($"[Stockpile] {name} 출고: {data.itemName} × {qty}");
        return ok;
    }

    #endregion
}
