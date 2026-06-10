using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전력망 노드의 종류.
/// </summary>
public enum PowerNodeKind
{
    /// <summary>발전기 (전력 생산)</summary>
    Producer,
    /// <summary>축전기 (전력 저장/방전)</summary>
    Battery,
    /// <summary>전력 소비 건물</summary>
    Consumer,
    /// <summary>전선 (전력 전송)</summary>
    Wire,
}

/// <summary>
/// 전력망(PowerNetwork)에 참여하는 노드의 공통 인터페이스.
/// 발전기·축전기·소비 건물·전선이 구현합니다.
///
/// 노드의 전력망 소속은 PowerManager가 위치 기반으로 재계산하여 관리하므로
/// 이 인터페이스는 인접 판정에 필요한 점유 셀과 종류만 노출합니다.
/// (전선은 GameMap의 점유 그리드를 쓰지 않고 자체 레지스트리로 위치를 관리합니다 — PowerWire 참고.)
/// </summary>
public interface IPowerNode
{
    /// <summary>이 노드가 점유하는 그리드 셀들. 전선은 단일 셀, 건물은 풋프린트 전체.</summary>
    IEnumerable<Vector2Int> OccupiedCells { get; }

    /// <summary>노드 종류.</summary>
    PowerNodeKind Kind { get; }
}
