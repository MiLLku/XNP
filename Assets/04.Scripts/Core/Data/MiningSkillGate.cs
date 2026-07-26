using UnityEngine;

/// <summary>
/// 타일별 채광 자격 게이트.
///
/// 깊이 진행을 막는 <b>하드 게이트</b>다. 경도(TileHardness)가 "얼마나 오래 걸리나"라면
/// 이쪽은 "애초에 팔 수 있나"를 결정한다.
///   채광 경험 → 채광 적성 레벨 → (스킬 포인트 소모) 채광 스킬 해제 → 해당 광물 채광 가능
///
/// 스킬 ID는 SkillTreeConfig의 채광 계열 3종을 가리킨다:
///   채광 I 기초(5)   — 지표·돌·석탄·구리  (기본 해제)
///   채광 II 심층(6)  — 철·은
///   채광 III 정밀(7) — 금·수정
/// </summary>
public static class MiningSkillGate
{
    public const int SKILL_MINING_I   = 5;
    public const int SKILL_MINING_II  = 6;
    public const int SKILL_MINING_III = 7;

    /// <summary>
    /// 해당 타일을 캐는 데 필요한 스킬 ID. 0이면 제한 없음.
    /// </summary>
    public static int RequiredSkillId(TileType tile)
    {
        switch (tile)
        {
            // 지표·기초 자원 — 제한 없음
            case TileType.Dirt:
            case TileType.GrassDirt:
            case TileType.ProcessedDirt:
            case TileType.Stone:
                return 0;

            // T1 — 채광 I
            case TileType.Coal:
            case TileType.CopperOre:
                return SKILL_MINING_I;

            // T2~T3 초입 — 채광 II 심층
            case TileType.IronOre:
            case TileType.SilverOre:
                return SKILL_MINING_II;

            // T3 후반~T4 — 채광 III 정밀
            case TileType.GoldOre:
            case TileType.Crystal:
                return SKILL_MINING_III;

            default:
                return 0;
        }
    }

    /// <summary>호환용 int 오버로드.</summary>
    public static int RequiredSkillId(int tileId) => RequiredSkillId((TileType)tileId);

    /// <summary>
    /// 해당 직원이 이 타일을 캘 수 있는지 확인합니다.
    /// 스킬 컴포넌트가 없으면 제한하지 않습니다(구 프리팹 호환).
    /// </summary>
    public static bool CanMine(Employee employee, int tileId)
    {
        int required = RequiredSkillId(tileId);
        if (required == 0) return true;
        if (employee == null) return false;

        var skills = employee.GetComponent<EmployeeSkillState>();
        if (skills == null) return true;

        return skills.IsUnlocked(required);
    }

    /// <summary>필요 스킬 이름 (로그·UI용).</summary>
    public static string RequiredSkillName(int tileId)
    {
        switch (RequiredSkillId(tileId))
        {
            case SKILL_MINING_I:   return "채광 I 기초";
            case SKILL_MINING_II:  return "채광 II 심층";
            case SKILL_MINING_III: return "채광 III 정밀";
            default:               return null;
        }
    }
}
