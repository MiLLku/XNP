using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 자재 운반 실패/작업 취소 시 이미 도착한 자재를 환불하는 공통 헬퍼.
///
/// 흐름:
///   1. 작업이 취소될 때(CancelConstruction / CancelMaterialRequest 등)
///   2. 이미 사용처에 도착해 있던 자재(_deliveredMaterials)를 사이트 위치에 DroppedItem으로 떨어뜨림
///   3. DroppedItem이 HaulOrder를 자동 생성 → 직원이 가장 가까운 창고로 다시 운반 → 인벤토리 복구
///
/// 시각적으로 자연스럽고(자재가 갑자기 사라지지 않음) 직원이 운반하는 흐름이 유지됩니다.
/// </summary>
public static class ItemRefundHelper
{
    /// <summary>
    /// materials의 모든 아이템을 origin 위치(또는 footprint 내부)에 DroppedItem으로 스폰합니다.
    /// </summary>
    /// <param name="materials">환불할 아이템과 수량</param>
    /// <param name="origin">사이트 좌측 하단 월드 좌표</param>
    /// <param name="footprint">건물 크기(타일 단위). 1×1이면 같은 자리, 큰 건물은 분산</param>
    public static void SpawnRefunds(Dictionary<ItemData, int> materials, Vector3 origin, Vector2Int footprint)
    {
        if (materials == null || materials.Count == 0) return;

        Vector2Int size = footprint;
        if (size.x < 1) size.x = 1;
        if (size.y < 1) size.y = 1;

        // DroppedItemManager가 없으면 인벤토리로 직접 반납 (폴백)
        if (DroppedItemManager.instance == null)
        {
            Debug.LogWarning("[ItemRefundHelper] DroppedItemManager 없음 → InventoryManager로 직접 반납");
            foreach (var kv in materials)
            {
                if (kv.Key == null || kv.Value <= 0) continue;
                InventoryManager.instance?.AddItem(kv.Key, kv.Value);
            }
            return;
        }

        int total = 0;
        foreach (var kv in materials)
        {
            if (kv.Key == null || kv.Value <= 0) continue;
            for (int i = 0; i < kv.Value; i++)
            {
                float dx = Random.Range(0f, size.x);
                float dy = Random.Range(0f, size.y);
                Vector3 dropPos = origin + new Vector3(dx, dy, 0f);
                DroppedItemManager.instance.SpawnItem(kv.Key, 1, dropPos);
                total++;
            }
        }

        Debug.Log($"[ItemRefundHelper] 자재 환불 드롭 {total}개 (origin={origin}, footprint={size})");
    }
}
