using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 스킬 트리 전체 구성 데이터 (ScriptableObject).
///
/// 게임 내 모든 직원이 동일한 스킬 트리를 공유합니다.
/// GameDatabase나 별도 참조 슬롯을 통해 씬에 주입합니다.
/// </summary>
[CreateAssetMenu(fileName = "SkillTreeConfig", menuName = "Employee/Skill Tree Config")]
public class SkillTreeConfig : ScriptableObject
{
    [Header("스킬 목록")]
    [Tooltip("스킬 트리를 구성하는 모든 SkillData. 순서는 트리 내 렌더 순서에 영향 없음")]
    public List<SkillData> skills = new List<SkillData>();

    // ─────────────────────────────────────────────
    #region 조회

    /// <summary>ID로 SkillData를 반환합니다. 없으면 null.</summary>
    public SkillData GetSkill(int id)
        => skills.FirstOrDefault(s => s != null && s.skillId == id);

    /// <summary>특정 스킬의 선행 스킬 목록을 반환합니다.</summary>
    public List<SkillData> GetPrerequisites(SkillData skill)
    {
        if (skill == null) return new List<SkillData>();
        return skill.prerequisiteSkillIds
            .Select(GetSkill)
            .Where(s => s != null)
            .ToList();
    }

    /// <summary>특정 스킬을 선행으로 요구하는 자식 스킬 목록을 반환합니다.</summary>
    public List<SkillData> GetChildSkills(SkillData skill)
    {
        if (skill == null) return new List<SkillData>();
        return skills
            .Where(s => s != null && s.prerequisiteSkillIds.Contains(skill.skillId))
            .ToList();
    }

    /// <summary>defaultUnlocked = true 인 스킬의 ID 목록을 반환합니다.</summary>
    public List<int> GetDefaultUnlockedIds()
        => skills
            .Where(s => s != null && s.defaultUnlocked)
            .Select(s => s.skillId)
            .ToList();

    /// <summary>특정 카테고리에 속한 스킬 목록을 반환합니다.</summary>
    public List<SkillData> GetSkillsByCategory(SkillCategory category)
        => skills.Where(s => s != null && s.category == category).ToList();

    #endregion
}
