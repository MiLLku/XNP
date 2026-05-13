using UnityEngine;

/// <summary>
/// 작업 대상 인터페이스
/// 채광할 타일, 벌목할 나무, 철거할 건물 등 직원이 작업할 수 있는 모든 대상이 구현해야 합니다.
/// </summary>
public interface IWorkTarget
{
    /// <summary>
    /// 작업을 수행할 위치를 반환합니다 (직원이 이동할 목표 지점).
    /// </summary>
    Vector3 GetWorkPosition();
    
    /// <summary>
    /// 작업 타입을 반환합니다 (Mining, Chopping, Building 등).
    /// </summary>
    WorkType GetWorkType();
    
    /// <summary>
    /// 작업 완료에 필요한 시간을 반환합니다 (초).
    /// </summary>
    float GetWorkTime();
    
    /// <summary>
    /// 현재 작업이 가능한 상태인지 확인합니다.
    /// </summary>
    bool IsWorkAvailable();
    
    /// <summary>
    /// 작업이 완료되었을 때 호출됩니다.
    /// </summary>
    /// <param name="worker">작업을 완료한 직원</param>
    void CompleteWork(Employee worker);
    
    /// <summary>
    /// 작업이 취소되었을 때 호출됩니다.
    /// </summary>
    /// <param name="worker">작업을 취소한 직원</param>
    void CancelWork(Employee worker);
}