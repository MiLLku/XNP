/// <summary>
/// 저장/로드 모듈 인터페이스.
/// 각 매니저(EmployeeManager, WorkSystemManager 등)가 구현하여
/// SaveManager에 자동으로 등록됩니다.
///
/// 사용법:
///   1. MonoBehaviour 매니저에 ISaveModule 구현
///   2. SaveOrder로 로드 순서 지정 (낮을수록 먼저)
///   3. Capture()에서 자기 데이터를 SaveData에 기록
///   4. Restore()에서 SaveData로부터 복원
///   5. PostRestore()에서 크로스레퍼런스 연결 (선택)
///
/// SaveManager가 FindObjectsOfType으로 자동 발견하므로
/// 별도 등록 코드가 필요 없습니다.
/// </summary>
public interface ISaveModule
{
    /// <summary>
    /// 로드 시 복원 순서 (낮을수록 먼저 복원).
    /// Map=10, Inventory=20, Building=30, Construction=40,
    /// Employee=50, WorkOrder=60, DroppedItem=70, Event=80
    /// </summary>
    int SaveOrder { get; }

    /// <summary>
    /// 현재 상태를 SaveData에 기록합니다 (저장 시 호출).
    /// </summary>
    /// <param name="data">기록할 SaveData 컨테이너</param>
    void Capture(SaveData data);

    /// <summary>
    /// SaveData로부터 상태를 복원합니다 (로드 시 호출).
    /// 이 단계에서는 자기 엔티티만 생성하고, 다른 모듈 참조는 하지 않습니다.
    /// </summary>
    /// <param name="data">복원할 SaveData 컨테이너</param>
    void Restore(SaveData data);

    /// <summary>
    /// 모든 모듈의 Restore()가 완료된 후 호출됩니다.
    /// RuntimeIDRegistry를 사용하여 크로스레퍼런스를 연결합니다.
    /// 기본 구현은 빈 메서드이므로, 필요한 모듈만 오버라이드합니다.
    /// </summary>
    /// <param name="data">SaveData 컨테이너</param>
    void PostRestore(SaveData data);
}
