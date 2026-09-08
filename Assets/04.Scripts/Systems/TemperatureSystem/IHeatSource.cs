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
    /// 목표 온도에서 멀 때의 평형은 <c>주변온도 + 출력 / 방의 누출계수</c>가 되므로,
    /// 같은 출력이라도 벽이 잘 막힌 방일수록 더 뜨거워집니다.
    /// </summary>
    float HeatOutput { get; }

    /// <summary>지금 열을 내고 있는지 (고장·정지 시 false)</summary>
    bool IsHeatActive { get; }

    /// <summary>목표 온도를 쓰는지. false면 상한 없이 계속 밉니다(예전 동작).</summary>
    bool HasHeatTarget { get; }

    /// <summary>
    /// 이 열원이 방을 데우거나 식힐 수 있는 <b>한계 온도(℃)</b>.
    /// 난방(출력 양수)은 이 온도 <b>이상</b>에서 멈추고, 냉방(출력 음수)은 이 온도 <b>이하</b>에서 멈춥니다.
    /// 목표에 가까워질수록 출력이 줄어들기 때문에 켜짐/꺼짐 진동 없이 부드럽게 붙습니다.
    /// </summary>
    float HeatTargetTemperature { get; }

    /// <summary>
    /// 플레이어가 목표를 지정하는 <b>능동 공조기</b>인지.
    /// true면 방의 <see cref="Room.NaturalEquilibrium"/>(공조기를 뺀 평형) 계산에서 자기 자신이 빠집니다 —
    /// 그 차이가 곧 공조기의 부하이자 전력 소모입니다.
    /// </summary>
    bool IsClimateControl { get; }
}
