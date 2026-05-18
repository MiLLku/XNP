using System;
using UnityEngine;

/// <summary>
/// 스탬프의 개별 요소.
/// 스탬프 내 하나의 타일/개체 배치 정보를 나타냅니다.
/// </summary>
[Serializable]
public struct StampElement
{
    /// <summary>스탬프 원점(0,0) 기준 상대 위치</summary>
    public Vector2Int position;

    /// <summary>요소의 타입 (타일, 건물, 적 등)</summary>
    public TypeObjectTile type;

    /// <summary>배치될 리소스의 고유 ID (타일 ID, 건물 ID 등)</summary>
    public int id;
}
