/// <summary>
/// 아이템 종류 식별자.
///
/// 정수값 규칙:
///   - 0          : None (없음)
///   - 1 ~ 100    : 원자재 (Raw Materials) — 채광·벌목 등으로 직접 획득
///   - 101 ~ 200  : 가공 자원 (Processed) — 제련·제작 등으로 만들어짐
///   - 201 ~      : 향후 확장 (도구·부품·소비재 등)
///
/// 정수값은 SaveSystem과 외부 통신에서 사용됩니다. 한 번 정한 값은 가급적 바꾸지 마세요
/// (저장 파일 호환성이 깨집니다).
/// </summary>
public enum ItemType
{
    None = 0,

    // ─── 원자재 (1~100) ──────────────────────────
    Dirt      = 1,
    Stone     = 2,
    IronOre   = 3,
    CopperOre = 4,
    SilverOre = 5,
    GoldOre   = 6,
    Wood      = 7,

    // ─── 가공 자원 (101~200) ────────────────────
    IronIngot   = 101,
    CopperIngot = 102,
    SilverIngot = 103,
    GoldIngot   = 104,
}
