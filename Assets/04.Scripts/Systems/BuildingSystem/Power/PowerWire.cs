using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전선. 건축물(바닥·벽·일반 건물)과 같은 타일에 겹쳐 설치되어 전력망을 형성합니다.
///
/// 점유 그리드(GameMap.OccupiedGrid)를 사용하지 않고, FloorTile과 동일한
/// 정적 위치 레지스트리(Dictionary&lt;Vector2Int, PowerWire&gt;)로 위치를 관리합니다.
/// 따라서 기존 건축물 위에 자유롭게 겹쳐 놓을 수 있습니다.
/// (BuildingData.allowOverlap = true 인 프리팹에 부착)
///
/// 전력망 소속/연결 계산은 PowerManager가 위치 기반(4방향 인접)으로 수행합니다.
/// </summary>
[RequireComponent(typeof(Building))]
public class PowerWire : MonoBehaviour, IPowerNode
{
    #region 필드

    private Vector2Int gridPosition;

    /// <summary>위치별 전선 정적 레지스트리</summary>
    private static readonly Dictionary<Vector2Int, PowerWire> wireRegistry = new Dictionary<Vector2Int, PowerWire>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearStaticRegistry()
    {
        wireRegistry.Clear();
    }

    #endregion

    #region 프로퍼티

    /// <summary>설치된 그리드 좌표</summary>
    public Vector2Int GridPosition => gridPosition;

    // ── IPowerNode ──────────────────────────────────────────────
    /// <summary>전선은 자신의 단일 셀을 점유합니다.</summary>
    public IEnumerable<Vector2Int> OccupiedCells { get { yield return gridPosition; } }

    public PowerNodeKind Kind => PowerNodeKind.Wire;

    #endregion

    #region 생명주기

    void Start()
    {
        gridPosition = new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y)
        );

        if (!wireRegistry.ContainsKey(gridPosition))
            wireRegistry[gridPosition] = this;

        // 전선 추가 → 전력망 위상 변화 → 재계산 요청
        var pm = PowerManager.instance;
        if (pm != null) pm.MarkDirty();
    }

    void OnDestroy()
    {
        if (wireRegistry.TryGetValue(gridPosition, out var existing) && existing == this)
            wireRegistry.Remove(gridPosition);

        // 전선 제거 → 전력망 위상 변화 → 재계산 요청
        var pm = PowerManager.instance;
        if (pm != null) pm.MarkDirty();
    }

    #endregion

    #region 정적 레지스트리 조회

    /// <summary>특정 위치의 전선을 조회합니다 (없으면 null).</summary>
    public static PowerWire GetWireAt(Vector2Int position)
    {
        wireRegistry.TryGetValue(position, out PowerWire wire);
        return wire;
    }

    /// <summary>특정 위치에 전선이 있는지 확인합니다.</summary>
    public static bool HasWireAt(Vector2Int position)
    {
        return wireRegistry.ContainsKey(position);
    }

    /// <summary>현재 설치된 모든 전선.</summary>
    public static IEnumerable<PowerWire> All => wireRegistry.Values;

    #endregion
}
