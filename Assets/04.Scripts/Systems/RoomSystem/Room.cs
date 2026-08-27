using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 밀폐된 공간 하나.
///
/// 실외는 방이 아닙니다 — 하늘이나 맵 가장자리에 닿는 공간은 <see cref="RoomManager.OUTDOOR_ID"/>로
/// 처리되며 Room 객체를 갖지 않습니다.
///
/// 환경 수치(온도·침식)는 <b>방마다 값 하나</b>입니다. 타일별로 나누지 않습니다.
/// 방이 합쳐지거나 갈라질 때는 칸 수 가중 평균으로 이어받으므로,
/// 벽을 트면 희석되고 벽을 세우면 양쪽이 같은 값에서 출발합니다.
/// </summary>
public class Room
{
    /// <summary>방 고유 번호. 재계산할 때마다 새로 부여되므로 <b>세이브에 저장하면 안 됩니다.</b></summary>
    public int Id { get; private set; }

    /// <summary>이 방에 속한 칸 목록</summary>
    public readonly List<Vector2Int> Cells = new List<Vector2Int>();

    /// <summary>방의 부피 (칸 수). 가중 평균과 열용량 계산에 쓰입니다.</summary>
    public int CellCount => Cells.Count;

    /// <summary>
    /// 세이브 복원 시 방을 다시 찾기 위한 대표 좌표.
    /// 방에서 가장 왼쪽 아래 칸이라 같은 지형이면 같은 칸이 뽑힙니다.
    /// </summary>
    public Vector2Int Representative { get; private set; }

    /// <summary>
    /// 방과 맞닿은 고체 칸 목록 — <b>접촉면 단위</b>입니다.
    /// 같은 벽 칸이 여러 번 들어올 수 있고, 그것이 맞습니다(맞닿은 면이 넓을수록 열이 많이 샙니다).
    /// </summary>
    public readonly List<Vector2Int> BoundaryFaces = new List<Vector2Int>();

    /// <summary>
    /// 경계를 통해 열이 새는 정도. 접촉면들의 전도율 합이며 방이 재계산될 때 갱신됩니다.
    /// 0이면 완전 밀폐 — 열원이 있으면 온도가 끝없이 오릅니다.
    /// </summary>
    public float LeakConductance;

    /// <summary>
    /// 경계 타일이 스스로 내는 열의 합 (초당). 방이 재계산될 때 갱신됩니다.
    /// 뜨거운 광맥에 맞닿을수록 커집니다.
    /// </summary>
    public float EnvironmentHeat;

    /// <summary>방 온도 (Phase 2)</summary>
    public float Temperature;

    /// <summary>온도가 한 번이라도 정해졌는지. false면 TemperatureManager가 주변 온도로 채웁니다.</summary>
    public bool TemperatureInitialized;

    /// <summary>방 침식 수치 (Phase 3). 고정 발원지만 여기에 기여하고, 개체 오라는 타일에만 기여합니다.</summary>
    public float Erosion;

    /// <summary>문으로 이어진 이웃 방 번호. 실외와 이어져 있으면 <see cref="RoomManager.OUTDOOR_ID"/>가 들어갑니다.</summary>
    public readonly HashSet<int> DoorLinks = new HashSet<int>();

    public Room(int id)
    {
        Id = id;
    }

    /// <summary>칸을 추가하고 대표 좌표를 갱신합니다.</summary>
    public void AddCell(Vector2Int cell)
    {
        if (Cells.Count == 0) Representative = cell;
        else if (cell.y < Representative.y || (cell.y == Representative.y && cell.x < Representative.x))
            Representative = cell;

        Cells.Add(cell);
    }

    public override string ToString()
        => $"Room#{Id} ({CellCount}칸, 대표={Representative}, 온도={Temperature:F1}, 침식={Erosion:F1})";
}
