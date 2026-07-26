using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스킬 포인트 상한 매니저.
///
/// 직원이 쓸 수 있는 스킬 포인트는 <b>직원당 기본값(고정) + 전역 해금 보너스</b>다.
/// 전역 보너스는 훈련소(TrainingHall)를 짓고 재료를 투입해 단계별로 올린다 —
/// 즉 "스킬을 더 찍고 싶으면 기지에 투자하라"는 구조.
///
/// SaveOrder: 86 (ResearchTreeManager 85 다음)
/// </summary>
public class SkillPointManager : DestroySingleton<SkillPointManager>, ISaveModule
{
    [Serializable]
    public class UpgradeTier
    {
        [Tooltip("이 단계 해금에 필요한 재료")]
        public List<ResourceCost> cost = new List<ResourceCost>();

        [Tooltip("이 단계 해금 시 전역 스킬 포인트 상한 증가량")]
        [Min(1)] public int bonusPoints = 1;
    }

    [Header("상한 확장 단계 (앞에서부터 순서대로 해금)")]
    [SerializeField] private List<UpgradeTier> upgradeTiers = new List<UpgradeTier>();

    [Header("현재 상태 (읽기 전용)")]
    [SerializeField] private int unlockedTierCount;

    /// <summary>전역 해금으로 얻은 추가 스킬 포인트</summary>
    public int GlobalBonusPoints
    {
        get
        {
            int sum = 0;
            for (int i = 0; i < unlockedTierCount && i < upgradeTiers.Count; i++)
                sum += upgradeTiers[i].bonusPoints;
            return sum;
        }
    }

    /// <summary>해금된 단계 수</summary>
    public int UnlockedTierCount => unlockedTierCount;

    /// <summary>남은 확장 단계가 있는지</summary>
    public bool HasNextTier => unlockedTierCount < upgradeTiers.Count;

    /// <summary>다음 단계 정보 (없으면 null)</summary>
    public UpgradeTier NextTier => HasNextTier ? upgradeTiers[unlockedTierCount] : null;

    /// <summary>상한이 늘어났을 때 발생 (새 전역 보너스)</summary>
    public event Action<int> OnCapIncreased;

    /// <summary>
    /// 다음 단계를 해금합니다. 재료가 부족하면 false.
    /// 훈련소 건물에서 호출합니다.
    /// </summary>
    public bool TryUnlockNextTier()
    {
        if (!HasNextTier)
        {
            Debug.Log("[SkillPoint] 모든 확장 단계를 이미 해금했습니다.");
            return false;
        }

        var tier = upgradeTiers[unlockedTierCount];
        var inv = InventoryManager.instance;
        if (inv == null) return false;

        if (tier.cost.Count > 0 && !inv.HasItems(tier.cost))
        {
            Debug.Log($"[SkillPoint] 재료 부족 — {DescribeCost(tier)}");
            return false;
        }

        if (tier.cost.Count > 0 && !inv.RemoveItems(tier.cost))
        {
            Debug.LogWarning("[SkillPoint] 재료 소비 실패");
            return false;
        }

        unlockedTierCount++;
        Debug.Log($"[SkillPoint] 스킬 포인트 상한 확장! 단계 {unlockedTierCount}/{upgradeTiers.Count} (전역 보너스 +{GlobalBonusPoints})");

        NotificationManager.instance?.PushLetter(new Letter
        {
            title = "숙련 한계 확장",
            body = $"훈련 설비를 확충했습니다. 모든 직원의 스킬 포인트 상한이 {GlobalBonusPoints}만큼 늘어났습니다.",
            type = LetterType.Positive
        });

        OnCapIncreased?.Invoke(GlobalBonusPoints);
        return true;
    }

    /// <summary>다음 단계 비용을 사람이 읽을 수 있는 문자열로 반환합니다.</summary>
    public string DescribeNextCost()
    {
        var tier = NextTier;
        return tier == null ? "확장 완료" : DescribeCost(tier);
    }

    private static string DescribeCost(UpgradeTier tier)
    {
        if (tier.cost.Count == 0) return "무료";
        var parts = new List<string>();
        foreach (var c in tier.cost)
            if (c.item != null) parts.Add($"{c.item.itemName} {c.amount}");
        return string.Join(", ", parts);
    }

    #region ISaveModule

    public int SaveOrder => 86;

    public void Capture(SaveData data)
    {
        data.skillPointTierCount = unlockedTierCount;
    }

    public void Restore(SaveData data)
    {
        unlockedTierCount = Mathf.Clamp(data.skillPointTierCount, 0, upgradeTiers.Count);
    }

    public void PostRestore(SaveData data) { }

    #endregion
}
