using UnityEngine;

/// <summary>
/// 건설 작업 명령.
/// 건설 현장(ConstructionSite)에서 건물을 완성하는 작업.
///
/// <b>작업량 누적 방식</b>(IProgressiveWork) — 직원이 초당 작업 속도만큼 작업량을 넣고,
/// 진행도는 건설 현장에 쌓인다. 직원이 떠나도 사라지지 않으며 다른 직원이 이어받는다.
/// </summary>
[System.Serializable]
public class BuildOrder : IWorkTarget, IProgressiveWork
{
    #region 필드

    /// <summary>건설 현장 참조</summary>
    public ConstructionSite constructionSite;

    /// <summary>건물 데이터</summary>
    public BuildingData buildingData;

    /// <summary>작업 위치 (월드 좌표)</summary>
    public Vector3 position;

    /// <summary>우선순위 (낮을수록 먼저)</summary>
    public int priority;

    /// <summary>완료 여부</summary>
    public bool completed;

    /// <summary>할당된 직원</summary>
    public Employee assignedWorker;

    #endregion

    #region IWorkTarget 구현

    /// <summary>작업 위치를 반환합니다.</summary>
    public Vector3 GetWorkPosition() => position;

    /// <summary>작업 타입을 반환합니다.</summary>
    public WorkType GetWorkType() => WorkType.Building;

    /// <summary>
    /// 총 작업량을 반환합니다.
    /// (누적 방식이라 실제 진행은 IProgressiveWork 경로를 타지만, 인터페이스 호환을 위해 유지)
    /// </summary>
    public float GetWorkTime() => GetWorkAmount();

    #endregion

    #region IProgressiveWork 구현 — 진행도는 건설 현장에 누적된다

    /// <inheritdoc/>
    public float GetWorkAmount()
        => constructionSite != null ? constructionSite.WorkAmount
                                    : (buildingData != null ? buildingData.workAmount : 5f);

    /// <inheritdoc/>
    public float GetAccumulatedWork()
        => constructionSite != null ? constructionSite.AccumulatedWork : 0f;

    /// <inheritdoc/>
    public void AddWork(float amount) => constructionSite?.AddWork(amount);

    /// <summary>
    /// 작업 가능 여부.
    /// 자재가 모두 도착해야 직원이 실제 건설 작업을 시작할 수 있습니다.
    /// (자재 운반은 별도 WithdrawOrder들로 처리됨)
    /// </summary>
    public bool IsWorkAvailable()
    {
        if (completed || constructionSite == null || constructionSite.IsCompleted)
            return false;
        return constructionSite.IsMaterialsReady;
    }

    /// <summary>
    /// 작업 완료 처리.
    /// 건설 현장의 CompleteConstruction을 호출하여 실제 건물을 생성합니다.
    /// </summary>
    /// <param name="worker">작업을 완료한 직원</param>
    public void CompleteWork(Employee worker)
    {
        if (completed) return;

        completed = true;
        assignedWorker = null;

        if (constructionSite != null)
        {
            constructionSite.CompleteConstruction();
        }

        Debug.Log($"[BuildOrder] 건설 완료: {buildingData?.buildingName}");
    }

    /// <summary>
    /// 작업 취소 처리.
    /// </summary>
    /// <param name="worker">작업을 취소한 직원</param>
    public void CancelWork(Employee worker)
    {
        assignedWorker = null;
    }

    #endregion
}
