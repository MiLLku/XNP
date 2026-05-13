using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 채굴 작업 관리자
/// 이벤트 기반 실시간 작업 재평가 시스템
/// 
/// 흐름:
/// 1. Event 발생 (타일 파괴)
/// 2. Grid Scan (주변 이동 가능 여부 체크)
/// 3. ReachabilityMap 갱신
/// 4. Job Search (최적 작업 재검색)
/// 5. Target Switch (필요시 대상 변경)
/// </summary>
public class MiningJobManager : MonoBehaviour
{
    #region 싱글톤
    
    public static MiningJobManager instance { get; private set; }
    
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    #endregion
    
    #region 설정
    
    [Header("가중치 설정")]
    [SerializeField] private float priorityWeight = 10f;
    [SerializeField] private float distanceWeight = 1f;
    [SerializeField] private float accessCostWeight = 2f;
    [SerializeField] private float heightBonusPerTile = 0.5f;
    [SerializeField] private float diagonalPreference = 1.5f;
    
    [Header("재평가 설정")]
    [SerializeField] private float reevaluationInterval = 0.5f;  // 재평가 간격 (초)
    [SerializeField] private int maxReevaluationsPerFrame = 3;   // 프레임당 최대 재평가 수
    
    [Header("디버그")]
    [SerializeField] private bool showDebugInfo = true;
    
    #endregion
    
    #region 내부 데이터
    
    private ReachabilityMap reachabilityMap;
    private MiningTaskSelector taskSelector;
    
    // 작업 중인 직원 -> 현재 작업 매핑
    private Dictionary<Employee, MiningJobContext> activeJobs;
    
    // 재평가 대기열
    private Queue<Employee> reevaluationQueue;
    private float lastReevaluationTime;
    
    // 타일 변경 이벤트 버퍼
    private HashSet<Vector2Int> pendingTileChanges;
    
    #endregion
    
    #region 이벤트
    
    public delegate void JobChangedHandler(Employee worker, WorkTask oldTask, WorkTask newTask);
    public event JobChangedHandler OnJobChanged;
    
    public delegate void TileMinedHandler(Vector2Int tilePos);
    public event TileMinedHandler OnTileMined;
    
    #endregion
    
    #region 초기화
    
    private void Start()
    {
        activeJobs = new Dictionary<Employee, MiningJobContext>();
        reevaluationQueue = new Queue<Employee>();
        pendingTileChanges = new HashSet<Vector2Int>();
        
        // GameMap이 준비되면 ReachabilityMap 초기화
        if (MapGenerator.instance != null && MapGenerator.instance.GameMapInstance != null)
        {
            InitializeReachabilityMap();
        }
        
        // 가중치 설정으로 TaskSelector 초기화
        var weights = new MiningTaskSelector.SelectorWeights
        {
            priorityWeight = this.priorityWeight,
            distanceWeight = this.distanceWeight,
            accessCostWeight = this.accessCostWeight,
            heightBonusPerTile = this.heightBonusPerTile,
            diagonalPreference = this.diagonalPreference
        };
        
        taskSelector = new MiningTaskSelector(weights, reachabilityMap);
    }
    
    private void InitializeReachabilityMap()
    {
        reachabilityMap = new ReachabilityMap(MapGenerator.instance.GameMapInstance);
        reachabilityMap.OnReachabilityChanged += OnReachabilityMapChanged;
        
        if (taskSelector != null)
        {
            taskSelector.SetReachabilityMap(reachabilityMap);
        }
        
        if (showDebugInfo)
        {
            Debug.Log("[MiningJobManager] ReachabilityMap 초기화 완료");
        }
    }
    
    #endregion
    
    #region Unity 업데이트
    
    private void Update()
    {
        // 타일 변경 처리
        ProcessPendingTileChanges();
        
        // 재평가 처리
        ProcessReevaluationQueue();
    }
    
    private void LateUpdate()
    {
        // 더티 청크 갱신
        reachabilityMap?.UpdateDirtyChunks();
    }
    
    #endregion
    
    #region 타일 변경 이벤트 (OnTileChanged)
    
    /// <summary>
    /// 타일이 파괴되었을 때 호출 (외부에서 호출)
    /// </summary>
    public void NotifyTileDestroyed(Vector2Int tilePos)
    {
        pendingTileChanges.Add(tilePos);
        OnTileMined?.Invoke(tilePos);
    }
    
    /// <summary>
    /// 타일이 변경되었을 때 호출 (배치, 파괴 등)
    /// </summary>
    public void NotifyTileChanged(Vector2Int tilePos)
    {
        pendingTileChanges.Add(tilePos);
    }
    
    private void ProcessPendingTileChanges()
    {
        if (pendingTileChanges.Count == 0)
            return;
        
        // ReachabilityMap 갱신
        reachabilityMap?.OnTilesChanged(pendingTileChanges);
        
        // 영향받는 직원들 재평가 대기열에 추가
        foreach (var tilePos in pendingTileChanges)
        {
            ScheduleAffectedWorkersForReevaluation(tilePos);
        }
        
        pendingTileChanges.Clear();
    }
    
    /// <summary>
    /// 타일 변경으로 영향받는 직원들을 재평가 대기열에 추가
    /// </summary>
    private void ScheduleAffectedWorkersForReevaluation(Vector2Int changedTile)
    {
        foreach (var kvp in activeJobs)
        {
            Employee worker = kvp.Key;
            MiningJobContext context = kvp.Value;
            
            if (context.currentTask == null)
                continue;
            
            // 현재 작업 타일과 가까운지 확인
            Vector2Int taskTile = GetTaskTile(context.currentTask);
            float distance = Vector2Int.Distance(changedTile, taskTile);
            
            // 가까운 타일이 변경되었으면 재평가
            if (distance <= 5f)
            {
                if (!reevaluationQueue.Contains(worker))
                {
                    reevaluationQueue.Enqueue(worker);
                }
            }
            
            // 직원이 가려던 경로에 영향이 있는지도 체크
            Vector2Int workerPos = GetWorkerFootTile(worker);
            if (Vector2Int.Distance(changedTile, workerPos) <= 3f)
            {
                if (!reevaluationQueue.Contains(worker))
                {
                    reevaluationQueue.Enqueue(worker);
                }
            }
        }
    }
    
    #endregion
    
    #region 재평가 루프
    
    private void ProcessReevaluationQueue()
    {
        if (reevaluationQueue.Count == 0)
            return;
        
        // 간격 체크
        if (Time.time - lastReevaluationTime < reevaluationInterval)
            return;
        
        lastReevaluationTime = Time.time;
        
        int processed = 0;
        while (reevaluationQueue.Count > 0 && processed < maxReevaluationsPerFrame)
        {
            Employee worker = reevaluationQueue.Dequeue();
            
            if (worker == null || !activeJobs.ContainsKey(worker))
                continue;
            
            ReevaluateWorkerTask(worker);
            processed++;
        }
    }
    
    /// <summary>
    /// 직원의 현재 작업을 재평가하고 필요시 변경
    /// </summary>
    private void ReevaluateWorkerTask(Employee worker)
    {
        if (!activeJobs.TryGetValue(worker, out MiningJobContext context))
            return;
        
        WorkTask currentTask = context.currentTask;
        IReadOnlyList<WorkTask> pendingTasks = context.taskQueue?.PendingTasks;
        
        if (pendingTasks == null || pendingTasks.Count == 0)
            return;
        
        // 현재 작업이 여전히 유효한지 확인
        if (currentTask != null && currentTask.IsValid())
        {
            // 현재 작업의 점수 계산
            var workerContext = CreateWorkerContext(worker);
            float currentScore = taskSelector.CalculateTaskScore(currentTask, workerContext);
            
            // 더 좋은 작업이 있는지 확인
            WorkTask bestTask = taskSelector.SelectBestTask(worker, pendingTasks);
            
            if (bestTask != null && bestTask != currentTask)
            {
                float bestScore = taskSelector.CalculateTaskScore(bestTask, workerContext);
                
                // 점수 차이가 충분히 크면 작업 변경
                if (bestScore > currentScore * 1.2f) // 20% 이상 좋아야 변경
                {
                    SwitchWorkerTask(worker, currentTask, bestTask, context);
                }
            }
        }
        else
        {
            // 현재 작업이 무효화됨 - 새 작업 할당
            WorkTask newTask = taskSelector.SelectBestTask(worker, pendingTasks);
            if (newTask != null)
            {
                AssignNewTask(worker, newTask, context);
            }
            else
            {
                // 할당 가능한 작업 없음 - 보류 상태로
                context.state = MiningJobState.Pending;
                
                if (showDebugInfo)
                {
                    Debug.Log($"[MiningJobManager] {worker.Data.employeeName}: 할당 가능한 작업 없음 (보류)");
                }
            }
        }
    }
    
    #endregion
    
    #region 작업 할당/변경
    
    /// <summary>
    /// 직원에게 채굴 작업을 시작시킵니다.
    /// </summary>
    public bool StartMiningJob(Employee worker, WorkOrder order, WorkTaskQueue taskQueue)
    {
        if (worker == null || taskQueue == null)
            return false;
        
        // ReachabilityMap 갱신
        reachabilityMap?.ForceUpdate();
        
        // 최적 작업 선택
        WorkTask bestTask = taskSelector.SelectBestTask(worker, taskQueue.PendingTasks);
        
        if (bestTask == null)
        {
            if (showDebugInfo)
            {
                Debug.LogWarning($"[MiningJobManager] {worker.Data.employeeName}: 적합한 작업 없음");
            }
            return false;
        }
        
        // 작업 컨텍스트 생성
        MiningJobContext context = new MiningJobContext
        {
            worker = worker,
            order = order,
            taskQueue = taskQueue,
            currentTask = bestTask,
            state = MiningJobState.Working
        };
        
        activeJobs[worker] = context;
        
        // 작업 시작
        if (bestTask.Assign(worker))
        {
            bestTask.Start();
            
            if (showDebugInfo)
            {
                Debug.Log($"[MiningJobManager] {worker.Data.employeeName}: 채굴 작업 시작 at {bestTask.GetPosition()}");
            }
            
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// 직원의 작업을 새 작업으로 변경
    /// </summary>
    private void SwitchWorkerTask(Employee worker, WorkTask oldTask, WorkTask newTask, MiningJobContext context)
    {
        // 이전 작업 해제
        if (oldTask != null)
        {
            oldTask.Unassign();
            // 다시 대기열에 추가됨
        }
        
        // 새 작업 할당
        if (newTask.Assign(worker))
        {
            context.currentTask = newTask;
            newTask.Start();
            
            OnJobChanged?.Invoke(worker, oldTask, newTask);
            
            if (showDebugInfo)
            {
                Debug.Log($"[MiningJobManager] {worker.Data.employeeName}: 작업 변경 " +
                         $"{oldTask?.GetPosition()} -> {newTask.GetPosition()}");
            }
        }
    }
    
    /// <summary>
    /// 새 작업 할당 (이전 작업 없음)
    /// </summary>
    private void AssignNewTask(Employee worker, WorkTask newTask, MiningJobContext context)
    {
        if (newTask.Assign(worker))
        {
            WorkTask oldTask = context.currentTask;
            context.currentTask = newTask;
            context.state = MiningJobState.Working;
            newTask.Start();
            
            OnJobChanged?.Invoke(worker, oldTask, newTask);
            
            if (showDebugInfo)
            {
                Debug.Log($"[MiningJobManager] {worker.Data.employeeName}: 새 작업 할당 at {newTask.GetPosition()}");
            }
        }
    }
    
    /// <summary>
    /// 작업 완료 처리
    /// </summary>
    public void OnTaskCompleted(Employee worker, WorkTask completedTask)
    {
        if (!activeJobs.TryGetValue(worker, out MiningJobContext context))
            return;
        
        // 타일 변경 알림
        Vector2Int taskTile = GetTaskTile(completedTask);
        NotifyTileDestroyed(taskTile);
        
        // 다음 작업 선택
        WorkTask nextTask = taskSelector.SelectBestTask(worker, context.taskQueue.PendingTasks);
        
        if (nextTask != null)
        {
            AssignNewTask(worker, nextTask, context);
        }
        else
        {
            // 더 이상 작업 없음
            context.state = MiningJobState.Completed;
            activeJobs.Remove(worker);
            
            if (showDebugInfo)
            {
                Debug.Log($"[MiningJobManager] {worker.Data.employeeName}: 모든 채굴 작업 완료");
            }
        }
    }
    
    /// <summary>
    /// 작업 취소
    /// </summary>
    public void CancelJob(Employee worker)
    {
        if (!activeJobs.TryGetValue(worker, out MiningJobContext context))
            return;
        
        if (context.currentTask != null)
        {
            context.currentTask.Unassign();
        }
        
        activeJobs.Remove(worker);
        
        if (showDebugInfo)
        {
            Debug.Log($"[MiningJobManager] {worker.Data.employeeName}: 채굴 작업 취소");
        }
    }
    
    #endregion
    
    #region 쿼리
    
    /// <summary>
    /// 직원의 현재 채굴 작업 반환
    /// </summary>
    public WorkTask GetCurrentTask(Employee worker)
    {
        if (activeJobs.TryGetValue(worker, out MiningJobContext context))
        {
            return context.currentTask;
        }
        return null;
    }
    
    /// <summary>
    /// 직원이 채굴 작업 중인지 확인
    /// </summary>
    public bool IsWorkerMining(Employee worker)
    {
        return activeJobs.ContainsKey(worker) && 
               activeJobs[worker].state == MiningJobState.Working;
    }
    
    #endregion
    
    #region 이벤트 핸들러
    
    private void OnReachabilityMapChanged(Vector2Int chunkCoord)
    {
        // 해당 청크 내에서 작업 중인 직원들 재평가
        foreach (var kvp in activeJobs)
        {
            Vector2Int workerChunk = GetChunkCoord(GetWorkerFootTile(kvp.Key));
            Vector2Int taskChunk = GetChunkCoord(GetTaskTile(kvp.Value.currentTask));
            
            if (workerChunk == chunkCoord || taskChunk == chunkCoord)
            {
                if (!reevaluationQueue.Contains(kvp.Key))
                {
                    reevaluationQueue.Enqueue(kvp.Key);
                }
            }
        }
    }
    
    #endregion
    
    #region 유틸리티
    
    private Vector2Int GetWorkerFootTile(Employee worker)
    {
        Vector3 pos = worker.transform.position;
        // X는 반올림으로 시각적 위치와 맞춤
        return new Vector2Int(
            Mathf.RoundToInt(pos.x),
            Mathf.FloorToInt(pos.y)
        );
    }
    
    private Vector2Int GetTaskTile(WorkTask task)
    {
        if (task == null) return Vector2Int.zero;
        Vector3 pos = task.GetPosition();
        return new Vector2Int(
            Mathf.FloorToInt(pos.x),
            Mathf.FloorToInt(pos.y)
        );
    }
    
    private Vector2Int GetChunkCoord(Vector2Int worldPos)
    {
        return new Vector2Int(
            worldPos.x / ReachabilityMap.CHUNK_SIZE,
            worldPos.y / ReachabilityMap.CHUNK_SIZE
        );
    }
    
    private MiningTaskSelector.WorkerContext CreateWorkerContext(Employee worker)
    {
        Vector2Int footTile = GetWorkerFootTile(worker);
        
        return new MiningTaskSelector.WorkerContext
        {
            footTile = footTile,
            occupiedTiles = new HashSet<Vector2Int>
            {
                footTile,
                new Vector2Int(footTile.x, footTile.y + 1),
                new Vector2Int(footTile.x, footTile.y + 2)
            },
            employeeHeight = 2,
            gameMap = MapGenerator.instance?.GameMapInstance,
            pathfinder = MapGenerator.instance?.GameMapInstance != null 
                ? new TilePathfinder(MapGenerator.instance.GameMapInstance) 
                : null
        };
    }
    
    #endregion
}

#region 데이터 클래스

public enum MiningJobState
{
    Idle,
    Working,
    Pending,    // 경로가 끊겨서 대기 중
    Completed
}

public class MiningJobContext
{
    public Employee worker;
    public WorkOrder order;
    public WorkTaskQueue taskQueue;
    public WorkTask currentTask;
    public MiningJobState state;
}

#endregion