using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 이벤트 선택지
/// 플레이어가 선택할 수 있는 옵션
/// </summary>
[Serializable]
public class EventChoice
{
    [Tooltip("선택지 텍스트")]
    public string choiceText;

    [Tooltip("선택지 설명 (결과 힌트)")]
    [TextArea(1, 2)]
    public string choiceDescription;

    [Tooltip("선택 시 적용되는 효과")]
    public List<EventEffect> effects;

    [Tooltip("선택 가능 조건 (비용 등)")]
    public List<EventCondition> requirements;

    [Tooltip("선택 불가능할 때 표시할 이유")]
    public string unavailableReason;
}
