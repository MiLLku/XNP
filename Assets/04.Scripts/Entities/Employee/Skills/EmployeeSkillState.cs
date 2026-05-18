using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 직원별 스킬 트리 상태 컴포넌트.
///
/// - 전 직원이 동일한 SkillTreeConfig를 참조하며, 해제 목록만 개인별로 관리합니다.
/// - 인스펙터에서 unlockedSkillIds를 직접 편집하여 프리팹 기본 스킬을 설정할 수 있습니다.
/// - 결격 사항(EmployeeWork.IsDisqualified)이 있는 카테고리의 스킬은 해제가 차단됩니다.
/// - 기능 효과는 현재 미구현 상태이며, SkillTreePanel에서 시각 구조만 확인 가능합니다.
/// </summary>
public class EmployeeSkillState : MonoBehaviour
{
    #region 직렬화 필드

    [Header("스킬 트리 설정")]
    [Tooltip("게임 전체 공용 스킬 트리 구성 데이터")]
    [SerializeField] private SkillTreeConfig config;

    [Header("해제된 스킬 (프리팹 설정 가능)")]
    [Tooltip("현재 해제된 스킬 ID 목록. 인스펙터에서 편집하여 프리팹별 기본 스킬 지정")]
    [SerializeField] private List<int> unlockedSkillIds = new List<int>();

    #endregion

    #region 내부 참조

    private EmployeeWork _work;
    private EmployeeStatsController _statsController;

    #endregion

    #region 초기화

    private void Awake()
    {
        _work = GetComponent<EmployeeWork>();
        _statsController = GetComponent<EmployeeStatsController>();
    }

    private void Start()
    {
        // SkillTreeConfig의 defaultUnlocked 스킬이 목록에 없으면 자동 추가
        ApplyDefaultUnlocks();
    }

    private void ApplyDefaultUnlocks()
    {
        if (config == null) return;
        foreach (int id in config.GetDefaultUnlockedIds())
        {
            if (!unlockedSkillIds.Contains(id))
                unlockedSkillIds.Add(id);
        }
    }

    #endregion

    #region 퍼블릭 API — 조회

    /// <summary>연결된 SkillTreeConfig</summary>
    public SkillTreeConfig Config => config;

    /// <summary>해제된 스킬 ID 목록 (읽기 전용)</summary>
    public IReadOnlyList<int> UnlockedSkillIds => unlockedSkillIds;

    /// <summary>특정 스킬이 해제되었는지 확인합니다.</summary>
    public bool IsUnlocked(int skillId) => unlockedSkillIds.Contains(skillId);

    /// <summary>
    /// 이 직원에게 해당 스킬 카테고리 결격이 있는지 확인합니다.
    /// General 카테고리는 항상 false를 반환합니다.
    /// </summary>
    public bool IsDisqualifiedForCategory(SkillCategory category)
    {
        if (_work == null) return false;
        if (category == SkillCategory.General) return false;

        WorkType? mapped = CategoryToWorkType(category);
        return mapped.HasValue && _work.IsDisqualified(mapped.Value);
    }

    /// <summary>
    /// 특정 스킬을 지금 해제할 수 있는지 확인합니다.
    /// 조건: 미해제 + 카테고리 결격 없음 + 선행 스킬 전부 해제
    /// </summary>
    public bool CanUnlock(SkillData skill)
    {
        if (skill == null || config == null) return false;
        if (IsUnlocked(skill.skillId)) return false;
        if (IsDisqualifiedForCategory(skill.category)) return false;

        foreach (int prereqId in skill.prerequisiteSkillIds)
        {
            if (!IsUnlocked(prereqId)) return false;
        }

        return true;
    }

    /// <summary>
    /// 해제를 막는 이유를 문자열로 반환합니다 (UI 툴팁용).
    /// 해제 가능 상태이거나 이미 해제됐으면 null 반환.
    /// </summary>
    public string GetBlockReason(SkillData skill)
    {
        if (skill == null) return "스킬 정보 없음";
        if (IsUnlocked(skill.skillId)) return null;

        if (IsDisqualifiedForCategory(skill.category))
            return $"[{skill.category}] 결격 사항으로 인해 잠금됨";

        if (config != null)
        {
            foreach (int prereqId in skill.prerequisiteSkillIds)
            {
                if (!IsUnlocked(prereqId))
                {
                    var prereq = config.GetSkill(prereqId);
                    return $"선행 스킬 필요: {(prereq != null ? prereq.skillName : prereqId.ToString())}";
                }
            }
        }

        return null;
    }

    #endregion

    #region 퍼블릭 API — 변경

    /// <summary>
    /// 스킬을 해제합니다.
    /// CanUnlock() 검사 없이 즉시 적용됩니다 — 호출 전 검증 필요.
    /// </summary>
    public void Unlock(int skillId)
    {
        if (!unlockedSkillIds.Contains(skillId))
        {
            unlockedSkillIds.Add(skillId);
            _statsController?.RecalculateModifiers();
        }
    }

    /// <summary>
    /// 스킬을 강제 잠금합니다.
    /// 결격 사항 발생 시 외부에서 호출하여 기존 해제 스킬을 회수할 수 있습니다.
    /// </summary>
    public void ForceRevoke(int skillId)
    {
        unlockedSkillIds.Remove(skillId);
    }

    /// <summary>
    /// 특정 카테고리의 모든 해제 스킬을 강제 잠금합니다.
    /// 해당 카테고리 결격 추가 시 호출합니다.
    /// </summary>
    public void ForceRevokeCategory(SkillCategory category)
    {
        if (config == null) return;
        foreach (var skill in config.GetSkillsByCategory(category))
        {
            if (skill != null)
                unlockedSkillIds.Remove(skill.skillId);
        }
    }

    #endregion

    #region 스킬 부여 특성 합산

    /// <summary>
    /// 현재 해제된 모든 스킬의 grantedTrait 효과를 합산하여 반환합니다.
    /// EmployeeStatsController에서 호출하여 캐시합니다.
    /// </summary>
    public TraitEffects GetActiveTraitEffects()
    {
        var merged = new TraitEffects();
        if (config == null) return merged;

        foreach (int id in unlockedSkillIds)
        {
            var skill = config.GetSkill(id);
            if (skill?.grantedTrait == null) continue;

            var fx = skill.grantedTrait.effects;
            merged.healthModifier              += fx.healthModifier;
            merged.mentalModifier              += fx.mentalModifier;
            merged.attackModifier              += fx.attackModifier;
            merged.globalWorkSpeedModifier     += fx.globalWorkSpeedModifier;
            merged.hungerRateModifier          += fx.hungerRateModifier;
            merged.fatigueRateModifier         += fx.fatigueRateModifier;
            merged.physicalDamageReduction     += fx.physicalDamageReduction;
            merged.erosionIgnoreBonus          += fx.erosionIgnoreBonus;
            merged.moveSpeedModifier           += fx.moveSpeedModifier;
            merged.skillGainRateModifier       += fx.skillGainRateModifier;
        }

        return merged;
    }

    #endregion

    #region 내부 유틸리티

    private static WorkType? CategoryToWorkType(SkillCategory category)
    {
        return category switch
        {
            SkillCategory.Mining    => WorkType.Mining,
            SkillCategory.Chopping  => WorkType.Chopping,
            SkillCategory.Research  => WorkType.Research,
            SkillCategory.Crafting  => WorkType.Crafting,
            SkillCategory.Gardening => WorkType.Gardening,
            SkillCategory.Hauling   => WorkType.Hauling,
            SkillCategory.Building  => WorkType.Building,
            _                       => (WorkType?)null,
        };
    }

    #endregion
}
