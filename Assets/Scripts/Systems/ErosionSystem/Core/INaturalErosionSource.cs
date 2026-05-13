using UnityEngine;

/// <summary>
/// 자연 침식 발원지 인터페이스.
///
/// 구현 클래스:
///   - NaturalErosionEmitter (범용 컴포넌트, 독성 식물/방사성 광물/특수 타일 등에 부착)
///
/// NaturalErosionManager가 이 인터페이스를 통해 BFS 침식 전파를 계산합니다.
/// 직원이 영향 범위 내 타일을 밟으면 워터마크 기반으로 침식이 적용됩니다.
/// </summary>
public interface INaturalErosionSource
{
    /// <summary>발원지의 타일 좌표 (FloorToInt 기준)</summary>
    Vector2Int TilePosition { get; }

    /// <summary>발원지에서 방출하는 최대 침식 수치 (거리 0 기준)</summary>
    float MaxIntensity { get; }

    /// <summary>
    /// 타일 1칸당 침식 수치 감소량.
    /// 유효 반경 = MaxIntensity / DecayPerTile.
    /// 0 이하이면 NaturalErosionManager에서 무시됩니다.
    /// </summary>
    float DecayPerTile { get; }

    /// <summary>현재 활성 상태 여부. false이면 NaturalErosionManager에서 무시됩니다.</summary>
    bool IsActive { get; }

    /// <summary>
    /// 장벽(벽/문) 무시 여부.
    /// true  → 원형 거리 기반 전파 (광물형 — 벽/문을 관통)
    /// false → BFS 경로 기반 전파 (식물형 — 벽/문에서 차단)
    /// </summary>
    bool IgnoresBarriers { get; }
}
