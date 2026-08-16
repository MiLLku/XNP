using System;
using UnityEngine;

/// <summary>
/// 스킬 해금에 요구되는 스탯 종류.
///
/// 모든 직원은 <b>동일한 스킬 트리</b>를 갖는다. 개인차는 무작위 생성된
/// 결격 사항과 현재 스탯에서 나오며, 그 결과 직원마다 찍을 수 있는 스킬이 달라진다.
/// </summary>
public enum SkillStatType
{
    /// <summary>최대 체력</summary>
    MaxHealth,

    /// <summary>최대 정신력</summary>
    MaxMental,

    /// <summary>근접 전투 숙련 레벨</summary>
    MeleeLevel,

    /// <summary>원거리 전투 숙련 레벨</summary>
    RangedLevel,

    /// <summary>스킬 카테고리에 대응하는 작업 속도 (채광 스킬이면 채광 속도)</summary>
    CategoryWorkSpeed,

    /// <summary>운반 용량</summary>
    CarryCapacity,

    /// <summary>직원 통합 레벨</summary>
    EmployeeLevel,
}

/// <summary>
/// 스킬 하나에 걸린 스탯 요구 조건 (현재 값이 minValue 이상이어야 해금 가능).
/// </summary>
[Serializable]
public class SkillStatRequirement
{
    [Tooltip("검사할 스탯 종류")]
    public SkillStatType stat;

    [Tooltip("이 값 이상이어야 해금할 수 있습니다")]
    public float minValue;

    /// <summary>UI 표기용 한글 이름</summary>
    public string DisplayName => stat switch
    {
        SkillStatType.MaxHealth         => "최대 체력",
        SkillStatType.MaxMental         => "최대 정신력",
        SkillStatType.MeleeLevel        => "근접 숙련",
        SkillStatType.RangedLevel       => "원거리 숙련",
        SkillStatType.CategoryWorkSpeed => "해당 작업 속도",
        SkillStatType.CarryCapacity     => "운반 용량",
        SkillStatType.EmployeeLevel     => "직원 레벨",
        _                               => stat.ToString(),
    };
}
