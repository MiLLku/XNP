using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방 하나의 환경 상태.
///
/// <b>방 번호는 저장하지 않습니다.</b> 방 번호는 재계산할 때마다 새로 부여되므로
/// 실행이 달라지면 의미가 없습니다. 대신 <see cref="representative"/>(방에서 가장 왼쪽 아래 칸)로
/// 복원 시 같은 방을 다시 찾습니다 — 지형이 같으면 같은 칸이 뽑힙니다.
/// </summary>
[Serializable]
public class RoomStateSaveData
{
    /// <summary>방을 다시 찾기 위한 대표 좌표</summary>
    public Vector2Int representative;

    /// <summary>방 온도(℃)</summary>
    public float temperature;

    /// <summary>방 침식 수치</summary>
    public float erosion;
}

/// <summary>
/// 방 시스템 저장 데이터.
/// 방 구조 자체(칸 목록·경계)는 지형에서 다시 계산되는 파생 값이라 저장하지 않고,
/// <b>다시 만들 수 없는 상태(온도·침식)만</b> 담습니다.
/// </summary>
[Serializable]
public class RoomSystemSaveData
{
    public List<RoomStateSaveData> rooms = new List<RoomStateSaveData>();
}
