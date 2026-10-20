using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 건물 부속 판정 — 본 건물과 <b>같은 방</b> 안에 있는 부속 건물만 센다.
/// 본 건물이 실외(방 없음)면 부속 효과는 없다. 방 판정은 온도 시스템과 같은 규칙(자기 칸 → 둘레 투표).
/// </summary>
public static class BuildingAddonUtil
{
    /// <summary>건물이 속한 방 번호 (실외면 RoomManager.OUTDOOR_ID)</summary>
    public static int GetRoomId(Building building)
    {
        if (building == null || TemperatureManager.instance == null) return RoomManager.OUTDOOR_ID;
        return TemperatureManager.instance.ResolveRoomOf(building.HeatTilePosition, building.HeatFootprint);
    }

    // 작업 속도 배율은 작업 루프가 자주 묻기 때문에 건물마다 1초 캐시
    private const float SPEED_CACHE_SECONDS = 1f;
    private static readonly Dictionary<Building, (float time, float mult)> speedCache = new Dictionary<Building, (float, float)>();

    /// <summary>
    /// 같은 방의 부속 건물이 주는 작업 속도 배율 (1 = 부속 없음).
    /// 부속 BuildingData.addonForBuildingIds에 host ID가 있으면 그 addonWorkSpeedBonus를 더한다 — 종류당 한 번.
    /// </summary>
    public static float GetWorkSpeedMultiplier(Building host)
    {
        if (host == null || host.buildingData == null) return 1f;
        if (speedCache.TryGetValue(host, out var c) && Time.time - c.time < SPEED_CACHE_SECONDS) return c.mult;

        float mult = 1f;
        int room = GetRoomId(host);
        if (room != RoomManager.OUTDOOR_ID)
        {
            int hostId = host.buildingData.buildingID;
            var counted = new HashSet<int>();
            foreach (var b in UnityEngine.Object.FindObjectsByType<Building>(FindObjectsSortMode.None))
            {
                var d = b != null ? b.buildingData : null;
                if (d == null || b == host || d.addonWorkSpeedBonus <= 0f || d.addonForBuildingIds == null) continue;
                if (System.Array.IndexOf(d.addonForBuildingIds, hostId) < 0 || counted.Contains(d.buildingID)) continue;
                if (GetRoomId(b) != room) continue;
                counted.Add(d.buildingID);
                mult += d.addonWorkSpeedBonus;
            }
        }

        speedCache[host] = (Time.time, mult);
        return mult;
    }

    /// <summary>host와 같은 방에 있는 buildingID 건물 수 (host 자신 제외)</summary>
    public static int CountInSameRoom(Building host, int addonBuildingId)
    {
        int room = GetRoomId(host);
        if (room == RoomManager.OUTDOOR_ID) return 0;

        int count = 0;
        // shortcut: 창을 열 때만 부르므로 전수 검색. 자주 부르게 되면 건물 목록 캐시로 바꿀 것
        foreach (var b in UnityEngine.Object.FindObjectsByType<Building>(FindObjectsSortMode.None))
        {
            if (b == null || b == host || b.buildingData == null || b.buildingData.buildingID != addonBuildingId) continue;
            if (GetRoomId(b) == room) count++;
        }
        return count;
    }
}
