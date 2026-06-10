using UnityEngine;

/// <summary>
/// 건설 작업 명령.
/// 건설 현장(ConstructionSite)에서 건물을 완성하는 작업.
/// IWorkTarget을 구현하여 WorkTask 시스템과 연동됩니다.
/// </summary>
[System.Serializable]
public class BuildOrder : IWorkTarget
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

    /// <summary>건설에 소요되는 시간을 반환합니다 (초).</summary>
    public float GetWorkTime() => buildingData != null ? buildingData.constructionTime : 5f;

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
