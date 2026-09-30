using UnityEngine;

/// <summary>
/// 게임 중 식생물 배치 — 자연 생성(<see cref="PlantSpawner"/>)과 파종이 함께 씁니다.
///
/// <b>점유 그리드를 건드리지 않습니다.</b> GameMap.UnmarkTileOccupied가 바닥 지지까지 지우므로,
/// 건설 바닥 위에 심은 식물을 치울 때 바닥이 망가지지 않도록 "칸에 식물이 있는가"는
/// <see cref="MapEntityIdentity.All"/>로 판정합니다.
/// </summary>
public static class PlantPlacement
{
    /// <summary>
    /// 블록 설치 조건 — 발 딛는 칸들이 비어 있고(공기·미점유·다른 식물 없음) 그 아래가 단단한지.
    /// 파종은 이것만 봅니다. 자라는지는 플레이어 몫입니다.
    /// </summary>
    public static bool CanPlaceAt(EntityDefinition def, Vector2Int cell)
    {
        var map = MapGenerator.instance != null ? MapGenerator.instance.GameMapInstance : null;
        if (def == null || map == null) return false;

        int width = Mathf.Max(1, def.footprintWidth);
        for (int i = 0; i < width; i++)
        {
            int x = cell.x + i, y = cell.y;
            if (x < 0 || x >= GameMap.MAP_WIDTH || y < 1 || y >= GameMap.MAP_HEIGHT) return false;
            if (map.TileGrid[x, y] != (int)TileType.Air) return false;
            if (map.IsTileOccupied(x, y)) return false;
            if (!map.IsSolidGround(x, y - 1)) return false;
            if (HasPlantAt(new Vector2Int(x, y))) return false;
        }
        return true;
    }

    /// <summary>자연 생성 가능 — 설치 조건 + 스폰 가능한 지표면·허용 타일 + 생성 조건(환경·주변 개수).</summary>
    public static bool CanSpawnNaturallyAt(EntityDefinition def, Vector2Int cell)
    {
        if (!CanPlaceAt(def, cell)) return false;

        var map = MapGenerator.instance.GameMapInstance;
        int width = Mathf.Max(1, def.footprintWidth);
        for (int i = 0; i < width; i++)
        {
            int ground = map.TileGrid[cell.x + i, cell.y - 1];
            if (!TileRules.IsSpawnableSurface(ground)) return false;
            if (def.allowedGroundTiles != null && def.allowedGroundTiles.Length > 0 &&
                System.Array.IndexOf(def.allowedGroundTiles, (TileType)ground) < 0) return false;
        }

        return PlantConditionChecker.CanSpawn(def, cell);
    }

    /// <summary>해당 칸을 발밑 폭으로 차지한 식생물이 있는지.</summary>
    public static bool HasPlantAt(Vector2Int cell)
    {
        foreach (var e in MapEntityIdentity.All)
        {
            var def = e.Definition;
            if (def == null || def.kind != EntityKind.Plant) continue;

            Vector2Int c = Vector2Int.FloorToInt(e.transform.position);
            if (c.y == cell.y && cell.x >= c.x && cell.x < c.x + Mathf.Max(1, def.footprintWidth)) return true;
        }
        return false;
    }

    /// <summary>
    /// 개체를 생성하고 성장도를 설정합니다. 저장·복원은 MapEntityIdentity 꼬리표로 자동 처리됩니다.
    /// </summary>
    public static PlantBase Spawn(EntityDefinition def, Vector2Int cell, float growth)
    {
        if (def == null || def.prefab == null) return null;

        var go = UnityEngine.Object.Instantiate(def.prefab, new Vector3(cell.x, cell.y, 0f), Quaternion.identity);
        MapEntityIdentity.Attach(go, def.id);

        var plant = go.GetComponentInChildren<PlantBase>();
        if (plant != null) plant.SetGrowth(growth);
        return plant;
    }
}
