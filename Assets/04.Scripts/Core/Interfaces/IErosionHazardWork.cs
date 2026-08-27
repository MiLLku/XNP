/// <summary>
/// 손대는 것만으로 작업자가 침식을 뒤집어쓰는 위험 작업.
///
/// 침식 발원지를 캐내거나 오염된 공간을 세척하는 것처럼,
/// <b>해결하는 대가로 사람이 오염되는</b> 작업이 구현합니다.
/// 작업이 완료되는 시점에 작업자에게 <see cref="WorkerErosionCost"/>만큼 침식이 붙습니다.
///
/// 이 대가가 있어야 "위험을 없애는 일" 자체가 자원 소모가 됩니다 —
/// 아무나 시키면 그 직원이 망가지므로, 침식 저항이 높은 직원이나 보호 장비가 의미를 갖습니다.
/// </summary>
public interface IErosionHazardWork
{
    /// <summary>작업 완료 시 작업자가 받는 침식량</summary>
    float WorkerErosionCost { get; }

    /// <summary>침식 내역에 남길 이름 (예: "침식 발원지 제거")</summary>
    string HazardDisplayName { get; }
}
