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
public class ConstructionSite : MonoBehaviour
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
    [SerializeField] private float constructionProgress = 0f;

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
    /// 자원 예약 ID를 설정합니다 (ConstructionManager에서 호출).
    /// </summary>
    /// <param name="id">자원 예약 ID</param>
    public void SetReservationId(int id)
    {
        reservationId = id;
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

        GameMap gameMap = MapGenerator.instance.GameMapInstance;

        for (int x = 0; x < buildingData.size.x; x++)
        {
            for (int y = 0; y < buildingData.size.y; y++)
            {
                int tileX = gridPosition.x + x;
                int tileY = gridPosition.y + y;
                // 청사진 단계: 이동을 차단하지 않음 (직원이 건설 현장에 접근·통과 가능)
                // 공간은 점유 상태로 표시해 다른 건물이 중복 배치되는 것만 방지
                // 완공 후 Building.RegisterToGameMap()이 buildingData.blocksMovement 값으로 재등록
                gameMap.MarkTileOccupied(tileX, tileY, blocksMovement: false);
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

    /// <summary>
    /// 건설이 완료될 때 호출됩니다.
    /// 예약된 자원을 실제로 소모하고, 실제 건물을 생성한 뒤 건설 현장을 제거합니다.
    /// </summary>
    public void CompleteConstruction()
    {
        if (state == ConstructionState.Completed) return;

        state = ConstructionState.Completed;
        constructionProgress = 1f;

        Debug.Log($"[ConstructionSite] 건설 완료, 실제 건물 생성: {buildingData.buildingName}");

        // 예약된 자원을 실제로 소모
        if (reservationId >= 0 && InventoryManager.instance != null)
        {
            InventoryManager.instance.ConsumeReservation(reservationId);
            reservationId = -1;
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
    /// 예약만 해제하면 자원이 자동으로 사용 가능해집니다 (자원 손실 없음).
    /// </summary>
    public void CancelConstruction()
    {
        Debug.Log($"[ConstructionSite] 건설 취소: {buildingData.buildingName}");

        // 예약 취소 (자원 손실 없음)
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
    /// </summary>
    public ConstructionSiteSaveData CreateSaveData()
    {
        return new ConstructionSiteSaveData
        {
            instanceId = _instanceId,
            buildingDataId = buildingData.buildingID,
            gridX = gridPosition.x,
            gridY = gridPosition.y,
            state = (int)state,
            progress = constructionProgress,
            workOrderId = workOrder != null ? workOrder.orderId : -1,
            reservationId = reservationId
        };
    }

    /// <summary>
    /// 저장된 데이터로 건설 현장을 복원합니다.
    /// 작업 주문은 생성하지 않습니다 (WorkSystemManager에서 별도 복원).
    /// </summary>
    public void RestoreFromSaveData(ConstructionSiteSaveData saveData, BuildingData data)
    {
        buildingData = data;
        gridPosition = new Vector3Int(saveData.gridX, saveData.gridY, 0);
        state = (ConstructionState)saveData.state;
        constructionProgress = saveData.progress;
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
