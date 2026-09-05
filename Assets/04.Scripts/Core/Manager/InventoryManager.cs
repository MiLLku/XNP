using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 글로벌 인벤토리 매니저
/// 아이템 추가/제거, 자원 예약 시스템, 저장/불러오기 기능
///
/// 자원 예약 흐름:
///   1. TryReserve()로 자원을 예약 (다른 곳에서 사용 불가)
///   2. 작업 완료 시 ConsumeReservation()으로 실제 소모
///   3. 작업 취소 시 CancelReservation()으로 예약 해제
///
/// 저장소 추상화:
///   IItemStorage를 구현하여 Stockpile의 linkedStorage가 이 매니저를 위임 대상으로 삼습니다.
///   향후 창고별 LocalStorage(IItemStorage) 구현체로 교체할 수 있습니다.
/// </summary>
public class InventoryManager : DestroySingleton<InventoryManager>, ISaveModule, IItemStorage
{
    #region 필드 및 설정

    /// <summary>
    /// 글로벌 인벤토리 (Key: 아이템 데이터, Value: 보유 수량).
    /// Dictionary는 Unity가 직렬화하지 못하므로 인스펙터 노출 대상이 아니다 —
    /// 저장은 ISaveModule 경로(Capture/Restore)가 담당한다.
    /// </summary>
    [System.NonSerialized]
    public Dictionary<ItemData, int> globalInventory = new Dictionary<ItemData, int>();

    /// <summary>아이템별 총 예약 수량</summary>
    private Dictionary<ItemData, int> reservedAmounts = new Dictionary<ItemData, int>();

    /// <summary>예약 ID → 예약 내역 매핑</summary>
    private Dictionary<int, List<ResourceCost>> reservations = new Dictionary<int, List<ResourceCost>>();

    /// <summary>다음 예약 ID</summary>
    private int nextReservationId = 1;  

    [Header("인벤토리 설정")]
    // 스택 상한은 없습니다. 한 종류를 얼마든지 쌓을 수 있고, 표시상 '묶음'이 나뉘더라도
    // 저장 모델은 종류당 총합 하나이므로 상한을 두지 않는 것이 곧 '묶음 자동 생성'과 같습니다.
    // (구 maxStackSize 필드는 제거됨 — 세척 결정체처럼 대량 산출되는 자원이 조용히 유실됐습니다)
    [SerializeField] private bool showDebugLogs = true;

    [Header("시작 인벤토리")]
    [Tooltip("새 게임 시작 시 지급할 기본 아이템 목록 (식량 등). 로드 게임에는 적용되지 않습니다.")]
    [SerializeField] private StartingInventoryConfig startingInventory;

    #endregion

    #region 생명주기

    private void Start()
    {
        GrantStartingInventory();
    }

    /// <summary>
    /// 시작 인벤토리 설정의 아이템을 글로벌 인벤토리에 1회 지급합니다.
    /// 세이브 로드 시에는 이후 Restore가 ClearInventory로 덮어쓰므로 중복되지 않습니다.
    /// </summary>
    private void GrantStartingInventory()
    {
        if (startingInventory == null || startingInventory.startingItems == null) return;

        int granted = 0;
        foreach (var stack in startingInventory.startingItems)
        {
            if (stack.item == null || stack.amount <= 0) continue;
            AddItem(stack.item, stack.amount);
            granted++;
        }

        if (showDebugLogs && granted > 0)
            Debug.Log($"[InventoryManager] 시작 인벤토리 지급: {granted}종");
    }

    #endregion

    #region 아이템 추가/제거

    /// <summary>
    /// 글로벌 인벤토리에 지정된 수량의 아이템을 추가
    /// </summary>
    /// <param name="itemData">추가할 아이템 데이터</param>
    /// <param name="amount">추가할 수량</param>
    /// <returns>추가 성공 여부</returns>
    public bool AddItem(ItemData itemData, int amount = 1)
    {
        if (itemData == null || amount <= 0)
        {
            return false;
        }

        int currentAmount = GetItemCount(itemData);
        int newAmount = currentAmount + amount;

        globalInventory[itemData] = newAmount;

        if (showDebugLogs)
        {
            Debug.Log($"[InventoryManager] '{itemData.itemName}' {amount}개 추가, (현재 총: {newAmount}개)");
        }

        GameMessageBus.Publish(new InventoryChangedMessage(itemData, amount));
        return true;
    }

    /// <summary>
    /// 특정 아이템을 지정된 개수만큼 제거
    /// </summary>
    /// <param name="itemData">제거할 아이템 데이터</param>
    /// <param name="amount">제거할 수량</param>
    /// <returns>제거 성공 여부</returns>
    public bool RemoveItem(ItemData itemData, int amount = 1)
    {
        if (itemData == null || amount <= 0)
        {
            return false;
        }

        int currentAmount = GetItemCount(itemData);
        if (currentAmount < amount)
        {
            if (showDebugLogs)
            {
                Debug.LogWarning($"[InventoryManager] '{itemData.itemName}' 제거 실패. 보유: {currentAmount}, 요청: {amount}");
            }
            return false;
        }

        int newAmount = currentAmount - amount;

        if (newAmount == 0)
        {
            globalInventory.Remove(itemData);
            if (showDebugLogs)
            {
                Debug.Log($"[InventoryManager] '{itemData.itemName}' 모두 소진됨");
            }
        }
        else
        {
            globalInventory[itemData] = newAmount;
            if (showDebugLogs)
            {
                Debug.Log($"[InventoryManager] '{itemData.itemName}' {amount}개 사용. (남은 수량: {newAmount})");
            }
        }

        GameMessageBus.Publish(new InventoryChangedMessage(itemData, -amount));
        return true;
    }

    /// <summary>
    /// 인벤토리에서 레시피에 필요한 재료를 모두 제거
    /// 내부적으로 HasItems로 충분한지 먼저 확인한 뒤 제거
    /// </summary>
    /// <param name="requiredMaterials">제거할 재료 목록</param>
    /// <returns>모든 재료 제거 성공 여부</returns>
    public bool RemoveItems(List<ResourceCost> requiredMaterials)
    {
        if (requiredMaterials == null)
        {
            return true;
        }

        if (!HasItems(requiredMaterials))
        {
            if (showDebugLogs)
            {
                Debug.LogWarning("[InventoryManager] 재료가 부족하여 아이템을 제거할 수 없습니다.");
            }
            return false;
        }

        foreach (var cost in requiredMaterials)
        {
            RemoveItem(cost.item, cost.amount);
        }
        return true;
    }

    /// <summary>인벤토리에 가용(예약 제외) 음식이 하나라도 있는지 여부.</summary>
    public bool HasAnyFood()
    {
        foreach (var kv in globalInventory)
        {
            if (kv.Key != null && kv.Key.isFood && GetAvailableAmount(kv.Key) > 0)
                return true;
        }
        return false;
    }

    /// <summary>인벤토리의 음식(isFood) 아이템 가용(예약 제외) 총량을 반환합니다.</summary>
    public int GetTotalFoodCount()
    {
        int total = 0;
        foreach (var kv in globalInventory)
        {
            if (kv.Key != null && kv.Key.isFood)
                total += GetAvailableAmount(kv.Key);
        }
        return total;
    }

    /// <summary>인벤토리에 가용(예약 제외) 약물이 하나라도 있는지 여부.</summary>
    public bool HasAnyDrug()
    {
        foreach (var kv in globalInventory)
        {
            if (kv.Key != null && kv.Key.isDrug && GetAvailableAmount(kv.Key) > 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 인벤토리에서 약물(isDrug) 아이템 1종을 amount만큼 꺼냅니다(차감).
    /// 직원 오락(복용)에 사용 — 성공 시 꺼낸 ItemData, 없으면 null.
    /// TakeAnyFood와 동일 패턴. 추후 '정책' 시스템으로 개인 소지 확장 시에도
    /// 이 메서드로 출고한 뒤 소지 슬롯에 넣는 흐름을 유지합니다.
    /// </summary>
    public ItemData TakeAnyDrug(int amount = 1)
    {
        if (amount <= 0) return null;

        ItemData found = null;
        foreach (var kv in globalInventory)
        {
            if (kv.Key != null && kv.Key.isDrug && GetAvailableAmount(kv.Key) >= amount)
            {
                found = kv.Key;
                break;
            }
        }

        if (found != null) RemoveItem(found, amount);
        return found;
    }

    /// <summary>
    /// 인벤토리에서 음식(isFood) 아이템 1종을 amount만큼 꺼냅니다(차감).
    /// 직원 식량 지급에 사용 — 성공 시 꺼낸 ItemData, 없으면 null.
    /// 예약분을 제외한 가용 수량 기준으로 판단합니다.
    /// </summary>
    public ItemData TakeAnyFood(int amount = 1)
    {
        if (amount <= 0) return null;

        ItemData found = null;
        foreach (var kv in globalInventory)
        {
            if (kv.Key != null && kv.Key.isFood && GetAvailableAmount(kv.Key) >= amount)
            {
                found = kv.Key;
                break;
            }
        }

        if (found != null) RemoveItem(found, amount);
        return found;
    }

    /// <summary>
    /// 인벤토리를 완전히 비움 (예약 포함).
    /// </summary>
    public void ClearInventory()
    {
        globalInventory.Clear();
        reservedAmounts.Clear();
        reservations.Clear();
        if (showDebugLogs)
        {
            Debug.Log("[InventoryManager] 인벤토리 및 예약이 초기화되었습니다.");
        }
        GameMessageBus.Publish(InventoryChangedMessage.Refresh);
    }

    #endregion

    #region 자원 확인

    /// <summary>
    /// 특정 아이템의 보유 수량을 반환
    /// 실제 사용 가능 수량은 <see cref="GetAvailableAmount"/>를 사용
    /// </summary>
    /// <param name="itemData">조회할 아이템 데이터</param>
    /// <returns>보유 수량</returns>
    public int GetItemCount(ItemData itemData)
    {
        if (itemData == null)
        {
            return 0;
        }
        return globalInventory.TryGetValue(itemData, out int count) ? count : 0;
    }

    /// <summary>
    /// 특정 아이템이 지정된 개수만큼 있는지 확인 (예약 고려 X).
    /// </summary>
    /// <param name="item">확인할 아이템</param>
    /// <param name="amount">필요 수량</param>
    /// <returns>충분한지 여부</returns>
    public bool HasItem(ItemData item, int amount = 1)
    {
        return GetItemCount(item) >= amount;
    }

    /// <summary>
    /// 인벤토리에 재료가 모두 있는지 확인 (예약 고려 X).
    /// 예약분을 고려하려면 <see cref="HasAvailableItems"/>를 사용
    /// </summary>
    /// <param name="requiredMaterials">필요한 재료 목록</param>
    /// <returns>모든 재료가 충분한지 여부</returns>
    public bool HasItems(List<ResourceCost> requiredMaterials)
    {
        if (requiredMaterials == null) return true;

        foreach (var cost in requiredMaterials)
        {
            if (GetItemCount(cost.item) < cost.amount)
            {
                return false;
            }
        }
        return true;
    }

    #endregion

    #region 자원 예약 시스템

    /// <summary>
    /// 실제 사용 가능한 수량을 반환 (보유량 - 예약량)
    /// 제작/건설 가능 여부를 판단할 때 이 메서드를 사용
    /// </summary>
    /// <param name="item">조회할 아이템</param>
    /// <returns>사용 가능 수량</returns>
    public int GetAvailableAmount(ItemData item)
    {
        if (item == null)
        {
            return 0;
        }
        int total = GetItemCount(item);
        int reserved = reservedAmounts.TryGetValue(item, out int r) ? r : 0;
        return total - reserved;
    }

    /// <summary>
    /// 사용 가능한 수량 기준으로 재료가 모두 있는지 확인 (예약분 제외)
    /// </summary>
    /// <param name="requiredMaterials">필요한 재료 목록</param>
    /// <returns>모든 재료가 사용 가능한지 여부</returns>
    public bool HasAvailableItems(List<ResourceCost> requiredMaterials)
    {
        if (requiredMaterials == null) return true;

        foreach (var cost in requiredMaterials)
        {
            if (GetAvailableAmount(cost.item) < cost.amount)
                return false;
        }
        return true;
    }

    /// <summary>
    /// 자원 예약을 시도
    /// 성공 시 양수 reservationId를 반환하고, 실패 시 -1을 반환
    /// 예약된 자원은 다른 곳에서 사용할 수 없게 됨
    /// </summary>
    /// <param name="costs">예약할 자원 비용 목록</param>
    /// <returns>예약 ID (성공 시 양수, 실패 시 -1)</returns>
    public int TryReserve(List<ResourceCost> costs)
    {
        if (costs == null || costs.Count == 0)
        {
            return 0; // 비용 없음 → 예약 불필요, 팬텀 ID 생성 방지
        }

        // 모든 자원이 사용 가능한지 확인
        foreach (var cost in costs)
        {
            if (GetAvailableAmount(cost.item) < cost.amount)
            {
                if (showDebugLogs)
                {
                    Debug.LogWarning($"[InventoryManager] 예약 실패: {cost.item.itemName} 필요 {cost.amount}, 사용가능 {GetAvailableAmount(cost.item)}");
                }
                return -1;
            }
        }

        // 예약 등록
        int reservationId = nextReservationId++;
        reservations[reservationId] = new List<ResourceCost>(costs);

        foreach (var cost in costs)
        {
            if (!reservedAmounts.ContainsKey(cost.item))
            {
                reservedAmounts[cost.item] = 0;
            }
            reservedAmounts[cost.item] += cost.amount;
        }

        if (showDebugLogs)
        {
            Debug.Log($"[InventoryManager] 예약 #{reservationId} 생성 ({costs.Count}종 자원)");
        }

        GameMessageBus.Publish(InventoryChangedMessage.Refresh);
        return reservationId;
    }

    /// <summary>
    /// 예약된 자원을 실제로 소모합니다 (작업 완료 시 호출).
    /// 예약을 해제하고 인벤토리에서 실제로 제거합니다.
    /// </summary>
    /// <param name="reservationId">소모할 예약 ID</param>
    /// <returns>소모 성공 여부</returns>
    public bool ConsumeReservation(int reservationId)
    {
        if (!reservations.TryGetValue(reservationId, out var costs))
        {
            if (showDebugLogs)
            {
                Debug.LogWarning($"[InventoryManager] 존재하지 않는 예약 #{reservationId} 소모 시도");
            }
            return false;
        }

        foreach (var cost in costs)
        {
            // 예약량 차감
            if (reservedAmounts.ContainsKey(cost.item))
            {
                reservedAmounts[cost.item] -= cost.amount;
                if (reservedAmounts[cost.item] <= 0)
                {
                    reservedAmounts.Remove(cost.item);
                }
            }

            // 실제 인벤토리에서 제거
            RemoveItem(cost.item, cost.amount);
        }

        reservations.Remove(reservationId);

        if (showDebugLogs)
        {
            Debug.Log($"[InventoryManager] 예약 #{reservationId} 소모 완료");
        }

        return true;
    }
    
    /// <summary>
    /// 예약을 취소 (작업 취소 시 호출)
    /// 실제 인벤토리는 변동 없이 예약만 해제 (자원 손실 없음)
    /// </summary>
    /// <param name="reservationId">취소할 예약 ID</param>
    public void CancelReservation(int reservationId)
    {
        if (!reservations.TryGetValue(reservationId, out var costs))
        {
            if (showDebugLogs)
            {
                Debug.LogWarning($"[InventoryManager] 존재하지 않는 예약 #{reservationId} 취소 시도");
            }
            return;
        }

        foreach (var cost in costs)
        {
            if (reservedAmounts.ContainsKey(cost.item))
            {
                reservedAmounts[cost.item] -= cost.amount;
                if (reservedAmounts[cost.item] <= 0)
                {
                    reservedAmounts.Remove(cost.item);
                }
            }
        }

        reservations.Remove(reservationId);

        if (showDebugLogs)
        {
            Debug.Log($"[InventoryManager] 예약 #{reservationId} 취소됨");
        }

        GameMessageBus.Publish(InventoryChangedMessage.Refresh);
    }

    /// <summary>
    /// 특정 아이템의 총 예약 수량을 반환
    /// </summary>
    /// <param name="item">조회할 아이템</param>
    /// <returns>예약된 총 수량</returns>
    public int GetReservedAmount(ItemData item)
    {
        if (item == null)
        {
            return 0;
        }
        return reservedAmounts.TryGetValue(item, out int r) ? r : 0;
    }

    /// <summary>
    /// 예약이 유효한지 확인
    /// </summary>
    /// <param name="reservationId">확인할 예약 ID</param>
    /// <returns>유효 여부</returns>
    public bool IsReservationValid(int reservationId)
    {
        return reservations.ContainsKey(reservationId);
    }

    #endregion

    #region 조회

    /// <summary>
    /// 현재 인벤토리의 모든 아이템 목록을 반환
    /// </summary>
    /// <returns>아이템-수량 쌍의 리스트</returns>
    public List<KeyValuePair<ItemData, int>> GetAllItems()
    {
        return globalInventory.ToList();
    }

    /// <summary>
    /// 인벤토리에 있는 아이템의 총 개수를 반환
    /// </summary>
    /// <returns>전체 아이템 수량 합계</returns>
    public int GetTotalItemCount()
    {
        int total = 0;
        foreach (var kvp in globalInventory)
        {
            total += kvp.Value;
        }
        return total;
    }

    /// <summary>
    /// 인벤토리에 있는 아이템 종류의 개수를 반환
    /// </summary>
    /// <returns>고유 아이템 종류 수</returns>
    public int GetUniqueItemCount()
    {
        return globalInventory.Count;
    }

    #endregion

    #region 디버그

    /// <summary>
    /// 디버그용: 인벤토리 내용을 콘솔에 출력
    /// </summary>
    [ContextMenu("Print Inventory")]
    public void PrintInventory()
    {
        if (globalInventory.Count == 0)
        {
            Debug.Log("[InventoryManager] 인벤토리가 비어있습니다.");
            return;
        }

        string inventoryLog = "[InventoryManager] 현재 인벤토리:\n";
        foreach (var kvp in globalInventory)
        {
            int reserved = GetReservedAmount(kvp.Key);
            string reservedInfo = reserved > 0 ? $" (예약: {reserved})" : "";
            inventoryLog += $"- {kvp.Key.itemName}: {kvp.Value}개{reservedInfo}\n";
        }
        Debug.Log(inventoryLog);
    }

    #endregion

    #region ISaveModule 구현

    public int SaveOrder => 20;

    public void Capture(SaveData data)
    {
        data.inventory = new InventorySaveData();

        // 아이템 보유량
        foreach (var kvp in globalInventory)
        {
            data.inventory.items.Add(new ItemStackSaveData
            {
                itemId = kvp.Key.itemID,
                amount = kvp.Value
            });
        }

        // 예약 데이터
        data.inventory.nextReservationId = nextReservationId;
        foreach (var kvp in reservations)
        {
            var resSave = new ReservationSaveData { reservationId = kvp.Key };
            foreach (var cost in kvp.Value)
            {
                resSave.reservedItems.Add(new ItemStackSaveData
                {
                    itemId = cost.item.itemID,
                    amount = cost.amount
                });
            }
            data.inventory.reservations.Add(resSave);
        }
    }

    public void Restore(SaveData data)
    {
        if (data.inventory == null) return;

        var db = GameDatabase.Instance;
        if (db == null)
        {
            Debug.LogError("[InventoryManager] GameDatabase가 없어 인벤토리 복원 불가");
            return;
        }

        ClearInventory();

        // 아이템 복원
        foreach (var stack in data.inventory.items)
        {
            ItemData item = db.GetItemData(stack.itemId);
            if (item != null)
            {
                globalInventory[item] = stack.amount;
            }
        }

        // 예약 복원
        nextReservationId = data.inventory.nextReservationId;
        foreach (var resSave in data.inventory.reservations)
        {
            var costs = new List<ResourceCost>();
            foreach (var stack in resSave.reservedItems)
            {
                ItemData item = db.GetItemData(stack.itemId);
                if (item != null)
                {
                    costs.Add(new ResourceCost { item = item, amount = stack.amount });
                }
            }

            if (costs.Count > 0)
            {
                reservations[resSave.reservationId] = costs;
                foreach (var cost in costs)
                {
                    if (!reservedAmounts.ContainsKey(cost.item))
                        reservedAmounts[cost.item] = 0;
                    reservedAmounts[cost.item] += cost.amount;
                }
            }
        }

        if (showDebugLogs)
        {
            Debug.Log($"[InventoryManager] 복원 완료: {globalInventory.Count}개 아이템, {reservations.Count}개 예약");
        }

        GameMessageBus.Publish(InventoryChangedMessage.Refresh);
    }

    public void PostRestore(SaveData data) { }

    #endregion

    #region 저장/불러오기 (레거시)

    /// <summary>
    /// 저장을 위한 인벤토리 직렬화 데이터 구조
    /// </summary>
    [System.Serializable]
    public class InventoryData
    {
        public List<int> itemIds = new List<int>();
        public List<int> itemCounts = new List<int>();
        public List<ReservationData> activeReservations = new List<ReservationData>();
        public int savedNextReservationId = 1;
    }

    /// <summary>
    /// 개별 예약의 직렬화 데이터 구조
    /// </summary>
    [System.Serializable]
    public class ReservationData
    {
        public int reservationId;
        public List<int> itemIds = new List<int>();
        public List<int> amounts = new List<int>();
    }

    /// <summary>
    /// 저장을 위해 인벤토리 데이터를 직렬화
    /// 아이템 보유량과 활성 예약 정보를 모두 포함
    /// </summary>
    /// <returns>직렬화된 인벤토리 데이터</returns>
    public InventoryData GetSaveData()
    {
        InventoryData data = new InventoryData();
        foreach (var kvp in globalInventory)
        {
            data.itemIds.Add(kvp.Key.itemID);
            data.itemCounts.Add(kvp.Value);
        }

        // 예약 데이터 저장
        data.savedNextReservationId = nextReservationId;
        foreach (var kvp in reservations)
        {
            var rd = new ReservationData { reservationId = kvp.Key };
            foreach (var cost in kvp.Value)
            {
                rd.itemIds.Add(cost.item.itemID);
                rd.amounts.Add(cost.amount);
            }
            data.activeReservations.Add(rd);
        }

        return data;
    }

    /// <summary>
    /// 저장된 데이터로부터 인벤토리를 복원
    /// 아이템 보유량과 활성 예약 정보를 모두 복원
    /// </summary>
    /// <param name="data">복원할 인벤토리 데이터</param>
    /// <param name="itemDatabase">아이템 ID → ItemData 변환용 데이터베이스</param>
    public void LoadSaveData(InventoryData data, ItemDatabase itemDatabase)
    {
        if (data == null || itemDatabase == null)
        {
            return;
        }

        ClearInventory();

        for (int i = 0; i < data.itemIds.Count; i++)
        {
            ItemData item = itemDatabase.GetItemByID(data.itemIds[i]);
            if (item != null)
            {
                globalInventory[item] = data.itemCounts[i];
            }
        }

        // 예약 데이터 복원
        nextReservationId = data.savedNextReservationId;
        foreach (var rd in data.activeReservations)
        {
            var costs = new List<ResourceCost>();
            for (int i = 0; i < rd.itemIds.Count; i++)
            {
                ItemData item = itemDatabase.GetItemByID(rd.itemIds[i]);
                if (item != null)
                {
                    costs.Add(new ResourceCost { item = item, amount = rd.amounts[i] });
                }
            }

            if (costs.Count > 0)
            {
                reservations[rd.reservationId] = costs;
                foreach (var cost in costs)
                {
                    if (!reservedAmounts.ContainsKey(cost.item))
                    {
                        reservedAmounts[cost.item] = 0;
                    }
                    reservedAmounts[cost.item] += cost.amount;
                }
            }
        }

        if (showDebugLogs)
        {
            Debug.Log($"[InventoryManager] {data.itemIds.Count}개 아이템, {reservations.Count}개 예약 불러오기 완료");
        }
    }

    #endregion
}

/// <summary>
/// 아이템 데이터베이스 인터페이스 (아이템 ID로 ItemData를 조회)
/// </summary>
public interface ItemDatabase
{
    ItemData GetItemByID(int id);
}
