/// <summary>
/// 재미(오락) 공급원 인터페이스. 오락 건물(RecreationFacility) 등이 구현합니다.
///
/// 직원 AI(EmployeeAI.ExecuteRecreation)가 오락거리를 선택할 때:
///   1. Priority가 높은 순 (디자이너가 인스펙터/에셋에서 조정)
///   2. 동률이면 거리가 가까운 순
///
/// 약물(소모품)은 건물이 아니라 인벤토리 재고라서 이 인터페이스를 구현하지 않고,
/// FunConfig.drugPriority로 같은 우선순위 축에서 비교됩니다.
/// </summary>
public interface IFunSource
{
    /// <summary>선택 우선순위 (높을수록 선호)</summary>
    int Priority { get; }

    /// <summary>이 직원이 지금 사용할 수 있는지 (정전·파괴 등 확인)</summary>
    bool CanUse(Employee employee);

    /// <summary>초당 재미 회복량 (예상 이득 비교용)</summary>
    float FunPerSecond { get; }
}
