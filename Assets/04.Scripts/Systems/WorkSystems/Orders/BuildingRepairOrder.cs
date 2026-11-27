using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 수리 — 부서져 '사용 불가'로 남은 건물(지금은 창고)을 건설 일로 고친다 (사용자 결정).
/// 작업량은 건설 작업량의 <see cref="WORK_RATIO"/>, 자재는 건설비의 <see cref="COST_RATIO"/> (임시 값, 올림).
/// 자재가 창고에 있어야 시작할 수 있고, 끝날 때 창고에서 뺀다 (함정 재장전과 같은 방식).
/// 저장하지 않는다 — 부서진 창고가 불러온 뒤 다시 건다.
/// </summary>
public class BuildingRepairOrder : IWorkTarget, IProgressiveWork, IExpiringWorkTarget
{
    public const float WORK_RATIO = 0.5f;
    public const float COST_RATIO = 0.5f;

    private readonly Building building;
    private readonly Vector3 position;
    private float accumulated;
    private bool completed;

    public BuildingRepairOrder(Building building, Vector3 position)
    {
        this.building = building;
        this.position = position;
    }

    public bool IsExpired => completed || building == null || !building.IsBroken;

    public Vector3 GetWorkPosition() => position;
    public WorkType GetWorkType() => WorkType.Building;
    public float GetWorkTime() => GetWorkAmount();
    public float GetWorkAmount()
        => building != null && building.buildingData != null ? Mathf.Max(1f, building.buildingData.workAmount * WORK_RATIO) : 1f;

    /// <summary>수리 자재 — 건설비의 절반 (올림)</summary>
    public List<ResourceCost> GetCosts()
    {
        var list = new List<ResourceCost>();
        var req = building != null && building.buildingData != null ? building.buildingData.requiredResources : null;
        if (req == null) return list;
        foreach (var c in req)
        {
            if (c.item == null || c.amount <= 0) continue;
            list.Add(new ResourceCost { item = c.item, amount = Mathf.CeilToInt(c.amount * COST_RATIO) });
        }
        return list;
    }

    public bool IsWorkAvailable()
    {
        if (IsExpired) return false;
        var inv = InventoryManager.instance;
        if (inv == null) return false;
        foreach (var c in GetCosts())
            if (inv.GetAvailableAmount(c.item) < c.amount) return false;
        return true;
    }

    public void CompleteWork(Employee worker)
    {
        if (completed || building == null) return;
        var inv = InventoryManager.instance;
        var costs = GetCosts();
        if (inv == null) return;
        foreach (var c in costs)
            if (inv.GetAvailableAmount(c.item) < c.amount) return; // 자재가 사라졌으면 다음에
        foreach (var c in costs) inv.RemoveItem(c.item, c.amount);
        completed = true;
        building.Repair();
    }

    public void CancelWork(Employee worker) { }

    public float GetAccumulatedWork() => accumulated;
    public void AddWork(float amount) { if (amount > 0f && !completed) accumulated = Mathf.Min(GetWorkAmount(), accumulated + amount); }
    public void ReduceWork(float amount) { if (amount > 0f && !completed) accumulated = Mathf.Max(0f, accumulated - amount); }
}
