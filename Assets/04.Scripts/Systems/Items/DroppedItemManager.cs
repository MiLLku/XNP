using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 바닥에 떨어진 재료 아이템을 추적하고 Haul(운반) 작업을 자동 생성하는 싱글톤.
///
/// 흐름:
///   1. DroppedItem.Start() → Register(item)
///   2. Register → GetOrCreateHaulOrder() → HaulOrder 태스크를 WorkOrder 큐에 추가
///   3. 직원 AI가 WorkType.Hauling 작업을 자동 픽업 → HaulOrder 실행
///   4. EmployeeWork이 픽업 + 창고 배달 처리
///   5. DroppedItem.OnDestroy() → Unregister(item)
///
/// 저장/복원:
///   Haul WorkOrder는 저장하지 않습니다.
///   DroppedItemSaveModule이 아이템 위치를 저장하고 복원 시 재스폰하면
///   DroppedItem.Start()가 다시 Register()를 호출하므로 자동으로 HaulOrder가 재생성됩니다.
/// </summary>
public class DroppedItemManager : DestroySingleton<DroppedItemManager>
{
    #region 설정

    [Header("드롭 아이템 프리팹")]
    [Tooltip("DroppedItem 컴포넌트가 붙은 프리팹. null이면 코드로 생성합니다.")]
    [SerializeField] private GameObject droppedItemPrefab;

    [Header("비주얼")]
    [Tooltip("원형 스프라이트 (Unity 내장 'Knob' 등). null이면 스프라이트 없이 색상만 표시.")]
    [SerializeField] private Sprite circleSprite;

    [Header("Haul 작업물 설정")]
    [SerializeField] private int haulMaxWorkers = 10;

    #endregion

    #region 내부 상태

    private readonly List<DroppedItem> _items = new();

    /// <summary>WorkSystemManager가 관리하는 전역 Haul WorkOrder</summary>
    private WorkOrder _haulOrder;

    // 풀링은 PoolManager가 통합 관리 (이전 _inactivePool은 제거됨)

    #endregion

    #region 등록 / 해제

    /// <summary>
    /// DroppedItem이 Start()에서 호출합니다.
    /// 아이템을 추적 목록에 추가하고 HaulOrder 태스크를 생성합니다.
    /// </summary>
    public void Register(DroppedItem item)
    {
        if (item == null || _items.Contains(item)) return;
        _items.Add(item);

        CreateHaulTask(item);
        Debug.Log($"[DroppedItemManager] 아이템 등록: {item.itemData?.itemName} × {item.quantity} @ {item.transform.position}");
    }

    /// <summary>
    /// DroppedItem이 OnDestroy()에서 호출합니다.
    /// </summary>
    public void Unregister(DroppedItem item)
    {
        _items.Remove(item);
    }

/// <summary>
    /// DroppedItem을 풀로 반환합니다. DroppedItem.Remove()에서 호출됩니다.
    /// 풀이 가득 차면 실제로 Destroy합니다.
    /// </summary>
public void Despawn(DroppedItem item)
    {
        if (item == null || item.gameObject == null) return;

        Unregister(item);

        if (PoolManager.instance != null)
            PoolManager.instance.Despawn(item.gameObject);
        else
            Destroy(item.gameObject);
    }

    /// <summary>
    /// 이미 등록된 DroppedItem인지 확인합니다. 씬 배치 폴백 등록의 중복을 막기 위해 사용합니다.
    /// </summary>
    public bool IsRegistered(DroppedItem item)
    {
        return item != null && _items.Contains(item);
    }

    /// <summary>
    /// 지정 위치에서 radius 내 사용 가능한 DroppedItem 목록을 거리순으로 최대 maxCount개 반환합니다.
    /// 다중 픽업(carryCapacity)에 사용됩니다. exclude로 첫 픽업한 아이템을 제외할 수 있습니다.
    /// </summary>
    public List<DroppedItem> GetNearbyAvailableItems(Vector2 from, float radius, int maxCount, DroppedItem exclude = null)
    {
        if (maxCount <= 0) return new List<DroppedItem>();

        return _items
            .Where(i => i != null && i != exclude && i.IsAvailable &&
                        Vector2.Distance(from, (Vector2)i.transform.position) <= radius)
            .OrderBy(i => Vector2.Distance(from, (Vector2)i.transform.position))
            .Take(maxCount)
            .ToList();
    }


    #endregion

    #region 스폰

    /// <summary>
    /// 지정 위치에 DroppedItem을 스폰합니다.
    /// MiningOrder.DropMinedResource()에서 호출합니다.
    /// </summary>
public DroppedItem SpawnItem(ItemData data, int qty, Vector3 worldPos)
    {
        if (data == null) return null;

        // 사용할 prefab 우선순위:
        //   1) ItemData.dropPrefab — 자원별 고유 비주얼
        //   2) 매니저 droppedItemPrefab — 공용 폴백
        //   3) CreateDroppedItemObject — 코드 생성 (풀링 안 됨)
        GameObject prefabToUse = data.dropPrefab != null ? data.dropPrefab : droppedItemPrefab;

        DroppedItem dropped = null;
        GameObject  obj     = null;

        if (prefabToUse != null && PoolManager.instance != null)
        {
            // PoolManager 통한 풀링 경로
            obj = PoolManager.instance.Spawn(prefabToUse, worldPos);
        }
        else if (prefabToUse != null)
        {
            // PoolManager 없을 때 — 풀링 없이 그냥 Instantiate
            obj = Instantiate(prefabToUse, worldPos, Quaternion.identity);
        }
        else
        {
            // 폴백: 코드로 GameObject 생성
            obj = CreateDroppedItemObject(data, worldPos);
        }

        if (obj == null) return null;

        // Resource prefab에는 DroppedItem이 없을 수 있으므로 보장
        dropped = obj.GetComponent<DroppedItem>();
        if (dropped == null) dropped = obj.AddComponent<DroppedItem>();

        dropped.Initialize(data, qty);
        Register(dropped);

        return dropped;
    }

    /// <summary>
    /// 프리팹 없을 때 DroppedItem GameObject를 코드로 생성합니다.
    /// </summary>
    private GameObject CreateDroppedItemObject(ItemData data, Vector3 pos)
    {
        GameObject obj = new GameObject($"Drop_{data.itemName}");
        obj.transform.position = pos;

        // SpriteRenderer — 원형 스프라이트
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = circleSprite != null ? circleSprite
                    : Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
        sr.color = Color.white;
        sr.sortingOrder = 3;

        // 트리거용 콜라이더 (옵션: 직원 도착 감지용)
        CircleCollider2D col = obj.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius   = 0.3f;

        return obj;
    }

    #endregion

    #region HaulOrder 생성

    /// <summary>
    /// 전역 Haul WorkOrder를 가져오거나 없으면 새로 생성합니다.
    /// </summary>
    private WorkOrder GetOrCreateHaulOrder()
    {
        // 이미 유효한 WorkOrder가 있으면 재사용
        if (_haulOrder != null && _haulOrder.isActive && !_haulOrder.IsCompleted())
            return _haulOrder;

        if (WorkSystemManager.instance == null) return null;

        _haulOrder = WorkSystemManager.instance.CreateWorkOrder(
            "운반 작업",
            WorkType.Hauling,
            haulMaxWorkers,
            priority: 3
        );
        // WorkType.Hauling은 IsAutoPickup = true (자동 픽업 카테고리)

        Debug.Log("[DroppedItemManager] Haul WorkOrder 생성");
        return _haulOrder;
    }

    /// <summary>
    /// 새 아이템에 대한 HaulOrder 태스크를 WorkOrder 큐에 추가합니다.
    /// </summary>
    private void CreateHaulTask(DroppedItem item)
    {
        if (WorkSystemManager.instance == null) return;

        WorkOrder order = GetOrCreateHaulOrder();
        if (order == null) return;

        HaulOrder haulTarget = new HaulOrder(item);
        WorkTask task = new WorkTask(haulTarget, taskPriority: 3);
        order.taskQueue.Enqueue(task);

        Debug.Log($"[DroppedItemManager] HaulOrder 태스크 추가: {item.itemData?.itemName} @ {item.transform.position}");
    }

    #endregion

    #region 쿼리

    /// <summary>
    /// 지정 위치에서 가장 가까운 사용 가능한(IsClaimed = false) DroppedItem을 반환합니다.
    /// </summary>
    public DroppedItem GetNearestAvailableItem(Vector2Int from)
    {
        return _items
            .Where(i => i != null && i.IsAvailable)
            .OrderBy(i => Vector2Int.Distance(from,
                new Vector2Int(
                    Mathf.FloorToInt(i.transform.position.x),
                    Mathf.FloorToInt(i.transform.position.y)
                )))
            .FirstOrDefault();
    }

    /// <summary>바닥에 운반 대기 중인 아이템 수.</summary>
    public int PendingItemCount => _items.Count(i => i != null && i.IsAvailable);

    #endregion

    #region 디버그

    [ContextMenu("Print Status")]
    public void PrintStatus()
    {
        Debug.Log($"[DroppedItemManager] 총 {_items.Count}개 아이템, 대기 {PendingItemCount}개");
        foreach (var item in _items)
        {
            if (item == null) continue;
            Debug.Log($"  - {item.itemData?.itemName} × {item.quantity} @ {item.transform.position} | Claimed={item.IsClaimed}");
        }
    }

    #endregion
}
