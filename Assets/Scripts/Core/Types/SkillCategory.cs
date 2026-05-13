/// <summary>
/// 스킬 카테고리 — WorkType 기반 결격 사항 연동용.
///
/// General을 제외한 각 카테고리는 대응하는 WorkType의 비자격 상태와 연동됩니다.
/// 예: SkillCategory.Gardening → WorkType.Gardening 비자격 시 해당 카테고리 스킬 잠금.
/// </summary>
public enum SkillCategory
{
    /// <summary>공통 스킬 — 어떤 결격 사항에도 영향받지 않음</summary>
    General,

    /// <summary>채광 관련 스킬 → WorkType.Mining 비자격 시 잠금</summary>
    Mining,

    /// <summary>벌목 관련 스킬 → WorkType.Chopping 비자격 시 잠금</summary>
    Chopping,

    /// <summary>연구 관련 스킬 → WorkType.Research 비자격 시 잠금</summary>
    Research,

    /// <summary>제작 관련 스킬 → WorkType.Crafting 비자격 시 잠금</summary>
    Crafting,

    /// <summary>원예 관련 스킬 → WorkType.Gardening 비자격 시 잠금</summary>
    Gardening,

    /// <summary>운반 관련 스킬 → WorkType.Hauling 비자격 시 잠금</summary>
    Hauling,

    /// <summary>건설 관련 스킬 → WorkType.Building 비자격 시 잠금</summary>
    Building,

    /// <summary>전투 관련 스킬 (미래 확장용)</summary>
    Combat,
}
