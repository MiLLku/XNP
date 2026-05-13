/// <summary>
/// 제노프스 타입별 행동 인터페이스.
/// IBuildingFunction 패턴을 따릅니다.
///
/// 새 타입 추가 시:
///   1. XenopsType enum에 값 추가
///   2. IXenopsBehavior를 구현하는 MonoBehaviour 작성
///   3. 프리팹에 해당 Behavior 컴포넌트 부착
///   → Xenops.cs가 GetComponent&lt;IXenopsBehavior&gt;()로 자동 감지
/// </summary>
public interface IXenopsBehavior
{
    /// <summary>이 Behavior가 담당하는 제노프스 타입</summary>
    XenopsType BehaviorType { get; }

    /// <summary>현재 활성 상태 여부</summary>
    bool IsActive { get; }

    /// <summary>제노프스가 활성화될 때 호출됩니다.</summary>
    void OnActivated();

    /// <summary>제노프스가 비활성화될 때 호출됩니다.</summary>
    void OnDeactivated();

    /// <summary>매 프레임 행동 업데이트. Xenops 코디네이터의 Update에서 호출됩니다.</summary>
    void UpdateBehavior();
}
