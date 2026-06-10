using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 작업 큐 관리자.
/// WorkOrder에 속한 모든 WorkTask를 큐로 관리합니다.
/// MiningTaskSelector를 사용하여 가중치 기반으로 최적 작업을 선택합니다.
/// </summary>
[System.Serializable]
public class WorkTaskQueue
{
    #region 상수

    /// <summary>위치 일치 판정 거리 (타일 단위)</summary>
    private const float POSITION_MATCH_THRESHOLD = 0.5f;

    #endregion

    #region 필드 및 설정

    [Header("큐 상태")]
    [SerializeField] private List<WorkTask> pendingTasks = new List<WorkTask>();
    [SerializeField] private List<WorkTask> assignedTasks = new List<WorkTask>();
    [SerializeField] private List<WorkTask> completedTasks = new List<WorkTask>();

    /// <summary>MiningTaskSelector 캐시</summary>
    private MiningTaskSelector _miningSelector;
    private MiningTaskSelector MiningSelector
    {
        get
        {
            if (_miningSelector == null)
            {
                _miningSelector = new MiningTaskSelector();
            }
            return _miningSelector;
        }
    }

    #endregion

    #region 이벤트

    public delegate void TaskDelegate(WorkTask task);
    /// <summary>개별 작업 완료 시 발생</summary>
    public event TaskDelegate OnTaskCompleted;
    /// <summary>개별 작업 취소 시 발생</summary>
    public event TaskDelegate OnTaskCancelled;
    /// <summary>모든 작업 완료 시 발생</summary>
    public event TaskDelegate OnAllTasksCompleted;

    #endregion

    #region 큐 관리

    /// <summary>
    /// 작업을 대기 큐에 추가합니다.
    /// </summary>
    /// <param name="task">추가할 작업</param>
    public void Enqueue(WorkTask task)
    {
        if (task == null) return;

        if (!pendingTasks.Contains(task))
        {
            pendingTasks.Add(task);
            SortPendingTasks();
        }
    }

    /// <summary>
    /// 여러 작업을 대기 큐에 한번에 추가합니다.
    /// </summary>
    /// <param name="tasks">추가할 작업 목록</param>
    public void EnqueueRange(IEnumerable<WorkTask> tasks)
    {
        foreach (var task in tasks)
        {
            if (task != null && !pendingTasks.Contains(task))
            {
                pendingTasks.Add(task);
            }
        }
        SortPendingTasks();
    }

    #endregion

    #region 작업 할당

    /// <summary>
    /// 특정 직원에게 가장 적합한 다음 작업을 할당합니다.
    /// MiningTaskSelector를 사용하여 가중치 기반으로 선택합니다.
    /// </summary>
    /// <param name="worker">작업을 받을 직원</param>
    /// <returns>할당된 WorkTask (실패 시 null)</returns>
    public WorkTask AssignNextTask(Employee worker)
    {
        if (worker == null || pendingTasks.Count == 0)
            return null;

        // nextRetryTime이 지나지 않은 태스크는 재시도 대상에서 제외 (도달불가 타일 무한재시도 방지)
        // CanBeAssigned: IsValid(영구 무효 아님) + IsWorkAvailable(일시 조건 만족)
        var validTasks = pendingTasks.Where(t => t.CanBeAssigned() && Time.time >= t.nextRetryTime).ToList();

        if (validTasks.Count == 0)
            return null;

        WorkTask bestTask = MiningSelector.SelectBestTask(worker, validTasks);

        if (bestTask == null)
        {
            Debug.LogWarning("[WorkTaskQueue] 적합한 작업을 찾을 수 없습니다.");
            return null;
        }

        if (bestTask.Assign(worker))
        {
            pendingTasks.Remove(bestTask);
            assignedTasks.Add(bestTask);

            Debug.Log($"[WorkTaskQueue] 작업 할당: {bestTask.GetPosition()}");
            return bestTask;
        }

        return null;
    }

    /// <summary>
    /// 특정 직원의 작업 범위 내에서 가장 적합한 작업을 할당합니다.
    /// 직원이 현재 차지하고 있는 타일은 제외합니다.
    /// </summary>
    /// <param name="worker">작업을 받을 직원</param>
    /// <returns>할당된 WorkTask (실패 시 null)</returns>
    public WorkTask AssignNextTaskInRange(Employee worker)
    {
        if (worker == null || pendingTasks.Count == 0)
            return null;

        var validTasks = pendingTasks.Where(t => t.CanBeAssigned() && Time.time >= t.nextRetryTime).ToList();

        if (validTasks.Count == 0)
            return null;

        // 직원 발 위치 — EmployeeWork.GetFootTile()과 동일하게 FloorToInt 사용
        Vector3 workerPos = worker.transform.position;
        Vector2Int workerFootTile = new Vector2Int(
            Mathf.FloorToInt(workerPos.x),
            Mathf.FloorToInt(workerPos.y)
        );

        // 직원이 차지하는 타일들 (발, 몸통)
        HashSet<Vector2Int> workerOccupiedTiles = new HashSet<Vector2Int>
        {
            workerFootTile,
            new Vector2Int(workerFootTile.x, workerFootTile.y + 1)
        };

        // 작업 범위 내 + 직원이 차지하지 않는 작업만 필터링
        var tasksInRange = validTasks.Where(task =>
        {
            Vector3 taskPos = task.GetPosition();
            Vector2Int taskTile = new Vector2Int(
                Mathf.FloorToInt(taskPos.x),
                Mathf.FloorToInt(taskPos.y)
            );
            Vector3Int taskTile3 = new Vector3Int(taskTile.x, taskTile.y, 0);

            if (workerOccupiedTiles.Contains(taskTile))
                return false;

            return worker.GetWorkableRange().Contains(taskTile3);
        }).ToList();

        if (tasksInRange.Count == 0)
            return null;

        WorkTask bestTask = MiningSelector.SelectBestTask(worker, tasksInRange);

        if (bestTask != null && bestTask.Assign(worker))
        {
            pendingTasks.Remove(bestTask);
            assignedTasks.Add(bestTask);
            return bestTask;
        }

        return null;
    }

    #endregion

    /// <summary>
    /// 특정 구역 내에 위치한 작업 중에서 가장 적합한 작업을 직원에게 할당합니다.
    /// 벌목/채광 등 작업물이 넓은 범위에 걸쳐 있을 때 구역 내 타겟만 선택합니다.
    /// </summary>
    /// <param name="worker">작업을 받을 직원</param>
    /// <param name="zone">허용 구역 (null이면 전체 탐색)</param>
    /// <returns>할당된 WorkTask (실패 시 null)</returns>
    public WorkTask AssignNextTaskInZone(Employee worker, Zone zone)
    {
        if (worker == null || pendingTasks.Count == 0) return null;
        if (zone == null) return AssignNextTask(worker);

        var validInZone = pendingTasks.Where(t =>
        {
            if (!t.CanBeAssigned()) return false;
            if (Time.time < t.nextRetryTime) return false; // 도달불가 재시도 쿨다운 (다른 할당 경로와 동일)
            Vector3 pos = t.GetPosition();
            var tile = new Vector2Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y));
            return zone.ContainsTile(tile);
        }).ToList();

        if (validInZone.Count == 0) return null;

        WorkTask bestTask = MiningSelector.SelectBestTask(worker, validInZone);
        if (bestTask == null) return null;

        if (bestTask.Assign(worker))
        {
            pendingTasks.Remove(bestTask);
            assignedTasks.Add(bestTask);
            return bestTask;
        }

        return null;
    }

    #region 작업 완료/취소

    /// <summary>
    /// 작업을 완료 처리합니다.
    /// 모든 작업이 완료되면 OnAllTasksCompleted 이벤트가 발생합니다.
    /// </summary>
    /// <param name="task">완료할 작업</param>
    public void CompleteTask(WorkTask task)
    {
        if (task == null) return;

        task.Complete();

        if (assignedTasks.Contains(task))
        {
            assignedTasks.Remove(task);
        }
        else if (pendingTasks.Contains(task))
        {
            pendingTasks.Remove(task);
        }

        completedTasks.Add(task);

        OnTaskCompleted?.Invoke(task);

        if (pendingTasks.Count == 0 && assignedTasks.Count == 0)
        {
            OnAllTasksCompleted?.Invoke(task);
        }
    }

    /// <summary>
    /// 작업을 취소합니다.
    /// </summary>
    /// <param name="task">취소할 작업</param>
    public void CancelTask(WorkTask task)
    {
        if (task == null) return;

        task.Cancel();

        pendingTasks.Remove(task);
        assignedTasks.Remove(task);

        OnTaskCancelled?.Invoke(task);
    }

    /// <summary>
    /// 특정 직원에게 할당된 모든 작업을 해제하고 대기 큐로 되돌립니다.
    /// </summary>
    /// <param name="worker">해제할 직원</param>
    public void UnassignWorkerTasks(Employee worker)
    {
        if (worker == null) return;

        var workerTasks = assignedTasks.Where(t => t.assignedWorker == worker).ToList();

        foreach (var task in workerTasks)
        {
            task.Unassign();
            assignedTasks.Remove(task);
            pendingTasks.Add(task);
        }

        SortPendingTasks();
    }

    /// <summary>
    /// 특정 직원의 현재 작업을 반환합니다.
    /// </summary>
    /// <param name="worker">조회할 직원</param>
    /// <returns>할당된 작업 (없으면 null)</returns>
    public WorkTask GetWorkerTask(Employee worker)
    {
        return assignedTasks.FirstOrDefault(t => t.assignedWorker == worker);
    }

    /// <summary>
    /// 특정 직원이 작업을 할당받고 있는지 확인합니다.
    /// </summary>
    /// <param name="worker">확인할 직원</param>
    /// <returns>할당 여부</returns>
    public bool IsWorkerAssigned(Employee worker)
    {
        return assignedTasks.Any(t => t.assignedWorker == worker);
    }

    /// <summary>
    /// 유효하지 않은 작업을 정리합니다.
    /// 대기 작업은 제거, 할당 작업은 취소합니다.
    /// </summary>
    public void CleanupInvalidTasks()
    {
        pendingTasks.RemoveAll(t => !t.IsValid());

        var invalidAssigned = assignedTasks.Where(t => !t.IsValid()).ToList();
        foreach (var task in invalidAssigned)
        {
            CancelTask(task);
        }
    }

    /// <summary>
    /// 모든 작업을 취소합니다.
    /// </summary>
    public void CancelAll()
    {
        foreach (var task in assignedTasks.ToList())
        {
            task.Cancel();
            OnTaskCancelled?.Invoke(task);
        }
        assignedTasks.Clear();

        foreach (var task in pendingTasks.ToList())
        {
            task.Cancel();
            OnTaskCancelled?.Invoke(task);
        }
        pendingTasks.Clear();
    }

    #endregion

    #region 상태 확인

    /// <summary>
    /// 진행률을 반환합니다 (0~1).
    /// </summary>
    /// <returns>완료된 작업 비율</returns>
    public float GetProgress()
    {
        int total = pendingTasks.Count + assignedTasks.Count + completedTasks.Count;
        if (total == 0) return 0f;

        return (float)completedTasks.Count / total;
    }

    /// <summary>
    /// 모든 작업이 완료되었는지 확인합니다.
    /// </summary>
    /// <returns>완료 여부</returns>
    public bool IsCompleted()
    {
        return completedTasks.Count > 0 && pendingTasks.Count == 0 && assignedTasks.Count == 0;
    }

    /// <summary>
    /// 대기 중인 작업이 있는지 확인합니다.
    /// </summary>
    /// <returns>대기 작업 존재 여부</returns>
    public bool HasPendingTasks()
    {
        return pendingTasks.Count > 0;
    }

    /// <summary>
    /// 특정 위치에 작업이 존재하는지 확인합니다.
    /// </summary>
    /// <param name="position">확인할 그리드 좌표</param>
    /// <returns>해당 위치 작업 존재 여부</returns>
    public bool HasTaskAtPosition(Vector3Int position)
    {
        Vector3 posFloat = new Vector3(position.x, position.y, 0);

        return pendingTasks.Any(t => Vector3.Distance(t.GetPosition(), posFloat) < POSITION_MATCH_THRESHOLD) ||
               assignedTasks.Any(t => Vector3.Distance(t.GetPosition(), posFloat) < POSITION_MATCH_THRESHOLD);
    }

    #endregion

    #region 프로퍼티

    /// <summary>대기 중인 작업 수</summary>
    public int PendingCount => pendingTasks.Count;
    /// <summary>할당된 작업 수</summary>
    public int AssignedCount => assignedTasks.Count;
    /// <summary>완료된 작업 수</summary>
    public int CompletedCount => completedTasks.Count;
    /// <summary>전체 작업 수</summary>
    public int TotalCount => pendingTasks.Count + assignedTasks.Count + completedTasks.Count;

    /// <summary>대기 작업 목록 (읽기 전용)</summary>
    /// <summary>
    /// 외부에서 특정 task를 직원에게 직접 예약합니다 (multi-pickup 같은 특수 케이스).
    /// pendingTasks에 있는 task만 예약 가능하며, 성공 시 assignedTasks로 이동합니다.
    /// WorkSystemManager의 employeeToTaskMap에는 등록되지 않으므로
    /// 호출한 코루틴이 직접 CompleteTask 또는 CancelTask를 호출해야 합니다.
    /// </summary>
    public bool TryReserveForWorker(WorkTask task, Employee worker)
    {
        if (task == null || worker == null) return false;
        if (!pendingTasks.Contains(task)) return false;
        if (!task.Assign(worker)) return false;

        pendingTasks.Remove(task);
        assignedTasks.Add(task);
        return true;
    }

    public IReadOnlyList<WorkTask> PendingTasks => pendingTasks;
    /// <summary>할당 작업 목록 (읽기 전용)</summary>
    public IReadOnlyList<WorkTask> AssignedTasks => assignedTasks;
    /// <summary>완료 작업 목록 (읽기 전용)</summary>
    public IReadOnlyList<WorkTask> CompletedTasks => completedTasks;

    #endregion

    #region 내부 메서드

    /// <summary>
    /// 대기 작업을 우선순위 → 생성 시간 순으로 정렬합니다.
    /// </summary>
    private void SortPendingTasks()
    {
        pendingTasks = pendingTasks
            .OrderBy(t => t.priority)
            .ThenBy(t => t.createdTime)
            .ToList();
    }

    #endregion
}
