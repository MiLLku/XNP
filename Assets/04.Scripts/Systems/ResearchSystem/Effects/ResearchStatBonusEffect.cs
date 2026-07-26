using UnityEngine;

/// <summary>
/// 연구 완료 시 전역 스탯 보너스를 누적하는 효과.
/// </summary>
[CreateAssetMenu(fileName = "StatBonus", menuName = "Research/Effects/Stat Bonus")]
public class ResearchStatBonusEffect : ResearchUnlockEffect
{
    [Tooltip("보너스를 적용할 스탯 종류")]
    public ResearchStatType statType;

    [Tooltip("보너스 크기 — 반드시 '비율'로 입력하세요. 0.15 = +15%.\n" +
             "절대값(예: +15 HP)이 아닙니다. 소비 지점이 모두 (1 + 합계) 형태로 곱합니다.")]
    public float bonusValue;

    public override void Apply()
    {
        ResearchTreeManager.instance?.ApplyStatBonus(statType, bonusValue);
    }

    public override string GetDescription()
        => $"{statType} +{bonusValue:P0}";
}
