/// <summary>
/// 저장 가능한 객체 인터페이스
/// </summary>
public interface ISaveable
{
    /// <summary>
    /// 현재 상태를 저장 데이터로 캡처합니다.
    /// </summary>
    object CaptureState();

    /// <summary>
    /// 저장 데이터로부터 상태를 복원합니다.
    /// </summary>
    void RestoreState(object state);
}

/// <summary>
/// 런타임 ID를 가지는 객체 인터페이스
/// </summary>
public interface IIdentifiable
{
    int InstanceId { get; set; }
}
