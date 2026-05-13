using UnityEngine;

/// <summary>
/// 직원의 작업 우선순위 데이터.
/// 작업 타입별 우선순위와 활성화 여부를 저장합니다.
/// </summary>
[System.Serializable]
public class WorkPriority
{
    /// <summary>작업 타입</summary>
    public WorkType workType;

    /// <summary>우선순위 (낮을수록 먼저 수행)</summary>
    public int priority;

    /// <summary>이 작업의 활성화 여부</summary>
    public bool enabled;
}
