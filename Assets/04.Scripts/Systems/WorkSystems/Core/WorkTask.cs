using UnityEngine;

/// <summary>
/// 작업의 최소 단위 (타일 하나, 나무 하나 등).
/// WorkOrder에 속한 개별 작업을 나타냅니다.
///
/// 상태 흐름:
///   Pending → Assigned → InProgress → Completed
///                ↓
///             Cancelled (또는 Unassign으로 Pending 복귀)
/// </summary>
[System.Serializable]
public class WorkTask
{
    #region 상태 열거형

    /// <summary>작업 상태</summary>
    public enum TaskState
    {
        /// <summary>대기 중 (큐에서 대기)</summary>
        Pending,
        /// <summary>할당됨 (직원에게 배정, 아직 미시작)</summary>
        Assigned,
        /// <summary>진행 중 (직원이 작업 수행 중)</summary>
        InProgress,
        /// <summary>완료됨</summary>
        Completed,
        /// <summary>취소됨</summary>
        Cancelled
    }

    #endregion

    #region 필드

    [Header("작업 정보")]
    /// <summary>고유 ID</summary>
    public int taskId;

    /// <summary>실제 작업 대상 (MiningOrder, HarvestOrder 등)</summary>
    public IWorkTarget target;

    /// <summary>현재 상태</summary>
    public TaskState state;

    /// <summary>생성 시간</summary>
    public float createdTime;

    [Header("할당 정보")]
    /// <summary>할당된 직원 (null이면 미할당)</summary>
    public Employee assignedWorker;

    /// <summary>할당된 시간</summary>
    public float assignedTime;

    /// <summary>작업 시작 시간</summary>
    public float startedTime;

    /// <summary>완료 시간</summary>
    public float completedTime;

    [Header("우선순위")]
    /// <summary>우선순위 (낮을수록 먼저)</summary>
    public int priority;

    /// <summary>작업자로부터의 거리 (동적 계산용)</summary>
    public float distanceFromWorker;

    /// <summary>정적 ID 카운터 (자동 증가)</summary>
    private static int nextTaskId = 1;

    /// <summary>현재 nextTaskId 값 조회 (저장용)</summary>
    public static int NextTaskId => nextTaskId;

    /// <summary>nextTaskId를 복원합니다 (로드용)</summary>
    public static void SetNextTaskId(int id) { nextTaskId = id > 0 ? id : 1; }

    /// <summary>
    /// 작업 위치를 찾지 못해 취소된 경우, 이 시각 이전엔 재시도하지 않습니다.
    /// WorkSystemManager.OnWorkerCancelledWork 에서 설정됩니다.
    /// </summary>
    public float nextRetryTime = 0f;

    /// <summary>도달불가 타일 재시도 대기 시간 (초)</summary>
    public const float RETRY_COOLDOWN = 10f;

    #endregion

    #region 생성자

    /// <summary>
    /// 새 작업을 생성합니다.
    /// </summary>
    /// <param name="workTarget">작업 대상</param>
    /// <param name="taskPriority">우선순위 (기본값: 5)</param>
    public WorkTask(IWorkTarget workTarget, int taskPriority = 5)
    {
        taskId = nextTaskId++;
        target = workTarget;
        state = TaskState.Pending;
        priority = taskPriority;
        createdTime = Time.time;
        assignedWorker = null;
    }

    #endregion

    #region 상태 전환

    /// <summary>
    /// 작업을 직원에게 할당합니다 (Pending → Assigned).
    /// </summary>
    /// <param name="worker">할당할 직원</param>
    /// <returns>할당 성공 여부</returns>
    public bool Assign(Employee worker)
    {
        if (state != TaskState.Pending)
        {
            Debug.LogWarning($"[WorkTask] 작업 {taskId}는 Pending 상태가 아니라 할당할 수 없습니다. (현재: {state})");
            return false;
        }

        if (worker == null)
        {
            Debug.LogWarning("[WorkTask] null 직원에게 할당할 수 없습니다.");
            return false;
        }

        assignedWorker = worker;
        state = TaskState.Assigned;
        assignedTime = Time.time;

        return true;
    }

    /// <summary>
    /// 작업을 시작합니다 (Assigned → InProgress).
    /// </summary>
    /// <returns>시작 성공 여부</returns>
    public bool Start()
    {
        if (state != TaskState.Assigned)
        {
            Debug.LogWarning($"[WorkTask] 작업 {taskId}는 Assigned 상태가 아니라 시작할 수 없습니다. (현재: {state})");
            return false;
        }

        state = TaskState.InProgress;
        startedTime = Time.time;

        return true;
    }

    /// <summary>
    /// 작업을 완료합니다.
    /// 작업 대상의 CompleteWork를 호출하고 직원 참조를 해제합니다.
    /// </summary>
    public void Complete()
    {
        if (state == TaskState.Completed || state == TaskState.Cancelled)
        {
            Debug.LogWarning($"[WorkTask] 작업 {taskId}는 {state} 상태라 완료할 수 없습니다.");
            return;
        }

        state = TaskState.Completed;
        completedTime = Time.time;

        if (target != null && assignedWorker != null)
        {
            target.CompleteWork(assignedWorker);
        }

        assignedWorker = null;
    }

    /// <summary>
    /// 작업을 취소합니다.
    /// 진행 중이었다면 작업 대상에게 취소를 알립니다.
    /// </summary>
    public void Cancel()
    {
        if (state == TaskState.Completed)
        {
            Debug.LogWarning($"[WorkTask] 이미 완료된 작업 {taskId}는 취소할 수 없습니다.");
            return;
        }

        if (target != null && assignedWorker != null)
        {
            target.CancelWork(assignedWorker);
        }

        state = TaskState.Cancelled;
        assignedWorker = null;
    }

    /// <summary>
    /// 할당을 해제하고 대기 상태로 되돌립니다 (Assigned/InProgress → Pending).
    /// 작업 대상에게 취소를 알린 뒤 다시 큐에 넣을 수 있습니다.
    /// </summary>
    public void Unassign()
    {
        if (state == TaskState.Completed || state == TaskState.Cancelled)
        {
            Debug.LogWarning($"[WorkTask] 완료/취소된 작업 {taskId}는 할당 해제할 수 없습니다.");
            return;
        }

        if (target != null && assignedWorker != null)
        {
            target.CancelWork(assignedWorker);
        }

        assignedWorker = null;
        state = TaskState.Pending;
    }

    #endregion

    #region 조회

    /// <summary>
    /// 작업이 유효한지 확인합니다 (대상이 아직 작업 가능한지).
    /// </summary>
    /// <returns>유효 여부</returns>
    public bool IsValid()
    {
        if (target == null) return false;
        if (state == TaskState.Completed || state == TaskState.Cancelled) return false;

        return target.IsWorkAvailable();
    }

    /// <summary>
    /// 작업 위치를 반환합니다.
    /// </summary>
    /// <returns>작업 대상의 월드 좌표</returns>
    public Vector3 GetPosition()
    {
        return target?.GetWorkPosition() ?? Vector3.zero;
    }

    /// <summary>
    /// 작업 타입을 반환합니다.
    /// </summary>
    /// <returns>작업 타입</returns>
    public WorkType GetWorkType()
    {
        return target?.GetWorkType() ?? WorkType.None;
    }

    /// <summary>
    /// 작업에 소요되는 시간을 반환합니다.
    /// </summary>
    /// <returns>작업 시간 (초)</returns>
    public float GetWorkTime()
    {
        return target?.GetWorkTime() ?? 0f;
    }

    #endregion

    #region 디버그

    /// <summary>
    /// 디버그 정보를 문자열로 반환합니다.
    /// </summary>
    public override string ToString()
    {
        string workerName = assignedWorker != null ? assignedWorker.Data.employeeName : "없음";
        return $"[Task {taskId}] State:{state} | Worker:{workerName} | Priority:{priority} | Pos:{GetPosition()}";
    }

    #endregion
}
