using UnityEngine;

/// <summary>
/// 건물 산출물 운반 명령.
/// 건물 안에 쌓인 산출물(제작 완성품·침식 결정체 등)을 창고로 옮기는 작업입니다.
///
/// 바닥의 <see cref="DroppedItem"/>을 옮기는 <see cref="HaulOrder"/>와 짝을 이룹니다.
/// 차이는 픽업 대상뿐 — 실제 실행(이동→수령→창고 배달)은
/// EmployeeWork의 BuildingHaulWorkAsync가 처리합니다.
///
/// WorkType은 Hauling을 재사용합니다(같은 운반 카테고리).
/// 직원의 우선순위·자격이 한 곳에서 일괄 관리됩니다.
/// </summary>
[System.Serializable]
public class BuildingHaulOrder : IWorkTarget
{
    #region 상수

    private const float PICKUP_TIME = 0.5f;

    #endregion

    #region 필드

    /// <summary>산출물을 보관 중인 건물 (인터페이스 참조)</summary>
    public IBuildingOutput source;

    /// <summary>작업 위치 캐시 (건물 파괴 후에도 위치 참조 가능)</summary>
    public Vector3 position;

    /// <summary>완료 여부</summary>
    public bool completed;

    #endregion

    #region 생성자

    public BuildingHaulOrder(IBuildingOutput source)
    {
        this.source = source;
        position    = source != null ? source.GetPickupPosition() : Vector3.zero;
    }

    #endregion

    #region IWorkTarget 구현

    public Vector3 GetWorkPosition()
    {
        if (IsSourceAlive()) position = source.GetPickupPosition();
        return position;
    }

    public WorkType GetWorkType() => WorkType.Hauling;

    public float GetWorkTime() => PICKUP_TIME;

    /// <summary>
    /// 건물이 살아 있고 가져갈 산출물이 남아 있을 때만 할당 가능.
    ///
    /// 산출물이 비면 자동으로 false가 되므로, 다른 직원이 먼저 비워간 경우에도
    /// 헛걸음을 보내지 않습니다. (WithdrawOrder의 재고 검사와 같은 규약)
    /// </summary>
    public bool IsWorkAvailable()
    {
        if (completed) return false;
        if (!IsSourceAlive()) return false;
        return source.IsOutputAccessible && source.HasPendingOutput;
    }

    /// <summary>
    /// EmployeeWork의 BuildingHaulWorkAsync가 수령 완료 후 직접 호출합니다.
    /// 일반 PerformWorkAsync에서는 호출되지 않습니다.
    /// </summary>
    public void CompleteWork(Employee worker)
    {
        completed = true;
    }

    public void CancelWork(Employee worker)
    {
        // 산출물은 건물에 그대로 남아 있으므로 되돌릴 것이 없습니다.
        // 레지스트리가 다음 주기에 태스크를 다시 만듭니다.
    }

    #endregion

    /// <summary>
    /// 소스 건물이 아직 살아 있는지.
    /// IBuildingOutput은 MonoBehaviour로 구현되므로 파괴 시 Unity의 가짜 null이 됩니다 —
    /// 인터페이스 참조로는 == null이 통하지 않아 Object로 캐스팅해 확인합니다.
    /// </summary>
    private bool IsSourceAlive()
    {
        if (source == null) return false;
        var obj = source as UnityEngine.Object;
        return obj != null;
    }
}
