using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 새 게임 시작 시 글로벌 인벤토리에 지급할 기본 아이템 목록.
///
/// InventoryManager가 Start에서 이 설정을 읽어 1회 주입합니다.
/// 세이브 로드 시에는 InventoryManager.Restore가 ClearInventory 후 저장분으로
/// 덮어쓰므로, 새 게임에만 적용되고 로드 게임에는 영향이 없습니다.
/// </summary>
[CreateAssetMenu(fileName = "StartingInventoryConfig", menuName = "StampSystem/Starting Inventory Config")]
public class StartingInventoryConfig : ScriptableObject
{
    [Tooltip("새 게임 시작 시 지급할 아이템과 수량. 식량을 포함해 초반 생존을 지탱합니다.")]
    public List<ResourceCost> startingItems = new List<ResourceCost>();

    [Tooltip("새 게임 시작 시 장비 보관소 풀에 지급할 장비. EquipmentStorageManager가 읽습니다.")]
    public List<StartingEquipmentEntry> startingEquipment = new List<StartingEquipmentEntry>();
}

/// <summary>시작 지급 장비 항목.</summary>
[System.Serializable]
public class StartingEquipmentEntry
{
    public EquipmentData equipment;
    [Min(1)] public int amount = 1;
}
