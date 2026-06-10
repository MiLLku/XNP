using System.Collections.Generic;
using UnityEngine;

/// <summary>전력 시스템 공통 유틸리티.</summary>
public static class PowerUtil
{
    /// <summary>
    /// 건물이 점유하는 풋프린트 셀들을 반환합니다 (왼쪽 아래 origin + BuildingData.size).
    /// 전력 노드의 4방향 인접 판정에 사용됩니다.
    /// </summary>
    public static IEnumerable<Vector2Int> FootprintCells(Transform t, Building building)
    {
        int bx = Mathf.FloorToInt(t.position.x);
        int by = Mathf.FloorToInt(t.position.y);

        Vector2Int size = (building != null && building.buildingData != null)
            ? building.buildingData.size
            : Vector2Int.one;
        if (size.x < 1) size.x = 1;
        if (size.y < 1) size.y = 1;

        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
                yield return new Vector2Int(bx + x, by + y);
    }
}
