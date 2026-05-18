using System.Linq;
using UnityEngine;

/// <summary>
/// 작업 할당 UI (임시 구현).
/// TODO [UI]: 직원 선택 UI로 확장 필요
/// </summary>
public class WorkAssignmentUI : DestroySingleton<WorkAssignmentUI>
{
    /// <summary>
    /// 작업 명령에 대한 할당 UI를 엽니다.
    /// 현재는 임시로 첫 번째 유휴 직원에게 자동 할당합니다.
    /// </summary>
    /// <param name="order">할당할 작업 명령</param>
    public void OpenAssignmentUI(WorkOrder order)
    {
        Debug.Log($"[WorkAssignmentUI] 작업 할당 UI 열기: {order.orderName}");

        if (WorkSystemManager.instance == null || EmployeeManager.instance == null) return;

        var idleEmployees = EmployeeManager.instance.AllEmployees
            .Where(e => e.State == EmployeeState.Idle && e.CanPerformWork(order.workType))
            .ToList();

        if (idleEmployees.Count > 0)
        {
            WorkSystemManager.instance.AssignEmployeeToOrder(idleEmployees[0], order);
            Debug.Log($"임시 할당: {idleEmployees[0].Data.employeeName} -> {order.orderName}");
        }
        else
        {
            Debug.LogWarning("할당 가능한 유휴 직원이 없습니다.");
        }
    }
}
