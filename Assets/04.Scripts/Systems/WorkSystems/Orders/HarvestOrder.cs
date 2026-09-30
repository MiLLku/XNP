using UnityEngine;

/// <summary>
/// 식생물 작업 명령 — 수확·벌목(<see cref="weed"/>=false)과 제초(<see cref="weed"/>=true).
/// IHarvestable을 구현한 대상에 대해 수행하며, 제초는 <see cref="PlantBase"/> 대상만 가능합니다.
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

    /// <summary>true면 제초(뿌리까지 제거), false면 수확·벌목</summary>
    public bool weed;

    #endregion

    #region IWorkTarget 구현

    /// <inheritdoc/>
    public Vector3 GetWorkPosition() => position;

    /// <inheritdoc/>
    public WorkType GetWorkType() => weed ? WorkType.Weeding : (target?.GetHarvestType() ?? WorkType.Weeding);

    /// <inheritdoc/>
    public float GetWorkTime()
    {
        if (!IsTargetAlive) return DEFAULT_HARVEST_TIME;
        return weed && target is PlantBase p ? p.WeedTime : target.GetHarvestTime();
    }

    /// <inheritdoc/>
    public bool IsWorkAvailable()
    {
        if (completed || !IsTargetAlive) return false;
        return weed ? target is PlantBase : target.CanHarvest();
    }

    /// <summary>대상이 파괴되지 않았는지 — 파괴된 Unity 객체는 인터페이스 null 비교를 통과하므로 따로 본다.</summary>
    private bool IsTargetAlive => target != null && !(target is UnityEngine.Object o && o == null);

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

        if (IsTargetAlive)
        {
            if (weed && target is PlantBase plant) plant.Weed();
            else target.Harvest();
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
