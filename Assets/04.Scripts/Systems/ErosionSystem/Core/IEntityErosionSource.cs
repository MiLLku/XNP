using UnityEngine;

/// <summary>
/// <b>개체 발원지</b> — 움직이는 것이 자기 주변에 만드는 침식.
/// 오염 구체, 제놉스 침식 오라, 침식 폭주 중인 직원 등이 여기 속합니다.
///
/// <b>작동 규칙 (지형 발원지와 다릅니다)</b>
///   · 범위에 <b>들어온 순간</b> 고정 침식량이 붙는다.
///   · 범위를 <b>벗어나면 그 침식은 돌아간다</b> (다른 출처의 침식은 그대로).
///   · 방 침식(농도)에는 <b>기여하지 않는다</b> — 개체가 사라지면 흔적이 남지 않는다.
///
/// 그래서 개체 발원지는 "지나가면 안 되는 구역"이지 "오염된 공간"이 아닙니다.
/// 공간을 실제로 오염시키는 것은 <see cref="ITerrainErosionSource"/>(지형 발원지)뿐입니다.
/// </summary>
public interface IEntityErosionSource
{
    /// <summary>월드 좌표 기준 중심 위치</summary>
    Vector2 EmitPosition { get; }

    /// <summary>영향 반경 (타일)</summary>
    float EmitRadius { get; }

    /// <summary>
    /// 범위 안에 있는 동안 붙는 <b>고정 침식량</b>.
    /// 시간이 지나도 늘지 않고, 범위를 벗어나면 이만큼 돌아갑니다.
    /// </summary>
    float FixedErosionAmount { get; }

    /// <summary>지금 활성 상태인지</summary>
    bool IsEmitting { get; }

    /// <summary>침식 내역 키 (개체별로 구분되어야 합니다 — 반환 처리의 기준이 됩니다)</summary>
    string ErosionSourceKey { get; }

    /// <summary>침식 내역 표시 이름</summary>
    string ErosionSourceName { get; }

    /// <summary>
    /// 가로 거리만으로 판정할지 여부.
    /// 오염 구체처럼 세로로는 제한이 없는 개체가 true를 씁니다.
    /// </summary>
    bool HorizontalOnly { get; }

    /// <summary>주어진 위치가 이 발원지의 범위 안인지</summary>
    bool Covers(Vector2 worldPosition);
}
