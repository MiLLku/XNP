using UnityEngine;

/// <summary>
/// 수확 작업 명령 (나무, 식물 등).
/// IHarvestable을 구현한 대상에 대해 수확 작업을 수행합니다.
/// </summary>
[System.Serializable]
public class HarvestOrder : IWorkTarget
{
    #region 상수

    private const float DEFAULT_HARVEST_TIME = 2f;

    #endregion

    #region 필드

    /// <summary>수확 대상</summary>
    public IHarvestable target;

    /// <summary>작업 위치</summary>
    public Vector3 position;

    /// <summary>작업 우선순위</summary>
    public int priority;

    /// <summary>완료 여부</summary>
    public bool completed;

    /// <summary>배정된 직원</summary>
    public Employee assignedWorker;

    #endregion

    #region IWorkTarget 구현

    /// <inheritdoc/>
    public Vector3 GetWorkPosition() => position;

    /// <inheritdoc/>
    public WorkType GetWorkType() => target?.GetHarvestType() ?? WorkType.Gardening;

    /// <inheritdoc/>
    public float GetWorkTime() => target?.GetHarvestTime() ?? DEFAULT_HARVEST_TIME;

    /// <inheritdoc/>
    public bool IsWorkAvailable() => !completed && target != null && target.CanHarvest();

    /// <inheritdoc/>
    public void CompleteWork(Employee worker)
    {
        if (completed) return; // 이중 호출 방지 (MiningOrder, BuildOrder와 동일 패턴)

        // 위험 작업이면 작업자가 침식을 뒤집어쓴다 — Harvest()가 대상을 파괴하기 전에 읽어둔다
        if (target is IErosionHazardWork hazard && hazard.WorkerErosionCost > 0f && worker != null)
        {
            worker.ErosionController?.AddErosion(
                hazard.WorkerErosionCost,
                ErosionSource.HazardKey(hazard.HazardDisplayName),
                hazard.HazardDisplayName);
        }

        if (target != null)
        {
            target.Harvest();
        }
        completed = true;
        assignedWorker = null;
    }

    /// <inheritdoc/>
    public void CancelWork(Employee worker)
    {
        assignedWorker = null;
    }

    #endregion
}
