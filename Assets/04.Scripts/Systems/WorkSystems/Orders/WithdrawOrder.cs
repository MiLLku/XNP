using UnityEngine;

/// <summary>
/// 출고 작업 명령.
/// 사용처(작업대·건설현장 등)의 자재 요청을 받아 Stockpile에서 꺼내 운반하는 작업.
///
/// 실제 실행(Stockpile 이동→출고→사용처 운반→인계)은
/// EmployeeWork의 WithdrawWorkAsync가 처리합니다.
///
/// WorkType은 Hauling을 재사용(입고와 같은 운반 카테고리).
/// 직원의 우선순위/자격은 한 곳에서 일괄 관리됩니다.
/// </summary>
[System.Serializable]
public class WithdrawOrder : IWorkTarget
{
    #region 상수

    private const float WITHDRAW_HANDLE_TIME = 0.5f;

    #endregion

    #region 필드

    /// <summary>운반할 자재 요청 데이터</summary>
    public MaterialRequest request;

    /// <summary>완료 여부</summary>
    public bool completed;

    #endregion

    #region 생성자

    public WithdrawOrder(MaterialRequest request)
    {
        this.request = request;
    }

    #endregion

    #region IWorkTarget 구현

    /// <summary>
    /// 작업 목표 위치 — 사용처 위치 반환(직원이 최종적으로 도달해야 할 곳).
    /// 단, WithdrawWorkAsync는 Stockpile부터 먼저 들르므로 이 값은 참고용입니다.
    /// </summary>
    public Vector3 GetWorkPosition()
    {
        if (request?.receiver != null)
            return request.receiver.GetDeliveryPosition();
        return Vector3.zero;
    }

    public WorkType GetWorkType() => WorkType.Hauling;

    public float GetWorkTime() => WITHDRAW_HANDLE_TIME;

    /// <summary>요청이 유효하고 미완료인 경우 true.</summary>
    public bool IsWorkAvailable()
    {
        if (completed) return false;
        if (request == null || request.delivered) return false;
        if (request.itemData == null || request.amount <= 0) return false;
        if (request.receiver == null) return false;

        // 창고에 자재가 실제로 있을 때만 할당 가능.
        // 자재가 없으면 직원을 보내지 않고(헛걸음·재시도 스팸 방지) 작업이 큐에 보류된 채 남으며,
        // 자재가 입고되면 다음 작업 평가 때 자동으로 다시 후보가 된다 (별도 이벤트 구독 불필요).
        bool inStockpile = StockpileManager.instance != null &&
                           StockpileManager.instance.HasItemAnywhere(request.itemData, request.amount);
        bool outside     = MaterialSourceRegistry.instance != null &&
                           MaterialSourceRegistry.instance.HasItemAnywhere(request.itemData, request.amount);

        if (!inStockpile && !outside) return false;

        return request.receiver.IsRequestStillValid();
    }

    /// <summary>
    /// EmployeeWork의 WithdrawWorkAsync가 인계 완료 후 직접 호출합니다.
    /// </summary>
    public void CompleteWork(Employee worker)
    {
        completed = true;
        if (request != null) request.delivered = true;
    }

    /// <summary>
    /// 작업 취소 시 호출. 직원이 자재를 들고 있지 않은 단계에서 호출되므로 받은 자재 환불 불필요.
    /// 작업 내부에서 출고 후 취소되는 경우는 별도 처리.
    /// </summary>
    public void CancelWork(Employee worker)
    {
        // request.delivered=false 유지 → 다른 직원이 다시 시도 가능
    }

    #endregion
}
