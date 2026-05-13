using System;
using UnityEngine;

/// <summary>
/// 이벤트 효과
/// </summary>
[Serializable]
public class EventEffect
{
    [Tooltip("효과 타입")]
    public EffectType type;

    [Tooltip("효과 대상")]
    public EffectTarget target;

    [Tooltip("효과 값 (수치)")]
    public float value;

    [Tooltip("대상 ID (아이템, 이벤트 등)")]
    public int targetId;

    [Tooltip("효과 설명 (에디터용)")]
    public string description;

    /// <summary>
    /// 효과를 사람이 읽을 수 있는 문자열로 변환
    /// </summary>
    public string ToReadableString()
    {
        string sign = value >= 0 ? "+" : "";
        return $"{type}({target}): {sign}{value}";
    }
}
