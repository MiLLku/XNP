/// <summary>
/// 진행도가 <b>대상에 누적</b>되는 작업.
///
/// 기본 작업(IWorkTarget)은 "정해진 시간 동안 붙어 있으면 완료" 방식이라, 직원이 중간에
/// 밥을 먹으러 가거나 소집되면 진행도가 사라지고 처음부터 다시 한다.
/// 이 인터페이스를 구현하면 진행도가 <b>작업 대상 쪽에 쌓이므로</b>:
///   • 직원이 떠나도 지금까지 한 작업이 남고
///   • 다른 직원이 이어받을 수 있으며
///   • 세이브에도 실제 진행 상태가 보존된다
///
/// 초당 투입되는 작업량은 직원의 <see cref="EmployeeWork.GetWorkSpeed"/> 결과다 —
/// 능력치·특성·피로·정신 이상·침식·연구 보정이 모두 곱해진 값이므로,
/// 유능한 직원일수록 같은 시간에 더 많은 작업량을 넣는다.
///
/// 적용 대상: 건설(ConstructionSite) · 철거(DemolishOrder)
/// 미적용: 채광(타일 경도로 판정) · 벌목/수확(중단 시 리셋) · 제작(CraftingOrder 자체 진행도 보유)
/// </summary>
public interface IProgressiveWork
{
    /// <summary>완료에 필요한 총 작업량.</summary>
    float GetWorkAmount();

    /// <summary>지금까지 누적된 작업량.</summary>
    float GetAccumulatedWork();

    /// <summary>작업량을 투입합니다. (직원 초당 작업량 × deltaTime)</summary>
    void AddWork(float amount);

    /// <summary>
    /// 누적된 작업량을 되돌립니다. 0 미만으로는 내려가지 않습니다.
    /// 침식 이상 행동 '작업 중단'처럼 이미 해놓은 작업을 망가뜨리는 경우에 쓰입니다.
    /// </summary>
    void ReduceWork(float amount);
}
