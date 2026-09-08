/// <summary>
/// 소비 전력이 <b>상황에 따라 달라지는</b> 건물 컴포넌트.
///
/// <see cref="PowerConsumer"/>가 같은 GameObject에서 이 인터페이스를 찾으면
/// BuildingData.powerConsumption 대신 매 틱 <see cref="GetPowerDraw"/>를 물어봅니다.
///
/// 냉난방기가 대표적입니다 — 목표 온도와 방의 자연 평형 온도가 멀수록 더 많이 먹습니다.
/// 그래서 단열을 잘 하거나 지열이 약한 얕은 곳에 지으면 같은 건물이 더 적게 먹습니다.
/// </summary>
public interface IVariablePowerDraw
{
    /// <summary>지금 이 순간의 소비 전력(W). 0 이상이어야 합니다.</summary>
    int GetPowerDraw();
}
