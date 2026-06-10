using UnityEngine;

/// <summary>
/// 자재 요청 데이터 — 사용처(CraftingTable/ConstructionSite 등)가
/// 특정 ItemData를 일정 수량 필요로 할 때 생성합니다.
///
/// 흐름:
///   1. 사용처가 InventoryManager.TryReserve()로 자원 예약 → reservationId 발급
///   2. 이 reservationId를 담은 MaterialRequest 생성
///   3. WithdrawOrder 태스크로 감싸 WorkSystemManager에 등록
///   4. 직원이 가장 가까운 Stockpile에서 출고 → 사용처로 운반
///   5. 도착 시 receiver.OnMaterialDelivered() 호출 + ConsumeReservation으로 실제 차감
/// </summary>
[System.Serializable]
public class MaterialRequest
{
    /// <summary>요청한 아이템 종류</summary>
    public ItemData itemData;

    /// <summary>요청 수량</summary>
    public int amount;

    /// <summary>자재를 받을 사용처 (콜백 인터페이스)</summary>
    public IMaterialReceiver receiver;

    /// <summary>InventoryManager에서 발급받은 자원 예약 ID (배달 완료 시 ConsumeReservation)</summary>
    public int reservationId;

    /// <summary>배달 완료 여부</summary>
    public bool delivered;

    public MaterialRequest(ItemData itemData, int amount, IMaterialReceiver receiver, int reservationId)
    {
        this.itemData      = itemData;
        this.amount        = amount;
        this.receiver      = receiver;
        this.reservationId = reservationId;
        this.delivered     = false;
    }
}
