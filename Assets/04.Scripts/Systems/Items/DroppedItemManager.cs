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

    /// <summary>직원 스폰 메시지 구독 핸들</summary>
    private System.IDisposable _spawnSubscription;

    // 풀링은 PoolManager가 통합 관리 (이전 _inactivePool은 제거됨)

    #endregion

    #region 생명주기

    protected override void Awake()
    {
        base.Awake();
        // 바닥 아이템 저장 모듈이 씬에 없어 저장·불러오기 때 바닥 더미(운반 중 물건 포함)가 전부 사라졌다 — 매니저와 함께 둔다
        if (GetComponent<DroppedItemSaveModule>() == null && FindAnyObjectByType<DroppedItemSaveModule>() == null)
            gameObject.AddComponent<DroppedItemSaveModule>();
    }

    void Start()
    {
        // 새 직원이 생성될 때 기존 모든 DroppedItem과 충돌 무시 처리
        _spawnSubscription = GameMessageBus.Subscribe<EmployeeSpawnedMessage>(
            m => IgnoreCollisionsForNewEmployee(m.employee));
    }

    void OnDestroy()
    {
        _spawnSubscription?.Dispose();
        _spawnSubscription = null;
    }

    #endregion

    #region 등록 / 해제

    /// <summary>
    /// DroppedItem이 Start()에서 호출합니다.
    /// 아이템을 추적 목록에 추가하고 HaulOrder 태스크를 생성합니다.
    /// 다른 DroppedItem 및 모든 Employee와 물리 충돌을 무시하여
    /// 아이템이 한 자리에 겹치고, 직원이 자재 위를 통과해도 밀려나지 않게 합니다.
    /// </summary>
    public void Register(DroppedItem item)
    {
        if (item == null || _items.Contains(item)) return;

        // 다른 DroppedItem과 충돌 무시 (아이템끼리 중첩 가능)
        IgnoreCollisionsWithOthers(item);

        // 모든 Employee와 충돌 무시 (직원이 자재 위/옆을 통과해도 밀리지 않음)
        IgnoreCollisionsWithAllEmployees(item);

        _items.Add(item);

        CreateHaulTask(item);
        Debug.Log($"[DroppedItemManager] 아이템 등록: {item.itemData?.itemName} × {item.quantity} @ {item.transform.position}");
    }

    /// <summary>
    /// 새로 등록되는 DroppedItem과 기존 모든 DroppedItem 간 물리 충돌을 무시 처리합니다.
    /// </summary>
    private void IgnoreCollisionsWithOthers(DroppedItem newItem)
    {
        if (newItem == null) return;
        Collider2D newCol = newItem.Collider;
        if (newCol == null) return;

        for (int i = 0; i < _items.Count; i++)
        {
            DroppedItem other = _items[i];
            if (other == null || other == newItem) continue;
            Collider2D otherCol = other.Collider;
            if (otherCol == null) continue;
            Physics2D.IgnoreCollision(newCol, otherCol, true);
        }
    }

    /// <summary>
    /// 새로 등록되는 DroppedItem과 모든 Employee의 Collider 간 물리 충돌을 무시 처리합니다.
    /// (자재가 직원의 발에 밀려나거나 직원의 이동을 차단하지 않도록)
    /// </summary>
    private void IgnoreCollisionsWithAllEmployees(DroppedItem newItem)
    {
        if (newItem == null) return;
        Collider2D newCol = newItem.Collider;
        if (newCol == null) return;
        if (EmployeeManager.instance == null) return;

        foreach (var emp in EmployeeManager.instance.AllEmployees)
        {
            if (emp == null) continue;
            // Employee는 RequireComponent(Collider2D)이므로 1개 이상 있음 — 모두 무시 처리
            var empCols = emp.GetComponentsInChildren<Collider2D>();
            foreach (var ec in empCols)
            {
                if (ec == null) continue;
                Physics2D.IgnoreCollision(newCol, ec, true);
            }
        }
    }

    /// <summary>
    /// 새 Employee가 spawn될 때 기존 모든 DroppedItem과 충돌 무시 처리합니다.
    /// EmployeeSpawnedMessage 수신 시 호출됩니다.
    /// </summary>
    private void IgnoreCollisionsForNewEmployee(Employee employee)
    {
        if (employee == null) return;
        var empCols = employee.GetComponentsInChildren<Collider2D>();
        if (empCols == null || empCols.Length == 0) return;

        foreach (var item in _items)
        {
            if (item == null) continue;
            Collider2D itemCol = item.Collider;
            if (itemCol == null) continue;
            foreach (var ec in empCols)
            {
                if (ec == null) continue;
                Physics2D.IgnoreCollision(itemCol, ec, true);
            }
        }
    }

    /// <summary>
    /// DroppedItem이 OnDestroy()에서 호출합니다.
    /// </summary>
    public void Unregister(DroppedItem item)
    {
        _items.Remove(item);

        // 운반 말고 다른 경로(보급 출고·추가 픽업)로 사라진 더미의 운반 태스크가 남으면 대기 수가 부풀고,
        // 풀에서 재사용된 같은 오브젝트에 옛 태스크가 다시 붙는다 → 대기 중인 태스크를 같이 지운다
        if (_haulOrder?.taskQueue == null) return;
        foreach (var t in _haulOrder.taskQueue.PendingTasks.ToList())
            if (t.target is HaulOrder h && h.item == item) _haulOrder.taskQueue.CancelTask(t);
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

    private const float DROP_ICON_SIZE = 0.6f; // 바닥 아이템 그림의 최대 크기 (월드 단위)
    private static Sprite _fallbackCircle;

    /// <summary>아이콘도 원형 스프라이트도 없을 때 쓰는 흰 원 (한 번만 만든다)</summary>
    private static Sprite FallbackCircle()
    {
        if (_fallbackCircle != null) return _fallbackCircle;
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        float r = n * 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        _fallbackCircle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n / DROP_ICON_SIZE);
        return _fallbackCircle;
    }

    /// <summary>
    /// 프리팹 없을 때 DroppedItem GameObject를 코드로 생성합니다.
    /// </summary>
    private GameObject CreateDroppedItemObject(ItemData data, Vector3 pos)
    {
        GameObject obj = new GameObject($"Drop_{data.itemName}");
        obj.transform.position = pos;

        // SpriteRenderer — 지정 원형 스프라이트 → 아이템 아이콘(한 칸 안에 들어가게 축소) → 코드로 만든 원
        // (예전 폴백 'UI/Skin/Knob.psd'는 에디터 전용 내장 리소스라 찾지 못해 오류만 냈다)
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = circleSprite != null ? circleSprite : data.itemIcon != null ? data.itemIcon : FallbackCircle();
        sr.color = Color.white;
        sr.sortingOrder = 3;
        float size = Mathf.Max(sr.sprite.bounds.size.x, sr.sprite.bounds.size.y);
        if (size > DROP_ICON_SIZE) obj.transform.localScale = Vector3.one * (DROP_ICON_SIZE / size);

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
