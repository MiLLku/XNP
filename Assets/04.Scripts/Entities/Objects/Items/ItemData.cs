using UnityEngine;

/// <summary>
/// 아이템 기본 데이터 ScriptableObject.
/// 인벤토리, 제작, 건설 등에서 아이템을 식별하는 데 사용됩니다.
/// </summary>
[CreateAssetMenu(fileName = "NewItemData", menuName = "StampSystem/Item Data")]
public class ItemData : ScriptableObject
{
    [Header("아이템 기본 정보")]

    [Tooltip("아이템 종류 — itemID는 이 enum의 정수값에서 자동 계산됩니다.")]
    public ItemType itemType = ItemType.None;

    /// <summary>아이템 고유 정수 ID (itemType의 정수값, 저장·통신용).</summary>
    public int itemID => (int)itemType;

    /// <summary>아이템 표시 이름</summary>
    public string itemName;

    /// <summary>아이템 아이콘 스프라이트 (인벤토리 UI·DroppedItem 폴백 비주얼)</summary>
    public Sprite itemIcon;

    [Header("드롭 비주얼")]
    [Tooltip("바닥에 떨어졌을 때 사용할 prefab. 비어있으면 DroppedItemManager의 공용 prefab을 사용합니다.")]
    public GameObject dropPrefab;

    [Header("음식")]
    [Tooltip("직원이 섭취할 수 있는 음식인지 여부.")]
    public bool isFood = false;

    [Tooltip("섭취 시 회복되는 배고픔 수치 (0~100). isFood가 true일 때만 의미가 있습니다.")]
    public int nutrition = 0;
}
