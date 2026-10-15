using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 물 퍼내기 작업 명령 — 수확 모드로 물이 고인 칸을 고르면 만들어집니다.
/// 물을 퍼내 '물'(침식이 높은 곳이면 '침식된 물') 아이템으로 바닥에 내놓습니다.
/// 누구나 하는 심부름 작업이라 능력치·결격과 무관합니다.
///
/// 한 칸에 한 번, <b>한 칸 분량씩</b> 퍼냅니다. 물은 퍼내는 동안에도 흘러 자리를 옮기므로
/// "이 칸의 물"이 아니라 <b>고른 범위에 남은 물</b>을 퍼내는 일로 봅니다 — 자기 칸이 말랐으면 범위 안의 다른 칸에서,
/// 한 칸 분량이 안 되면 이어진 물에서 마저 퍼냅니다. 범위의 물이 다 빠지면 남은 칸의 작업은 사라집니다.
/// </summary>
[System.Serializable]
public class ScoopOrder : IWorkTarget, IExpiringWorkTarget
{
    private const float SCOOP_TIME = 2f;

    /// <summary>퍼낼 칸 (직원이 가서 서는 기준)</summary>
    public Vector3Int cell;

    /// <summary>같이 지시된 칸 전부 — 한 주문의 작업들이 같은 목록을 나눠 갖습니다</summary>
    public List<Vector3Int> area;

    /// <summary>작업 우선순위</summary>
    public int priority;

    /// <summary>완료 여부</summary>
    public bool completed;

    /// <summary>배정된 직원</summary>
    public Employee assignedWorker;

    /// <inheritdoc/>
    public Vector3 GetWorkPosition() => new Vector3(cell.x, cell.y, 0);

    /// <inheritdoc/>
    public WorkType GetWorkType() => WorkType.Errand;

    /// <inheritdoc/>
    public float GetWorkTime() => SCOOP_TIME;

    /// <inheritdoc/>
    public bool IsWorkAvailable() => !completed && TryFindWater(out _);

    /// <summary>범위의 물이 다 빠졌으면 더 할 일이 없다 — 작업 목록에서 지워진다</summary>
    public bool IsExpired => completed || !TryFindWater(out _);

    /// <inheritdoc/>
    public void CompleteWork(Employee worker)
    {
        if (completed) return;
        completed = true;
        assignedWorker = null;

        var fluids = FluidManager.instance;
        if (fluids == null || !TryFindWater(out Vector3Int from)) return;

        int count = fluids.TakeWaterAsItems(from.x, from.y, FluidManager.FULL, out ItemData item, fromSurroundings: true);
        if (count <= 0 || item == null) return;

        // 바닥에 내놓는다 → 직원이 창고로 운반 (채광 드롭과 같은 경로)
        Vector3 dropPos = new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
        if (DroppedItemManager.instance != null) DroppedItemManager.instance.SpawnItem(item, count, dropPos);
        else InventoryManager.instance?.AddItem(item, count);
    }

    /// <inheritdoc/>
    public void CancelWork(Employee worker)
    {
        assignedWorker = null;
    }

    /// <summary>물이 있는 칸 — 자기 칸이 먼저, 말랐으면 범위 안의 다른 칸.</summary>
    private bool TryFindWater(out Vector3Int found)
    {
        found = cell;
        var fluids = FluidManager.instance;
        if (fluids == null) return false;
        if (fluids.GetFluid(cell.x, cell.y) == FluidType.Water) return true;
        if (area == null) return false;

        foreach (var other in area)
        {
            if (fluids.GetFluid(other.x, other.y) != FluidType.Water) continue;
            found = other;
            return true;
        }
        return false;
    }
}
