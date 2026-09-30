using UnityEngine;

/// <summary>
/// 조리 작업 대상 — 재료가 모두 도착한 <see cref="CookingStation"/>에서 요리합니다 (WorkType.Cooking).
/// </summary>
public class CookOrder : IWorkTarget
{
    public CookingStation station;
    public bool completed;

    public Vector3 GetWorkPosition() => station != null ? station.WorkPosition : Vector3.zero;

    public WorkType GetWorkType() => WorkType.Cooking;

    public float GetWorkTime() => station != null ? station.CookWorkTime : 1f;

    public bool IsWorkAvailable() => !completed && station != null && station.CanCookNow;

    public void CompleteWork(Employee worker)
    {
        if (completed) return;
        completed = true;
        if (station != null) station.CompleteCooking();
    }

    public void CancelWork(Employee worker) { }
}
