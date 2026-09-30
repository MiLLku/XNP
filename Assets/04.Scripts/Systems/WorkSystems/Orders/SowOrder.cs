using UnityEngine;

/// <summary>
/// 파종 작업 대상 — 묘목이 도착한 <see cref="SowSite"/>에 심습니다 (WorkType.Sowing, 원예 능력).
/// </summary>
[System.Serializable]
public class SowOrder : IWorkTarget
{
    private const float SOW_TIME = 2f;

    public SowSite site;
    public Vector3 position;
    public bool completed;

    public Vector3 GetWorkPosition() => position;

    public WorkType GetWorkType() => WorkType.Sowing;

    public float GetWorkTime() => SOW_TIME;

    public bool IsWorkAvailable()
        => !completed && site != null && site.IsDelivered && (site.Owner == null || site.Owner.CanSowNow);

    public void CompleteWork(Employee worker)
    {
        if (completed) return;
        completed = true;
        if (site != null) site.CompleteSowing();
    }

    public void CancelWork(Employee worker) { }
}
