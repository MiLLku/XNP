using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.EventSystems;
using System.Linq;

/// <summary>
/// 상호작용 관리자
/// 플레이어 입력 처리, 모드 전환, 드래그 선택 등을 담당합니다.
/// 
/// 저장 위치: Assets/Scripts/Core/Manager/InteractionManager.cs
/// 
/// [수정 사항]
/// - Build 모드를 ConstructionManager와 연동하도록 변경
/// - 기존 placementGhost 관련 코드 제거 (ConstructionManager로 이전)
/// - 건설 현장 클릭 처리 추가
/// </summary>
public class InteractionManager : DestroySingleton<InteractionManager> 
{
    public enum InteractMode
    {
        Normal,
        Mine,
        Harvest,
        Build,
        Demolish
    }

    [Header("필수 연결 (씬)")]
    [SerializeField] private Tilemap groundTilemap;
    [SerializeField] private Transform itemDropParent;
    
    [Header("프리팹 연결")]
    [SerializeField] private GameObject selectionBoxPrefab;
    [SerializeField] private GameObject workOrderVisualPrefab; 
    
    [Header("드래그 선택 색상")]
    [SerializeField] private Color miningSelectionColor = new Color(1, 1, 0, 0.3f);
    [SerializeField] private Color harvestSelectionColor = new Color(0, 1, 0, 0.3f);
    [SerializeField] private Color demolishSelectionColor = new Color(1, 0, 0, 0.3f);
    
    [Header("작업 설정")]
    [SerializeField] private int defaultMiningWorkers = 3;
    [SerializeField] private int defaultHarvestWorkers = 2;
    [SerializeField] private int defaultDemolishWorkers = 1;
    
    [Header("최적화 설정")]
    [SerializeField] private int maxTilesPerOrder = 200;
    
    [Header("테스트용")]
    [SerializeField] private BuildingData testBuildingToBuild;
    [SerializeField] private bool enableCheatKey = true;

    // 시스템 참조
    private GameMap _gameMap;
    private MapRenderer _mapRenderer;
    private ResourceManager _resourceManager;
    private CameraController _cameraController;
    private WorkSystemManager _workSystemManager;
    private TileHighlighter _tileHighlighter;
    private ConstructionManager _constructionManager;
    
    // 모드 관리
    private InteractMode _currentMode = InteractMode.Normal;

    // 드래그 선택
    private bool _isDragging = false;
    private Vector3 _dragStartPos;
    private Vector3 _dragEndPos;
    private GameObject _selectionBox;
    private List<GameObject> _selectedObjects = new List<GameObject>();
    
    // Normal 모드 호버링
    private GameObject _hoveredObject;
    private Color _originalColor;
    private SpriteRenderer _hoveredRenderer;
    
    // 직원 선택 (Normal 모드 좌클릭 — 태세 바 UI가 구독)
    private Employee _selectedEmployee;

    // 이벤트
    public delegate void ModeChangedDelegate(InteractMode newMode);
    public event ModeChangedDelegate OnModeChanged;

    /// <summary>직원 선택/해제 시 발생 (null = 해제)</summary>
    public event System.Action<Employee> OnEmployeeSelected;

    /// <summary>현재 선택된 직원 (없으면 null)</summary>
    public Employee SelectedEmployee => _selectedEmployee;
    
    protected override void Awake()
    {
        base.Awake();
        CreateSelectionBox();
    }

    void Start()
    {
        if (MapGenerator.instance == null)
        {
            Debug.LogError("MapGenerator.instance가 null입니다!");
            return;
        }

        _gameMap = MapGenerator.instance.GameMapInstance;
        _mapRenderer = MapGenerator.instance.MapRendererInstance;
        _resourceManager = MapGenerator.instance.ResourceManagerInstance;
        _cameraController = Camera.main.GetComponent<CameraController>();
        _workSystemManager = WorkSystemManager.instance;
        _tileHighlighter = TileHighlighter.instance;
        _constructionManager = ConstructionManager.instance;
        
        if (_constructionManager != null)
        {
            _constructionManager.OnPlacementModeChanged += OnConstructionPlacementModeChanged;
        }
    }
    
    void OnDestroy()
    {
        if (_constructionManager != null)
        {
            _constructionManager.OnPlacementModeChanged -= OnConstructionPlacementModeChanged;
        }
    }

    void Update()
    {
        if (_gameMap == null) return;
        
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_currentMode != InteractMode.Normal)
                SetMode(InteractMode.Normal);
            else
                DeselectEmployee();
        }

        if (enableCheatKey && Input.GetKeyDown(KeyCode.F9))
        {
            ExecuteAllOrdersInstantly();
        }
        
        switch (_currentMode)
        {
            case InteractMode.Normal:
                HandleNormalMode();
                break;
            case InteractMode.Mine:
                HandleMineMode();
                break;
            case InteractMode.Harvest:
                HandleHarvestMode();
                break;
            case InteractMode.Build:
                HandleBuildMode();
                break;
            case InteractMode.Demolish:
                HandleDemolishMode();
                break;
        }
        
        if (_isDragging && _selectionBox != null && _currentMode != InteractMode.Mine)
        {
            UpdateSelectionBox();
        }
    }
    
    #region 초기화
    
    private void CreateSelectionBox()
    {
        if (selectionBoxPrefab != null)
        {
            _selectionBox = Instantiate(selectionBoxPrefab);
        }
        else
        {
            _selectionBox = new GameObject("SelectionBox");
            SpriteRenderer sr = _selectionBox.AddComponent<SpriteRenderer>();
            sr.sprite = CreateWhiteSprite();
            sr.sortingOrder = 100;
        }
        _selectionBox.SetActive(false);
    }
    
    private Sprite CreateWhiteSprite()
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
    }
    
    #endregion
    
    #region 모드 관리
    
    public void SetMode(InteractMode mode)
    {
        if (_currentMode == mode) return;
    
        ExitCurrentMode();
        _currentMode = mode;

        if (mode == InteractMode.Build)
        {
            // UIManager를 통해 건설 UI 열기
            if (UIManager.instance != null)
            {
                UIManager.instance.ShowPanel(UIPanelType.ConstructionUI);
            }
        }

        Debug.Log($"[Interaction] 모드 변경: {mode}");
        OnModeChanged?.Invoke(mode);
    
        UpdateSelectionBoxColor();
    }
    
    private void ExitCurrentMode()
    {
        CancelDrag();

        if (_currentMode == InteractMode.Build)
        {
            // 배치 모드가 활성화되어 있으면 종료
            if (_constructionManager != null && _constructionManager.IsPlacementMode)
            {
                _constructionManager.ExitPlacementMode();
            }

            // UIManager를 통해 건설 UI 닫기
            if (UIManager.instance != null)
            {
                UIManager.instance.HidePanel(UIPanelType.ConstructionUI);
            }
        }
    }
    
    private void UpdateSelectionBoxColor()
    {
        if (_selectionBox == null) return;
        SpriteRenderer sr = _selectionBox.GetComponent<SpriteRenderer>();
        if (sr == null) return;
        
        switch (_currentMode)
        {
            case InteractMode.Mine: sr.color = miningSelectionColor; break;
            case InteractMode.Harvest: sr.color = harvestSelectionColor; break;
            case InteractMode.Demolish: sr.color = demolishSelectionColor; break;
            default: sr.color = new Color(1, 1, 1, 0.3f); break;
        }
    }
    
    private void OnConstructionPlacementModeChanged(bool isActive, BuildingData buildingData)
    {
        // 배치 모드 변경 시 필요한 처리
    }
    
    public InteractMode GetCurrentMode() => _currentMode;
    
    #endregion

    #region Normal 모드
    
    private void HandleNormalMode()
    {
        Vector3 mousePos = _cameraController.GetMouseWorldPosition();

        // 호버 처리: 단순 Raycast (첫 번째 히트)
        RaycastHit2D hit = Physics2D.Raycast(mousePos, Vector2.zero);
        if (hit.collider != null)
        {
            GameObject hitObject = hit.collider.gameObject;
            if (_hoveredObject != hitObject)
            {
                ClearHover();
                SetHover(hitObject);
            }
        }
        else
        {
            ClearHover();
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (IsPointerOverInteractiveUI()) return;

            // 클릭 처리: 모든 콜라이더를 검사해 우선순위 높은 오브젝트부터 처리
            // (TilemapCollider가 BuildingCollider를 가리는 문제 방지)
            Collider2D[] allHits = Physics2D.OverlapPointAll(mousePos);
            bool consumed = allHits.Length > 0 && HandleNormalModeClickAll(allHits);

            // 직원이 아닌 곳 클릭 → 선택 해제
            if (!consumed) DeselectEmployee();
        }

        // 우클릭: 선택된 소집 직원에게 이동 명령
        if (Input.GetMouseButtonDown(1))
        {
            if (!IsPointerOverInteractiveUI() &&
                _selectedEmployee != null && _selectedEmployee.IsDrafted)
            {
                _selectedEmployee.CommandMoveTo(mousePos);
            }
        }
    }

    /// <summary>
    /// 겹친 모든 콜라이더 중 우선순위 순으로 클릭 처리.
    /// 우선순위: WorkOrderVisual > ConstructionSite > Employee > Building > Harvestable
    /// </summary>
    /// <returns>클릭이 오브젝트에 소비되었는지 (false = 빈 곳 클릭)</returns>
    private bool HandleNormalModeClickAll(Collider2D[] hits)
    {
        // 우선순위 1: WorkOrderVisual
        foreach (var col in hits)
        {
            WorkOrderVisual workVisual = col.GetComponent<WorkOrderVisual>();
            if (workVisual != null) { DeselectEmployee(); workVisual.OnClicked(); return true; }
        }
        // 우선순위 2: ConstructionSite
        foreach (var col in hits)
        {
            ConstructionSite site = col.GetComponent<ConstructionSite>();
            if (site != null) { DeselectEmployee(); OnConstructionSiteClicked(site); return true; }
        }
        // 우선순위 3: Employee
        foreach (var col in hits)
        {
            Employee emp = col.GetComponent<Employee>();
            if (emp != null) { OnEmployeeClicked(emp); return true; }
        }
        // 우선순위 4: Building
        foreach (var col in hits)
        {
            Building building = col.GetComponent<Building>();
            if (building != null) { DeselectEmployee(); OnBuildingClicked(building); return true; }
        }
        // 우선순위 5: IHarvestable
        foreach (var col in hits)
        {
            IHarvestable harvestable = col.GetComponent<IHarvestable>();
            if (harvestable != null) { DeselectEmployee(); OnHarvestableClicked(harvestable, col.gameObject); return true; }
        }
        return false;
    }

    private bool IsPointerOverInteractiveUI()
    {
        if (EventSystem.current == null) return false;

        var eventData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };

        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);

        foreach (var result in results)
        {
            if (result.gameObject.GetComponent<UnityEngine.UI.Selectable>() != null)
            {
                return true;
            }
        }

        return false;
    }
    
    private void HandleNormalModeClick(GameObject clickedObject)
    {
        Debug.Log($"[Interaction] 클릭된 오브젝트: {clickedObject.name} (Layer:{clickedObject.layer})");

        // 작업 더미 클릭
        WorkOrderVisual workVisual = clickedObject.GetComponent<WorkOrderVisual>();
        if (workVisual != null)
        {
            workVisual.OnClicked();
            return;
        }
        
        // 건설 현장 클릭
        ConstructionSite constructionSite = clickedObject.GetComponent<ConstructionSite>();
        if (constructionSite != null)
        {
            OnConstructionSiteClicked(constructionSite);
            return;
        }

        // 직원 클릭
        Employee employee = clickedObject.GetComponent<Employee>();
        if (employee != null)
        {
            OnEmployeeClicked(employee);
            return;
        }
        
        // 건물 클릭
        Building building = clickedObject.GetComponent<Building>();
        if (building != null)
        {
            OnBuildingClicked(building);
            return;
        }
        
        // 수확 가능 오브젝트 클릭
        IHarvestable harvestable = clickedObject.GetComponent<IHarvestable>();
        if (harvestable != null)
        {
            OnHarvestableClicked(harvestable, clickedObject);
            return;
        }
    }
    
    private void SetHover(GameObject obj)
    {
        _hoveredObject = obj;
        _hoveredRenderer = obj.GetComponent<SpriteRenderer>();
        if (_hoveredRenderer != null)
        {
            _originalColor = _hoveredRenderer.color;
            _hoveredRenderer.color = new Color(
                _originalColor.r * 1.2f, 
                _originalColor.g * 1.2f, 
                _originalColor.b * 1.2f, 
                _originalColor.a
            );
        }
    }
    
    private void ClearHover()
    {
        if (_hoveredObject != null && _hoveredRenderer != null)
        {
            _hoveredRenderer.color = _originalColor;
        }
        _hoveredObject = null;
        _hoveredRenderer = null;
    }
    
    private void OnEmployeeClicked(Employee employee)
    {
        SelectEmployee(employee);
    }

    /// <summary>직원을 선택합니다 (태세 바 표시). 다른 UI에서도 호출 가능.</summary>
    public void SelectEmployee(Employee employee)
    {
        if (_selectedEmployee == employee) return;
        _selectedEmployee = employee;
        OnEmployeeSelected?.Invoke(employee);
        Debug.Log($"[Interaction] 직원 선택: {employee.name}");
    }

    /// <summary>직원 선택을 해제합니다 (선택이 없으면 무시).</summary>
    public void DeselectEmployee()
    {
        if (_selectedEmployee == null) return;
        _selectedEmployee = null;
        OnEmployeeSelected?.Invoke(null);
    }
    
    private void OnBuildingClicked(Building building)
    {
        Debug.Log($"[Interaction] OnBuildingClicked 호출됨: {building.gameObject.name}");

        // 직원 채용 건물 처리
        HiringOffice hiringOffice = building.GetComponent<HiringOffice>();
        if (hiringOffice != null)
        {
            Debug.Log($"[Interaction] HiringOffice 발견. IsUsed={hiringOffice.IsUsed}, Candidates={hiringOffice.GetCandidates().Count}");

            if (hiringOffice.IsUsed)
            {
                Debug.Log("[Interaction] 채용 건물: 이미 채용이 완료되었습니다.");
                return;
            }

            if (UIManager.instance != null)
            {
                HiringPanel panel = UIManager.instance.GetPanel<HiringPanel>(UIPanelType.HiringUI);
                Debug.Log($"[Interaction] GetPanel<HiringPanel>: {(panel != null ? "찾음" : "null")}");
                if (panel != null)
                {
                    panel.Setup(hiringOffice, hiringOffice.GetCandidates());
                    UIManager.instance.ShowPanel(UIPanelType.HiringUI);
                    Debug.Log("[Interaction] HiringPanel 표시 완료");
                }
                else
                {
                    Debug.LogWarning("[Interaction] HiringPanel이 UIManager에 등록되지 않았습니다.");
                }
            }
            else
            {
                Debug.LogWarning("[Interaction] UIManager.instance가 null입니다.");
            }
            return;
        }

        Debug.Log($"[Interaction] 건물 클릭: {building.buildingData?.buildingName}");
    }
    
    private void OnHarvestableClicked(IHarvestable harvestable, GameObject obj)
    {
        Debug.Log($"[Interaction] 수확 가능 오브젝트 클릭: {obj.name}");
    }
    
    private void OnConstructionSiteClicked(ConstructionSite site)
    {
        if (site.WorkOrder != null && _workSystemManager != null)
        {
            _workSystemManager.ShowAssignmentUI(site.WorkOrder, null);
        }
    }
    
    #endregion

    #region 채광 모드

    /// <summary>
    /// 채광 모드 드래그 처리.
    /// - 좌클릭 드래그: 마우스를 떼는 순간 영역 내 채광 가능 타일로 즉시 WorkOrder 생성
    /// - 우클릭 드래그: 영역 내 기존 채광 작업 더미 취소
    /// </summary>
    private void HandleMineMode()
    {
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
        {
            if (IsPointerOverInteractiveUI()) return;
            _dragStartPos = _cameraController.GetMouseWorldPosition();
            _isDragging = true;
        }

        if (_isDragging)
        {
            _dragEndPos = _cameraController.GetMouseWorldPosition();
            UpdateSelectionBox();
        }

        if ((Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1)) && _isDragging)
        {
            bool isAdding = Input.GetMouseButtonUp(0);
            if (isAdding)
            {
                CreateMiningWorkOrderFromDragArea();
            }
            else
            {
                CancelMiningWorkOrdersInDragArea();
            }
            _isDragging = false;
            if (_selectionBox != null) _selectionBox.SetActive(false);
        }
    }

    /// <summary>
    /// 현재 드래그 영역(_dragStartPos~_dragEndPos)을 순회하여 채광 가능 타일을 모으고
    /// 즉시 단일 WorkOrder + WorkOrderVisual을 생성합니다.
    /// </summary>
    private void CreateMiningWorkOrderFromDragArea()
    {
        Vector3Int startCell = groundTilemap.WorldToCell(_dragStartPos);
        Vector3Int endCell = groundTilemap.WorldToCell(_dragEndPos);

        int minX = Mathf.Min(startCell.x, endCell.x);
        int maxX = Mathf.Max(startCell.x, endCell.x);
        int minY = Mathf.Min(startCell.y, endCell.y);
        int maxY = Mathf.Max(startCell.y, endCell.y);

        List<Vector3Int> tiles = new List<Vector3Int>();
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (CanMineTile(x, y))
                    tiles.Add(new Vector3Int(x, y, 0));
            }
        }

        if (tiles.Count == 0)
        {
            Debug.Log("[Interaction] 채광 가능한 타일이 영역 내에 없습니다.");
            return;
        }

        if (tiles.Count > maxTilesPerOrder)
        {
            Debug.LogWarning($"[Interaction] 선택 타일 {tiles.Count}개가 최대 {maxTilesPerOrder}개를 초과 → 앞 {maxTilesPerOrder}개만 사용");
            tiles = tiles.GetRange(0, maxTilesPerOrder);
        }

        CreateMiningWorkOrder(tiles);
        Debug.Log($"[Interaction] 채광 작업 더미 생성: {tiles.Count}개 타일");
    }

    /// <summary>
    /// 현재 드래그 영역과 겹치는 모든 채광 작업 더미를 취소합니다.
    /// (한 WorkOrder의 일부 타일만 영역에 포함되어도 해당 WorkOrder 전체를 취소)
    /// </summary>
    private void CancelMiningWorkOrdersInDragArea()
    {
        if (_workSystemManager == null) return;

        Vector3Int startCell = groundTilemap.WorldToCell(_dragStartPos);
        Vector3Int endCell = groundTilemap.WorldToCell(_dragEndPos);

        int minX = Mathf.Min(startCell.x, endCell.x);
        int maxX = Mathf.Max(startCell.x, endCell.x);
        int minY = Mathf.Min(startCell.y, endCell.y);
        int maxY = Mathf.Max(startCell.y, endCell.y);

        var miningOrders = _workSystemManager.AllOrders
            .Where(o => o.workType == WorkType.Mining && o.isActive)
            .ToList();

        int cancelled = 0;
        foreach (var order in miningOrders)
        {
            bool overlaps = false;
            foreach (var task in order.taskQueue.PendingTasks)
            {
                Vector3 pos = task.GetPosition();
                int px = Mathf.FloorToInt(pos.x);
                int py = Mathf.FloorToInt(pos.y);
                if (px >= minX && px <= maxX && py >= minY && py <= maxY)
                {
                    overlaps = true;
                    break;
                }
            }

            if (!overlaps)
            {
                foreach (var task in order.taskQueue.AssignedTasks)
                {
                    Vector3 pos = task.GetPosition();
                    int px = Mathf.FloorToInt(pos.x);
                    int py = Mathf.FloorToInt(pos.y);
                    if (px >= minX && px <= maxX && py >= minY && py <= maxY)
                    {
                        overlaps = true;
                        break;
                    }
                }
            }

            if (overlaps)
            {
                _workSystemManager.RemoveWorkOrder(order);
                cancelled++;
            }
        }

        if (cancelled > 0)
            Debug.Log($"[Interaction] 채광 작업 더미 {cancelled}개 취소");
    }

    private bool CanMineTile(int x, int y)
    {
        if (x < 0 || x >= GameMap.MAP_WIDTH || y < 0 || y >= GameMap.MAP_HEIGHT) return false;

        int tileID = _gameMap.TileGrid[x, y];
        if (tileID == 0 || tileID == 7) return false;

        if (_workSystemManager != null && _workSystemManager.IsTileUnderWork(new Vector3Int(x, y, 0)))
        {
            return false;
        }

        return true;
    }

    private void CreateMiningWorkOrder(List<Vector3Int> tiles)
    {
        if (_workSystemManager == null) return;

        WorkOrderVisual visual = _workSystemManager.CreateWorkOrderWithVisual(
            $"채광 작업 ({tiles.Count}개)",
            WorkType.Mining,
            maxWorkers: defaultMiningWorkers,
            tiles: tiles,
            priority: 3
        );

        if (visual != null)
        {
            WorkOrder workOrder = visual.WorkOrder;
            List<IWorkTarget> targets = new List<IWorkTarget>();

            foreach (var tile in tiles)
            {
                MiningOrder miningOrder = new MiningOrder
                {
                    position = tile,
                    tileID = _gameMap.TileGrid[tile.x, tile.y],
                    priority = 3,
                    completed = false,
                    assignedWorker = null
                };
                targets.Add(miningOrder);
            }
            workOrder.AddTargets(targets);
        }
        else
        {
            Debug.LogError("[Interaction] WorkOrderVisual 생성 실패 — WorkSystemManager 인스펙터에 workOrderVisualPrefab이 할당됐는지 확인하세요.");
        }
    }

    #endregion

    #region 수확 모드
    
    private void HandleHarvestMode()
    {
        if (Input.GetMouseButtonDown(0))
        {
            _dragStartPos = _cameraController.GetMouseWorldPosition();
            _isDragging = true;
        }
        else if (Input.GetMouseButton(0) && _isDragging)
        {
            _dragEndPos = _cameraController.GetMouseWorldPosition();
            UpdateHarvestSelection();
        }
        else if (Input.GetMouseButtonUp(0) && _isDragging)
        {
            FinishHarvestSelection();
            CancelDrag();
        }
        
        if (Input.GetMouseButtonDown(1)) CancelDrag();
    }
    
    private void UpdateHarvestSelection()
    {
        Bounds selectionBounds = GetSelectionBounds();
        foreach (var obj in _selectedObjects) 
        {
            if (obj != null) SetObjectHighlight(obj, false);
        }
        _selectedObjects.Clear();
        
        Collider2D[] colliders = Physics2D.OverlapBoxAll(selectionBounds.center, selectionBounds.size, 0f);
        int maxSelection = 50;
        int count = 0;
        
        foreach (var collider in colliders)
        {
            if (count >= maxSelection) break;
            IHarvestable harvestable = collider.GetComponent<IHarvestable>();
            if (harvestable != null && harvestable.CanHarvest())
            {
                _selectedObjects.Add(collider.gameObject);
                SetObjectHighlight(collider.gameObject, true);
                count++;
            }
        }
    }
    
    private void FinishHarvestSelection()
    {
        if (_selectedObjects.Count == 0) return;

        WorkType workType = DetermineHarvestWorkType(_selectedObjects[0]);
        
        List<Vector3Int> objectTiles = _selectedObjects
            .Select(obj => new Vector3Int(
                Mathf.FloorToInt(obj.transform.position.x), 
                Mathf.FloorToInt(obj.transform.position.y), 0))
            .ToList();
        
        WorkOrderVisual visual = _workSystemManager.CreateWorkOrderWithVisual(
            $"{GetWorkTypeName(workType)} 작업 ({_selectedObjects.Count}개)", 
            workType, 
            defaultHarvestWorkers, 
            objectTiles, 
            4
        );
            
        if (visual != null)
        {
            WorkOrder workOrder = visual.WorkOrder;
            List<IWorkTarget> targets = new List<IWorkTarget>();
            foreach (var obj in _selectedObjects)
            {
                IHarvestable harvestable = obj.GetComponent<IHarvestable>();
                if (harvestable != null)
                {
                    targets.Add(new HarvestOrder 
                    { 
                        target = harvestable, 
                        position = obj.transform.position, 
                        priority = 4 
                    });
                }
                SetObjectHighlight(obj, false);
            }
            workOrder.AddTargets(targets);
        }
        _selectedObjects.Clear();
    }
    
    private WorkType DetermineHarvestWorkType(GameObject obj)
    {
        if (obj.GetComponent<ChoppableTree>() != null) return WorkType.Chopping;
        return WorkType.Gardening;
    }
    
    private string GetWorkTypeName(WorkType type)
    {
        switch (type) 
        { 
            case WorkType.Chopping: return "벌목"; 
            case WorkType.Mining: return "채광"; 
            default: return "작업"; 
        }
    }
    
    private void SetObjectHighlight(GameObject obj, bool highlight)
    {
        SpriteRenderer sr = obj.GetComponent<SpriteRenderer>();
        if (sr != null) 
        {
            sr.color = highlight ? new Color(1.5f, 1.5f, 1.5f) : Color.white;
        }
    }
    
    #endregion

    #region 건설 모드
    
    private void HandleBuildMode()
    {
        // 배치 로직은 ConstructionManager가 처리
        // InteractionManager는 모드 전환과 UI 관리만 담당
        
        if (Input.GetMouseButtonDown(1))
        {
            if (_constructionManager != null && _constructionManager.IsPlacementMode)
            {
                _constructionManager.ExitPlacementMode();
            }
            else
            {
                SetMode(InteractMode.Normal);
            }
        }
    }
    
    #endregion

    #region 철거 모드
    
    private void HandleDemolishMode()
    {
        if (Input.GetMouseButtonDown(0))
        {
            _dragStartPos = _cameraController.GetMouseWorldPosition();
            _isDragging = true;
        }
        else if (Input.GetMouseButton(0) && _isDragging)
        {
            _dragEndPos = _cameraController.GetMouseWorldPosition();
            UpdateDemolishSelection();
        }
        else if (Input.GetMouseButtonUp(0) && _isDragging)
        {
            FinishDemolishSelection();
            CancelDrag();
        }
        if (Input.GetMouseButtonDown(1)) CancelDrag();
    }
    
    private void UpdateDemolishSelection()
    {
        Bounds selectionBounds = GetSelectionBounds();
        foreach (var obj in _selectedObjects) 
        {
            if (obj != null) SetObjectHighlight(obj, false);
        }
        _selectedObjects.Clear();
        
        Collider2D[] colliders = Physics2D.OverlapBoxAll(selectionBounds.center, selectionBounds.size, 0f);
        foreach (var collider in colliders)
        {
            Building building = collider.GetComponent<Building>();
            ConstructionSite constructionSite = collider.GetComponent<ConstructionSite>();
            
            if (building != null || constructionSite != null)
            {
                _selectedObjects.Add(collider.gameObject);
                SetObjectHighlight(collider.gameObject, true);
            }
        }
    }
    
    private void FinishDemolishSelection()
    {
        if (_selectedObjects.Count == 0) return;
        if (_workSystemManager == null) return;
        
        foreach (var obj in _selectedObjects)
        {
            // 건설 현장이면 즉시 취소 (자원 환불)
            ConstructionSite constructionSite = obj.GetComponent<ConstructionSite>();
            if (constructionSite != null)
            {
                if (_constructionManager != null)
                {
                    _constructionManager.CancelConstruction(constructionSite);
                }
                SetObjectHighlight(obj, false);
                continue;
            }
            
            // 완성된 건물이면 철거 작업 생성
            Building building = obj.GetComponent<Building>();
            if (building != null)
            {
                WorkOrder workOrder = _workSystemManager.CreateWorkOrder(
                    $"철거: {building.buildingData.buildingName}", 
                    WorkType.Demolish, 
                    defaultDemolishWorkers, 
                    6
                );
                workOrder.AddTarget(new DemolishOrder 
                { 
                    building = building, 
                    position = obj.transform.position, 
                    priority = 6 
                });
            }
            SetObjectHighlight(obj, false);
        }
        _selectedObjects.Clear();
    }
    
    #endregion

    #region 공통 유틸
    
    private void UpdateSelectionBox()
    {
        if (_selectionBox == null) return;
        _selectionBox.SetActive(true);
        Vector3 currentMousePos = _cameraController.GetMouseWorldPosition();
        Vector3 center = (_dragStartPos + currentMousePos) / 2f;
        Vector3 size = new Vector3(
            Mathf.Abs(currentMousePos.x - _dragStartPos.x), 
            Mathf.Abs(currentMousePos.y - _dragStartPos.y), 
            1f
        );
        _selectionBox.transform.position = center;
        _selectionBox.transform.localScale = size;
    }
    
    private Bounds GetSelectionBounds()
    {
        Vector3 center = (_dragStartPos + _dragEndPos) / 2f;
        Vector3 size = new Vector3(
            Mathf.Abs(_dragEndPos.x - _dragStartPos.x), 
            Mathf.Abs(_dragEndPos.y - _dragStartPos.y), 
            1f
        );
        return new Bounds(center, size);
    }
    
    private void CancelDrag()
    {
        _isDragging = false;
        if (_selectionBox != null) _selectionBox.SetActive(false);
        foreach (var obj in _selectedObjects) 
        {
            if (obj != null) SetObjectHighlight(obj, false);
        }
        _selectedObjects.Clear();
    }
    
    private void ExecuteAllOrdersInstantly()
    {
        if (_workSystemManager == null) return;
        var allOrders = _workSystemManager.AllOrders.ToList();
        foreach (var workOrder in allOrders)
        {
            foreach (var target in workOrder.targets.ToList())
            {
                if (target is MiningOrder miningOrder)
                {
                    int x = miningOrder.position.x;
                    int y = miningOrder.position.y;
                    GameObject dropPrefab = _resourceManager.GetDropPrefab(miningOrder.tileID);
                    if (dropPrefab != null)
                    {
                        Instantiate(dropPrefab, 
                            groundTilemap.CellToWorld(miningOrder.position) + new Vector3(0.5f, 0.5f, 0), 
                            Quaternion.identity, 
                            itemDropParent);
                    }
                    _gameMap.SetTile(x, y, 0);
                    _gameMap.UnmarkTileOccupied(x, y);
                    _mapRenderer.UpdateTileVisual(x, y);
                }
                workOrder.CompleteTarget(target, null);
            }
            _workSystemManager.RemoveWorkOrder(workOrder);
        }
    }
    
    #endregion
}