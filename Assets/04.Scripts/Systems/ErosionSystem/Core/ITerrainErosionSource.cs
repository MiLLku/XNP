using UnityEngine;

/// <summary>
/// 고정 침식 발원지 — 자기가 속한 <b>방의 침식 수치</b>를 올립니다.
///
/// 구현 클래스:
///   - TerrainErosionEmitter (범용 컴포넌트, 침식 식물·오염 광맥 등에 부착)
///
/// <b>타일 단위로 퍼지지 않습니다.</b> 방 단위로 고이며, 실외에서는 아무 일도 일어나지 않습니다
/// (바깥은 부피가 무한해 희석됩니다). 그래서 밀폐된 동굴만 위험해집니다.
///
/// 움직이는 개체(제놉스 오라 등)의 침식 발산은 이 인터페이스가 아니라
/// 타일 단위 동적 레이어가 담당합니다 — 성격이 다릅니다.
/// </summary>
public interface ITerrainErosionSource
{
    /// <summary>발원지의 타일 좌표 (FloorToInt 기준)</summary>
    Vector2Int TilePosition { get; }

    /// <summary>이 발원지가 방에 넣는 초당 침식량</summary>
    float ErosionPerSecond { get; }

    /// <summary>
    /// 포화 수치 — 방 침식이 이 값 이상이면 <b>활동을 멈춥니다</b>.
    ///
    /// 일반 발원지는 여기서 평형을 이루므로 오래 방치해도 방이 일정 수준에서 안정됩니다.
    /// <b>0 이하면 한계가 없습니다</b> — 시한폭탄형 특수 발원지가 그 값을 씁니다.
    /// </summary>
    float SaturationLevel { get; }

    /// <summary>현재 활성 상태 여부. false이면 무시됩니다.</summary>
    bool IsActive { get; }

    /// <summary>UI·로그 표기용 이름</summary>
    string SourceDisplayName { get; }
}
