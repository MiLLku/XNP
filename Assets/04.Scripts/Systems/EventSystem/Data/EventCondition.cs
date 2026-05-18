using System;
using UnityEngine;

/// <summary>
/// 이벤트 발동 조건
/// </summary>
[Serializable]
public class EventCondition
{
    [Tooltip("조건 타입")]
    public ConditionType type;

    [Tooltip("비교 연산자")]
    public ComparisonOperator comparison;

    [Tooltip("비교할 값")]
    public float value;

    [Tooltip("특정 아이템/이벤트 ID (필요한 경우)")]
    public int targetId;

    [Tooltip("조건 설명 (에디터용)")]
    public string description;

    /// <summary>
    /// 조건을 사람이 읽을 수 있는 문자열로 변환
    /// </summary>
    public string ToReadableString()
    {
        string op = comparison switch
        {
            ComparisonOperator.GreaterThan => ">",
            ComparisonOperator.GreaterOrEqual => ">=",
            ComparisonOperator.LessThan => "<",
            ComparisonOperator.LessOrEqual => "<=",
            ComparisonOperator.Equal => "==",
            ComparisonOperator.NotEqual => "!=",
            _ => "?"
        };

        return $"{type} {op} {value}";
    }
}
