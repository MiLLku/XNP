using UnityEngine;

[CreateAssetMenu(fileName = "StatBonus", menuName = "Research/Effects/Stat Bonus")]
public class ResearchStatBonusEffect : ResearchUnlockEffect
{
    public ResearchStatType statType;
    public float bonusValue;

    public override void Apply()
    {
        ResearchTreeManager.instance?.ApplyStatBonus(statType, bonusValue);
    }

    public override string GetDescription()
        => $"{statType} +{bonusValue}";
}
