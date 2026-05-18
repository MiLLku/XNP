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
/// </summary>
[Serializable]
public class ActiveMentalEvent
{
    /// <summary>이벤트 타입</summary>
    public MentalEventType type;

    /// <summary>남은 지속 시간</summary>
    public float remainingTime;

    /// <summary>쿨다운 남은 시간</summary>
    public float cooldownRemaining;
}

/// <summary>
/// 정신 이벤트 저장 데이터.
/// </summary>
[Serializable]
public class MentalEventSaveData
{
    /// <summary>이벤트 타입 (MentalEventType as int)</summary>
    public int eventType;

    /// <summary>남은 지속 시간</summary>
    public float remainingTime;

    /// <summary>쿨다운 남은 시간</summary>
    public float cooldownRemaining;
}
