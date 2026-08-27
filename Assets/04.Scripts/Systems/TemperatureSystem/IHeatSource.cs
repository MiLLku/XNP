using UnityEngine;

/// <summary>
/// 열을 내는 것. <see cref="TemperatureManager"/>에 등록되면 자기가 서 있는 방을 데웁니다.
///
/// 실외에 있는 열원은 무시됩니다 — 바깥은 부피가 무한해서 데워지지 않습니다.
/// 이 규칙 때문에 "화로를 쓰려면 먼저 공간을 막아야 한다"가 성립합니다.
///
/// 구현: <see cref="Building"/>이 BuildingData.heatOutput이 0이 아니면 스스로 등록합니다.
/// 따로 컴포넌트를 붙일 필요 없이 에셋에 값만 넣으면 됩니다.
/// </summary>
public interface IHeatSource
{
    /// <summary>열원이 놓인 타일 좌표 (좌하단 기준)</summary>
    Vector2Int HeatTilePosition { get; }

    /// <summary>
    /// 열원이 차지하는 칸 수. 2×2 건물은 (2,2).
    /// 다중 타일 건물은 자기 풋프린트가 주변을 다 가려서, 이 크기를 알아야 바깥 띠를 볼 수 있습니다.
    /// </summary>
    Vector2Int HeatFootprint { get; }

    /// <summary>
    /// 초당 열 출력. 양수면 난방, 음수면 냉방입니다.
    /// 평형 온도는 <c>주변온도 + 출력 / 방의 누출계수</c>가 되므로,
    /// 같은 출력이라도 벽이 잘 막힌 방일수록 더 뜨거워집니다.
    /// </summary>
    float HeatOutput { get; }

    /// <summary>지금 열을 내고 있는지 (고장·정지 시 false)</summary>
    bool IsHeatActive { get; }
}
