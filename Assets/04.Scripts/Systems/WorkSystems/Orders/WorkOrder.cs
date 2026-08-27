using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// 플레이어가 생성한 작업 명령 (작업물).
/// WorkTaskQueue를 통해 개별 작업(WorkTask)을 관리합니다.
///
/// 구조:
///   WorkOrder (작업물) → WorkTaskQueue (큐) → WorkTask[] (개별 작업) → IWorkTarget (작업 대상)
///
/// 흐름:
///   생성 → AddTarget() → AssignWorker() → AssignNextTaskToWorker() → CompleteTask() → OnCompleted
/// </summary>
[System.Serializable]
public class WorkOrder
{
    #region 필드 및 설정

    [Header("작업 기본 정보")]
    /// <summary>고유 ID</summary>
    public int orderId;

    /// <summary>작업물 이름 (플레이어가 지정)</summary>
    public string orderName;

    /// <summary>작업 타입</summary>
    public WorkType workType;

    /// <summary>우선순위 (낮을수록 우선)</summary>
    public int priority;

    /// <summary>생성 시간</summary>
    public float createdTime;

    [Header("작업 큐")]
    /// <summary>개별 타일/대상별 작업을 관리하는 큐</summary>
    public WorkTaskQueue taskQueue;

    [Header("인력 할당")]
    /// <summary>최대 할당 가능 작업자 수</summary>
    public int maxAssignedWorkers;

    /// <summary>현재 할당된 작업자 목록</summary>
    public List<Employee> assignedWorkers;

    [Header("진행 상태")]
    /// <summary>활성화 상태</summary>
    public bool isActive;

    /// <summary>일시정지 상태</summary>
    public bool isPaused;

    #endregion

    #region 이벤트

    public delegate void WorkOrderDelegate(WorkOrder order);
    /// <summary>작업물 완료 시 발생</summary>
    public event WorkOrderDelegate OnCompleted;
    /// <summary>작업물 취소 시 발생</summary>
    public event WorkOrderDelegate OnCancelled;

    #endregion

    #region 호환성 래퍼 프로퍼티

    /// <summary>
    /// [호환성] 아직 완료되지 않은 작업 대상 목록.
    /// 기존 코드에서 targets를 직접 참조하는 경우를 위한 래퍼.
    /// </summary>
    public List<IWorkTarget> targets
    {
        get
        {
            var result = new List<IWorkTarget>();
            foreach (var task in taskQueue.PendingTasks)
            {
                if (task.target != null) result.Add(task.target);
            }
            foreach (var task in taskQueue.AssignedTasks)
            {
                if (task.target != null) result.Add(task.target);
            }
            return result;
        }
    }

    /// <summary>
    /// [호환성] 완료된 작업 대상 목록.
    /// </summary>
    public List<IWorkTarget> completedTargets
    {
        get
        {
            var result = new List<IWorkTarget>();
            foreach (var task in taskQueue.CompletedTasks)
            {
                if (task.target != null) result.Add(task.target);
            }
            return result;
        }
    }

    /// <summary>
    /// [호환성] 작업자별 할당된 작업 매핑.
    /// </summary>
    public Dictionary<Employee, IWorkTarget> workerAssignments
    {
        get
        {
            var result = new Dictionary<Employee, IWorkTarget>();
            foreach (var task in taskQueue.AssignedTasks)
            {
                if (task.assignedWorker != null && task.target != null)
                {
                    result[task.assignedWorker] = task.target;
                }
            }
            return result;
        }
    }

    #endregion

    #region 생성자

    public WorkOrder()
    {
        taskQueue = new WorkTaskQueue();
        assignedWorkers = new List<Employee>();

        taskQueue.OnAllTasksCompleted += OnQueueCompleted;
    }

    private void OnQueueCompleted(WorkTask task)
    {
        OnCompleted?.Invoke(this);
    }

    #endregion

    #region 작업 타입

    /// <summary>
    /// 작업 타입별 동시 작업 가능 여부를 반환합니다.
    /// 채굴, 벌목 등은 여러 명이 동시 작업 가능하고, 제작/건설 등은 1명만 가능합니다.
    /// </summary>
    /// <param name="type">확인할 작업 타입</param>
    /// <returns>동시 작업 가능 여부</returns>
    public static bool CanMultipleWorkersWork(WorkType type)
    {
        switch (type)
        {
            case WorkType.Mining:
            case WorkType.Chopping:
            case WorkType.Gardening:
            case WorkType.Hauling:
            case WorkType.Demolish:
            case WorkType.Cleaning:
                return true;

            case WorkType.Crafting:
            case WorkType.Research:
            case WorkType.Building:
            case WorkType.Resting:
            case WorkType.Eating:
                return false;

            default:
                return false;
        }
    }

    #endregion

    #region 작업 대상(타겟) 관리

    /// <summary>
    /// 작업 대상을 추가합니다 (WorkTask로 변환하여 큐에 추가).
    /// </summary>
    /// <param name="target">추가할 작업 대상</param>
    public void AddTarget(IWorkTarget target)
    {
        if (target == null) return;

        WorkTask task = new WorkTask(target, priority);
        taskQueue.Enqueue(task);
    }

    /// <summary>
    /// 여러 작업 대상을 한번에 추가합니다.
    /// </summary>
    /// <param name="newTargets">추가할 작업 대상 목록</param>
    public void AddTargets(List<IWorkTarget> newTargets)
    {
        if (newTargets == null) return;

        var tasks = new List<WorkTask>();
        foreach (var target in newTargets)
        {
            if (target != null)
            {
                tasks.Add(new WorkTask(target, priority));
            }
        }
        taskQueue.EnqueueRange(tasks);
    }

    /// <summary>
    /// [호환성] 아직 할당되지 않은 할당 가능한 작업 대상을 반환합니다.
    /// CanBeAssigned: 큐에 머물러야 함(IsValid) + 지금 작업 가능(IsWorkAvailable)
    /// </summary>
    /// <returns>대기 중인 할당 가능한 작업 대상 목록</returns>
    public List<IWorkTarget> GetAvailableTargets()
    {
        return taskQueue.PendingTasks
            .Where(t => t.CanBeAssigned())
            .Select(t => t.target)
            .ToList();
    }

    #endregion

    #region 작업자 관리

    /// <summary>
    /// 자동 픽업 작업인지 여부.
    /// true면 자격 직원이 자유롭게 작업을 가져갈 수 있고, maxAssignedWorkers 제한을 받지 않습니다.
    /// false면 플레이어가 명시적으로 지정한 직원만 작업할 수 있습니다 (연구/제작 등 침식 노출 작업).
    /// </summary>
    public bool IsAutoPickup => WorkTypeCategory.IsAutoPickup(workType);

    /// <summary>
    /// 전용 직원 할당이 필요한지 여부 (자동 픽업의 반대).
    /// </summary>
    public bool RequiresDedicatedAssignment => WorkTypeCategory.RequiresDedicatedAssignment(workType);

    /// <summary>
    /// 작업자를 할당할 수 있는지 확인합니다.
    ///
    /// 자동 픽업 작업: 활성 상태와 대기 작업만 확인 (인원 제한 없음).
    /// 전용 할당 작업: 활성 상태 + 대기 작업 + 최대 인원 미초과 + 단일 작업 검증.
    /// </summary>
    /// <returns>할당 가능 여부</returns>
    public bool CanAssignWorker()
    {
        if (!isActive || isPaused) return false;
        if (!taskQueue.HasPendingTasks()) return false;

        // 자동 픽업: 인원 제한 없음. 자격 직원이면 누구든 작업 가능.
        if (IsAutoPickup) return true;

        // 전용 할당: 인원 제한 적용.
        if (assignedWorkers.Count >= maxAssignedWorkers) return false;

        if (!CanMultipleWorkersWork(workType) && assignedWorkers.Count > 0)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 작업자를 작업물에 등록합니다.
    /// 실제 작업 할당은 <see cref="AssignNextTaskToWorker"/>에서 수행됩니다.
    /// </summary>
    /// <param name="worker">등록할 직원</param>
    /// <returns>등록 성공 여부</returns>
    public bool AssignWorker(Employee worker)
    {
        if (!CanAssignWorker() || worker == null)
            return false;

        if (assignedWorkers.Contains(worker))
            return true;

        assignedWorkers.Add(worker);
        return true;
    }

    /// <summary>
    /// 작업자 등록을 해제합니다.
    /// 해당 작업자에게 할당된 작업도 함께 해제됩니다.
    /// </summary>
    /// <param name="worker">해제할 직원</param>
    public void UnassignWorker(Employee worker)
    {
        if (worker == null) return;

        assignedWorkers.Remove(worker);
        taskQueue.UnassignWorkerTasks(worker);
    }

    /// <summary>
    /// 특정 직원에게 다음 작업을 할당합니다.
    ///
    /// [채광/벌목] 글로벌 선택기(MiningTaskSelector)를 직접 사용합니다.
    ///   - AssignNextTaskInRange는 현재 범위(dy ≤ 2) 내 타일만 후보로 보기 때문에,
    ///     y=1·y=2를 캐낸 직후에도 y=0(범위 내)을 y=3보다 먼저 할당합니다.
    ///     그 결과 y=0(발판) 이 사라져 y=3이 영구 도달 불가가 되는 버그가 발생합니다.
    ///   - 글로벌 선택기는 columnOrderPenalty(500) + heightBonus + 도달가능성 체크를 통해
    ///     항상 올바른 채광 순서(위→아래)를 보장합니다.
    ///
    /// [그 외 작업] 효율성을 위해 현재 범위 내 작업을 먼저 시도합니다.
    /// </summary>
    /// <param name="worker">작업을 받을 직원</param>
    /// <returns>할당된 WorkTask (실패 시 null)</returns>
    public WorkTask AssignNextTaskToWorker(Employee worker)
    {
        if (worker == null || !assignedWorkers.Contains(worker))
            return null;

        // 채광·벌목은 글로벌 선택기로 최적 순서 보장 (범위 제한 없이)
        if (workType == WorkType.Mining || workType == WorkType.Chopping)
        {
            return taskQueue.AssignNextTask(worker);
        }

        // 그 외 작업: 현재 범위 내 → 전체 fallback
        WorkTask task = taskQueue.AssignNextTaskInRange(worker);
        if (task == null)
            task = taskQueue.AssignNextTask(worker);

        return task;
    }

    /// <summary>
    /// 특정 직원에게 구역 내에 있는 다음 작업을 할당합니다.
    /// 구역 내 작업이 없으면 null을 반환합니다 (전체 fallback 없음).
    /// </summary>
    /// <param name="worker">작업을 받을 직원</param>
    /// <param name="zone">허용 구역</param>
    /// <returns>할당된 WorkTask (구역 내 작업 없으면 null)</returns>
    public WorkTask AssignNextTaskToWorkerInZone(Employee worker, Zone zone)
    {
        if (worker == null || !assignedWorkers.Contains(worker) || zone == null)
            return null;

        // 구역 내 태스크만 선택하여 할당 (MiningTaskSelector가 최적 순서 결정)
        return taskQueue.AssignNextTaskInZone(worker, zone);
    }

    /// <summary>
    /// 구역 내에 있는 Pending 태스크 목록을 반환합니다.
    /// </summary>
    public List<WorkTask> GetPendingTasksInZone(Zone zone)
    {
        if (zone == null || taskQueue == null) return new List<WorkTask>();

        return taskQueue.PendingTasks.Where(t =>
        {
            if (!t.CanBeAssigned()) return false;
            Vector3 pos = t.GetPosition();
            var tile = new Vector2Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y));
            return zone.ContainsTile(tile);
        }).ToList();
    }

    /// <summary>
    /// [호환성] 특정 작업자에게 지정된 작업 대상을 할당합니다.
    /// </summary>
    /// <param name="worker">할당할 직원</param>
    /// <param name="target">할당할 작업 대상</param>
    /// <returns>할당 성공 여부</returns>
    public bool AssignTargetToWorker(Employee worker, IWorkTarget target)
    {
        if (worker == null || target == null) return false;

        var task = taskQueue.PendingTasks.FirstOrDefault(t => t.target == target);

        if (task != null)
        {
            return task.Assign(worker);
        }

        return false;
    }

    /// <summary>
    /// 특정 직원이 이 작업물에 등록되어 있는지 확인합니다.
    /// </summary>
    /// <param name="worker">확인할 직원</param>
    /// <returns>등록 여부</returns>
    public bool IsWorkerAssigned(Employee worker)
    {
        return assignedWorkers.Contains(worker);
    }

    /// <summary>
    /// 특정 직원의 현재 작업을 반환합니다.
    /// </summary>
    /// <param name="worker">조회할 직원</param>
    /// <returns>현재 할당된 WorkTask (없으면 null)</returns>
    public WorkTask GetWorkerCurrentTask(Employee worker)
    {
        return taskQueue.GetWorkerTask(worker);
    }

    #endregion

    #region 작업 완료 처리

    /// <summary>
    /// [호환성] 특정 작업 대상의 완료를 처리합니다.
    /// </summary>
    /// <param name="target">완료된 작업 대상</param>
    /// <param name="worker">작업을 완료한 직원</param>
    public void CompleteTarget(IWorkTarget target, Employee worker)
    {
        if (target == null) return;

        var task = taskQueue.AssignedTasks.FirstOrDefault(t => t.target == target);

        if (task != null)
        {
            taskQueue.CompleteTask(task);
        }
        else
        {
            // 큐에 추적되지 않는 타겟(비정상 경로) — WorkTask.Complete를 못 타므로
            // 부수효과 단일 호출을 여기서 보장한다. (정상 경로는 task.Complete가 호출)
            target.CompleteWork(worker);
        }
    }

    /// <summary>
    /// WorkTask 완료를 처리합니다.
    /// </summary>
    /// <param name="task">완료된 작업</param>
    public void CompleteTask(WorkTask task)
    {
        if (task == null) return;
        taskQueue.CompleteTask(task);
    }

    #endregion

    #region 상태 관리

    /// <summary>
    /// 작업물이 완전히 완료되었는지 확인합니다.
    /// </summary>
    /// <returns>완료 여부</returns>
    public bool IsCompleted()
    {
        return taskQueue.IsCompleted();
    }

    /// <summary>
    /// 진행률을 반환합니다 (0~1).
    /// </summary>
    /// <returns>완료된 작업 비율</returns>
    public float GetProgress()
    {
        return taskQueue.GetProgress();
    }

    /// <summary>
    /// 작업물을 일시정지합니다.
    /// 모든 작업자의 현재 작업이 취소됩니다.
    /// </summary>
    public void Pause()
    {
        isPaused = true;

        foreach (var worker in assignedWorkers.ToList())
        {
            taskQueue.UnassignWorkerTasks(worker);
            worker.CancelWork();
        }
    }

    /// <summary>
    /// 작업물을 재개합니다.
    /// </summary>
    public void Resume()
    {
        isPaused = false;
    }

    /// <summary>
    /// 작업물을 취소합니다.
    /// 모든 작업과 작업자가 해제됩니다.
    /// </summary>
    public void Cancel()
    {
        // 호출처 추적용 — 의도하지 않은 cancel 발생 시 원인 진단
        Debug.Log($"[WorkOrder] Cancel 호출: '{orderName}' (id={orderId}, type={workType})\n" +
                  $"StackTrace:\n{System.Environment.StackTrace}");

        isActive = false;

        taskQueue.CancelAll();

        foreach (var worker in assignedWorkers.ToList())
        {
            worker.CancelWork();
        }

        assignedWorkers.Clear();

        OnCancelled?.Invoke(this);
    }

    /// <summary>
    /// 유효하지 않은 작업을 정리합니다.
    /// </summary>
    public void CleanupInvalidTasks()
    {
        taskQueue.CleanupInvalidTasks();
    }

    #endregion

    #region 디버그

    /// <summary>
    /// 디버그 정보를 문자열로 반환합니다.
    /// </summary>
    /// <returns>작업물 상태 문자열</returns>
    public string GetDebugInfo()
    {
        return $"[WorkOrder {orderId}] {orderName} | Type:{workType} | " +
               $"Priority:{priority} | Workers:{assignedWorkers.Count}/{maxAssignedWorkers} | " +
               $"Queue: Pending:{taskQueue.PendingCount} Assigned:{taskQueue.AssignedCount} Completed:{taskQueue.CompletedCount} | " +
               $"Progress:{GetProgress() * 100:F0}% | Active:{isActive} Paused:{isPaused}";
    }

    #endregion
}
