using System;

/// <summary>
/// 정신 이벤트 설정 데이터.
/// 이벤트 발생 조건과 효과를 정의합니다.
/// </summary>
[Serializable]
public class MentalEventConfig
{
    /// <summary>이벤트 타입</summary>
    public MentalEventType eventType;

    /// <summary>이 비율 이하일 때 발생 가능 (0~1)</summary>
    public float minMentalRatio;

    /// <summary>발생 확률 (0~1)</summary>
    public float probability;

    /// <summary>지속 시간 (초)</summary>
    public float duration;

    /// <summary>재발생 대기 시간 (초)</summary>
    public float cooldown;

    /// <summary>효과 수치 (속도 감소율, 정신력 영향량 등)</summary>
    public float effectValue;
}

/// <summary>
/// 활성 정신 이벤트 데이터 (런타임).
///
/// 정신 이상은 두 계열이 있고 한 항목은 둘 중 하나만 채웁니다:
///   • 일반 계열 — type에 값이 있고 abnormalType은 None
///   • 침식 계열 — abnormalType에 값이 있고 type은 None (AbnormalBehaviorRegistry 구현체가 실행)
/// </summary>
[Serializable]
public class ActiveMentalEvent
{
    /// <summary>일반 계열 이벤트 타입 (침식 계열이면 None)</summary>
    public MentalEventType type;

    /// <summary>침식 계열 이상 행동 타입 (일반 계열이면 None)</summary>
    public AbnormalBehaviorType abnormalType;

    /// <summary>남은 지속 시간</summary>
    public float remainingTime;

    /// <summary>쿨다운 남은 시간</summary>
    public float cooldownRemaining;

    /// <summary>침식 계열 정신 이상인지 여부</summary>
    public bool IsErosionKind => abnormalType != AbnormalBehaviorType.None;
}

/// <summary>
/// 정신 이벤트 저장 데이터.
/// </summary>
[Serializable]
public class MentalEventSaveData
{
    /// <summary>일반 계열 이벤트 타입 (MentalEventType as int)</summary>
    public int eventType;

    /// <summary>침식 계열 이상 행동 타입 (AbnormalBehaviorType as int, v8 추가)</summary>
    public int abnormalType;

    /// <summary>남은 지속 시간</summary>
    public float remainingTime;

    /// <summary>쿨다운 남은 시간</summary>
    public float cooldownRemaining;
}
