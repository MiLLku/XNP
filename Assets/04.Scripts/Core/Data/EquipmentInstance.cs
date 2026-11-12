using System;

/// <summary>
/// 장비 실물 인스턴스. EquipmentData(SO)는 종류 정의, 이 클래스는 개별 실물(내구도 보유).
/// 장비 보관소 풀 또는 직원 장착 슬롯 중 한 곳에만 존재한다.
/// </summary>
[Serializable]
public class EquipmentInstance
{
    /// <summary>풀 전역 고유 ID (EquipmentStorageManager가 부여)</summary>
    public int instanceId;

    /// <summary>장비 종류 ID (= EquipmentData.itemData.itemID)</summary>
    public int equipmentId;

    /// <summary>현재 내구도. 0 이하가 되면 파괴된다 (indestructible 장비는 감소하지 않음).</summary>
    public float durability;

    /// <summary>제작 품질 (없던 세이브는 0 = 평범함)</summary>
    public EquipmentQuality quality;

    /// <summary>품질 배율 — 성능과 최대 내구도에 곱한다</summary>
    public float QualityMultiplier => EquipmentQualities.Multiplier(quality);

    /// <summary>이 장비의 최대 내구도 (금속 단계 × 품질)</summary>
    public float MaxDurability(EquipmentData data) => data != null ? data.MaxDurability * QualityMultiplier : 0f;
}
