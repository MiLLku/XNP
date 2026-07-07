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
}
