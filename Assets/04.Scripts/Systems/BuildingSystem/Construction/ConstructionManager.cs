using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 건설 시스템 매니저.
/// 건설 모드 진입/퇴장, 청사진 배치, 건설 현장 관리를 담당합니다.
///
/// 건설 흐름:
///   1. EnterPlacementMode() → 배치 모드 시작 (고스트 표시)
///   2. PlaceConstruction() → 자원 예약 + 건설 현장 생성
///   3. 직원 작업 → ConstructionSite.CompleteConstruction() → 예약 소모 + 건물 생성
///   4. 건설 취소 → ConstructionSite.CancelConstruction() → 예약 해제 (자원 손실 없음)
/// </summary>
public class ConstructionManager : DestroySingleton<ConstructionManager>, ISaveModule
{
    #region 필드 및 설정

    [Header("프리팹")]
    [Tooltip("건설 현장 프리팹 (ConstructionSite 컴포넌트 포함)")]
    [SerializeField] private GameObject constructionSitePrefab;

    [Tooltip("배치 미리보기용 고스트 프리팹")]
    [SerializeField] private GameObject placementGhostPrefab;

    [Header("건물 데이터베이스")]
    [Tooltip("모든 건물 데이터 목록")]
    [SerializeField] private List<BuildingData> allBuildingData = new List<BuildingData>();

    [Header("배치 설정")]
    [SerializeField] private Color validPlacementColor        = new Color(0.3f, 1f,  0.3f, 0.5f); // 초록: 배치 가능 + 자원 충분
    [SerializeField] private Color cannotAffordPlacementColor = new Color(1f,  1f,  0.3f, 0.5f); // 노란: 위치 OK, 자원 부족
    [SerializeField] private Color invalidPlacementColor      = new Color(1f,  0.3f,0.3f, 0.5f); // 빨강: 배치 불가
    [SerializeField] private Color isolationWarningColor      = new Color(1f,  0.6f,0.1f, 0.5f); // 주황: 직원 고립 경고

    [Header("건설 현장 관리")]
    [SerializeField] private Transform constructionParent;
    [SerializeField] private List<ConstructionSite> activeConstructionSites = new List<ConstructionSite>();

    [Header("디버그")]
    [SerializeField] private bool showDebugInfo = true;

    // 배치 모드 상태
    private bool isPlacementMode = false;
    private BuildingData selectedBuildingData;
    private GameObject ghostObject;
    private List<SpriteRenderer> ghostRenderers = new List<SpriteRenderer>();
    private bool isCurrentPlacementValid = false;

    // 관절점 체크 캐시 (매 프레임 BFS 실행 방지)
    private Vector3Int lastCheckedIsolationPos = new Vector3Int(int.MinValue, int.MinValue, 0);
    private bool cachedIsolationResult = false;

    // 캐시
    private GameMap gameMap;
    private Dictionary<BuildingCategory, List<BuildingData>> buildingsByCategory;

    #endregion

    #region 초기화

    protected override void Awake()
    {
        base.Awake();

        if (constructionParent == null)
        {
            GameObject parent = new GameObject("ConstructionSites");
            constructionParent = parent.transform;
        }

        OrganizeBuildingsByCategory();
    }

    void Start()
    {
        if (MapGenerator.instance != null)
        {
            gameMap = MapGenerator.instance.GameMapInstance;
        }

        // GameDatabase에 등록된 건물 데이터 자동 보완 로드
        // (씬 Inspector에 없어도 GameDatabase에만 추가하면 자동으로 UI에 반영됨)
        if (GameDatabase.Instance != null)
        {
            foreach (var data in GameDatabase.Instance.GetAllBuildingData())
            {
                RegisterBuildingData(data);
            }
        }
    }

    void Update()
    {
        if (isPlacementMode)
        {
            UpdatePlacementGhost();
            HandlePlacementInput();
        }
    }

    #endregion

    #region 건물 데이터베이스

    /// <summary>
    /// 건물 데이터를 카테고리별로 분류합니다.
    /// </summary>
    private void OrganizeBuildingsByCategory()
    {
        buildingsByCategory = new Dictionary<BuildingCategory, List<BuildingData>>();

        foreach (BuildingCategory category in System.Enum.GetValues(typeof(BuildingCategory)))
        {
            buildingsByCategory[category] = new List<BuildingData>();
        }

        foreach (var data in allBuildingData)
        {
            if (data != null)
            {
                buildingsByCategory[data.category].Add(data);
            }
        }

        if (showDebugInfo)
        {
            Debug.Log($"[ConstructionManager] 건물 데이터 분류 완료: 총 {allBuildingData.Count}개");
            foreach (var kvp in buildingsByCategory)
            {
                if (kvp.Value.Count > 0)
                {
                    Debug.Log($"  - {kvp.Key}: {kvp.Value.Count}개");
                }
            }
        }
    }

    /// <summary>
    /// 특정 카테고리의 건물 목록을 반환합니다.
    /// </summary>
    /// <param name="category">조회할 건물 카테고리</param>
    /// <returns>해당 카테고리의 건물 데이터 목록</returns>
    public List<BuildingData> GetBuildingsByCategory(BuildingCategory category)
    {
        if (buildingsByCategory.TryGetValue(category, out List<BuildingData> buildings))
        {
            // 내부 리스트를 그대로 넘기지 않는다 (호출부가 수정하면 등록 목록이 오염된다)
            return buildings.Where(IsBuildingAvailable).ToList();
        }
        return new List<BuildingData>();
    }

    /// <summary>
    /// 지금 건설할 수 있는 건물인지 (연구 해금 여부).
    ///
    /// 주의: 세이브 복원 경로(GameDatabase.GetBuildingData)에는 절대 이 필터를 걸지 마세요.
    /// 이미 지어진 잠긴 건물이 로드에서 사라집니다. 이건 '건설 목록 표시' 전용입니다.
    /// </summary>
    public bool IsBuildingAvailable(BuildingData data)
    {
        if (data == null) return false;
        if (!data.requiresResearch) return true;

        // 연구 매니저가 없는 씬(테스트 등)에서는 잠그지 않는다
        var research = ResearchTreeManager.instance;
        return research == null || research.IsBuildingUnlocked(data);
    }

    /// <summary>
    /// 건물이 1개 이상 등록된 모든 카테고리 목록을 반환합니다.
    /// </summary>
    /// <returns>유효한 카테고리 목록</returns>
    public List<BuildingCategory> GetAvailableCategories()
    {
        return buildingsByCategory
            .Where(kvp => kvp.Value.Any(IsBuildingAvailable))
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// 건물 ID로 BuildingData를 찾습니다.
    /// </summary>
    /// <param name="buildingId">검색할 건물 ID</param>
    /// <returns>해당 ID의 BuildingData (없으면 null)</returns>
    public BuildingData GetBuildingDataById(int buildingId)
    {
        return allBuildingData.FirstOrDefault(b => b.buildingID == buildingId);
    }

    /// <summary>
    /// 건물 데이터를 런타임에 추가합니다.
    /// </summary>
    /// <param name="data">등록할 건물 데이터</param>
    public void RegisterBuildingData(BuildingData data)
    {
        if (data == null || allBuildingData.Contains(data)) return;

        allBuildingData.Add(data);
        buildingsByCategory[data.category].Add(data);

        if (showDebugInfo)
        {
            Debug.Log($"[ConstructionManager] 건물 등록: {data.buildingName}");
        }
    }

    #endregion

    #region 배치 모드

    /// <summary>
    /// 배치 모드를 시작합니다.
    /// 자원이 충분해야 진입할 수 있습니다.
    /// </summary>
    /// <param name="buildingData">배치할 건물 데이터</param>
    /// <returns>배치 모드 진입 성공 여부</returns>
    public bool EnterPlacementMode(BuildingData buildingData)
    {
        if (buildingData == null)
        {
            Debug.LogWarning("[ConstructionManager] BuildingData가 null입니다.");
            return false;
        }

        if (!HasRequiredResources(buildingData))
        {
            // 자원 부족해도 배치 모드 진입 허용 — 고스트가 노란색으로 표시됨
            Debug.LogWarning($"[ConstructionManager] 자원 부족 (배치 미리보기는 허용): {buildingData.buildingName}");
            LogMissingResources(buildingData);
        }

        if (isPlacementMode)
        {
            ExitPlacementMode();
        }

        selectedBuildingData = buildingData;
        isPlacementMode = true;

        CreatePlacementGhost();

        GameMessageBus.Publish(new BuildingPlacementModeChangedMessage(true, buildingData));

        if (showDebugInfo)
        {
            Debug.Log($"[ConstructionManager] 배치 모드 시작: {buildingData.buildingName}");
        }

        return true;
    }

    /// <summary>
    /// 배치 모드를 종료합니다.
    /// </summary>
    public void ExitPlacementMode()
    {
        if (!isPlacementMode) return;

        isPlacementMode = false;
        selectedBuildingData = null;
        lastCheckedIsolationPos = new Vector3Int(int.MinValue, int.MinValue, 0);
        cachedIsolationResult   = false;

        DestroyPlacementGhost();

        GameMessageBus.Publish(new BuildingPlacementModeChangedMessage(false, null));

        if (showDebugInfo)
        {
            Debug.Log("[ConstructionManager] 배치 모드 종료");
        }
    }

    private void CreatePlacementGhost()
    {
        if (selectedBuildingData == null) return;

        ghostRenderers.Clear();

        ghostObject = new GameObject("PlacementGhost");

        // 건물 크기만큼 타일 고스트 생성
        for (int x = 0; x < selectedBuildingData.size.x; x++)
        {
            for (int y = 0; y < selectedBuildingData.size.y; y++)
            {
                GameObject tile;

                if (placementGhostPrefab != null)
                {
                    tile = Instantiate(placementGhostPrefab, ghostObject.transform);
                }
                else
                {
                    tile = new GameObject($"GhostTile_{x}_{y}");
                    tile.transform.SetParent(ghostObject.transform);
                    SpriteRenderer sr = tile.AddComponent<SpriteRenderer>();
                    sr.sprite = CreateDefaultSprite();
                    sr.sortingOrder = 100;
                }

                tile.transform.localPosition = new Vector3(x + 0.5f, y + 0.5f, 0);

                SpriteRenderer renderer = tile.GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    ghostRenderers.Add(renderer);
                }
            }
        }

        // 건물 프리팹의 스프라이트도 반투명으로 표시
        if (selectedBuildingData.buildingPrefab != null)
        {
            SpriteRenderer prefabRenderer = selectedBuildingData.buildingPrefab.GetComponent<SpriteRenderer>();
            if (prefabRenderer != null && prefabRenderer.sprite != null)
            {
                GameObject buildingGhost = new GameObject("BuildingGhost");
                buildingGhost.transform.SetParent(ghostObject.transform);
                SpriteRenderer sr = buildingGhost.AddComponent<SpriteRenderer>();
                Sprite spr = prefabRenderer.sprite;
                sr.sprite = spr;
                sr.sortingOrder = 101;
                sr.color = new Color(1f, 1f, 1f, 0.5f);

                // 스프라이트 피벗이 어디든 건물 footprint 중앙에 정렬
                // ghostObject origin = footprint 왼쪽 하단 (gridPos)
                // footprint 중심 = (size.x/2, size.y/2)
                // 스프라이트 bounds.center = 피벗 기준 스프라이트 중심
                // => localPos = footprint중심 - bounds중심 → 스프라이트가 footprint를 정확히 덮음
                buildingGhost.transform.localPosition = new Vector3(
                    selectedBuildingData.size.x * 0.5f - spr.bounds.center.x,
                    selectedBuildingData.size.y * 0.5f - spr.bounds.center.y,
                    0f
                );
            }
        }
    }

    private Sprite CreateDefaultSprite()
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
    }

    private void DestroyPlacementGhost()
    {
        if (ghostObject != null)
        {
            Destroy(ghostObject);
            ghostObject = null;
        }
        ghostRenderers.Clear();
    }

    private void UpdatePlacementGhost()
    {
        if (ghostObject == null || selectedBuildingData == null) return;

        // gameMap 레이지 초기화
        if (gameMap == null && MapGenerator.instance != null)
            gameMap = MapGenerator.instance.GameMapInstance;

        Vector3 mouseWorld = GetMouseWorldPosition();
        Vector3Int gridPos = new Vector3Int(
            Mathf.FloorToInt(mouseWorld.x),
            Mathf.FloorToInt(mouseWorld.y),
            0
        );

        ghostObject.transform.position = new Vector3(gridPos.x, gridPos.y, 0);

        bool isPositionValid = CanPlaceAt(gridPos);
        bool canAfford = HasRequiredResources(selectedBuildingData);

        // isCurrentPlacementValid = 위치 유효 AND 자원 충분 (실제 배치 가능 여부)
        isCurrentPlacementValid = isPositionValid && canAfford;

        // 관절점 체크: 그리드 위치가 바뀐 경우에만 BFS 재실행 (캐시)
        bool wouldIsolate = false;
        if (isPositionValid && gameMap != null)
        {
            if (gridPos != lastCheckedIsolationPos)
            {
                lastCheckedIsolationPos = gridPos;
                cachedIsolationResult   = ArticulationPointChecker.WouldIsolateEmployee(
                    gameMap, gridPos, selectedBuildingData.size);
            }
            wouldIsolate = cachedIsolationResult;
        }

        // 4색 고스트:
        //   초록  → 배치 가능
        //   주황  → 배치 가능하나 직원 고립 경고
        //   노란  → 위치 OK, 자원 부족
        //   빨강  → 배치 불가
        Color color;
        if (!isPositionValid)
            color = invalidPlacementColor;
        else if (wouldIsolate)
            color = isolationWarningColor;
        else if (!canAfford)
            color = cannotAffordPlacementColor;
        else
            color = validPlacementColor;

        foreach (var renderer in ghostRenderers)
        {
            if (renderer != null)
                renderer.color = color;
        }

        // 건물 고스트 색상 업데이트
        Transform buildingGhost = ghostObject.transform.Find("BuildingGhost");
        if (buildingGhost != null)
        {
            SpriteRenderer sr = buildingGhost.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.color = new Color(color.r, color.g, color.b, 0.5f);
        }
    }

    private void HandlePlacementInput()
    {
        // 좌클릭: 배치
        if (Input.GetMouseButtonDown(0))
        {
            if (isCurrentPlacementValid)
            {
                Vector3 mouseWorld = GetMouseWorldPosition();
                Vector3Int gridPos = new Vector3Int(
                    Mathf.FloorToInt(mouseWorld.x),
                    Mathf.FloorToInt(mouseWorld.y),
                    0
                );

                PlaceConstruction(gridPos);
            }
            else
            {
                Debug.LogWarning("[ConstructionManager] 이 위치에 건설할 수 없습니다.");
            }
        }

        // 우클릭: 취소
        if (Input.GetMouseButtonDown(1))
        {
            ExitPlacementMode();
        }
    }

    #endregion

    #region 배치 검증

    /// <summary>
    /// 해당 그리드 위치에 건물을 배치할 수 있는지 확인합니다.
    /// 모든 타일이 비어있고, 바닥에 고체 타일이 있어야 합니다.
    /// </summary>
    /// <param name="gridPos">확인할 그리드 좌표 (왼쪽 아래 기준)</param>
    /// <returns>배치 가능 여부</returns>
    public bool CanPlaceAt(Vector3Int gridPos)
    {
        if (selectedBuildingData == null) return false;

        // gameMap 레이지 초기화 (MapGenerator가 Start() 이후에 준비될 수 있음)
        if (gameMap == null && MapGenerator.instance != null)
            gameMap = MapGenerator.instance.GameMapInstance;

        if (gameMap == null) return false;

        // 오버레이 설치물(전선 등)은 점유 그리드를 무시하고 별도 규칙으로 검사합니다.
        if (selectedBuildingData.allowOverlap)
            return CanPlaceOverlay(gridPos);

        // 모든 타일 검사
        for (int x = 0; x < selectedBuildingData.size.x; x++)
        {
            for (int y = 0; y < selectedBuildingData.size.y; y++)
            {
                int checkX = gridPos.x + x;
                int checkY = gridPos.y + y;

                if (checkX < 0 || checkX >= GameMap.MAP_WIDTH ||
                    checkY < 0 || checkY >= GameMap.MAP_HEIGHT)
                {
                    return false;
                }

                if (!gameMap.IsSpaceAvailable(checkX, checkY))
                {
                    // 진단 로그: 어떤 좌표가 왜 막혔는지 사용자가 알 수 있도록
                    // (안개 영역의 경우 일반적으로 자연 흙/돌이라 AIR가 아니므로 차단됨)
                    int tileId = gameMap.TileGrid[checkX, checkY];
                    bool occupied = gameMap.IsTileOccupied(checkX, checkY);
                    Debug.Log($"[ConstructionManager] CanPlaceAt 실패: ({checkX},{checkY}) — " +
                              $"tileId={tileId}(0=AIR), occupied={occupied}. " +
                              $"안개 영역이라면 채광 먼저 필요.");
                    return false;
                }
            }
        }

        // 바닥 확인 (requiresFloorSupport가 true인 건물만 적용)
        // 기반 시설(바닥 타일, 사다리, 다리 등)은 이 조건을 건너뜁니다.
        if (selectedBuildingData.requiresFloorSupport)
        {
            for (int x = 0; x < selectedBuildingData.size.x; x++)
            {
                int checkX = gridPos.x + x;
                int groundY = gridPos.y - 1;

                if (groundY < 0 || !gameMap.IsSolidGround(checkX, groundY))
                {
                    Debug.Log($"[ConstructionManager] CanPlaceAt 실패: 바닥 없음 ({checkX},{groundY}). " +
                              $"requiresFloorSupport=true 건물은 아래 솔리드 타일 필요.");
                    return false;
                }
            }
        }

        // TODO [건설시스템]: 물 위, 특정 지형 등 특수 배치 조건 추가

        return true;
    }

    /// <summary>
    /// 오버레이 설치물(전선 등 allowOverlap=true)의 배치 가능 여부를 검사합니다.
    /// 점유 그리드를 무시하므로 기존 건축물(바닥·벽·건물) 위에도 겹쳐 놓을 수 있으며,
    /// 같은 셀에 동일 오버레이(전선)가 중복되지 않는지와 맵 범위만 확인합니다.
    /// </summary>
    /// <param name="gridPos">확인할 그리드 좌표 (왼쪽 아래 기준)</param>
    /// <returns>배치 가능 여부</returns>
    private bool CanPlaceOverlay(Vector3Int gridPos)
    {
        for (int x = 0; x < selectedBuildingData.size.x; x++)
        {
            for (int y = 0; y < selectedBuildingData.size.y; y++)
            {
                int cx = gridPos.x + x;
                int cy = gridPos.y + y;

                if (cx < 0 || cx >= GameMap.MAP_WIDTH || cy < 0 || cy >= GameMap.MAP_HEIGHT)
                    return false;

                // 같은 자리에 전선이 이미 있으면 중복 설치 금지
                if (PowerWire.HasWireAt(new Vector2Int(cx, cy)))
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 필요한 자원이 사용 가능한지 확인합니다 (예약분 제외).
    /// </summary>
    /// <param name="buildingData">확인할 건물 데이터</param>
    /// <returns>자원 충분 여부</returns>
    public bool HasRequiredResources(BuildingData buildingData)
    {
        if (buildingData.requiredResources == null || buildingData.requiredResources.Count == 0)
        {
            return true;
        }

        if (InventoryManager.instance == null)
        {
            Debug.LogError("[ConstructionManager] InventoryManager가 없습니다.");
            return false;
        }

        return InventoryManager.instance.HasAvailableItems(buildingData.requiredResources);
    }

    /// <summary>
    /// 부족한 자원을 디버그 로그로 출력합니다.
    /// </summary>
    /// <param name="buildingData">확인할 건물 데이터</param>
    private void LogMissingResources(BuildingData buildingData)
    {
        if (InventoryManager.instance == null || buildingData.requiredResources == null) return;

        foreach (var cost in buildingData.requiredResources)
        {
            int available = InventoryManager.instance.GetAvailableAmount(cost.item);
            if (available < cost.amount)
            {
                Debug.LogWarning($"  - {cost.item.itemName}: {available}/{cost.amount} (사용가능/필요)");
            }
        }
    }

    #endregion

    #region 건설 현장 생성

    /// <summary>
    /// 건설 현장을 배치합니다.
    /// 자원을 예약하고 ConstructionSite를 생성합니다.
    /// </summary>
    /// <param name="gridPos">배치할 그리드 좌표</param>
    private void PlaceConstruction(Vector3Int gridPos)
    {
        if (selectedBuildingData == null) return;

        // 배치 시점에 자원 재확인 (배치 모드 진입 후 자원이 변경되었을 수 있음)
        if (!HasRequiredResources(selectedBuildingData))
        {
            Debug.LogWarning($"[ConstructionManager] 자원이 부족하여 배치 불가: {selectedBuildingData.buildingName}");
            return;
        }

        // 자원 예약
        int reservationId = ReserveResources(selectedBuildingData);
        if (reservationId < 0)
        {
            Debug.LogWarning("[ConstructionManager] 자원 예약 실패");
            return;
        }

        // 건설 현장 생성
        ConstructionSite site = CreateConstructionSite(selectedBuildingData, gridPos);

        if (site != null)
        {
            site.SetReservationId(reservationId);
            activeConstructionSites.Add(site);
            GameMessageBus.Publish(new ConstructionSiteCreatedMessage(site));

            if (showDebugInfo)
            {
                Debug.Log($"[ConstructionManager] 건설 현장 배치: {selectedBuildingData.buildingName} at {gridPos} (예약 #{reservationId})");
            }
        }
        else
        {
            // 건설 현장 생성 실패 시 예약 취소
            InventoryManager.instance.CancelReservation(reservationId);
        }
    }

    /// <summary>
    /// 자원을 예약합니다.
    /// </summary>
    /// <param name="buildingData">건물 데이터</param>
    /// <returns>예약 ID (성공 시 양수, 실패 시 -1, 비용 없음 시 0)</returns>
    private int ReserveResources(BuildingData buildingData)
    {
        if (InventoryManager.instance == null) return -1;

        if (buildingData.requiredResources == null || buildingData.requiredResources.Count == 0)
        {
            return 0;
        }

        return InventoryManager.instance.TryReserve(buildingData.requiredResources);
    }

    /// <summary>
    /// 건설 현장 GameObject를 생성합니다.
    /// </summary>
    /// <param name="buildingData">건물 데이터</param>
    /// <param name="gridPos">배치할 그리드 좌표</param>
    /// <returns>생성된 ConstructionSite 컴포넌트</returns>
    private ConstructionSite CreateConstructionSite(BuildingData buildingData, Vector3Int gridPos)
    {
        GameObject siteObj;

        if (constructionSitePrefab != null)
        {
            siteObj = Instantiate(constructionSitePrefab, constructionParent);
        }
        else
        {
            siteObj = new GameObject("ConstructionSite");
            siteObj.transform.SetParent(constructionParent);
            siteObj.AddComponent<SpriteRenderer>();
            siteObj.AddComponent<BoxCollider2D>();
            siteObj.AddComponent<ConstructionSite>();
        }

        siteObj.transform.position = new Vector3(gridPos.x, gridPos.y, 0);

        ConstructionSite site = siteObj.GetComponent<ConstructionSite>();
        if (site != null)
        {
            site.Initialize(buildingData, gridPos);
        }

        return site;
    }

    #endregion

    #region 건설 현장 관리

    /// <summary>
    /// 건설 현장을 취소합니다.
    /// 예약된 자원이 해제되고 건설 현장이 제거됩니다.
    /// </summary>
    /// <param name="site">취소할 건설 현장</param>
    public void CancelConstruction(ConstructionSite site)
    {
        if (site == null) return;

        activeConstructionSites.Remove(site);
        GameMessageBus.Publish(new ConstructionSiteCancelledMessage(site));

        site.CancelConstruction();
    }

    /// <summary>
    /// 건설이 완료되었을 때 호출됩니다.
    /// 활성 건설 현장 목록에서 제거합니다.
    /// </summary>
    /// <param name="site">완료된 건설 현장</param>
    public void OnConstructionCompleted(ConstructionSite site)
    {
        if (site == null) return;

        activeConstructionSites.Remove(site);
        GameMessageBus.Publish(new ConstructionSiteCompletedMessage(site));
    }

    /// <summary>
    /// 특정 그리드 위치의 건설 현장을 찾습니다.
    /// </summary>
    /// <param name="gridPos">검색할 그리드 좌표</param>
    /// <returns>해당 위치의 건설 현장 (없으면 null)</returns>
    public ConstructionSite GetConstructionSiteAt(Vector3Int gridPos)
    {
        return activeConstructionSites.FirstOrDefault(s => s.GridPosition == gridPos);
    }

    #endregion

    #region 유틸리티

    private Vector3 GetMouseWorldPosition()
    {
        Vector3 mousePos = Input.mousePosition;
        mousePos.z = -Camera.main.transform.position.z;
        return Camera.main.ScreenToWorldPoint(mousePos);
    }

    #endregion

    #region ISaveModule 구현

    public int SaveOrder => 30;

    public void Capture(SaveData data)
    {
        // 완성된 건물 캡처
        data.buildings.Clear();
        var buildings = FindObjectsByType<Building>();
        foreach (var building in buildings)
        {
            if (building.buildingData != null)
            {
                data.buildings.Add(building.CreateSaveData());
            }
        }

        // 건설 현장 캡처
        data.constructionSites.Clear();
        foreach (var site in activeConstructionSites)
        {
            if (site != null && !site.IsCompleted)
            {
                data.constructionSites.Add(site.CreateSaveData());
            }
        }
    }

    public void Restore(SaveData data)
    {
        var db = GameDatabase.Instance;
        if (db == null)
        {
            Debug.LogError("[ConstructionManager] GameDatabase가 없어 건물 복원 불가");
            return;
        }

        // 1. 완성된 건물 복원
        if (data.buildings != null)
        {
            foreach (var bsd in data.buildings)
            {
                BuildingData bData = db.GetBuildingData(bsd.dataId);
                if (bData == null || bData.buildingPrefab == null)
                {
                    Debug.LogWarning($"[ConstructionManager] 건물 데이터 없음: ID {bsd.dataId}");
                    continue;
                }

                Vector3 pos = new Vector3(bsd.gridX, bsd.gridY, 0);
                Transform parent = null;
                if (MapGenerator.instance != null && MapGenerator.instance.MapRendererInstance != null)
                {
                    parent = MapGenerator.instance.MapRendererInstance.entityParent;
                }

                GameObject obj = Instantiate(bData.buildingPrefab, pos, Quaternion.identity, parent);
                Building building = obj.GetComponent<Building>();
                if (building != null)
                {
                    building.RestoreState(bsd);
                }
            }
        }

        // 2. 건설 현장 복원
        activeConstructionSites.Clear();
        if (data.constructionSites != null)
        {
            foreach (var csd in data.constructionSites)
            {
                BuildingData bData = db.GetBuildingData(csd.buildingDataId);
                if (bData == null)
                {
                    Debug.LogWarning($"[ConstructionManager] 건설 현장 건물 데이터 없음: ID {csd.buildingDataId}");
                    continue;
                }

                GameObject siteObj;
                if (constructionSitePrefab != null)
                {
                    siteObj = Instantiate(constructionSitePrefab, constructionParent);
                }
                else
                {
                    siteObj = new GameObject("ConstructionSite");
                    siteObj.transform.SetParent(constructionParent);
                    siteObj.AddComponent<SpriteRenderer>();
                    siteObj.AddComponent<BoxCollider2D>();
                    siteObj.AddComponent<ConstructionSite>();
                }

                siteObj.transform.position = new Vector3(csd.gridX, csd.gridY, 0);

                ConstructionSite site = siteObj.GetComponent<ConstructionSite>();
                if (site != null)
                {
                    site.RestoreFromSaveData(csd, bData);
                    activeConstructionSites.Add(site);
                }
            }
        }
    }

    public void PostRestore(SaveData data) { }

    #endregion

    #region Public 프로퍼티

    /// <summary>현재 배치 모드 활성화 여부</summary>
    public bool IsPlacementMode => isPlacementMode;

    /// <summary>현재 선택된 건물 데이터</summary>
    public BuildingData SelectedBuildingData => selectedBuildingData;

    /// <summary>활성 건설 현장 목록</summary>
    public List<ConstructionSite> ActiveConstructionSites => activeConstructionSites;

    /// <summary>활성 건설 현장 수</summary>
    public int ActiveConstructionCount => activeConstructionSites.Count;

    #endregion
}
