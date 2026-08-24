using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 건설 현장 (청사진).
/// 배치된 후 직원이 작업을 완료하면 실제 건물로 변환됩니다.
///
/// 상태 흐름:
///   Blueprint → InProgress → Completed (→ 실제 건물 생성 → 건설 현장 제거)
///
/// 예약 시스템:
///   - ConstructionManager에서 배치 시 자원 예약 ID를 받음
///   - 완료 시 ConsumeReservation으로 실제 소모
///   - 취소 시 CancelReservation으로 예약 해제 (자원 손실 없음)
/// </summary>
[RequireComponent(typeof(SpriteRenderer), typeof(BoxCollider2D))]
public class ConstructionSite : MonoBehaviour, IMaterialReceiver
{
    #region 상수

    /// <summary>청사진 스프라이트의 정렬 순서</summary>
    private const int BLUEPRINT_SORTING_ORDER = 5;

    #endregion

    #region 필드 및 설정

    [Header("건설 정보")]
    [SerializeField] private BuildingData buildingData;
    [SerializeField] private Vector3Int gridPosition; // 왼쪽 아래 기준

    [Header("상태")]
    [SerializeField] private ConstructionState state = ConstructionState.Blueprint;
    [Tooltip("진행 비율 (0~1). accumulatedWork에서 파생되는 표시용 값")]
    [SerializeField] private float constructionProgress = 0f;

    [Tooltip("누적된 작업량. 직원이 떠나도 남으며 다른 직원이 이어받는다")]
    [SerializeField] private float accumulatedWork = 0f;

    [Header("시각 설정")]
    [SerializeField] private Color blueprintColor = new Color(0.5f, 0.8f, 1f, 0.5f);
    [SerializeField] private Color inProgressColor = new Color(1f, 0.9f, 0.5f, 0.7f);

    // 컴포넌트
    private SpriteRenderer spriteRenderer;
    private BoxCollider2D boxCollider;

    // 작업 관련
    private WorkOrder workOrder;
    private BuildOrder buildOrder;

    /// <summary>
    /// InventoryManager에서 발급받은 자원 예약 ID.
    /// -1이면 예약 없음. 완료 시 ConsumeReservation으로 소모됨.
    /// </summary>
    private int reservationId = -1;

    /// <summary>런타임 고유 ID (세이브/로드 및 크로스레퍼런스용)</summary>
    private int _instanceId = -1;

    /// <summary>타일 해제 완료 여부 (이중 해제 방지)</summary>
    private bool _tilesReleased = false;

    // ── 자재 운반(출고) 상태 ──────────────────────────────────────────────
    [Header("자재 운반 설정")]
    [Tooltip("자재 운반 작업물 최대 동시 작업자 수")]
    [SerializeField] private int withdrawMaxWorkers = 3;
    [Tooltip("자재 운반 작업물 우선순위 (낮을수록 우선)")]
    [SerializeField] private int withdrawPriority = 3;

    /// <summary>아직 도착하지 않은 자재 (모두 0이 되면 IsMaterialsReady=true)</summary>
    private readonly Dictionary<ItemData, int> _pendingMaterials = new Dictionary<ItemData, int>();

    /// <summary>이미 도착한 자재 (취소 시 환불 드롭으로 사용)</summary>
    private readonly Dictionary<ItemData, int> _deliveredMaterials = new Dictionary<ItemData, int>();

    /// <summary>자재 운반 WorkOrder (모두 도착 후 자동 정리)</summary>
    private WorkOrder _withdrawWorkOrder;

    /// <summary>모든 자재가 사이트에 도착했는지 여부</summary>
    public bool IsMaterialsReady { get; private set; } = false;

    #endregion

    #region 상태 열거형

    /// <summary>건설 현장의 상태</summary>
    public enum ConstructionState
    {
        /// <summary>청사진 (배치됨, 작업 대기)</summary>
        Blueprint,
        /// <summary>건설 중</summary>
        InProgress,
        /// <summary>완료됨</summary>
        Completed
    }

    #endregion

    #region 프로퍼티

    /// <summary>건설 완료 여부</summary>
    public bool IsCompleted => state == ConstructionState.Completed;

    /// <summary>건물 데이터</summary>
    public BuildingData BuildingData => buildingData;

    /// <summary>그리드 위치 (왼쪽 아래 기준)</summary>
    public Vector3Int GridPosition => gridPosition;

    /// <summary>현재 건설 상태</summary>
    public ConstructionState State => state;

    /// <summary>건설 진행도 (0~1)</summary>
    public float Progress => constructionProgress;

    /// <summary>할당된 작업 주문</summary>
    public WorkOrder WorkOrder => workOrder;

    /// <summary>런타임 고유 ID</summary>
    public int InstanceId => _instanceId;

    /// <summary>자원 예약 ID</summary>
    public int ReservationId => reservationId;

    #endregion

    #region 초기화

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        boxCollider = GetComponent<BoxCollider2D>();
    }

    /// <summary>
    /// 건설 현장을 초기화합니다.
    /// 스프라이트, 콜라이더, 타일 점유, 작업 주문을 설정합니다.
    /// </summary>
    /// <param name="data">건물 데이터</param>
    /// <param name="gridPos">배치할 그리드 좌표</param>
    public void Initialize(BuildingData data, Vector3Int gridPos)
    {
        buildingData = data;
        gridPosition = gridPos;
        state = ConstructionState.Blueprint;
        constructionProgress = 0f;
        accumulatedWork = 0f;

        gameObject.name = $"ConstructionSite_{data.buildingName}_{gridPos.x}_{gridPos.y}";

        SetupVisuals();
        SetupCollider();
        OccupyTiles();
        CreateWorkOrder();

        // instanceId 발급 및 등록
        if (SaveManager.instance != null)
        {
            _instanceId = SaveManager.instance.GenerateInstanceId();
        }
        if (_instanceId >= 0 && RuntimeIDRegistry.instance != null)
        {
            RuntimeIDRegistry.instance.Register(_instanceId, this);
        }

        Debug.Log($"[ConstructionSite] 건설 현장 생성: {data.buildingName} at {gridPos}");
    }

    /// <summary>
    /// 자원 예약 ID를 설정하고 자재 운반(WithdrawOrder)을 요청합니다.
    /// (ConstructionManager에서 reservation 발급 직후 호출)
    /// </summary>
    /// <param name="id">자원 예약 ID</param>
    public void SetReservationId(int id)
    {
        reservationId = id;
        RequestMaterialDeliveries();
    }

    /// <summary>
    /// 필요한 자재마다 WithdrawOrder를 생성해 WorkSystemManager에 등록합니다.
    /// 자재가 필요 없는 건물은 즉시 IsMaterialsReady=true 처리합니다.
    /// </summary>
    private void RequestMaterialDeliveries()
    {
        _pendingMaterials.Clear();
        _deliveredMaterials.Clear();
        IsMaterialsReady = false;

        if (buildingData?.requiredResources == null || buildingData.requiredResources.Count == 0)
        {
            // 자재 없는 건물 → 즉시 시작 가능
            IsMaterialsReady = true;
            return;
        }

        foreach (var cost in buildingData.requiredResources)
        {
            if (cost.item == null || cost.amount <= 0) continue;
            _pendingMaterials[cost.item] = cost.amount;
        }

        if (_pendingMaterials.Count == 0)
        {
            IsMaterialsReady = true;
            return;
        }

        if (WorkSystemManager.instance == null)
        {
            Debug.LogError("[ConstructionSite] WorkSystemManager가 없어 자재 운반을 요청할 수 없습니다.");
            return;
        }

        _withdrawWorkOrder = WorkSystemManager.instance.CreateWorkOrder(
            $"건설 자재 운반: {buildingData.buildingName}",
            WorkType.Hauling,
            maxWorkers: withdrawMaxWorkers,
            priority:   withdrawPriority
        );

        foreach (var cost in buildingData.requiredResources)
        {
            if (cost.item == null || cost.amount <= 0) continue;
            var request = new MaterialRequest(cost.item, cost.amount, this, reservationId);
            var task    = new WithdrawOrder(request);
            _withdrawWorkOrder.AddTarget(task);
        }

        Debug.Log($"[ConstructionSite] '{buildingData.buildingName}' 자재 운반 요청 생성 ({_pendingMaterials.Count}종)");
    }

    #endregion

    #region 시각/물리 설정

    private void SetupVisuals()
    {
        if (buildingData.buildingPrefab != null)
        {
            SpriteRenderer prefabRenderer = buildingData.buildingPrefab.GetComponent<SpriteRenderer>();
            if (prefabRenderer != null)
            {
                spriteRenderer.sprite = prefabRenderer.sprite;
            }
        }

        spriteRenderer.color = blueprintColor;
        spriteRenderer.sortingOrder = BLUEPRINT_SORTING_ORDER;
    }

    private void SetupCollider()
    {
        Vector2 size = new Vector2(buildingData.size.x, buildingData.size.y);
        boxCollider.size = size;
        boxCollider.offset = new Vector2(size.x / 2f, size.y / 2f);
        boxCollider.isTrigger = true;
    }

    #endregion

    #region 타일 점유

    /// <summary>
    /// 건물 영역의 타일을 점유 상태로 표시합니다.
    /// </summary>
    private void OccupyTiles()
    {
        if (MapGenerator.instance == null) return;
        // 전선 등 오버레이 설치물은 점유 그리드를 사용하지 않습니다 (PowerWire 자체 레지스트리로 관리).
        if (buildingData != null && buildingData.allowOverlap) return;

        GameMap gameMap = MapGenerator.instance.GameMapInstance;

        // 청사진 단계: 건물의 실제 blocksMovement 값을 따라감.
        // Floor처럼 통행 불가 건물은 청사진 단계에서도 통행 불가로 처리해서
        // 직원이 미완성 사이트 위로 fall하지 않도록 한다 (이전엔 false 고정이라 직원이 그 위로 가서 fall).
        // 자재 운반 destination은 GetDeliveryPosition이 인접 발판 위치를 반환하므로 접근 가능.
        bool willBlockMovement = buildingData != null && buildingData.blocksMovement;

        for (int x = 0; x < buildingData.size.x; x++)
        {
            for (int y = 0; y < buildingData.size.y; y++)
            {
                int tileX = gridPosition.x + x;
                int tileY = gridPosition.y + y;
                gameMap.MarkTileOccupied(tileX, tileY, blocksMovement: willBlockMovement);
            }
        }
    }

    /// <summary>
    /// 건물 영역의 타일 점유를 해제합니다.
    /// </summary>
    private void ReleaseTiles()
    {
        if (_tilesReleased) return;
        _tilesReleased = true;
        if (MapGenerator.instance == null) return;
        // 오버레이 설치물은 점유한 적이 없으므로 해제도 불필요합니다 (기존 건물 점유를 건드리지 않도록).
        if (buildingData != null && buildingData.allowOverlap) return;

        GameMap gameMap = MapGenerator.instance.GameMapInstance;

        for (int x = 0; x < buildingData.size.x; x++)
        {
            for (int y = 0; y < buildingData.size.y; y++)
            {
                int tileX = gridPosition.x + x;
                int tileY = gridPosition.y + y;
                gameMap.UnmarkTileOccupied(tileX, tileY);
            }
        }
    }

    #endregion

    #region 작업 주문

    /// <summary>
    /// WorkSystemManager에 BuildOrder를 등록합니다.
    /// </summary>
    private void CreateWorkOrder()
    {
        if (WorkSystemManager.instance == null)
        {
            Debug.LogError("[ConstructionSite] WorkSystemManager가 없습니다!");
            return;
        }

        buildOrder = new BuildOrder
        {
            constructionSite = this,
            buildingData = buildingData,
            position = GetWorkPosition(),
            priority = 5,
            completed = false
        };

        // 건설은 자동 픽업 작업이므로 maxWorkers 제한이 적용되지 않음.
        // 단일 건설 현장에는 BuildOrder가 1개뿐이므로 자연스럽게 1명만 작업하게 됨.
        workOrder = WorkSystemManager.instance.CreateWorkOrder(
            $"건설: {buildingData.buildingName}",
            WorkType.Building,
            maxWorkers: 1,
            priority: 5
        );

        workOrder.AddTarget(buildOrder);

        Debug.Log($"[ConstructionSite] 작업물 생성 완료: {workOrder.orderName}");
    }

    /// <summary>
    /// 작업 위치를 반환합니다 (건물 왼쪽 아래 기준).
    /// </summary>
    /// <returns>작업 위치 (월드 좌표)</returns>
    public Vector3 GetWorkPosition()
    {
        return new Vector3(gridPosition.x, gridPosition.y, 0);
    }

    #endregion

    #region 건설 진행

    /// <summary>
    /// 건설 작업이 시작될 때 호출됩니다.
    /// 상태를 InProgress로 변경하고 시각 효과를 업데이트합니다.
    /// </summary>
    public void StartConstruction()
    {
        if (state != ConstructionState.Blueprint) return;

        state = ConstructionState.InProgress;
        spriteRenderer.color = inProgressColor;

        Debug.Log($"[ConstructionSite] 건설 시작: {buildingData.buildingName}");
    }

    /// <summary>완료에 필요한 총 작업량 (BuildingData.workAmount).</summary>
    public float WorkAmount => buildingData != null ? Mathf.Max(0.01f, buildingData.workAmount) : 5f;

    /// <summary>지금까지 누적된 작업량.</summary>
    public float AccumulatedWork => accumulatedWork;

    /// <summary>
    /// 작업량을 투입합니다. 직원이 초당 작업 속도만큼 넣습니다.
    ///
    /// 진행도는 <b>이 현장에 남습니다</b> — 직원이 밥을 먹으러 가거나 소집되어 떠나도
    /// 지금까지 한 작업은 사라지지 않고, 다른 직원이 이어받을 수 있습니다.
    /// </summary>
    public void AddWork(float amount)
    {
        if (amount <= 0f || state == ConstructionState.Completed) return;

        accumulatedWork = Mathf.Min(WorkAmount, accumulatedWork + amount);
        constructionProgress = Mathf.Clamp01(accumulatedWork / WorkAmount);
    }

    /// <summary>
    /// 누적 작업량을 되돌립니다 (침식 이상 행동 '작업 중단' 등).
    /// 완료된 현장은 영향을 받지 않으며 0 미만으로 내려가지 않습니다.
    /// </summary>
    public void ReduceWork(float amount)
    {
        if (amount <= 0f || state == ConstructionState.Completed) return;

        accumulatedWork = Mathf.Max(0f, accumulatedWork - amount);
        constructionProgress = WorkAmount > 0f ? Mathf.Clamp01(accumulatedWork / WorkAmount) : 0f;
    }

    /// <summary>
    /// 건설이 완료될 때 호출됩니다.
    /// 자재는 직원이 운반 시 InventoryManager에서 이미 차감되었으므로
    /// 예약은 ConsumeReservation이 아닌 CancelReservation으로 해제합니다.
    /// </summary>
    public void CompleteConstruction()
    {
        if (state == ConstructionState.Completed) return;

        state = ConstructionState.Completed;
        constructionProgress = 1f;
        accumulatedWork = WorkAmount;

        Debug.Log($"[ConstructionSite] 건설 완료, 실제 건물 생성: {buildingData.buildingName}");

        // 자재 차감은 직원이 Withdraw 시점에 이미 완료됨 → 예약 잠금만 해제
        if (reservationId >= 0 && InventoryManager.instance != null)
        {
            InventoryManager.instance.CancelReservation(reservationId);
            reservationId = -1;
        }

        // 자재 운반 WorkOrder는 정상 흐름이면 이미 모두 완료/제거되어 있지만 안전하게 정리
        if (_withdrawWorkOrder != null && WorkSystemManager.instance != null)
        {
            WorkSystemManager.instance.RemoveWorkOrder(_withdrawWorkOrder, isCancellation: false);
            _withdrawWorkOrder = null;
        }

        // 건설 현장 타일 해제 후 건물 생성 (이중 점유 방지)
        ReleaseTiles();
        SpawnBuilding();

        // 건물 안에 갇힌 직원을 인접 위치로 밀어냄 (SpawnBuilding 이후 GameMap이 갱신된 뒤 실행)
        SnapEmployeesOutOfFootprint();

        if (ConstructionManager.instance != null)
        {
            ConstructionManager.instance.OnConstructionCompleted(this);
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// 건설을 취소합니다.
    /// 예약 + 자재 운반 작업물 모두 취소합니다.
    /// 이미 직원이 운반해 도착한 자재는 사이트가 사라지면서 함께 손실됩니다(추후 환불 흐름 추가 가능).
    /// </summary>
    public void CancelConstruction()
    {
        Debug.Log($"[ConstructionSite] 건설 취소: {buildingData.buildingName}");

        // 자재 운반 WorkOrder 취소 (운반 중인 직원은 carryPile을 가장 가까운 창고로 반납)
        if (_withdrawWorkOrder != null && WorkSystemManager.instance != null)
        {
            WorkSystemManager.instance.RemoveWorkOrder(_withdrawWorkOrder, isCancellation: true);
            _withdrawWorkOrder = null;
        }

        // 이미 도착한 자재 환불 (DroppedItem으로 사이트 위치에 떨어뜨림 → 직원이 가까운 창고로 재운반)
        if (_deliveredMaterials.Count > 0)
        {
            Vector2Int siteSize = buildingData != null ? buildingData.size : Vector2Int.one;
            ItemRefundHelper.SpawnRefunds(_deliveredMaterials, transform.position, siteSize);
            _deliveredMaterials.Clear();
        }

        _pendingMaterials.Clear();
        IsMaterialsReady = false;

        // 예약 취소 (아직 출고되지 않은 자재의 잠금 해제)
        if (reservationId >= 0 && InventoryManager.instance != null)
        {
            InventoryManager.instance.CancelReservation(reservationId);
            Debug.Log($"[ConstructionSite] 자원 예약 #{reservationId} 취소됨");
            reservationId = -1;
        }

        if (workOrder != null && WorkSystemManager.instance != null)
        {
            WorkSystemManager.instance.RemoveWorkOrder(workOrder);
        }

        ReleaseTiles();
        Destroy(gameObject);
    }

    // ─── IMaterialReceiver ──────────────────────────────────────────────────

    public Vector3 GetDeliveryPosition()
    {
        // 사이트 footprint 자체는 보통 미완성이라 발판이 없음 → 직원이 그 위로 가면 fall.
        // 발판이 있는 인접 위치를 우선 탐색하고, 없으면 사이트 중앙 fallback.
        Vector2Int size = buildingData != null ? buildingData.size : new Vector2Int(1, 1);
        int cx = gridPosition.x + size.x / 2;
        int cy = gridPosition.y;

        var gameMap = MapGenerator.instance?.GameMapInstance;
        if (gameMap != null)
        {
            // 후보 우선순위: 사이트 좌/우 → 사이트 footprint 내부 → 위 → 아래
            var candidates = new System.Collections.Generic.List<Vector2Int>
            {
                new Vector2Int(gridPosition.x - 1, cy),                 // 좌측
                new Vector2Int(gridPosition.x + size.x, cy),            // 우측
                new Vector2Int(cx, cy),                                 // footprint 중앙
                new Vector2Int(cx, gridPosition.y + size.y),            // 위쪽
                new Vector2Int(cx, gridPosition.y - 1),                 // 아래쪽
            };

            foreach (var pos in candidates)
            {
                if (pos.x < 0 || pos.x >= GameMap.MAP_WIDTH) continue;
                if (pos.y < 0 || pos.y >= GameMap.MAP_HEIGHT) continue;

                // 해당 위치가 공기(또는 통행 가능)이고 발 아래에 발판 있어야 직원이 설 수 있음
                int tileId = gameMap.TileGrid[pos.x, pos.y];
                bool isWalkable = tileId == 0 && !gameMap.DoesTileBlockMovement(pos.x, pos.y);
                if (!isWalkable) continue;

                int groundY = pos.y - 1;
                bool hasGround = groundY >= 0 && (
                    gameMap.IsSolidGround(pos.x, groundY) ||
                    FloorTile.HasFloorTileAt(new Vector2Int(pos.x, groundY))
                );
                if (!hasGround) continue;

                return new Vector3(pos.x + 0.5f, pos.y, 0f);
            }
        }

        // fallback: 기존 동작 (사이트 중앙)
        return new Vector3(cx + 0.5f, cy, 0f);
    }

    public bool IsRequestStillValid()
    {
        return this != null && state != ConstructionState.Completed;
    }

    public void OnMaterialDelivered(ItemData itemData, int amount)
    {
        if (itemData == null || amount <= 0) return;

        // 도착 자재 누적 기록 (취소 시 환불용)
        if (_deliveredMaterials.TryGetValue(itemData, out int already))
            _deliveredMaterials[itemData] = already + amount;
        else
            _deliveredMaterials[itemData] = amount;

        if (_pendingMaterials.TryGetValue(itemData, out int remaining))
        {
            remaining -= amount;
            if (remaining <= 0) _pendingMaterials.Remove(itemData);
            else                _pendingMaterials[itemData] = remaining;

            Debug.Log($"[ConstructionSite] '{buildingData.buildingName}' 자재 도착: {itemData.itemName}×{amount} (남은 종류: {_pendingMaterials.Count})");
        }

        if (_pendingMaterials.Count == 0 && !IsMaterialsReady)
        {
            IsMaterialsReady = true;
            Debug.Log($"[ConstructionSite] '{buildingData.buildingName}' 모든 자재 도착 → 건설 시작 가능");

            // 자재 운반 WorkOrder는 모든 태스크 완료 시 자동 정리되지만, 명시적으로 해제
            if (_withdrawWorkOrder != null && WorkSystemManager.instance != null)
            {
                WorkSystemManager.instance.RemoveWorkOrder(_withdrawWorkOrder, isCancellation: false);
                _withdrawWorkOrder = null;
            }
        }
    }

    public void OnMaterialRequestFailed(ItemData itemData, int amount)
    {
        // 자재 운반 실패는 일시적일 수 있으니 사이트를 즉시 취소하지 않습니다.
        // (해당 WithdrawOrder 태스크는 큐에 남아 다른 직원이 재시도할 수 있음)
        Debug.LogWarning($"[ConstructionSite] '{buildingData?.buildingName}' 자재 운반 실패: {itemData?.itemName}×{amount} — 재시도 대기");
    }

    #endregion

    #region 직원 위치 보정

    /// <summary>
    /// 건설이 완료된 건물의 footprint 안에 있는 직원을 외부로 밀어냅니다.
    /// SpawnBuilding() 이후 GameMap이 최신 상태일 때 호출해야 합니다.
    /// </summary>
    private void SnapEmployeesOutOfFootprint()
    {
        if (EmployeeManager.instance == null) return;

        // 통행 가능 건물(바닥 타일, 다리 등)은 직원이 그 내부에 있어도 이동·착지에 지장이 없으므로
        // 옆으로 밀어낼 필요가 없습니다. 오히려 스냅하면 지면이 없는 위치로 이동해 낙하할 수 있습니다.
        if (!buildingData.blocksMovement) return;

        foreach (var emp in EmployeeManager.instance.AllEmployees)
        {
            if (emp == null) continue;

            var movement = emp.GetComponent<EmployeeMovement>();
            if (movement == null) continue;

            movement.SnapOutOfBuilding(gridPosition, buildingData.size);
        }
    }

    #endregion

    #region 건물 생성

    /// <summary>
    /// 실제 건물 프리팹을 인스턴스화합니다.
    /// </summary>
    private void SpawnBuilding()
    {
        if (buildingData.buildingPrefab == null)
        {
            Debug.LogError($"[ConstructionSite] buildingPrefab이 없습니다: {buildingData.buildingName}");
            ReleaseTiles();
            return;
        }

        Vector3 worldPos = new Vector3(gridPosition.x, gridPosition.y, 0);

        Transform parent = null;
        if (MapGenerator.instance != null && MapGenerator.instance.MapRendererInstance != null)
        {
            parent = MapGenerator.instance.MapRendererInstance.entityParent;
        }

        GameObject buildingObj = Instantiate(buildingData.buildingPrefab, worldPos, Quaternion.identity, parent);

        Building building = buildingObj.GetComponent<Building>();
        if (building != null)
        {
            building.Initialize(buildingData);
        }

        Debug.Log($"[ConstructionSite] 건물 생성 완료: {buildingData.buildingName} at {worldPos}");
    }

    #endregion

    #region 저장/복원

    /// <summary>
    /// 현재 상태를 저장 데이터로 변환합니다.
    /// 자재 운반 상태(pending/delivered/ready)도 함께 직렬화하여 복원 시 이어갈 수 있게 합니다.
    /// </summary>
    public ConstructionSiteSaveData CreateSaveData()
    {
        var data = new ConstructionSiteSaveData
        {
            instanceId = _instanceId,
            buildingDataId = buildingData.buildingID,
            gridX = gridPosition.x,
            gridY = gridPosition.y,
            state = (int)state,
            progress = constructionProgress,
            workOrderId = workOrder != null ? workOrder.orderId : -1,
            reservationId = reservationId,
            isMaterialsReady = IsMaterialsReady
        };

        foreach (var kv in _pendingMaterials)
        {
            if (kv.Key == null || kv.Value <= 0) continue;
            data.pendingMaterials.Add(new ItemStackSaveData
            {
                itemId = kv.Key.itemID,
                amount = kv.Value
            });
        }
        foreach (var kv in _deliveredMaterials)
        {
            if (kv.Key == null || kv.Value <= 0) continue;
            data.deliveredMaterials.Add(new ItemStackSaveData
            {
                itemId = kv.Key.itemID,
                amount = kv.Value
            });
        }

        return data;
    }

    /// <summary>
    /// 저장된 데이터로 건설 현장을 복원합니다.
    /// 작업 주문은 생성하지 않습니다 (WorkSystemManager에서 별도 복원).
    ///
    /// 자재 운반 상태(pending/delivered/ready)도 복원하며,
    /// 미도착 자재가 남아 있으면 WithdrawOrder를 재생성합니다 (직원이 운반 재개).
    /// </summary>
    public void RestoreFromSaveData(ConstructionSiteSaveData saveData, BuildingData data)
    {
        buildingData = data;
        gridPosition = new Vector3Int(saveData.gridX, saveData.gridY, 0);
        state = (ConstructionState)saveData.state;
        constructionProgress = saveData.progress;
        // 진행 비율에서 누적 작업량을 되살린다 (직원이 이어서 지을 수 있도록)
        accumulatedWork = Mathf.Clamp01(saveData.progress) * WorkAmount;
        reservationId = saveData.reservationId;
        _instanceId = saveData.instanceId;

        gameObject.name = $"ConstructionSite_{data.buildingName}_{gridPosition.x}_{gridPosition.y}";

        SetupVisuals();
        SetupCollider();
        OccupyTiles();

        // 상태에 따른 시각 업데이트
        if (state == ConstructionState.InProgress)
        {
            spriteRenderer.color = inProgressColor;
        }

        if (_instanceId >= 0 && RuntimeIDRegistry.instance != null)
        {
            RuntimeIDRegistry.instance.Register(_instanceId, this);
        }

        // ── 자재 운반 상태 복원 ──────────────────────────────────────────
        _pendingMaterials.Clear();
        _deliveredMaterials.Clear();
        IsMaterialsReady = saveData.isMaterialsReady;

        var db = GameDatabase.Instance;
        if (db != null)
        {
            if (saveData.pendingMaterials != null)
            {
                foreach (var s in saveData.pendingMaterials)
                {
                    ItemData itm = db.GetItemData(s.itemId);
                    if (itm != null && s.amount > 0)
                        _pendingMaterials[itm] = s.amount;
                }
            }
            if (saveData.deliveredMaterials != null)
            {
                foreach (var s in saveData.deliveredMaterials)
                {
                    ItemData itm = db.GetItemData(s.itemId);
                    if (itm != null && s.amount > 0)
                        _deliveredMaterials[itm] = s.amount;
                }
            }
        }

        // 미도착 자재가 남아있으면 WithdrawOrder 재생성 (운반 재개)
        if (!IsMaterialsReady && _pendingMaterials.Count > 0)
        {
            RecreateWithdrawOrdersFromPending();
        }
    }

    /// <summary>
    /// 복원 후 또는 외부 트리거로, 현재 _pendingMaterials 기준으로
    /// WithdrawOrder들을 다시 생성합니다 (직원이 운반 재개).
    /// _pendingMaterials는 클리어하지 않고 그대로 사용합니다.
    /// </summary>
    private void RecreateWithdrawOrdersFromPending()
    {
        if (_pendingMaterials.Count == 0 || IsMaterialsReady) return;
        if (WorkSystemManager.instance == null) return;

        _withdrawWorkOrder = WorkSystemManager.instance.CreateWorkOrder(
            $"건설 자재 운반(재개): {buildingData.buildingName}",
            WorkType.Hauling,
            maxWorkers: withdrawMaxWorkers,
            priority:   withdrawPriority
        );

        foreach (var kv in _pendingMaterials)
        {
            if (kv.Key == null || kv.Value <= 0) continue;
            var request = new MaterialRequest(kv.Key, kv.Value, this, reservationId);
            _withdrawWorkOrder.AddTarget(new WithdrawOrder(request));
        }

        Debug.Log($"[ConstructionSite] '{buildingData.buildingName}' 자재 운반 재개 ({_pendingMaterials.Count}종)");
    }

    /// <summary>
    /// 복원 시 WorkOrder를 외부에서 연결합니다 (WorkSystemManager.PostRestore에서 호출).
    /// </summary>
    public void SetWorkOrder(WorkOrder order)
    {
        workOrder = order;
    }

    #endregion

    #region 생명주기

    void OnDestroy()
    {
        if (_instanceId >= 0 && RuntimeIDRegistry.instance != null)
        {
            RuntimeIDRegistry.instance.Unregister(_instanceId);
        }
    }

    #endregion

    #region 마우스 상호작용

    /// <summary>
    /// 클릭 시 작업 할당 UI를 엽니다.
    /// </summary>
    void OnMouseDown()
    {
        if (state == ConstructionState.Completed) return;

        if (workOrder != null && WorkSystemManager.instance != null)
        {
            WorkSystemManager.instance.ShowAssignmentUI(workOrder, null);
        }
    }

    void OnMouseEnter()
    {
        if (state == ConstructionState.Completed) return;

        Color hoverColor = spriteRenderer.color;
        hoverColor.a = Mathf.Min(1f, hoverColor.a + 0.2f);
        spriteRenderer.color = hoverColor;
    }

    void OnMouseExit()
    {
        if (state == ConstructionState.Completed) return;

        spriteRenderer.color = (state == ConstructionState.InProgress) ? inProgressColor : blueprintColor;
    }

    #endregion
}
