using UnityEngine;

/// <summary>
/// 자재를 받을 수 있는 사용처(작업대·건설현장 등) 인터페이스.
/// 직원이 Stockpile에서 출고한 자재를 운반해 도착하면 이 인터페이스의 메서드를 호출합니다.
/// </summary>
public interface IMaterialReceiver
{
    /// <summary>직원이 자재를 인계할 월드 위치.</summary>
    Vector3 GetDeliveryPosition();

    /// <summary>
    /// 요청이 아직 유효한지 확인.
    /// 사용처가 파괴되었거나 작업이 취소된 경우 false → 직원이 자재를 들고 가지 않도록.
    /// </summary>
    bool IsRequestStillValid();

    /// <summary>
    /// 자재가 도착했을 때 호출됩니다.
    /// 사용처는 이때 ConsumeReservation으로 자원을 실제 차감하고 본격 작업을 시작합니다.
    /// </summary>
    void OnMaterialDelivered(ItemData itemData, int amount);

    /// <summary>
    /// 자재 운반이 실패(직원 사망·자재 부족 등)했을 때 호출됩니다.
    /// 사용처는 CancelReservation으로 예약을 해제하고 재요청을 결정할 수 있습니다.
    /// </summary>
    void OnMaterialRequestFailed(ItemData itemData, int amount);
}
