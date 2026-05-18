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
