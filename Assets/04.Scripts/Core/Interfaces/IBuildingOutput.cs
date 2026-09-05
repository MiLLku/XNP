using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 산출물을 건물 안에 쌓아두고 직원이 창고로 옮겨가길 기다리는 건물.
///
/// 이 게임의 생산물은 완성 즉시 전역 인벤토리로 순간이동하지 않습니다.
/// 만든 자리에 놓이고, 누군가 옮겨야 창고 재고가 됩니다 — 운반이 실제 병목이 되도록.
///
/// 구현체:
///   - <see cref="WashStation"/>       — 세척으로 나온 침식 결정체
///   - <see cref="BuildingOutputBuffer"/> — 제작 건물 공용 산출물 보관함
///
/// 흐름:
///   1. 건물이 산출물을 자기 버퍼에 적립 → <see cref="BuildingOutputRegistry"/>에 알림
///   2. 레지스트리가 운반 WorkOrder에 BuildingHaulOrder 태스크를 추가
///   3. 직원이 픽업 위치로 이동 → TakeOutput으로 받아감 → 가장 가까운 창고에 입고
///
/// 주의: 장비(EquipmentData)는 인스턴스 단위로 장비 보관소가 따로 관리하므로
/// 이 경로를 타지 않습니다.
/// </summary>
public interface IBuildingOutput
{
    /// <summary>
    /// 이 건물의 산출물을 자동 물류에 태울지 여부 (플레이어 토글).
    ///
    /// 끄면 두 가지가 함께 멈춥니다:
    ///   • 직원이 창고로 옮기지 않음 (운반 작업이 생기지 않음)
    ///   • 제작·건설이 이 건물의 재고를 꺼내 쓰지 않음 (자재 공급원에서 제외)
    ///
    /// "여기 쌓인 건 건드리지 마라"를 한 스위치로 표현합니다.
    /// 단, 세척 시설처럼 보관함이 차면 멈추는 건물은 꺼둔 채 방치하면 가동이 멎습니다.
    /// </summary>
    bool AutoHaulEnabled { get; set; }

    /// <summary>지금 가져갈 산출물이 하나라도 있는지.</summary>
    bool HasPendingOutput { get; }

    /// <summary>산출물이 유효한 상태인지 (건물이 파괴·파손되지 않았는지).</summary>
    bool IsOutputAccessible { get; }

    /// <summary>직원이 산출물을 받아갈 월드 위치.</summary>
    Vector3 GetPickupPosition();

    /// <summary>
    /// 보관 중인 산출물 목록을 <paramref name="into"/>에 채웁니다 (호출 측이 리스트를 재사용).
    /// 매 프레임 조회될 수 있으므로 새 컬렉션을 할당하지 않습니다.
    /// </summary>
    void GetPendingOutputs(List<ResourceCost> into);

    /// <summary>
    /// 산출물을 실제로 꺼냅니다. 남은 양이 요청량보다 적으면 남은 만큼만 주고,
    /// <b>실제로 꺼낸 수량</b>을 반환합니다.
    /// </summary>
    int TakeOutput(ItemData item, int amount);
}
