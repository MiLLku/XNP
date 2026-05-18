using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 바닥에 떨어진 아이템(ClickableItem)의 저장/복원 모듈.
///
/// 저장: 씬 내 모든 ClickableItem을 DroppedItemSaveData로 캡처.
/// 복원: 아이템을 인벤토리에 직접 추가 (월드 스폰 대신).
///       향후 월드 스폰이 필요하면 Restore에서 프리팹 인스턴스화 추가 가능.
/// </summary>
public class DroppedItemSaveModule : MonoBehaviour, ISaveModule
{
    public int SaveOrder => 70;

    public void Capture(SaveData data)
    {
        data.droppedItems = new List<DroppedItemSaveData>();

        // DroppedItem (채광/수확 드롭, 직원 운반 대상)
        var droppedItems = FindObjectsByType<DroppedItem>(FindObjectsSortMode.None);
        foreach (var item in droppedItems)
        {
            if (item.itemData == null) continue;

            data.droppedItems.Add(new DroppedItemSaveData
            {
                itemId = item.itemData.itemID,
                amount = item.quantity,
                posX   = item.transform.position.x,
                posY   = item.transform.position.y
            });
        }

        // ClickableItem (플레이어 클릭 픽업 아이템)
        var clickableItems = FindObjectsByType<ClickableItem>(FindObjectsSortMode.None);
        foreach (var item in clickableItems)
        {
            ItemData itemData = item.GetItemData();
            if (itemData == null) continue;

            data.droppedItems.Add(new DroppedItemSaveData
            {
                itemId = itemData.itemID,
                amount = 1,
                posX   = item.transform.position.x,
                posY   = item.transform.position.y
            });
        }

        Debug.Log($"[DroppedItemSaveModule] Capture: {data.droppedItems.Count}개 드롭 아이템 저장");
    }

    public void Restore(SaveData data)
    {
        // 기존 DroppedItem 제거 (ClickableItem은 건드리지 않음)
        var existingDropped = FindObjectsByType<DroppedItem>(FindObjectsSortMode.None);
        foreach (var item in existingDropped)
            Destroy(item.gameObject);

        // 기존 ClickableItem 제거
        var existingClickable = FindObjectsByType<ClickableItem>(FindObjectsSortMode.None);
        foreach (var item in existingClickable)
            Destroy(item.gameObject);

        if (data.droppedItems == null || data.droppedItems.Count == 0) return;
        if (GameDatabase.Instance == null) return;

        int restoredCount = 0;
        foreach (var droppedData in data.droppedItems)
        {
            ItemData itemData = GameDatabase.Instance.GetItemData(droppedData.itemId);
            if (itemData == null) continue;

            Vector3 pos = new Vector3(droppedData.posX, droppedData.posY, 0f);

            // DroppedItemManager가 있으면 월드에 스폰 (직원이 운반)
            if (DroppedItemManager.instance != null)
            {
                DroppedItemManager.instance.SpawnItem(itemData, droppedData.amount, pos);
            }
            else if (InventoryManager.instance != null)
            {
                // 폴백: 인벤토리에 직접 추가
                InventoryManager.instance.AddItem(itemData, droppedData.amount);
            }

            restoredCount++;
        }

        Debug.Log($"[DroppedItemSaveModule] Restore: {restoredCount}개 드롭 아이템 → 월드 스폰");
    }

    public void PostRestore(SaveData data) { }
}
