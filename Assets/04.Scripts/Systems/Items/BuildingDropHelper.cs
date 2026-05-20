using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 건물 파괴/철거 시 페이백 재료를 DroppedItem으로 스폰하는 공통 헬퍼.
///
/// 우선순위:
///   1. BuildingData.destructionDrops가 비어있지 않으면 그대로 사용
///   2. 비어있으면 BuildingData.requiredResources × paybackRatio를 자동 계산
///
/// 스폰 위치:
///   - 건물 footprint 내부에 무작위 분산 (작은 건물은 거의 같은 자리, 큰 건물은 흩어짐)
///   - DroppedItemManager가 없으면 경고 후 무시 (인벤토리 폴백 없음 — 항상 드롭 전환)
/// </summary>
public static class BuildingDropHelper
{
    /// <summary>
    /// 건물 파괴/철거 페이백 재료를 바닥에 드롭합니다.
    /// </summary>
    /// <param name="data">건물 데이터 (destructionDrops·paybackRatio·size·requiredResources 사용)</param>
    /// <param name="buildingOrigin">건물의 좌측 하단 월드 좌표 (transform.position과 동일)</param>
    public static void SpawnDestructionDrops(BuildingData data, Vector3 buildingOrigin)
    {
        if (data == null) return;

        if (DroppedItemManager.instance == null)
        {
            Debug.LogWarning($"[BuildingDropHelper] DroppedItemManager가 없어 '{data.buildingName}' 페이백 드롭을 건너뜁니다.");
            return;
        }

        List<ResourceCost> drops = ResolveDrops(data);
        if (drops == null || drops.Count == 0) return;

        Vector2Int size = data.size;
        if (size.x < 1) size.x = 1;
        if (size.y < 1) size.y = 1;

        int spawnedTotal = 0;
        foreach (var cost in drops)
        {
            if (cost.item == null || cost.amount <= 0) continue;

            for (int i = 0; i < cost.amount; i++)
            {
                float dx = Random.Range(0f, size.x);
                float dy = Random.Range(0f, size.y);
                Vector3 dropPos = buildingOrigin + new Vector3(dx, dy, 0f);

                DroppedItemManager.instance.SpawnItem(cost.item, 1, dropPos);
                spawnedTotal++;
            }
        }

        Debug.Log($"[BuildingDropHelper] '{data.buildingName}' 파괴 드롭 {spawnedTotal}개 스폰 완료");
    }

    /// <summary>
    /// 실제로 드롭할 ResourceCost 목록을 계산합니다.
    /// destructionDrops 우선, 비어있으면 requiredResources × paybackRatio.
    /// </summary>
    private static List<ResourceCost> ResolveDrops(BuildingData data)
    {
        // 1) 명시된 destructionDrops 사용
        if (data.destructionDrops != null && data.destructionDrops.Count > 0)
            return data.destructionDrops;

        // 2) requiredResources × paybackRatio 자동 계산
        var auto = new List<ResourceCost>();
        if (data.requiredResources == null) return auto;

        float ratio = Mathf.Clamp01(data.paybackRatio);
        foreach (var cost in data.requiredResources)
        {
            if (cost.item == null || cost.amount <= 0) continue;
            int amt = Mathf.Max(1, Mathf.RoundToInt(cost.amount * ratio));
            auto.Add(new ResourceCost { item = cost.item, amount = amt });
        }
        return auto;
    }
}
