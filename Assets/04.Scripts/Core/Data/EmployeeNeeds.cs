using UnityEngine;

/// <summary>
/// 직원 욕구 데이터 구조체.
/// 배고픔, 피로 등 직원의 생리적 욕구를 저장합니다.
/// 값이 0에 가까울수록 위험한 상태입니다.
/// </summary>
[System.Serializable]
public struct EmployeeNeeds
{
    /// <summary>배고픔 (0~100, 0이면 굶주림)</summary>
    [Range(0, 100)]
    public float hunger;

    /// <summary>피로 (0~100, 0이면 탈진)</summary>
    [Range(0, 100)]
    public float fatigue;
}
