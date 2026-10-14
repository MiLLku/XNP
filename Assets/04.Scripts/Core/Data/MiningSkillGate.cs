/// <summary>
/// 타일별 채광 자격 게이트.
///
/// 깊이 진행을 막는 <b>하드 게이트</b>다. 경도(TileHardness)가 "얼마나 오래 걸리나"라면
/// 이쪽은 "애초에 팔 수 있나"를 결정한다.
///
/// 자격은 <b>기지 전체의 채광 등급</b>(연구 '채광 I~IV', <see cref="ResearchMiningTierEffect"/>)이다.
/// 예전에는 직원 스킬(채광 I~III + 에테리온 각인)이었지만, 연구로 옮기고 직원 채광 스킬은
/// 채광 속도 보너스로 바뀌었다 — 연구 재료가 "지금 캘 수 있는 층의 귀한 재료"라서
/// 한 층을 캐야 다음 층을 열 수 있다.
///   1 = 석탄·구리 / 2 = 철·은 / 3 = 금·수정·티타늄 / 4 = 경계암
///
/// 어떤 타일이 몇 등급을 요구하는지는 TileDefinition 에셋의 requiredMiningTier 필드에 있다.
/// </summary>
public static class MiningSkillGate
{
    /// <summary>최고 채광 등급 (경계암 — 에테리온)</summary>
    public const int MAX_TIER = 4;

    /// <summary>해당 타일을 캐는 데 필요한 채광 등급. 0이면 제한 없음.</summary>
    public static int RequiredTier(TileType tile) => RequiredTier((int)tile);

    /// <summary>타일 ID(정수)로 조회합니다.</summary>
    public static int RequiredTier(int tileId)
    {
        var def = TileDefinitionLookup.Find(tileId);
        return def != null ? def.requiredMiningTier : 0;
    }

    /// <summary>기지가 지금 연구로 연 채광 등급 (연구 매니저가 없으면 0)</summary>
    public static int CurrentTier =>
        ResearchTreeManager.instance != null ? ResearchTreeManager.instance.MiningTier : 0;

    /// <summary>
    /// 이 타일을 지금 캘 수 있는지 — 기지 채광 등급만 봅니다(직원 무관).
    /// <paramref name="employee"/>는 예전 호출부 호환용이며 판정에 쓰이지 않습니다.
    /// </summary>
    public static bool CanMine(Employee employee, int tileId) => CanMine(tileId);

    /// <inheritdoc cref="CanMine(Employee,int)"/>
    public static bool CanMine(int tileId)
    {
        int required = RequiredTier(tileId);
        return required == 0 || CurrentTier >= required;
    }

    /// <summary>필요 등급 이름 (로그·UI용). 제한이 없으면 null.</summary>
    public static string RequiredTierName(int tileId)
    {
        int tier = RequiredTier(tileId);
        return tier == 0 ? null : $"채광 {RomanNumeral(tier)}";
    }

    /// <summary>등급 번호의 로마 숫자 표기 (1~4)</summary>
    public static string RomanNumeral(int tier)
    {
        switch (tier)
        {
            case 1: return "I";
            case 2: return "II";
            case 3: return "III";
            case 4: return "IV";
            default: return tier.ToString();
        }
    }

    /// <summary>
    /// 구 채광 스킬 ID → 채광 등급 (채광 I=5 → 1, II=6 → 2, III=7 → 3, 에테리온 각인=19 → 4).
    /// 세이브 마이그레이션(v15)과 정의 부트스트랩이 씁니다.
    /// </summary>
    public static int TierFromLegacySkillId(int skillId)
    {
        switch (skillId)
        {
            case 5: return 1;
            case 6: return 2;
            case 7: return 3;
            case 19: return 4;
            default: return 0;
        }
    }
}
