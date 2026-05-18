using System;

/// <summary>
/// 장비 슬롯 저장 데이터.
/// 직원이 장착한 각 슬롯의 장비 정보를 직렬화합니다.
/// </summary>
[Serializable]
public class EquipmentSlotSaveData
{
    /// <summary>장비 슬롯 (EquipmentSlot enum)</summary>
    public int slot;

    /// <summary>장비 출처 타입 (EquipmentSourceType enum)</summary>
    public int sourceType;

    /// <summary>일반 장비 아이템 ID (EquipmentData → itemData.itemID, 0 = 없음)</summary>
    public int itemId;

    /// <summary>장비형 제노프스 인스턴스 ID (0 = 없음)</summary>
    public int xenopsInstanceId;
}
