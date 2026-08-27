using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 문 위치 기반 조회를 제공하는 싱글톤 매니저.
///
/// 역할:
///   - Door 컴포넌트가 Start()에서 자신의 타일 좌표를 등록
///   - EmployeeMovement가 이동 전 해당 타일에 문이 있는지 조회
///   - 문이 파괴될 때 OnDestroy()에서 자동 해제
///
/// 등록 방식:
///   문은 1×2 크기이므로 하단 타일과 상단 타일(+Vector2Int.up) 두 위치를 모두 등록합니다.
/// </summary>
public class DoorManager : DestroySingleton<DoorManager>
{
    /// <summary>타일 좌표 → Door 인스턴스 매핑</summary>
    private readonly Dictionary<Vector2Int, Door> _doors = new();

    // ── 등록/해제 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 문을 등록합니다. 하단 타일(bottomTile)과 상단 타일(bottomTile+up) 두 위치를 모두 등록합니다.
    /// </summary>
    public void Register(Vector2Int bottomTile, Door door)
    {
        _doors[bottomTile]                 = door;
        _doors[bottomTile + Vector2Int.up] = door;
        Debug.Log($"[DoorManager] 문 등록: {bottomTile} / {bottomTile + Vector2Int.up}");
    }

    /// <summary>
    /// 문 등록을 해제합니다. 파괴된 문이 OnDestroy()에서 호출합니다.
    /// </summary>
    public void Unregister(Vector2Int bottomTile)
    {
        _doors.Remove(bottomTile);
        _doors.Remove(bottomTile + Vector2Int.up);
        Debug.Log($"[DoorManager] 문 해제: {bottomTile}");
    }

    // ── 조회 ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 타일 좌표에 있는 Door 인스턴스를 반환합니다. 없으면 null.
    /// </summary>
    public Door GetDoorAt(Vector2Int tile)
    {
        _doors.TryGetValue(tile, out var door);
        return door;
    }

    /// <summary>
    /// 타일 좌표에 문이 존재하는지 여부를 반환합니다.
    /// </summary>
    public bool IsDoorAt(Vector2Int tile) => _doors.ContainsKey(tile);

    /// <summary>
    /// 등록된 모든 문 타일. 문 하나가 하단·상단 두 칸으로 들어 있습니다.
    /// RoomManager가 문이 잇는 두 방을 찾을 때 사용합니다.
    /// </summary>
    public IReadOnlyDictionary<Vector2Int, Door> AllDoors => _doors;
}
