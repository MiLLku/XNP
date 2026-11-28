using UnityEngine;

/// <summary>
/// 파손된 건물을 원래의 건설 작업량만큼 일해서 수리한다. 누적 작업량은 건물에 저장한다.
/// </summary>
public class BuildingRepairOrder : IWorkTarget, IProgressiveWork, IExpiringWorkTarget
{
    private readonly Building building;
    private bool completed;

    public BuildingRepairOrder(Building building) { this.building = building; }

    public bool IsExpired => completed || building == null || !building.NeedsRepair;
    public Vector2Int BuildingSize => building != null && building.buildingData != null
        ? building.buildingData.size : Vector2Int.one;

    public Vector3 GetWorkPosition() => building != null ? building.transform.position : Vector3.zero;
    public WorkType GetWorkType() => WorkType.Building;
    public float GetWorkTime() => GetWorkAmount();
    public float GetWorkAmount() => building != null ? building.RepairWorkRequired : 0.01f;

    public bool IsWorkAvailable() => !IsExpired;

    public void CompleteWork(Employee worker)
    {
        if (IsExpired) return;
        completed = true;
        building.Repair();
    }

    public void CancelWork(Employee worker) { }

    public float GetAccumulatedWork() => building != null ? building.RepairWorkDone : 0f;
    public void AddWork(float amount) { if (amount > 0f && !IsExpired) building.ChangeRepairWork(amount); }
    public void ReduceWork(float amount) { if (amount > 0f && !IsExpired) building.ChangeRepairWork(-amount); }
}
