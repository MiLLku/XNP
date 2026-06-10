using System;

/// <summary>
/// 아이템 저장소 추상화 인터페이스.
///
/// 현재 구현체:
///   - <see cref="InventoryManager"/> — 전역 단일 저장소 (모든 Stockpile이 공유)
///
/// 향후 확장 가능:
///   - LocalStorage — 창고별 개별 저장소 (등급/필터 정책 지원)
///   - FilteredStorage — 허용 아이템만 받는 저장소
///
/// Stockpile 건물은 <c>linkedStorage</c>를 통해 이 인터페이스를 위임하므로
/// 구체 구현이 바뀌어도 입출고 흐름은 동일하게 유지됩니다.
/// </summary>
public interface IItemStorage
{
    /// <summary>아이템을 저장소에 추가합니다.</summary>
    /// <returns>성공 여부 (용량 초과·null 데이터 등 실패 시 false)</returns>
    bool AddItem(ItemData item, int amount = 1);

    /// <summary>아이템을 저장소에서 제거합니다.</summary>
    /// <returns>성공 여부 (수량 부족·null 데이터 등 실패 시 false)</returns>
    bool RemoveItem(ItemData item, int amount = 1);

    /// <summary>현재 저장된 아이템 수량.</summary>
    int GetItemCount(ItemData item);

    /// <summary>특정 수량 이상 보유 중인지 여부.</summary>
    bool HasItem(ItemData item, int amount = 1);
}
