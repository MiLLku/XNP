using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// 작업 시스템 통합 매니저
/// - 작업물(WorkOrder) 생성/삭제/관리
/// - 직원 등록/할당/작업 완료 처리
/// - 작업 할당 UI 관리
/// </summary>
public class WorkSystemManager : DestroySingleton<WorkSystemManager>, ISaveModule
{
    #region 직렬화 필드
    
    [Header("작업물 관리")]
    [SerializeField] private List<WorkOrder> allOrders = new List<WorkOrder>();
    [SerializeField] private int nextOrderId = 1;
    
    [Header("비주얼 설정")]
    [SerializeField] private GameObject workOrderVisualPrefab;
    [SerializeField] private Transform visualParent;
    
    [Header("직원 관리")]
    [SerializeField] private List<Employee> allEmployees = new List<Employee>();

    [Header("디버그")]
    [SerializeField] private bool showDebugInfo = true;

    #endregion

    #region 내부 변수

    // 작업물 ID -> 비주얼 매핑
    private Dictionary<int, WorkOrderVisual> orderVisuals = new Dictionary<int, WorkOrderVisual>();

    // 직원별 현재 작업물 매핑
    private Dictionary<Employee, WorkOrder> employeeToOrderMap = new Dictionary<Employee, WorkOrder>();

    // 직원별 현재 작업 Task 매핑
    private Dictionary<Employee, WorkTask> employeeToTaskMap = new Dictionary<Employee, WorkTask>();

    // UI 관련
    private WorkAssignmentPanel workAssignmentPanel;
    private WorkOrder currentUIOrder;
    private WorkOrderVisual currentUIVisual;
    
    #endregion
    
    #region Unity 생명주기
    
    protected override void Awake()
    {
        base.Awake();
        
        // 비주얼 부모 오브젝트 생성
        if (visualParent == null)
        {
            GameObject parent = new GameObject("WorkOrderVisuals");
            visualParent = parent.transform;
        }
    }
    
    void Start()
    {
        // EmployeeManager 연동
        if (EmployeeManager.instance != null)
        {
            foreach (var employee in EmployeeManager.instance.AllEmployees)
            {
                RegisterEmployee(employee);
            }
            
            EmployeeManager.instance.OnEmployeeSpawned += RegisterEmployee;
            EmployeeManager.instance.OnEmployeeRemoved += UnregisterEmployee;
            
            if (showDebugInfo)
            {
                Debug.Log($"[WorkSystemManager] 직원 {EmployeeManager.instance.EmployeeCount}명 등록 완료");
            }
        }
    }
    
    void Update()
    {
        // 주기적으로 완료된 작업물 정리 (5초마다)
        if (Time.frameCount % 300 == 0)
        {
            CleanupCompletedOrders();
            CleanupInvalidTasks();
        }
    }
    
    void OnDestroy()
    {
        if (EmployeeManager.instance != null)
        {
            EmployeeManager.instance.OnEmployeeSpawned -= RegisterEmployee;
            EmployeeManager.instance.OnEmployeeRemoved -= UnregisterEmployee;
        }
    }
    
    #endregion
    
    #region 작업물(WorkOrder) 관리
    
    /// <summary>
    /// 새 작업물을 생성합니다.
    /// </summary>
    public WorkOrder CreateWorkOrder(string name, WorkType workType, int maxWorkers, int priority = 5)
    {
        WorkOrder order = new WorkOrder
        {
            orderId = nextOrderId++,
            orderName = name,
            workType = workType,
            maxAssignedWorkers = maxWorkers,
            priority = priority,
            createdTime = Time.time,
            isActive = true,
            isPaused = false
        };
        
        allOrders.Add(order);
        
        if (showDebugInfo)
        {
            Debug.Log($"[WorkSystemManager] 작업물 생성: {name} (ID: {order.orderId})");
        }
        
        return order;
    }
    
    /// <summary>
    /// 작업물에 비주얼을 생성합니다 (타일 위치 포함).
    /// </summary>
    public WorkOrderVisual CreateWorkOrderWithVisual(string name, WorkType workType, int maxWorkers, 
        List<Vector3Int> tiles, int priority = 5)
    {
        WorkOrder order = CreateWorkOrder(name, workType, maxWorkers, priority);
        
        if (workOrderVisualPrefab == null)
        {
            Debug.LogWarning("[WorkSystemManager] WorkOrderVisual 프리팹이 설정되지 않았습니다!");
            return null;
        }
        
        // 비주얼 오브젝트 생성
        GameObject visualObj = Instantiate(workOrderVisualPrefab, visualParent);
        visualObj.name = $"WorkOrder_{order.orderId}_{name}";
        
        WorkOrderVisual visual = visualObj.GetComponent<WorkOrderVisual>();
        if (visual != null)
        {
            visual.Initialize(order, tiles);
            orderVisuals[order.orderId] = visual;
            
            if (showDebugInfo)
            {
                Debug.Log($"[WorkSystemManager] 작업물 비주얼 생성: {name} (타일 {tiles.Count}개)");
            }
        }
        
        return visual;
    }
    
    /// <summary>
    /// 특정 좌표가 현재 진행 중인 작업에 포함되어 있는지 확인합니다.
    /// </summary>
    public bool IsTileUnderWork(Vector3Int tilePos)
    {
        foreach (var order in allOrders)
        {
            if (!order.isActive || order.IsCompleted()) continue;
            
            if (order.taskQueue.HasTaskAtPosition(tilePos))
            {
                return true;
            }
        }
        return false;
    }
    
    /// <summary>
    /// 작업물을 삭제합니다.
    /// </summary>
    public void RemoveWorkOrder(WorkOrder order, bool isCancellation = true)
    {
        if (order == null) return;

        if (isCancellation)
        {
            order.Cancel();
        }
        allOrders.Remove(order);
        
        // 비주얼도 삭제
        if (orderVisuals.ContainsKey(order.orderId))
        {
            WorkOrderVisual visual = orderVisuals[order.orderId];
            if (visual != null)
            {
                Destroy(visual.gameObject);
            }
            orderVisuals.Remove(order.orderId);
        }
        
        // 해당 작업물에 할당된 직원들 매핑 제거
        var employeesToRemove = employeeToOrderMap
            .Where(kvp => kvp.Value == order)
            .Select(kvp => kvp.Key)
            .ToList();
            
        foreach (var emp in employeesToRemove)
        {
            employeeToOrderMap.Remove(emp);
            employeeToTaskMap.Remove(emp);
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"[WorkSystemManager] 작업물 삭제: {order.orderName}");
        }
    }
    
    /// <summary>
    /// 모든 작업물을 제거합니다 (로드 시 초기화용).
    /// </summary>
    public void ClearAllOrders()
    {
        foreach (var order in allOrders.ToList())
        {
            order.Cancel();
        }
        allOrders.Clear();

        foreach (var visual in orderVisuals.Values)
        {
            if (visual != null)
            {
                Destroy(visual.gameObject);
            }
        }
        orderVisuals.Clear();
        employeeToOrderMap.Clear();
        employeeToTaskMap.Clear();
        nextOrderId = 1;

        if (showDebugInfo)
        {
            Debug.Log("[WorkSystemManager] 모든 작업물 초기화 완료");
        }
    }

    /// <summary>
    /// 완료된 작업물을 자동으로 제거합니다.
    /// </summary>
    public void CleanupCompletedOrders()
    {
        var completedOrders = allOrders.Where(o => o.IsCompleted()).ToList();
        
        foreach (var order in completedOrders)
        {
            if (showDebugInfo)
            {
                Debug.Log($"[WorkSystemManager] 작업물 완료: {order.orderName}");
            }
            RemoveWorkOrder(order, isCancellation: false);
        }
    }

    /// <summary>
    /// 유효하지 않은 작업 정리
    /// </summary>
    public void CleanupInvalidTasks()
    {
        foreach (var order in allOrders)
        {
            order.CleanupInvalidTasks();
        }
    }
    
    /// <summary>
    /// 우선순위별로 정렬된 활성 작업물 목록을 반환합니다.
    /// </summary>
    public List<WorkOrder> GetActiveOrders()
    {
        return allOrders
            .Where(o => o.isActive && !o.isPaused)
            .OrderBy(o => o.priority)
            .ThenBy(o => o.createdTime)
            .ToList();
    }
    
    /// <summary>
    /// 특정 작업 타입의 작업물을 반환합니다.
    /// </summary>
    public List<WorkOrder> GetOrdersByType(WorkType type)
    {
        return allOrders.Where(o => o.workType == type && o.isActive && !o.isPaused).ToList();
    }
    
    /// <summary>
    /// ID로 작업물을 찾습니다.
    /// </summary>
    public WorkOrder GetOrderById(int id)
    {
        return allOrders.FirstOrDefault(o => o.orderId == id);
    }
    
    /// <summary>
    /// ID로 작업물 비주얼을 찾습니다.
    /// </summary>
    public WorkOrderVisual GetVisualById(int id)
    {
        orderVisuals.TryGetValue(id, out WorkOrderVisual visual);
        return visual;
    }
    
    #endregion
    
    #region 직원 관리
    
    public void RegisterEmployee(Employee employee)
    {
        if (employee == null || allEmployees.Contains(employee)) return;
        
        allEmployees.Add(employee);
        
        if (showDebugInfo)
        {
            Debug.Log($"[WorkSystemManager] 직원 등록: {employee.Data.employeeName}");
        }
    }
    
    public void UnregisterEmployee(Employee employee)
    {
        if (employee == null || !allEmployees.Contains(employee)) return;
        
        allEmployees.Remove(employee);
        
        // 해당 직원이 작업 중이던 작업물에서 제거
        if (employeeToOrderMap.ContainsKey(employee))
        {
            WorkOrder order = employeeToOrderMap[employee];
            order.UnassignWorker(employee);
            employeeToOrderMap.Remove(employee);
        }
        
        employeeToTaskMap.Remove(employee);
        
        if (showDebugInfo)
        {
            Debug.Log($"[WorkSystemManager] 직원 제거: {employee.Data.employeeName}");
        }
    }
    
    #endregion
    
    #region 작업 할당

    /// <summary>
    /// AI가 호출: 직원에게 적합한 작업을 자동으로 찾아 할당합니다.
    /// workZoneId가 유효하면 해당 구역 내 태스크를 우선 탐색합니다.
    /// 구역 내 태스크가 없으면 구역 미할당 상태에서 전체 탐색으로 fallback합니다.
    /// </summary>
    public bool TryAssignWorkToEmployee(Employee employee, int workZoneId = -1)
    {
        if (employee == null)
        {
            if (showDebugInfo) Debug.LogWarning("[WorkSystemManager] TryAssign 실패: employee=null");
            return false;
        }
        if (!employee.IsAvailableForWork)
        {
            if (showDebugInfo)
                Debug.Log($"[WorkSystemManager] TryAssign 스킵 ({employee.Data?.employeeName}): " +
                          $"IsAvailableForWork=false, state={employee.State}");
            return false;
        }
        if (employeeToOrderMap.ContainsKey(employee))
        {
            if (showDebugInfo)
                Debug.Log($"[WorkSystemManager] TryAssign 스킵 ({employee.Data?.employeeName}): " +
                          $"이미 작업 중 (order='{employeeToOrderMap[employee].orderName}')");
            return false;
        }

        List<WorkType> enabledTypes = employee.GetEnabledWorkTypes();
        if (showDebugInfo)
        {
            Debug.Log($"[WorkSystemManager] {employee.Data?.employeeName} 작업 할당 시작. " +
                      $"활성화된 작업: [{string.Join(",", enabledTypes)}], 전체 작업물 수={allOrders.Count}");
        }

        // 구역 지정: 구역 내 태스크 우선 시도
        Zone workZone = (workZoneId >= 0 && ZoneManager.instance != null)
            ? ZoneManager.instance.GetZone(workZoneId)
            : null;

        if (workZone != null)
        {
            if (TryAssignInZone(employee, enabledTypes, workZone))
                return true;
            // 구역 내 작업 없음 → fallback
            if (showDebugInfo)
                Debug.Log($"[WorkSystemManager] 구역 내 작업 없음 → 전체 탐색으로 fallback");
        }

        // 전체 탐색 (구역 미지정 또는 구역 내 작업 없음)
        return TryAssignGlobal(employee, enabledTypes);
    }

    /// <summary>구역 내 태스크만 대상으로 할당을 시도합니다.</summary>
    private bool TryAssignInZone(Employee employee, List<WorkType> enabledTypes, Zone zone)
    {
        var candidates = GetCandidateOrders(employee, enabledTypes).ToList();
        if (showDebugInfo)
            Debug.Log($"[WorkSystemManager] 구역 내 후보 작업물 수={candidates.Count}");

        foreach (var order in candidates)
        {
            // 작업물 내에 구역 내 태스크가 하나라도 있는지 확인
            int zoneTaskCount = order.GetPendingTasksInZone(zone).Count;
            if (zoneTaskCount == 0)
            {
                if (showDebugInfo)
                    Debug.Log($"[WorkSystemManager]   '{order.orderName}': 구역 내 태스크 없음, 스킵");
                continue;
            }

            if (AssignEmployeeToOrderInZone(employee, order, zone))
                return true;
        }
        return false;
    }

    /// <summary>전체 작업물에서 할당을 시도합니다.</summary>
    private bool TryAssignGlobal(Employee employee, List<WorkType> enabledTypes)
    {
        var candidates = GetCandidateOrders(employee, enabledTypes).ToList();
        if (showDebugInfo)
            Debug.Log($"[WorkSystemManager] 전체 후보 작업물 수={candidates.Count}");

        if (candidates.Count == 0)
        {
            if (showDebugInfo)
            {
                // 후보가 0인 이유 추적 (어떤 작업물이 왜 걸러졌는지)
                foreach (var order in allOrders)
                {
                    string reason = "";
                    if (!enabledTypes.Contains(order.workType)) reason += $"타입{order.workType}활성안됨,";
                    if (!order.isActive) reason += "비활성,";
                    if (order.isPaused) reason += "일시정지,";
                    if (!order.CanAssignWorker()) reason += $"할당불가(pending={order.taskQueue.PendingCount},workers={order.assignedWorkers.Count}/{order.maxAssignedWorkers}),";
                    if (!string.IsNullOrEmpty(reason))
                        Debug.Log($"[WorkSystemManager]   '{order.orderName}' 제외: {reason}");
                }
            }
            return false;
        }

        foreach (var order in candidates)
        {
            if (AssignEmployeeToOrder(employee, order))
                return true;
        }

        if (showDebugInfo)
            Debug.LogWarning($"[WorkSystemManager] {employee.Data?.employeeName}: 모든 후보 작업물 할당 실패");
        return false;
    }

    /// <summary>
    /// 할당 가능한 후보 작업물을 우선순위순으로 반환합니다.
    /// - 자동 픽업 작업(채광/건설/벌목 등): 자격 직원이면 모두 후보.
    /// - 전용 할당 작업(연구/제작 등): 미리 명시적으로 등록된 직원만 후보.
    ///
    /// 정렬 규칙:
    ///   1) 직원이 설정한 작업별 우선순위 (1~9, 낮을수록 먼저)
    ///   2) 동률이면 작업 종류의 기본 우선순위 (WorkTypeDefaults.BaseOrder)
    ///   3) 같은 작업 종류 안에서는 작업물 우선순위 (order.priority)
    ///   4) 그래도 같으면 먼저 만들어진 작업물부터
    /// </summary>
    private IEnumerable<WorkOrder> GetCandidateOrders(Employee employee, List<WorkType> enabledTypes)
    {
        return enabledTypes
            .Where(t => employee.CanPerformWork(t))
            .SelectMany(t => allOrders.Where(o =>
                o.workType == t && o.isActive && !o.isPaused && o.CanAssignWorker() &&
                // 전용 할당 작업물은 사전 등록된 직원만 자격을 가짐
                (o.IsAutoPickup || o.IsWorkerAssigned(employee))
            ))
            .OrderBy(o => employee.GetWorkPriority(o.workType))
            .ThenBy(o => WorkTypeDefaults.GetBasePriority(o.workType))
            .ThenBy(o => o.priority)
            .ThenBy(o => o.createdTime);
    }

    /// <summary>
    /// 플레이어가 특정 직원을 특정 작업물에 수동으로 할당합니다 (구역 필터 없음).
    /// </summary>
    public bool AssignEmployeeToOrder(Employee employee, WorkOrder order)
        => AssignEmployeeToOrderInZone(employee, order, null);

    /// <summary>
    /// 특정 직원을 특정 작업물에 할당합니다.
    /// zone이 null이 아니면 구역 내 태스크만 선택합니다.
    ///
    /// 직원이 이미 다른 작업물에 할당되어 있으면, 이전 할당을 완전히 정리한 후 새 작업물에 할당합니다.
    /// (이전 작업물의 assignedWorkers에 ghost 직원이 남는 버그 방지)
    /// </summary>
    public bool AssignEmployeeToOrderInZone(Employee employee, WorkOrder order, Zone zone)
    {
        if (employee == null || order == null)
        {
            Debug.LogWarning("[WorkSystemManager] 직원 또는 작업물이 null입니다.");
            return false;
        }

        if (!employee.CanPerformWork(order.workType))
        {
            Debug.LogWarning($"[WorkSystemManager] {employee.Data?.employeeName}: " +
                $"{order.workType} 작업 수행 불가 (CanPerformWork=false). " +
                $"work={employee.GetComponent<EmployeeWork>() != null}, " +
                $"state={employee.State}");
            return false;
        }

        if (!order.CanAssignWorker())
        {
            Debug.LogWarning($"[WorkSystemManager] '{order.orderName}' CanAssignWorker=false. " +
                $"isActive={order.isActive}, isPaused={order.isPaused}, " +
                $"pendingTasks={order.taskQueue.PendingCount}, " +
                $"workers={order.assignedWorkers.Count}/{order.maxAssignedWorkers}, " +
                $"autoPickup={order.IsAutoPickup}");
            return false;
        }

        // 이미 다른 작업물에 매핑되어 있으면 완전히 정리 (ghost 매핑 방지)
        if (employeeToOrderMap.TryGetValue(employee, out WorkOrder previousOrder) && previousOrder != order)
        {
            if (showDebugInfo)
                Debug.Log($"[WorkSystemManager] {employee.Data?.employeeName}: 이전 작업물 '{previousOrder.orderName}' 정리 후 '{order.orderName}'으로 재할당");
            UnassignEmployee(employee);
        }

        // 현재 진행 중인 작업이 있으면 취소 (Working/Moving 모두)
        if (employee.State == EmployeeState.Working || employee.State == EmployeeState.Moving)
            employee.CancelWork();

        if (!order.AssignWorker(employee))
            return false;

        employeeToOrderMap[employee] = order;

        if (AssignNextTaskFromQueue(employee, order, zone))
        {
            if (showDebugInfo)
                Debug.Log($"[WorkSystemManager] {employee.Data.employeeName} → '{order.orderName}' 할당 완료");
            return true;
        }

        // 태스크 할당 실패 시 롤백
        order.UnassignWorker(employee);
        employeeToOrderMap.Remove(employee);
        return false;
    }

    /// <summary>
    /// 작업물의 큐에서 직원에게 다음 작업을 할당합니다.
    /// zone이 null이 아니면 구역 내 태스크만 선택합니다.
    /// </summary>
    private bool AssignNextTaskFromQueue(Employee worker, WorkOrder order, Zone zone = null)
    {
        WorkTask task = zone != null
            ? order.AssignNextTaskToWorkerInZone(worker, zone)
            : order.AssignNextTaskToWorker(worker);

        if (task == null)
        {
            if (showDebugInfo)
            {
                Debug.LogWarning($"[WorkSystemManager] '{order.orderName}'에 할당 가능한 작업 없음{(zone != null ? " (구역 내)" : "")}");

                // 진단: pending tasks가 왜 모두 제외됐는지 출력
                var pendings = order.taskQueue?.PendingTasks;
                if (pendings != null && pendings.Count > 0)
                {
                    Debug.Log($"  └─ pendingTasks={pendings.Count}개 — 제외 이유:");
                    foreach (var t in pendings)
                    {
                        string reason;
                        if (t.target == null) reason = "target=null";
                        else if (t.state == WorkTask.TaskState.Completed) reason = "state=Completed";
                        else if (t.state == WorkTask.TaskState.Cancelled) reason = "state=Cancelled";
                        else if (!t.target.IsWorkAvailable()) reason = $"IsWorkAvailable=false ({t.target.GetType().Name})";
                        else if (Time.time < t.nextRetryTime) reason = $"cooldown {(t.nextRetryTime - Time.time):F1}s 남음";
                        else reason = "알 수 없음(zone 필터?)";
                        Debug.Log($"     task {t.taskId} @ {t.GetPosition()}: {reason}");
                    }
                }
                else
                {
                    Debug.Log($"  └─ pendingTasks=0 (할당 가능한 후보 자체가 없음)");
                }
            }
            return false;
        }

        employeeToTaskMap[worker] = task;
        task.Start();

        if (showDebugInfo)
            Debug.Log($"[WorkSystemManager] {worker.Data.employeeName} → Task {task.taskId} @ {task.GetPosition()}");

        worker.AssignWork(order, task.target);
        return true;
    }
    
    /// <summary>
    /// 직원의 할당을 해제합니다.
    /// </summary>
    public void UnassignEmployee(Employee employee)
    {
        if (employee == null) return;
        
        // 현재 작업 Task가 있으면 할당 해제
        if (employeeToTaskMap.TryGetValue(employee, out WorkTask task))
        {
            task.Unassign();
            employeeToTaskMap.Remove(employee);
        }
        
        if (employeeToOrderMap.TryGetValue(employee, out WorkOrder order))
        {
            order.UnassignWorker(employee);
            employeeToOrderMap.Remove(employee);
        }
        
        employee.CancelWork();
    }
    
    #endregion
    
    #region 작업 완료 처리
    
    /// <summary>
    /// 직원이 작업을 완료했을 때 호출됩니다.
    /// </summary>
    public void OnWorkerCompletedTarget(Employee worker, IWorkTarget target, WorkOrder order)
    {
        if (worker == null || order == null) return;
        
        // 현재 작업 Task 찾기
        WorkTask completedTask = null;
        if (employeeToTaskMap.TryGetValue(worker, out completedTask))
        {
            // 작업물의 큐에서 완료 처리
            order.CompleteTask(completedTask);
            employeeToTaskMap.Remove(worker);
        }
        else
        {
            // 기존 방식으로 폴백
            order.CompleteTarget(target, worker);
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"[WorkSystemManager] {worker.Data.employeeName}이(가) 작업 완료. " +
                     $"진행률: {order.GetProgress() * 100:F0}%");
        }
        
        // 작업물이 완전히 완료되었는지 확인
        if (order.IsCompleted())
        {
            if (showDebugInfo)
            {
                Debug.Log($"[WorkSystemManager] 작업물 '{order.orderName}' 완전 완료!");
            }

            order.UnassignWorker(worker);
            employeeToOrderMap.Remove(worker);
            employeeToTaskMap.Remove(worker);

            RemoveWorkOrder(order, isCancellation: false);

            // 직원이 다음 작업을 EmployeeAI에서 가져올 수 있도록 Idle로 전환
            worker.SetState(EmployeeState.Idle);
            return;
        }

        // 같은 작업물에서 다음 작업 할당 (큐에서)
        if (showDebugInfo)
        {
            Debug.Log($"[WorkSystemManager] {worker.Data.employeeName}에게 다음 작업 할당 시도...");
        }

        // ★ 직원이 낙하 중이거나 발 아래 바닥이 없으면 착지 후 재시도
        var movement = worker.GetComponent<EmployeeMovement>();
        bool needsToWaitForLanding = false;
        
        if (movement != null)
        {
            if (movement.IsFalling)
            {
                needsToWaitForLanding = true;
                Debug.Log($"[WorkSystemManager] {worker.Data.employeeName}: 낙하 중!");
            }
            else
            {
                // 발 아래 바닥 체크 (타일이 제거되었을 수 있음)
                Vector2Int footTile = movement.GetFootTile();
                Vector2Int groundTile = new Vector2Int(footTile.x, footTile.y - 1);
                var gameMap = MapGenerator.instance?.GameMapInstance;
                
                if (gameMap != null && groundTile.y >= 0 &&
                    groundTile.x >= 0 && groundTile.x < GameMap.MAP_WIDTH)
                {
                    int groundTileId = gameMap.TileGrid[groundTile.x, groundTile.y];
                    bool hasFloor = FloorTile.HasFloorTileAt(groundTile);
                    // 건설된 바닥 타일도 지면으로 인정 (TileGrid엔 없지만 OccupiedGrid에 있음)
                    bool hasConstructedFloor = gameMap.IsTileOccupied(groundTile.x, groundTile.y)
                                              && !gameMap.DoesTileBlockMovement(groundTile.x, groundTile.y);

                    if (groundTileId == 0 && !hasFloor && !hasConstructedFloor)
                    {
                        needsToWaitForLanding = true;
                        Debug.Log($"[WorkSystemManager] {worker.Data.employeeName}: 발 아래 바닥 없음! ({groundTile})");
                    }
                }
            }
        }
        
        if (needsToWaitForLanding && movement != null)
        {
            Debug.Log($"[WorkSystemManager] {worker.Data.employeeName}: 착지 후 재시도 예약");
            
            // 착지 이벤트에 한 번만 구독
            System.Action<Vector2Int> onLandedHandler = null;
            onLandedHandler = (landedFootTile) =>
            {
                movement.OnLanded -= onLandedHandler;
                Debug.Log($"[WorkSystemManager] {worker.Data.employeeName}: 착지 완료 at {landedFootTile}. 다음 작업 할당 시도");
                
                // 착지 후 다음 작업 할당
                if (!AssignNextTaskFromQueue(worker, order))
                {
                    if (showDebugInfo)
                    {
                        Debug.Log($"[WorkSystemManager] {worker.Data.employeeName}: 착지 후에도 할당할 작업 없음. 작업자 해제.");
                    }
                    order.UnassignWorker(worker);
                    employeeToOrderMap.Remove(worker);
                    employeeToTaskMap.Remove(worker);
                    worker.SetState(EmployeeState.Idle);
                }
            };
            movement.OnLanded += onLandedHandler;
            return;
        }

        if (!AssignNextTaskFromQueue(worker, order))
        {
            // 더 이상 할당할 작업이 없으면 작업자 해제
            if (showDebugInfo)
            {
                Debug.Log($"[WorkSystemManager] {worker.Data.employeeName}: 더 이상 할당할 작업 없음. 작업자 해제.");
            }
            order.UnassignWorker(worker);
            employeeToOrderMap.Remove(worker);
            employeeToTaskMap.Remove(worker);

            // 직원이 다음 작업(다른 WorkOrder)을 EmployeeAI에서 가져올 수 있도록 Idle로 전환
            worker.SetState(EmployeeState.Idle);
        }
        else
        {
            if (showDebugInfo)
            {
                Debug.Log($"[WorkSystemManager] {worker.Data.employeeName}에게 다음 작업 할당 성공!");
            }
        }
    }
    
    /// <summary>
    /// 직원이 작업을 취소했을 때 호출됩니다.
    /// </summary>
    public void OnWorkerCancelledWork(Employee worker)
    {
        if (worker == null) return;

        // ★ 순서 중요: order.UnassignWorker를 먼저 호출해야 합니다.
        //
        // taskQueue.UnassignWorkerTasks(worker)는 assignedTasks 중
        // t.assignedWorker == worker 인 태스크를 찾아 pending으로 되돌립니다.
        // task.Unassign()을 먼저 호출하면 assignedWorker가 null로 초기화되어
        // UnassignWorkerTasks가 해당 태스크를 찾지 못하고 assignedTasks에 좀비로 남습니다.
        // 결과: pendingTasks=0 이지만 태스크는 영영 사라지지 않는 버그 발생.

        // 재시도 쿨다운 설정 (도달불가 타일 무한재시도 방지)
        if (employeeToTaskMap.TryGetValue(worker, out WorkTask task))
        {
            task.nextRetryTime = Time.time + WorkTask.RETRY_COOLDOWN;
        }

        // UnassignWorker → UnassignWorkerTasks → task.Unassign() + assigned→pending 이동
        if (employeeToOrderMap.TryGetValue(worker, out WorkOrder order))
        {
            order.UnassignWorker(worker);
            employeeToOrderMap.Remove(worker);
        }

        employeeToTaskMap.Remove(worker);

        if (showDebugInfo)
        {
            Debug.Log($"[WorkSystemManager] {worker.Data.employeeName}의 작업 취소");
        }
    }
    
    #endregion
    
    #region UI 관리
    
    /// <summary>
    /// 작업 할당 UI를 표시합니다. (WorkOrderVisual에서 호출)
    /// UIManager를 통해 WorkAssignmentPanel을 표시합니다.
    /// </summary>
    public void ShowAssignmentUI(WorkOrder order, WorkOrderVisual visual)
    {
        CloseAssignmentUI();

        currentUIOrder = order;
        currentUIVisual = visual;

        // UIManager를 통해 패널 가져오기
        if (UIManager.instance != null)
        {
            workAssignmentPanel = UIManager.instance.GetPanel<WorkAssignmentPanel>(UIPanelType.WorkAssignment);

            if (workAssignmentPanel != null)
            {
                // UI 초기화 (내용 갱신)
                workAssignmentPanel.Setup(currentUIOrder, OnWorkerToggled, CloseAssignmentUI, OnCancelOrder);

                // 패널 표시
                UIManager.instance.ShowPanel(UIPanelType.WorkAssignment);

                if (showDebugInfo)
                {
                    Debug.Log($"[WorkSystemManager] 작업 할당 UI 열기: {order.orderName}");
                }
            }
            else
            {
                Debug.LogError("[WorkSystemManager] WorkAssignmentPanel을 찾을 수 없습니다. UIManager에 등록되어 있는지 확인하세요.");
            }
        }
        else
        {
            Debug.LogError("[WorkSystemManager] UIManager 인스턴스를 찾을 수 없습니다.");
        }
    }
    
    private void OnWorkerToggled(Employee employee)
    {
        if (currentUIOrder == null) return;

        // 작업물이 이미 비활성/완료 상태면 UI를 닫고 무시 (잔존 패널 정리)
        if (!currentUIOrder.isActive || currentUIOrder.IsCompleted())
        {
            Debug.Log($"[WorkSystemManager] 작업물 '{currentUIOrder.orderName}' 이미 비활성/완료 — UI 자동 닫기");
            CloseAssignmentUI();
            return;
        }

        if (currentUIOrder.IsWorkerAssigned(employee))
        {
            // 할당 해제
            UnassignEmployee(employee);
        }
        else
        {
            // 자동 픽업 작업물은 maxAssignedWorkers 제한 없음
            bool canAdd = currentUIOrder.IsAutoPickup ||
                          currentUIOrder.assignedWorkers.Count < currentUIOrder.maxAssignedWorkers;

            if (canAdd)
            {
                bool success = AssignEmployeeToOrder(employee, currentUIOrder);
                if (!success)
                {
                    Debug.LogWarning("[WorkSystemManager] 할당 실패");
                }
            }
            else
            {
                Debug.Log("[WorkSystemManager] 인원 가득 참");
            }
        }

        // UI 갱신
        workAssignmentPanel?.RefreshUI();
    }

    private void OnCancelOrder()
    {
        if (currentUIOrder != null)
        {
            Debug.Log($"[WorkSystemManager] 작업 취소 요청: {currentUIOrder.orderName}");
            RemoveWorkOrder(currentUIOrder);
            CloseAssignmentUI();
        }
    }

    public void CloseAssignmentUI()
    {
        // UIManager를 통해 패널 닫기
        if (UIManager.instance != null)
        {
            UIManager.instance.HidePanel(UIPanelType.WorkAssignment);
        }

        if (currentUIVisual != null)
        {
            currentUIVisual.Deselect();
            currentUIVisual = null;
        }

        currentUIOrder = null;
        workAssignmentPanel = null;
    }

    #endregion
    
    #region 유틸리티
    
    /// <summary>
    /// 특정 직원의 현재 작업물 반환
    /// </summary>
    public WorkOrder GetEmployeeOrder(Employee employee)
    {
        employeeToOrderMap.TryGetValue(employee, out WorkOrder order);
        return order;
    }
    
    /// <summary>
    /// 특정 직원의 현재 작업 Task 반환
    /// </summary>
    public WorkTask GetEmployeeTask(Employee employee)
    {
        employeeToTaskMap.TryGetValue(employee, out WorkTask task);
        return task;
    }
    
    /// <summary>
    /// 작업 통계 반환
    /// </summary>
    public WorkStatistics GetStatistics()
    {
        int pendingTasks = 0;
        int completedTasks = 0;
        
        foreach (var order in allOrders)
        {
            pendingTasks += order.taskQueue.PendingCount;
            completedTasks += order.taskQueue.CompletedCount;
        }
        
        return new WorkStatistics
        {
            totalEmployees = allEmployees.Count,
            idleEmployees = allEmployees.Count(e => e.State == EmployeeState.Idle),
            workingEmployees = allEmployees.Count(e => e.State == EmployeeState.Working),
            activeOrders = ActiveOrderCount,
            pendingTasks = pendingTasks,
            completedTasks = completedTasks
        };
    }
    
    #endregion
    
    #region Public 프로퍼티

    public List<WorkOrder> AllOrders => allOrders;
    public int ActiveOrderCount => allOrders.Count(o => o.isActive && !o.isPaused);
    public List<Employee> AllEmployees => allEmployees;
    public List<Employee> IdleEmployees => allEmployees.Where(e => e.State == EmployeeState.Idle).ToList();

    #endregion

    #region ISaveModule 구현

    public int SaveOrder => 60;

    public void Capture(SaveData data)
    {
        data.workOrders = new List<WorkOrderSaveData>();
        data.nextOrderId = nextOrderId;
        data.nextTaskId = WorkTask.NextTaskId;

        foreach (var order in allOrders)
        {
            if (!order.isActive) continue;
            // Haul(운반) 작업물은 저장하지 않습니다.
            // DroppedItemSaveModule이 아이템 위치를 저장하고,
            // 복원 시 DroppedItem.Start() → DroppedItemManager.Register() → HaulOrder 자동 재생성됩니다.
            if (order.workType == WorkType.Hauling) continue;

            var orderData = new WorkOrderSaveData
            {
                orderId = order.orderId,
                orderName = order.orderName,
                workType = (int)order.workType,
                priority = order.priority,
                createdTime = order.createdTime,
                maxWorkers = order.maxAssignedWorkers,
                isActive = order.isActive,
                isPaused = order.isPaused
            };

            // 배정된 직원 ID
            foreach (var worker in order.assignedWorkers)
            {
                if (worker != null)
                    orderData.assignedWorkerIds.Add(worker.InstanceId);
            }

            // 대기 작업 직렬화
            foreach (var task in order.taskQueue.PendingTasks)
            {
                var taskData = SerializeWorkTask(task, order);
                if (taskData != null)
                    orderData.pendingTasks.Add(taskData);
            }

            // 진행 중 작업 직렬화
            foreach (var task in order.taskQueue.AssignedTasks)
            {
                var taskData = SerializeWorkTask(task, order);
                if (taskData != null)
                    orderData.assignedTasks.Add(taskData);
            }

            data.workOrders.Add(orderData);
        }

        if (showDebugInfo)
        {
            Debug.Log($"[WorkSystemManager] Capture: {data.workOrders.Count}개 작업물 저장");
        }
    }

    public void Restore(SaveData data)
    {
        ClearAllOrders();

        if (data.workOrders == null || data.workOrders.Count == 0) return;

        foreach (var orderData in data.workOrders)
        {
            WorkOrder order;

            // CraftingOrder 특수 처리
            if ((WorkType)orderData.workType == WorkType.Crafting)
            {
                order = RestoreCraftingOrder(orderData);
                if (order == null) continue;
            }
            else
            {
                order = new WorkOrder
                {
                    orderId = orderData.orderId,
                    orderName = orderData.orderName,
                    workType = (WorkType)orderData.workType,
                    priority = orderData.priority,
                    createdTime = orderData.createdTime,
                    maxAssignedWorkers = orderData.maxWorkers,
                    isActive = orderData.isActive,
                    isPaused = orderData.isPaused
                };

                // 대기 작업 복원
                foreach (var taskData in orderData.pendingTasks)
                {
                    IWorkTarget target = DeserializeWorkTarget(taskData);
                    if (target == null) continue;

                    WorkTask task = new WorkTask(target, taskData.priority);
                    task.taskId = taskData.taskId;
                    task.createdTime = taskData.createdTime;
                    order.taskQueue.Enqueue(task);
                }

                // 진행 중 작업 → 대기로 복원 (PostRestore에서 재할당)
                foreach (var taskData in orderData.assignedTasks)
                {
                    IWorkTarget target = DeserializeWorkTarget(taskData);
                    if (target == null) continue;

                    WorkTask task = new WorkTask(target, taskData.priority);
                    task.taskId = taskData.taskId;
                    task.createdTime = taskData.createdTime;
                    order.taskQueue.Enqueue(task);
                }
            }

            allOrders.Add(order);
        }

        // 카운터 복원
        nextOrderId = data.nextOrderId > 0 ? data.nextOrderId : CalcNextOrderId();
        WorkTask.SetNextTaskId(data.nextTaskId > 0 ? data.nextTaskId : CalcNextTaskId());

        if (showDebugInfo)
        {
            Debug.Log($"[WorkSystemManager] Restore: {allOrders.Count}개 작업물 복원, nextOrderId={nextOrderId}");
        }
    }

    public void PostRestore(SaveData data)
    {
        if (data.workOrders == null) return;

        var registry = RuntimeIDRegistry.instance;
        if (registry == null) return;

        // 작업물별 직원 재할당
        foreach (var orderData in data.workOrders)
        {
            var order = allOrders.FirstOrDefault(o => o.orderId == orderData.orderId);
            if (order == null) continue;

            foreach (int workerId in orderData.assignedWorkerIds)
            {
                if (workerId <= 0) continue;

                Employee emp = registry.ResolveComponent<Employee>(workerId);
                if (emp == null)
                {
                    Debug.LogWarning($"[WorkSystemManager] PostRestore: 직원 ID {workerId} 찾을 수 없음");
                    continue;
                }

                // 직원 등록 (Start에서 이미 했을 수 있지만 안전 확인)
                if (!allEmployees.Contains(emp))
                    allEmployees.Add(emp);

                // 작업물에 직원 배정 + 작업 할당
                order.assignedWorkers.Add(emp);
                employeeToOrderMap[emp] = order;

                // 큐에서 작업 할당 시도
                WorkTask task = order.AssignNextTaskToWorker(emp);
                if (task != null)
                {
                    employeeToTaskMap[emp] = task;
                    task.Start();
                    emp.AssignWork(order, task.target);

                    if (showDebugInfo)
                    {
                        Debug.Log($"[WorkSystemManager] PostRestore: {emp.Data.employeeName} → {order.orderName} 재할당");
                    }
                }
            }

            // BuildOrder ↔ ConstructionSite 연결
            LinkBuildOrdersToConstructionSites(order, orderData, registry);
        }
    }

    #endregion

    #region ISaveModule 헬퍼

    /// <summary>WorkTask를 WorkTaskSaveData로 직렬화합니다.</summary>
    private WorkTaskSaveData SerializeWorkTask(WorkTask task, WorkOrder order)
    {
        if (task == null || task.target == null) return null;

        var taskData = new WorkTaskSaveData
        {
            taskId = task.taskId,
            state = (int)task.state,
            priority = task.priority,
            assignedWorkerId = task.assignedWorker != null ? task.assignedWorker.InstanceId : -1,
            createdTime = task.createdTime,
            assignedTime = task.assignedTime,
            startedTime = task.startedTime
        };

        // IWorkTarget 다형 직렬화
        if (task.target is CraftingOrder crafting)
        {
            taskData.targetType = (int)WorkTargetType.Crafting;
            taskData.targetData = JsonUtility.ToJson(new CraftingTargetData
            {
                recipeId = crafting.recipe != null ? crafting.recipe.recipeID : -1,
                craftAmount = crafting.craftAmount,
                craftingProgress = crafting.CraftingProgress,
                buildingInstanceId = crafting.building != null ? (crafting.building.GetComponent<Building>()?.InstanceId ?? -1) : -1,
                reservationId = crafting.ReservationId
            });
        }
        else if (task.target is MiningOrder mining)
        {
            taskData.targetType = (int)WorkTargetType.Mining;
            taskData.targetData = JsonUtility.ToJson(new MiningTargetData
            {
                tileX = mining.position.x,
                tileY = mining.position.y,
                tileId = mining.tileID
            });
        }
        else if (task.target is BuildOrder build)
        {
            taskData.targetType = (int)WorkTargetType.Build;
            taskData.targetData = JsonUtility.ToJson(new BuildTargetData
            {
                constructionSiteId = build.constructionSite != null ? build.constructionSite.InstanceId : -1
            });
        }
        else if (task.target is HarvestOrder harvest)
        {
            taskData.targetType = (int)WorkTargetType.Harvest;
            taskData.targetData = JsonUtility.ToJson(new HarvestTargetData
            {
                entityX = Mathf.FloorToInt(harvest.position.x),
                entityY = Mathf.FloorToInt(harvest.position.y),
                entityType = 0
            });
        }
        else if (task.target is DemolishOrder demolish)
        {
            taskData.targetType = (int)WorkTargetType.Demolish;
            taskData.targetData = JsonUtility.ToJson(new DemolishTargetData
            {
                buildingInstanceId = demolish.building != null ? demolish.building.InstanceId : -1
            });
        }
        else
        {
            Debug.LogWarning($"[WorkSystemManager] 알 수 없는 IWorkTarget 타입: {task.target.GetType().Name}");
            return null;
        }

        return taskData;
    }

    /// <summary>WorkTaskSaveData에서 IWorkTarget을 역직렬화합니다.</summary>
    private IWorkTarget DeserializeWorkTarget(WorkTaskSaveData taskData)
    {
        var targetType = (WorkTargetType)taskData.targetType;

        switch (targetType)
        {
            case WorkTargetType.Mining:
            {
                var td = JsonUtility.FromJson<MiningTargetData>(taskData.targetData);
                return new MiningOrder
                {
                    position = new Vector3Int(td.tileX, td.tileY, 0),
                    tileID = td.tileId,
                    completed = false
                };
            }

            case WorkTargetType.Build:
            {
                var td = JsonUtility.FromJson<BuildTargetData>(taskData.targetData);
                // ConstructionSite는 이미 ConstructionManager(SaveOrder=30)에서 복원됨
                var site = RuntimeIDRegistry.instance?.ResolveComponent<ConstructionSite>(td.constructionSiteId);
                if (site == null)
                {
                    Debug.LogWarning($"[WorkSystemManager] BuildOrder 복원 실패: ConstructionSite ID {td.constructionSiteId} 없음");
                    return null;
                }
                return new BuildOrder
                {
                    constructionSite = site,
                    buildingData = site.BuildingData,
                    position = site.transform.position,
                    completed = false
                };
            }

            case WorkTargetType.Harvest:
            {
                var td = JsonUtility.FromJson<HarvestTargetData>(taskData.targetData);
                // 위치 기반으로 IHarvestable 검색
                IHarvestable harvestable = FindHarvestableAtPosition(td.entityX, td.entityY);
                if (harvestable == null)
                {
                    Debug.LogWarning($"[WorkSystemManager] HarvestOrder 복원 실패: ({td.entityX}, {td.entityY}) 에 수확 대상 없음");
                    return null;
                }
                return new HarvestOrder
                {
                    target = harvestable,
                    position = new Vector3(td.entityX, td.entityY, 0),
                    completed = false
                };
            }

            case WorkTargetType.Demolish:
            {
                var td = JsonUtility.FromJson<DemolishTargetData>(taskData.targetData);
                var building = RuntimeIDRegistry.instance?.ResolveComponent<Building>(td.buildingInstanceId);
                if (building == null)
                {
                    Debug.LogWarning($"[WorkSystemManager] DemolishOrder 복원 실패: Building ID {td.buildingInstanceId} 없음");
                    return null;
                }
                return new DemolishOrder
                {
                    building = building,
                    position = building.transform.position,
                    completed = false
                };
            }

            case WorkTargetType.Crafting:
                // CraftingOrder는 RestoreCraftingOrder에서 처리
                return null;

            default:
                Debug.LogWarning($"[WorkSystemManager] 알 수 없는 WorkTargetType: {targetType}");
                return null;
        }
    }

    /// <summary>CraftingOrder를 복원합니다.</summary>
    private CraftingOrder RestoreCraftingOrder(WorkOrderSaveData orderData)
    {
        // CraftingOrder의 작업 데이터 찾기 (pending 또는 assigned에 Crafting 타입이 있음)
        WorkTaskSaveData craftTaskData = null;
        foreach (var t in orderData.pendingTasks)
        {
            if ((WorkTargetType)t.targetType == WorkTargetType.Crafting) { craftTaskData = t; break; }
        }
        if (craftTaskData == null)
        {
            foreach (var t in orderData.assignedTasks)
            {
                if ((WorkTargetType)t.targetType == WorkTargetType.Crafting) { craftTaskData = t; break; }
            }
        }

        if (craftTaskData == null)
        {
            Debug.LogWarning($"[WorkSystemManager] CraftingOrder 복원 실패: 작업 데이터 없음 (Order: {orderData.orderName})");
            return null;
        }

        var craftData = JsonUtility.FromJson<CraftingTargetData>(craftTaskData.targetData);
        var recipe = GameDatabase.Instance?.GetRecipeData(craftData.recipeId);
        if (recipe == null)
        {
            Debug.LogWarning($"[WorkSystemManager] CraftingOrder 복원 실패: 레시피 ID {craftData.recipeId} 없음");
            return null;
        }

        var building = RuntimeIDRegistry.instance?.ResolveComponent<ProductionBuilding>(craftData.buildingInstanceId);

        return CraftingOrder.CreateForRestore(orderData, craftData, recipe, building);
    }

    /// <summary>위치 기반으로 IHarvestable 오브젝트를 검색합니다.</summary>
    private IHarvestable FindHarvestableAtPosition(int x, int y)
    {
        // 씬 내 모든 IHarvestable 검색
        var allHarvestables = UnityEngine.Object.FindObjectsByType<MonoBehaviour>();
        foreach (var mb in allHarvestables)
        {
            if (!(mb is IHarvestable harvestable)) continue;

            Vector2Int pos = new Vector2Int(
                Mathf.FloorToInt(mb.transform.position.x),
                Mathf.FloorToInt(mb.transform.position.y)
            );

            if (pos.x == x && pos.y == y && harvestable.CanHarvest())
            {
                return harvestable;
            }
        }
        return null;
    }

    /// <summary>BuildOrder가 있는 작업물에서 ConstructionSite와 연결합니다.</summary>
    private void LinkBuildOrdersToConstructionSites(WorkOrder order, WorkOrderSaveData orderData, RuntimeIDRegistry registry)
    {
        // 작업 내 BuildOrder가 있으면 ConstructionSite에 WorkOrder 연결
        foreach (var task in order.taskQueue.PendingTasks)
        {
            if (task.target is BuildOrder buildOrder && buildOrder.constructionSite != null)
            {
                buildOrder.constructionSite.SetWorkOrder(order);
            }
        }
        foreach (var task in order.taskQueue.AssignedTasks)
        {
            if (task.target is BuildOrder buildOrder && buildOrder.constructionSite != null)
            {
                buildOrder.constructionSite.SetWorkOrder(order);
            }
        }
    }

    /// <summary>allOrders에서 최대 orderId를 기반으로 다음 ID를 계산합니다.</summary>
    private int CalcNextOrderId()
    {
        int maxId = 0;
        foreach (var o in allOrders)
        {
            if (o.orderId > maxId) maxId = o.orderId;
        }
        return maxId + 1;
    }

    /// <summary>전체 작업에서 최대 taskId를 기반으로 다음 ID를 계산합니다.</summary>
    private int CalcNextTaskId()
    {
        int maxId = 0;
        foreach (var order in allOrders)
        {
            foreach (var task in order.taskQueue.PendingTasks)
            {
                if (task.taskId > maxId) maxId = task.taskId;
            }
            foreach (var task in order.taskQueue.AssignedTasks)
            {
                if (task.taskId > maxId) maxId = task.taskId;
            }
        }
        return maxId + 1;
    }

    #endregion
}