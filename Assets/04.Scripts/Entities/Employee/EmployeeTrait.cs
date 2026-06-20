using UnityEngine;

/// <summary>
/// 직원 특성 데이터 (ScriptableObject).
/// 각 특성은 이름, 설명, 아이콘, 타입, 효과를 가집니다.
/// StampSystem 메뉴에서 생성 가능합니다.
/// </summary>
[CreateAssetMenu(fileName = "NewTrait", menuName = "StampSystem/Employee Trait")]
public class EmployeeTrait : ScriptableObject
{
    [Header("특성 정보")]
    public string traitName;
    public string description;
    public Sprite icon;

    [Header("특성 타입")]
    public TraitType type;

    [Header("효과")]
    public TraitEffects effects;
}

/// <summary>
/// 작업 타입별 속도 배율 데이터 (곱연산). 특성의 특정 작업 속도 보정에 사용합니다.
/// </summary>
[System.Serializable]
public struct WorkSpeedMultiplier
{
    /// <summary>대상 작업 타입</summary>
    public WorkType workType;

    /// <summary>속도 배율 (1.0 = 변화 없음)</summary>
    [Range(0f, 3f)]
    public float multiplier;
}

/// <summary>
/// 작업 타입별 속도 보정 데이터 (가산 %). 장비(EquipmentData) 등에서 사용합니다.
/// </summary>
[System.Serializable]
public struct WorkSpeedModifier
{
    /// <summary>대상 작업 타입</summary>
    public WorkType workType;

    /// <summary>속도 보정 (%)</summary>
    [Range(-50, 100)]
    public float speedModifier;
}

/// <summary>
/// 특성 팩토리.
/// 사전 정의된 특성 이름과 효과를 제공합니다.
/// </summary>
public static class TraitFactory
{
    /// <summary>사전 정의된 특성 이름 상수</summary>
    public static class TraitNames
    {
        // 긍정적 특성
        public const string HARDWORKER = "근면함";
        public const string NIGHT_OWL = "야행성";
        public const string EFFICIENT = "효율적";
        public const string STRONG = "강인함";
        public const string FOCUSED = "집중력";

        // 부정적 특성
        public const string LAZY = "게으름";
        public const string FRAGILE = "허약함";
        public const string CLUMSY = "서투름";
        public const string GLUTTON = "대식가";

        // 중립적 특성
        public const string LONEWOLF = "외톨이";
        public const string TEAMPLAYER = "팀플레이어";
    }

    /// <summary>"근면함" 특성 효과를 반환합니다 (작업 속도 +20%, 피로 속도 +10%)</summary>
    public static TraitEffects GetHardworkerEffects()
    {
        return new TraitEffects
        {
            globalWorkSpeedModifier = 20f,
            fatigueRateModifier = 10f
        };
    }

    /// <summary>"게으름" 특성 효과를 반환합니다 (작업 속도 -20%, 피로 속도 -10%)</summary>
    public static TraitEffects GetLazyEffects()
    {
        return new TraitEffects
        {
            globalWorkSpeedModifier = -20f,
            fatigueRateModifier = -10f
        };
    }

    /// <summary>"대식가" 특성 효과를 반환합니다 (배고픔 감소 +50%, 최대 체력 ×1.1)</summary>
    public static TraitEffects GetGluttonEffects()
    {
        return new TraitEffects
        {
            hungerRateModifier = 50f,
            healthMult = 1.1f
        };
    }
}
