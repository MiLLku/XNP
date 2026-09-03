using UnityEngine;

/// <summary>
/// 운반 작업 명령.
/// 바닥의 DroppedItem을 창고(Stockpile)까지 운반하는 작업을 나타냅니다.
///
/// 실제 실행(픽업→이동→배달)은 EmployeeWork의 HaulWorkAsync가 처리합니다.
/// 이 클래스는 작업 대상 정보(아이템 위치 등)만 담습니다.
/// </summary>
[System.Serializable]
public class HaulOrder : IWorkTarget
{
    #region 상수

    private const float HAUL_PICKUP_TIME = 0.5f;

    #endregion

    #region 필드

    /// <summary>운반할 아이템</summary>
    public DroppedItem item;

    /// <summary>작업 위치 캐시 (아이템 파괴 후에도 위치 참조 가능)</summary>
    public Vector3 position;

    /// <summary>완료 여부</summary>
    public bool completed;

    #endregion

    #region 생성자

    public HaulOrder(DroppedItem droppedItem)
    {
        item     = droppedItem;
        position = droppedItem != null ? droppedItem.transform.position : Vector3.zero;
    }

    #endregion

    #region IWorkTarget 구현

    /// <summary>직원이 이동할 픽업 위치 (타일 중앙).</summary>
    public Vector3 GetWorkPosition()
    {
        if (item != null && item.gameObject != null)
            position = item.transform.position;
        return position;
    }

    public WorkType GetWorkType() => WorkType.Hauling;

    /// <summary>픽업 소요 시간 (짧게 설정).</summary>
    public float GetWorkTime() => HAUL_PICKUP_TIME;

    /// <summary>아이템이 여전히 유효하고 예약되지 않은 경우 true.</summary>
    public bool IsWorkAvailable() => !completed && item != null && item.gameObject != null && item.isActiveAndEnabled;

    /// <summary>
    /// EmployeeWork의 HaulWorkAsync가 픽업 완료 후 직접 호출합니다.
    /// 일반 PerformWorkAsync에서는 호출되지 않습니다.
    /// </summary>
    public void CompleteWork(Employee worker)
    {
        completed = true;
        // 아이템 제거는 HaulWorkAsync에서 처리
    }

    public void CancelWork(Employee worker)
    {
        // 예약 해제
        item?.Unclaim();
    }

    #endregion
}
