using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 모든 구역을 등록/관리하고 타일↔구역 매핑을 제공하는 전역 매니저.
///
/// 핵심 기능:
///   - 구역 CRUD (생성/삭제/타일 확장·축소)
///   - 타일 좌표 → 구역 조회 (O(1))
///   - 타일 파괴 시 자동 구역 갱신
///
/// 구역에는 용도가 없습니다. 직원에게 배정해야 의미가 생깁니다([[EmployeeZoneAssignment]]).
///
/// 사용 예:
///   var zone = ZoneManager.instance.CreateZone();            // "구역 3" 자동 명명
///   ZoneManager.instance.PaintTiles(zone.zoneId, tiles);     // 확장
///   ZoneManager.instance.EraseTilesFromZone(zone.zoneId, t); // 축소
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

    #region 리비전

    /// <summary>
    /// 구역 구성이 바뀔 때마다 증가하는 리비전 번호.
    /// 구역은 지형이 아니므로 GameMap.NavVersion으로는 변화를 감지할 수 없습니다.
    /// ReachabilityMap이 제한구역 기준 연결 성분을 다시 만들어야 할 시점을 판단하는 데 씁니다.
    /// </summary>
    public int ZoneVersion { get; private set; } = 0;

    #endregion

    #region 구역 생성/삭제

    /// <summary>
    /// 새 구역을 생성합니다. 이름을 비우면 "구역 N"으로 자동 부여합니다.
    /// </summary>
    /// <param name="name">구역 이름 (null/빈 문자열이면 자동)</param>
    /// <param name="color">오버레이 색상 (기본: 구역 번호별 자동 팔레트)</param>
    /// <returns>생성된 구역</returns>
    public Zone CreateZone(string name = null, Color? color = null)
    {
        int id = nextZoneId++;

        var zone = new Zone
        {
            zoneId = id,
            zoneName = string.IsNullOrWhiteSpace(name) ? $"구역 {id}" : name,
            displayColor = color ?? GetPaletteColor(id)
        };

        zones[zone.zoneId] = zone;
        ZoneVersion++;
        GameMessageBus.Publish(new ZoneCreatedMessage(zone));

        if (showDebugLogs)
            Debug.Log($"[ZoneManager] 구역 생성: [{zone.zoneId}] {zone.zoneName}");

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
        ZoneVersion++;
        GameMessageBus.Publish(new ZoneDeletedMessage(zoneId));

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
                ZoneVersion++;
                GameMessageBus.Publish(new ZoneTilesChangedMessage(existingZoneId));
            }
        }

        zone.AddTile(tile);
        tileToZoneId[tile] = zoneId;
        zone.RecalculateBounds();
        ZoneVersion++;
        GameMessageBus.Publish(new ZoneTilesChangedMessage(zoneId));
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
        ZoneVersion++;
        GameMessageBus.Publish(new ZoneTilesChangedMessage(zoneId));
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
        ZoneVersion++;
        GameMessageBus.Publish(new ZoneTilesChangedMessage(zoneId));
    }

    /// <summary>
    /// 여러 타일을 소속 구역에서 한 번에 제거합니다 (드래그 지우기).
    /// 타일마다 소속 구역이 다를 수 있으므로 구역별로 모아 처리합니다.
    /// </summary>
    /// <returns>실제로 제거된 타일 수</returns>
    public int RemoveTilesFromZones(IEnumerable<Vector2Int> tiles)
    {
        var touched = new HashSet<int>();
        int removed = 0;

        foreach (var tile in tiles)
        {
            if (!tileToZoneId.TryGetValue(tile, out int zoneId)) continue;

            if (zones.TryGetValue(zoneId, out Zone zone))
            {
                zone.RemoveTile(tile);
                touched.Add(zoneId);
                removed++;
            }
            tileToZoneId.Remove(tile);
        }

        if (removed == 0) return 0;

        foreach (int zoneId in touched)
        {
            if (zones.TryGetValue(zoneId, out Zone zone))
                zone.RecalculateBounds();
        }

        ZoneVersion++;
        foreach (int zoneId in touched)
            GameMessageBus.Publish(new ZoneTilesChangedMessage(zoneId));

        return removed;
    }

    /// <summary>
    /// 특정 구역에 타일들을 추가합니다 (구역 편집 — 확장).
    /// </summary>
    public void PaintTiles(int zoneId, IEnumerable<Vector2Int> tiles)
        => AddTilesToZone(zoneId, tiles);

    /// <summary>
    /// 특정 구역에서만 타일들을 제거합니다 (구역 편집 — 축소).
    /// 다른 구역의 타일은 건드리지 않습니다.
    /// </summary>
    /// <returns>실제로 제거된 타일 수</returns>
    public int EraseTilesFromZone(int zoneId, IEnumerable<Vector2Int> tiles)
    {
        if (!zones.TryGetValue(zoneId, out Zone zone)) return 0;

        int removed = 0;
        foreach (var tile in tiles)
        {
            if (!tileToZoneId.TryGetValue(tile, out int mappedId) || mappedId != zoneId) continue;

            zone.RemoveTile(tile);
            tileToZoneId.Remove(tile);
            removed++;
        }

        if (removed == 0) return 0;

        zone.RecalculateBounds();
        ZoneVersion++;
        GameMessageBus.Publish(new ZoneTilesChangedMessage(zoneId));
        return removed;
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
                ZoneVersion++;
                GameMessageBus.Publish(new ZoneTilesChangedMessage(zoneId));
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

    /// <summary>등록된 모든 구역을 반환합니다.</summary>
    public List<Zone> GetAllZones()
    {
        return zones.Values.ToList();
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

    /// <summary>
    /// 구역 번호별 표시색 (오버레이·리스트·선택 박스가 공유).
    /// 구역에 용도가 없으므로 번호를 색으로 구분합니다.
    /// </summary>
    public static Color GetPaletteColor(int zoneId)
    {
        // 채도/명도를 고정하고 색상만 황금비로 돌려 인접 구역이 잘 구분되게 한다
        float hue = (zoneId * 0.618033988f) % 1f;
        Color c = Color.HSVToRGB(hue, 0.65f, 0.95f);
        c.a = 0.3f;
        return c;
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

        ZoneVersion++; // 로드로 구역 구성이 통째로 바뀜
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
