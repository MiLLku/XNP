using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스탬프 데이터.
/// 맵에 배치할 타일/개체 패턴을 정의합니다 (예: 광맥, 던전 등).
/// </summary>
[Serializable]
public class StampData
{
    /// <summary>스탬프를 찾는 고유 키 (예: "Iron_Vein_Large", "BossDungeon_1")</summary>
    public string key;

    /// <summary>스탬프의 전체 크기 (정보용)</summary>
    public Vector2Int size;

    /// <summary>배치 시 기준점 ((0,0)=왼쪽 아래, (size.x/2, size.y/2)=중앙)</summary>
    public Vector2Int pivot;

    /// <summary>지형 타일 요소 목록</summary>
    public List<StampElement> elements;

    /// <summary>벽 타일 요소 목록</summary>
    public List<StampElement> wallElements;

    public StampData()
    {
        elements = new List<StampElement>();
        wallElements = new List<StampElement>();
    }
}
