/// <summary>
/// 침식 이상 행동 플러그인 인터페이스.
/// 새 이상 행동을 추가하려면 이 인터페이스를 구현하고
/// AbnormalBehaviorRegistry.Register()로 등록하세요.
/// </summary>
public interface IAbnormalBehavior
{
    /// <summary>이 구현체가 처리하는 이상 행동 타입</summary>
    AbnormalBehaviorType BehaviorType { get; }

    /// <summary>
    /// 이 행동을 지금 실행할 수 있는지 확인합니다.
    /// </summary>
    /// <param name="employee">대상 직원</param>
    /// <returns>실행 가능하면 true</returns>
    bool CanExecute(Employee employee);

    /// <summary>
    /// 이상 행동을 실행합니다.
    /// </summary>
    /// <param name="employee">대상 직원</param>
    /// <returns>이상 행동 지속 시간 (초). 0이면 즉시 완료.</returns>
    float Execute(Employee employee);

    /// <summary>
    /// 지속 시간 동안 매 프레임 호출됩니다 (EmployeeMental이 구동).
    /// 제자리 침식 방출·건물 공격처럼 시간에 걸쳐 동작하는 행동이 사용합니다.
    /// 한 번에 끝나는 행동은 구현하지 않아도 됩니다(기본 구현은 아무것도 하지 않음).
    /// </summary>
    /// <remarks>
    /// 구현체는 레지스트리에 <b>단일 인스턴스</b>로 등록되어 모든 직원이 공유하므로,
    /// 직원별 상태는 반드시 직원을 키로 하는 컬렉션에 보관하고 OnEnd에서 정리해야 합니다.
    /// 또한 세이브 로드 직후에는 Execute를 거치지 않고 Tick부터 재개되므로,
    /// 상태가 없으면 그 자리에서 만들어 쓰도록 작성해야 합니다.
    /// </remarks>
    /// <param name="employee">대상 직원</param>
    /// <param name="deltaTime">경과 시간 (초)</param>
    void Tick(Employee employee, float deltaTime);

    /// <summary>
    /// 이상 행동 지속 시간이 끝났을 때 호출됩니다.
    /// </summary>
    /// <param name="employee">대상 직원</param>
    void OnEnd(Employee employee);
}
