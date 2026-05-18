using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 하나의 구역을 나타내는 데이터 클래스.
/// 플레이어가 타일을 "칠하기" 방식으로 구역을 생성합니다.
///
/// 구역은 타일 좌표 집합으로 정의되며,
/// 직원에게 할당하여 스케줄 활동의 목적지로 사용됩니다.
/// </summary>
[System.Serializable]
public class Zone
{
    #region 필드

    /// <summary>구역 고유 ID</summary>
    public int zoneId;

    /// <summary>구역 이름 (플레이어가 지정, UI 표시용)</summary>
    public string zoneName;

    /// <summary>구역 용도</summary>
    public ZoneType zoneType;

    /// <summary>구역 오버레이 색상 (맵에 표시)</summary>
    public Color displayColor = Color.white;

    /// <summary>이 구역에 포함된 타일 좌표 집합</summary>
    [NonSerialized] public HashSet<Vector2Int> tiles = new HashSet<Vector2Int>();

    /// <summary>직렬화용 타일 목록 (저장/로드)</summary>
    public List<Vector2Int> tileList = new List<Vector2Int>();

    #endregion

    #region AABB 캐시

    /// <summary>구역의 바운딩 박스 (빠른 범위 판정용)</summary>
    [NonSerialized] public Vector2Int boundsMin;
    [NonSerialized] public Vector2Int boundsMax;

    /// <summary>바운딩 박스를 타일 목록으로부터 재계산합니다.</summary>
    public void RecalculateBounds()
    {
        if (tiles.Count == 0)
        {
            boundsMin = Vector2Int.zero;
            boundsMax = Vector2Int.zero;
            return;
        }

        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;

        foreach (var t in tiles)
        {
            if (t.x < minX) minX = t.x;
            if (t.x > maxX) maxX = t.x;
            if (t.y < minY) minY = t.y;
            if (t.y > maxY) maxY = t.y;
        }

        boundsMin = new Vector2Int(minX, minY);
        boundsMax = new Vector2Int(maxX, maxY);
    }

    #endregion

    #region 조회

    /// <summary>타일이 이 구역에 포함되는지 확인합니다.</summary>
    public bool ContainsTile(Vector2Int tile) => tiles.Contains(tile);

    /// <summary>구역의 타일 수를 반환합니다.</summary>
    public int TileCount => tiles.Count;

    /// <summary>구역의 중심 타일 좌표를 반환합니다 (대략적).</summary>
    public Vector2 Center
    {
        get
        {
            if (tiles.Count == 0) return Vector2.zero;
            return new Vector2(
                (boundsMin.x + boundsMax.x) / 2f,
                (boundsMin.y + boundsMax.y) / 2f
            );
        }
    }

    #endregion

    #region 타일 관리

    /// <summary>타일을 구역에 추가합니다.</summary>
    public void AddTile(Vector2Int tile)
    {
        tiles.Add(tile);
    }

    /// <summary>타일을 구역에서 제거합니다.</summary>
    public void RemoveTile(Vector2Int tile)
    {
        tiles.Remove(tile);
    }

    #endregion

    #region 저장/로드

    /// <summary>HashSet → List 동기화 (저장 전 호출)</summary>
    public void PrepareForSave()
    {
        tileList = new List<Vector2Int>(tiles);
    }

    /// <summary>List → HashSet 동기화 (로드 후 호출)</summary>
    public void RestoreFromLoad()
    {
        tiles = new HashSet<Vector2Int>(tileList);
        RecalculateBounds();
    }

    #endregion
}
