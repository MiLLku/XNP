using System;
using UnityEngine;

/// <summary>
/// 제노프스 개별 효과 정의.
/// 해석도(interpretation ratio)에 따라 baseValue ~ maxValue 사이를 보간합니다.
///
/// 이로운 효과(isBenefit=true):  해석도↑ → 값이 baseValue에서 maxValue로 증가
/// 해로운 효과(isBenefit=false): 해석도↑ → 값이 baseValue에서 maxValue(0에 가까운 값)로 감소
/// </summary>
[Serializable]
public class XenopsEffect
{
    [Tooltip("효과 이름 (에디터 식별용)")]
    public string effectName;

    [Tooltip("효과 종류")]
    public XenopsEffectType effectType;

    [Tooltip("효과 적용 대상")]
    public XenopsEffectTarget target;

    [Tooltip("이로운 효과 여부 (false = 해로운 효과)")]
    public bool isBenefit;

    [Tooltip("해석도 0일 때 값")]
    public float baseValue;

    [Tooltip("해석도 MAX일 때 값")]
    public float maxValue;

    [Tooltip("대상 ID (아이템, 작업 타입 등 — 필요 시)")]
    public int targetId;

    /// <summary>
    /// 해석도 비율(0~1)에 따른 현재 효과 값을 계산합니다.
    /// </summary>
    /// <param name="interpretationRatio">해석도 비율 (0 = 미해석, 1 = 완전 해석)</param>
    /// <returns>현재 효과 값</returns>
    public float GetCurrentValue(float interpretationRatio)
    {
        float ratio = Mathf.Clamp01(interpretationRatio);
        return Mathf.Lerp(baseValue, maxValue, ratio);
    }

    /// <summary>
    /// 효과를 사람이 읽을 수 있는 문자열로 변환합니다.
    /// </summary>
    public string ToReadableString(float interpretationRatio)
    {
        float current = GetCurrentValue(interpretationRatio);
        string sign = current >= 0 ? "+" : "";
        string benefitTag = isBenefit ? "[이점]" : "[해악]";
        return $"{benefitTag} {effectName}: {sign}{current:F1} ({effectType} → {target})";
    }
}

/// <summary>
/// 제노프스 효과 종류.
/// </summary>
public enum XenopsEffectType
{
    // === 직원 스탯 관련 ===

    /// <summary>체력 수정 (절대값/초)</summary>
    ModifyHealth,

    /// <summary>정신력 수정 (절대값/초)</summary>
    ModifyMental,

    /// <summary>작업 속도 보정 (%)</summary>
    ModifyWorkSpeed,

    /// <summary>배고픔 감소 속도 보정 (%)</summary>
    ModifyHungerRate,

    /// <summary>피로 증가 속도 보정 (%)</summary>
    ModifyFatigueRate,

    /// <summary>공격력 보정 (%)</summary>
    ModifyAttackPower,

    // === 자원/생산 관련 ===

    /// <summary>생산 속도 보정 (%)</summary>
    ModifyProductionSpeed,

    /// <summary>자원 산출량 보정 (%)</summary>
    ModifyResourceYield,

    /// <summary>주기적 자원 소모 (절대값/간격)</summary>
    ConsumeResource,

    /// <summary>주기적 자원 생산 (절대값/간격)</summary>
    ProduceResource,

    // === 환경 관련 ===

    /// <summary>타일 성장 속도 보정 (%)</summary>
    ModifyTileGrowth,

    /// <summary>건물 체력 부식 (절대값/초)</summary>
    CorrodeBuildingHealth,

    // === 스탯 최대치 관련 ===

    /// <summary>최대 체력 변경 (절대값)</summary>
    ModifyMaxHealth,

    /// <summary>최대 정신력 변경 (절대값)</summary>
    ModifyMaxMental,

    // === 특수 ===

    /// <summary>작업 능력 부여 (targetId = WorkType)</summary>
    GrantWorkAbility,

    /// <summary>작업 타입 차단 (targetId = WorkType)</summary>
    BlockWorkType
}

/// <summary>
/// 제노프스 효과 적용 대상.
/// </summary>
public enum XenopsEffectTarget
{
    /// <summary>장착된 직원 (장비형 전용)</summary>
    EquippedEmployee,

    /// <summary>범위 내 직원</summary>
    NearbyEmployees,

    /// <summary>전체 직원</summary>
    AllEmployees,

    /// <summary>잠입한 건물 (잠입체 전용)</summary>
    TargetBuilding,

    /// <summary>범위 내 건물</summary>
    NearbyBuildings,

    /// <summary>범위 내 타일</summary>
    NearbyTiles,

    /// <summary>전역 효과</summary>
    Global
}
