/// <summary>
/// 작업 시스템 통계 데이터.
/// 현재 작업 시스템의 상태를 요약하여 UI 등에 전달합니다.
/// </summary>
public class WorkStatistics
{
    /// <summary>전체 직원 수</summary>
    public int totalEmployees;

    /// <summary>대기 중인 직원 수</summary>
    public int idleEmployees;

    /// <summary>작업 중인 직원 수</summary>
    public int workingEmployees;

    /// <summary>활성 작업 명령 수</summary>
    public int activeOrders;

    /// <summary>대기 중인 작업 수</summary>
    public int pendingTasks;

    /// <summary>완료된 작업 수</summary>
    public int completedTasks;
}
