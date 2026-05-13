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
    /// 이상 행동 지속 시간이 끝났을 때 호출됩니다.
    /// </summary>
    /// <param name="employee">대상 직원</param>
    void OnEnd(Employee employee);
}
