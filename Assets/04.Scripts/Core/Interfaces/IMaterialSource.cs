using UnityEngine;

/// <summary>
/// 자재를 꺼내갈 수 있는 물리적 지점.
///
/// 창고(Stockpile)만이 자재 공급원이던 것을 일반화합니다. 이제 제작·건설은
/// 창고뿐 아니라 <b>생산 건물에 쌓인 산출물</b>과 <b>바닥에 떨어진 아이템</b>에서도
/// 자재를 가져올 수 있습니다 — 창고로 한 번 옮겨진 뒤에야 쓸 수 있는 제약이 사라집니다.
///
/// 구현체:
///   - <see cref="Stockpile"/>            — 전역 인벤토리의 물리적 접근점
///   - <see cref="WashStation"/>          — 세척으로 나온 침식 결정체
///   - <see cref="BuildingOutputBuffer"/> — 제작 건물 산출물 보관함
///   - <see cref="DroppedItem"/>          — 바닥에 떨어진 더미
///
/// 자재 회계 규약 (기존 흐름과 동일):
///   예약(TryReserve)은 <b>잠금만</b> 걸고, 실제 차감은 직원이 <see cref="Withdraw"/>하는
///   순간 이 소스에서 일어납니다. 완료 시 사용처는 CancelReservation으로 잠금만 풉니다.
///   (ConsumeReservation은 RemoveItem을 또 부르므로 이 경로에서 쓰지 않습니다)
///
/// 자동 운반이 꺼진 건물은 <see cref="IsSourceAvailable"/>이 false가 되어
/// 자재 공급원에서 빠집니다 — 플레이어가 특정 건물의 재고를 손대지 않게 잠글 수 있습니다.
/// </summary>
public interface IMaterialSource
{
    /// <summary>지금 이 소스에서 자재를 꺼내갈 수 있는지 (건물 정상 + 자동 운반 ON).</summary>
    bool IsSourceAvailable { get; }

    /// <summary>직원이 자재를 받아갈 월드 위치.</summary>
    Vector3 GetWithdrawPosition();

    /// <summary>이 소스가 보유한 해당 자재의 수량.</summary>
    int GetStoredAmount(ItemData item);

    /// <summary>
    /// 자재를 꺼냅니다. 요청량을 전부 댈 수 없으면 아무것도 꺼내지 않고 false를 반환합니다
    /// (부분 출고로 직원이 반쪽짜리 자재를 들고 가지 않도록 — Stockpile.Withdraw와 같은 규약).
    /// </summary>
    bool Withdraw(ItemData item, int amount);
}
