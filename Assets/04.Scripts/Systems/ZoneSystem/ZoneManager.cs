using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 모든 구역을 등록/관리하고 타일↔구역 매핑을 제공하는 전역 매니저.
///
/// 핵심 기능:
///   - 구역 CRUD (생성/삭제/타일 추가·제거)
///   - 타일 좌표 → 구역 조회 (O(1))
///   - 구역 타입별 조회
///   - 타일 파괴 시 자동 구역 갱신
///
/// 사용 예:
///   var zone = ZoneManager.instance.CreateZone("침실 A", ZoneType.Sleep);
///   ZoneManager.instance.AddTileToZone(zone.zoneId, new Vector2Int(10, 5));
///   Zone found = ZoneManager.instance.GetZoneAt(new Vector2Int(10, 5));
/// </summary>
public class ZoneManager : DestroySingleton<ZoneManager>, ISaveModule
{
    #region 필드

    [Header("디버그")]
    [SerializeField] private bool showDebugLogs = false;

    /// <summary>등록된 모든 구역 (zoneId → Zone)</summary>
    private Dictionary<int, Zone> zones = new Dictionary<int, Zone>();

    /// <summary>타일 → 구역ID 매핑 (한 타일은 하나의 구역에만 속함)</summary>
    private Dictionary<Vector2Int, int> tileToZoneId = new Dictionary<Vector2Int, int>();

    /// <summary>다음 구역 ID</summary>
    private int nextZoneId = 1;

    #endregion

    #region 이벤트

    /// <summary>구역 생성 시 발생</summary>
    public event Action<Zone> OnZoneCreated;

    /// <summary>구역 삭제 시 발생</summary>
    public event Action<int> OnZoneDeleted;

    /// <summary>구역 타일 변경 시 발생 (zoneId)</summary>
    public event Action<int> OnZoneTilesChanged;

    #endregion

    #region 구역 생성/삭제

    /// <summary>
    /// 새 구역을 생성합니다.
    /// </summary>
    /// <param name="name">구역 이름</param>
    /// <param name="type">구역 용도</param>
    /// <param name="color">오버레이 색상 (기본: 타입별 자동)</param>
    /// <returns>생성된 구역</returns>
    public Zone CreateZone(string name, ZoneType type, Color? color = null)
    {
        var zone = new Zone
        {
            zoneId = nextZoneId++,
            zoneName = name,
            zoneType = type,
            displayColor = color ?? GetDefaultColor(type)
        };

        zones[zone.zoneId] = zone;
        OnZoneCreated?.Invoke(zone);

        if (showDebugLogs)
            Debug.Log($"[ZoneManager] 구역 생성: [{zone.zoneId}] {name} ({type})");

        return zone;
    }

    /// <summary>
    /// 구역을 삭제합니다.
    /// 해당 구역에 속한 모든 타일 매핑도 제거됩니다.
    /// </summary>
    public void DeleteZone(int zoneId)
    {
        if (!zones.TryGetValue(zoneId, out Zone zone)) return;

        // 타일 매핑 제거
        foreach (var tile in zone.tiles)
        {
            if (tileToZoneId.TryGetValue(tile, out int mappedId) && mappedId == zoneId)
                tileToZoneId.Remove(tile);
        }

        zones.Remove(zoneId);
        OnZoneDeleted?.Invoke(zoneId);

        if (showDebugLogs)
            Debug.Log($"[ZoneManager] 구역 삭제: [{zoneId}] {zone.zoneName}");
    }

    #endregion

    #region 타일 관리

    /// <summary>
    /// 구역에 타일을 추가합니다.
    /// 타일이 이미 다른 구역에 속해 있으면 기존 구역에서 제거됩니다.
    /// </summary>
    public void AddTileToZone(int zoneId, Vector2Int tile)
    {
        if (!zones.TryGetValue(zoneId, out Zone zone)) return;

        // 기존 구역에서 제거
        if (tileToZoneId.TryGetValue(tile, out int existingZoneId) && existingZoneId != zoneId)
        {
            if (zones.TryGetValue(existingZoneId, out Zone existingZone))
            {
                existingZone.RemoveTile(tile);
                existingZone.RecalculateBounds();
                OnZoneTilesChanged?.Invoke(existingZoneId);
            }
        }

        zone.AddTile(tile);
        tileToZoneId[tile] = zoneId;
        zone.RecalculateBounds();
        OnZoneTilesChanged?.Invoke(zoneId);
    }

    /// <summary>
    /// 구역에서 타일을 제거합니다.
    /// </summary>
    public void RemoveTileFromZone(int zoneId, Vector2Int tile)
    {
        if (!zones.TryGetValue(zoneId, out Zone zone)) return;

        zone.RemoveTile(tile);

        if (tileToZoneId.TryGetValue(tile, out int mappedId) && mappedId == zoneId)
            tileToZoneId.Remove(tile);

        zone.RecalculateBounds();
        OnZoneTilesChanged?.Invoke(zoneId);
    }

    /// <summary>
    /// 여러 타일을 한 번에 구역에 추가합니다 (드래그 칠하기).
    /// </summary>
    public void AddTilesToZone(int zoneId, IEnumerable<Vector2Int> tiles)
    {
        if (!zones.TryGetValue(zoneId, out Zone zone)) return;

        foreach (var tile in tiles)
        {
            if (tileToZoneId.TryGetValue(tile, out int existingId) && existingId != zoneId)
            {
                if (zones.TryGetValue(existingId, out Zone existingZone))
                    existingZone.RemoveTile(tile);
            }

            zone.AddTile(tile);
            tileToZoneId[tile] = zoneId;
        }

        zone.RecalculateBounds();
        OnZoneTilesChanged?.Invoke(zoneId);
    }

    /// <summary>
    /// 타일이 파괴되었을 때 호출합니다 (채굴/건설 등).
    /// 해당 타일의 구역 매핑을 자동 제거합니다.
    /// </summary>
    public void OnTileDestroyed(Vector2Int tile)
    {
        if (tileToZoneId.TryGetValue(tile, out int zoneId))
        {
            if (zones.TryGetValue(zoneId, out Zone zone))
            {
                zone.RemoveTile(tile);
                zone.RecalculateBounds();
                OnZoneTilesChanged?.Invoke(zoneId);
            }

            tileToZoneId.Remove(tile);
        }
    }

    #endregion

    #region 조회

    /// <summary>구역 ID로 구역을 반환합니다.</summary>
    public Zone GetZone(int zoneId)
    {
        zones.TryGetValue(zoneId, out Zone zone);
        return zone;
    }

    /// <summary>타일 좌표에 있는 구역을 반환합니다 (없으면 null).</summary>
    public Zone GetZoneAt(Vector2Int tile)
    {
        if (tileToZoneId.TryGetValue(tile, out int zoneId))
            return GetZone(zoneId);
        return null;
    }

    /// <summary>타일 좌표의 구역 ID를 반환합니다 (없으면 -1).</summary>
    public int GetZoneIdAt(Vector2Int tile)
    {
        return tileToZoneId.TryGetValue(tile, out int zoneId) ? zoneId : -1;
    }

    /// <summary>특정 타입의 모든 구역을 반환합니다.</summary>
    public List<Zone> GetZonesByType(ZoneType type)
    {
        return zones.Values.Where(z => z.zoneType == type).ToList();
    }

    /// <summary>등록된 모든 구역을 반환합니다.</summary>
    public List<Zone> GetAllZones()
    {
        return zones.Values.ToList();
    }

    /// <summary>특정 타일이 제한 구역인지 확인합니다.</summary>
    public bool IsRestrictedTile(Vector2Int tile)
    {
        if (tileToZoneId.TryGetValue(tile, out int zoneId))
        {
            if (zones.TryGetValue(zoneId, out Zone zone))
                return zone.zoneType == ZoneType.Restricted;
        }
        return false;
    }

    #endregion

    #region 구역 이름 변경

    /// <summary>구역 이름을 변경합니다.</summary>
    public void RenameZone(int zoneId, string newName)
    {
        if (zones.TryGetValue(zoneId, out Zone zone))
            zone.zoneName = newName;
    }

    #endregion

    #region 기본 색상

    private Color GetDefaultColor(ZoneType type)
    {
        return type switch
        {
            ZoneType.Sleep       => new Color(0.3f, 0.3f, 0.8f, 0.3f),
            ZoneType.Recreation  => new Color(0.2f, 0.8f, 0.2f, 0.3f),
            ZoneType.Wash        => new Color(0.2f, 0.7f, 0.9f, 0.3f),
            ZoneType.Work        => new Color(0.9f, 0.7f, 0.1f, 0.3f),
            ZoneType.Storage     => new Color(0.6f, 0.4f, 0.2f, 0.3f),
            ZoneType.Restricted  => new Color(0.9f, 0.1f, 0.1f, 0.3f),
            _                    => new Color(0.5f, 0.5f, 0.5f, 0.3f)
        };
    }

    #endregion

    #region ISaveModule

    /// <summary>맵 이후, 직원 이전에 로드 (직원이 zoneId를 참조하므로)</summary>
    public int SaveOrder => 25;

    public void Capture(SaveData data)
    {
        var saveData = new ZoneManagerSaveData
        {
            nextZoneId = nextZoneId,
            zones = new List<Zone>()
        };

        foreach (var zone in zones.Values)
        {
            zone.PrepareForSave();
            saveData.zones.Add(zone);
        }

        data.zoneSystem = saveData;
    }

    public void Restore(SaveData data)
    {
        if (data.zoneSystem == null) return;

        nextZoneId = data.zoneSystem.nextZoneId;
        zones.Clear();
        tileToZoneId.Clear();

        if (data.zoneSystem.zones == null) return;

        foreach (var zone in data.zoneSystem.zones)
        {
            zone.RestoreFromLoad();
            zones[zone.zoneId] = zone;

            foreach (var tile in zone.tiles)
                tileToZoneId[tile] = zone.zoneId;
        }
    }

    public void PostRestore(SaveData data) { }

    #endregion
}

/// <summary>ZoneManager 저장 데이터</summary>
[System.Serializable]
public class ZoneManagerSaveData
{
    public int nextZoneId;
    public List<Zone> zones;
}
