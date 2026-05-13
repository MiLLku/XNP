using UnityEngine;

/// <summary>
/// 아이템 기본 데이터 ScriptableObject.
/// 인벤토리, 제작, 건설 등에서 아이템을 식별하는 데 사용됩니다.
/// </summary>
[CreateAssetMenu(fileName = "NewItemData", menuName = "StampSystem/Item Data")]
public class ItemData : ScriptableObject
{
    [Header("아이템 기본 정보")]

    /// <summary>아이템 고유 ID</summary>
    public int itemID;

    /// <summary>아이템 표시 이름</summary>
    public string itemName;

    /// <summary>아이템 아이콘 스프라이트</summary>
    public Sprite itemIcon;
}
