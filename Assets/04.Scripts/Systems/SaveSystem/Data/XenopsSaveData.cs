using System;

/// <summary>
/// 제노프스 저장 데이터.
/// Xenops 인스턴스의 상태를 직렬화합니다.
/// </summary>
[Serializable]
public class XenopsSaveData
{
    #region 식별

    /// <summary>런타임 고유 ID</summary>
    public int instanceId;

    /// <summary>XenopsData 템플릿 ID (GameIDRegistry.Xenops 범위)</summary>
    public int templateId;

    #endregion

    #region 상태

    /// <summary>현재 상태 (XenopsState enum)</summary>
    public int state;

    /// <summary>위치 X</summary>
    public float posX;

    /// <summary>위치 Y</summary>
    public float posY;

    #endregion

    #region 해석도

    /// <summary>해석도 레벨</summary>
    public int interpretationLevel;

    /// <summary>해석도 경험치</summary>
    public int interpretationExp;

    /// <summary>다음 레벨 필요 경험치</summary>
    public int interpretationExpToNext;

    #endregion

    #region 크로스레퍼런스

    /// <summary>장착 대상 직원 인스턴스 ID (장비형, 0 = 없음)</summary>
    public int equippedEmployeeInstanceId;

    /// <summary>잠입 대상 건물 인스턴스 ID (잠입체, 0 = 없음)</summary>
    public int infiltratedBuildingInstanceId;

    /// <summary>장착된 장비 슬롯 (EquipmentSlot enum, -1 = 미장착)</summary>
    public int equippedSlot = -1;

    #endregion
}
