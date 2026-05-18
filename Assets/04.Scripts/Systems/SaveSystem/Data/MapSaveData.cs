using System;
using System.Collections.Generic;

/// <summary>
/// 맵 저장 데이터.
/// 타일 그리드와 맵 엔티티(나무, 광물 등)를 저장합니다.
/// 2D 배열은 1D로 평탄화하여 직렬화합니다 (인덱스 = y * width + x).
/// </summary>
[Serializable]
public class MapSaveData
{
    /// <summary>맵 너비</summary>
    public int width;

    /// <summary>맵 높이</summary>
    public int height;

    /// <summary>타일 그리드 (1D 평탄화, 인덱스 = y * width + x)</summary>
    public int[] tileGrid;

    /// <summary>벽 그리드 (1D 평탄화, 인덱스 = y * width + x)</summary>
    public int[] wallGrid;

    /// <summary>맵 엔티티 목록 (나무, 광물 등 자연물)</summary>
    public List<MapEntitySaveData> entities;

    public MapSaveData()
    {
        entities = new List<MapEntitySaveData>();
    }
}

/// <summary>
/// 맵 엔티티 저장 데이터 (나무, 광물 등 자연물).
/// </summary>
[Serializable]
public class MapEntitySaveData
{
    /// <summary>타일 X 좌표</summary>
    public int x;

    /// <summary>타일 Y 좌표</summary>
    public int y;

    /// <summary>엔티티 타입 (TypeObjectTile enum 값)</summary>
    public int entityType;

    /// <summary>변형 ID (나무 종류 등)</summary>
    public int variantId;

    /// <summary>남은 자원량</summary>
    public float remainingResource;
}
