using UnityEngine;

/// <summary>
/// 맵 스탬퍼.
/// StampLibrary에서 스탬프를 조회하여 GameMap에 배치합니다.
/// 피벗 좌표를 기준으로 상대 좌표를 계산하여 타일/개체를 기록합니다.
/// </summary>
public class MapStamper
{
    private readonly GameMap _map;
    private readonly StampLibrary _library;

    public MapStamper(GameMap map, StampLibrary library)
    {
        _map = map;
        _library = library;
    }

    /// <summary>
    /// 지정된 위치에 스탬프를 배치합니다.
    /// </summary>
    /// <param name="key">라이브러리에서 찾을 스탬프 키</param>
    /// <param name="worldPosition">맵에 배치할 기준 좌표</param>
    /// <returns>성공 여부</returns>
    public bool PlaceStamp(string key, Vector2Int worldPosition)
    {
        StampData stamp = _library.GetStamp(key);
        if (stamp == null)
        {
            Debug.LogWarning($"[MapStamper] 스탬프를 찾지 못했습니다: {key}");
            return false;
        }

        // 지형 타일 배치
        foreach (var element in stamp.elements)
        {
            int targetX = worldPosition.x + element.position.x - stamp.pivot.x;
            int targetY = worldPosition.y + element.position.y - stamp.pivot.y;

            switch (element.type)
            {
                case TypeObjectTile.Tile:
                    _map.SetTile(targetX, targetY, element.id);
                    break;

                case TypeObjectTile.Building:
                case TypeObjectTile.Enemy:
                case TypeObjectTile.Plant:
                case TypeObjectTile.MapObject:
                    _map.AddEntity(new MapEntity
                    {
                        position = new Vector2Int(targetX, targetY),
                        type = element.type,
                        id = element.id
                    });
                    break;
            }
        }

        // 벽 타일 배치
        foreach (var wallElement in stamp.wallElements)
        {
            int targetX = worldPosition.x + wallElement.position.x - stamp.pivot.x;
            int targetY = worldPosition.y + wallElement.position.y - stamp.pivot.y;
            _map.SetWall(targetX, targetY, wallElement.id);
        }

        return true;
    }
}
