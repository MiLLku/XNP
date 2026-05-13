using UnityEngine;

/// <summary>
/// 자원 비용 데이터.
/// 건설/제작에 필요한 아이템과 수량을 정의합니다.
/// </summary>
[System.Serializable]
public struct ResourceCost
{
    /// <summary>필요한 아이템 데이터</summary>
    public ItemData item;

    /// <summary>필요 수량</summary>
    public int amount;
}
