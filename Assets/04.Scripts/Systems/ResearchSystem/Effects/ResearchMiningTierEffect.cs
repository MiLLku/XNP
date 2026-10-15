using UnityEngine;

/// <summary>
/// 연구 완료 시 기지의 채광 등급을 올리는 효과 — 그 등급 이하의 타일을 캘 수 있게 됩니다.
/// 등급은 누적이 아니라 최댓값입니다(채광 III을 마치면 3).
/// 판정은 <see cref="MiningSkillGate.CanMine(int)"/>.
/// </summary>
[CreateAssetMenu(fileName = "MiningTier", menuName = "Research/Effects/Mining Tier")]
public class ResearchMiningTierEffect : ResearchUnlockEffect
{
    [Tooltip("연구를 마치면 열리는 채광 등급 (1 = 석탄·구리 / 2 = 철·은 / 3 = 금·수정·티타늄 / 4 = 경계암)")]
    [Range(1, MiningSkillGate.MAX_TIER)] public int tier = 1;

    public override void Apply()
    {
        ResearchTreeManager.instance?.RegisterMiningTier(tier);
    }

    public override string GetDescription()
        => $"채광 등급 {MiningSkillGate.RomanNumeral(tier)}";
}
