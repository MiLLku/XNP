/// <summary>
/// 아이템 종류 식별자.
///
/// 정수값 규칙:
///   - 0          : None (없음)
///   - 1 ~ 100    : 원자재 (Raw Materials) — 채광·벌목 등으로 직접 획득
///   - 101 ~ 200  : 가공 자원 (Processed) — 제련·제작 등으로 만들어짐
///   - 201 ~ 250  : 음식 (Food) — 직원이 섭취해 배고픔 회복
///   - 251 ~ 300  : 약물 (Drug) — 직원이 복용해 재미 회복
///   - 301 ~ 350  : 무기 (Weapon) — 장비 (EquipmentData 연동)
///   - 351 ~ 400  : 방어구 (Armor) — 장비 (EquipmentData 연동)
///   - 401 ~ 450  : 식물 (Plant) — 묘목·씨앗. ItemData.plantEntity로 심을 개체를 지정
///   - 451 ~ 500  : 성장 (Growth) — 레벨업 재료 등
///   - 501 ~      : 향후 확장 (도구·부품 등)
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
    Coal      = 8,
    Crystal   = 9,
    TitaniumOre = 10,
    Ether       = 11,
    AbyssFruit  = 12,

    // ─── 가공 자원 (101~200) ────────────────────
    IronIngot   = 101,
    CopperIngot = 102,
    SilverIngot = 103,
    GoldIngot   = 104,

    /// <summary>침식 결정체 — 세척 시설이 직원의 침식을 씻어내며 산출합니다.</summary>
    ErosionCrystal = 105,

    /// <summary>에테리온 — 에테르를 제련한 것. 태워서 직원에게 최하층 경계 채굴 자격을 새깁니다.</summary>
    Etherion = 106,

    // ─── 음식 (201~250) ─────────────────────────
    Berry       = 201,
    Rice          = 202,
    Potato        = 203,
    /// <summary>침식 쌀 — 배는 더 차지만 침식이 조금 오르고 기분이 잠시 떨어집니다.</summary>
    ErosionRice   = 204,
    /// <summary>침식 감자 — 배는 더 차지만 침식이 조금 오르고 기분이 잠시 떨어집니다.</summary>
    ErosionPotato = 205,
    /// <summary>간단한 식사 — 아무 식재료 4개로 조리. 영양 = 재료 합 × 1.2</summary>
    SimpleMeal    = 206,
    /// <summary>침식된 간단한 식사 — 침식 재료가 하나라도 들어간 간단한 식사</summary>
    ErosionSimpleMeal = 207,

    // ─── 약물 (251~300) ─────────────────────────
    Sedative    = 251,

    // ─── 무기 (301~350) ─────────────────────────
    IronSword   = 301,
    HuntingBow  = 302,

    // ─── 방어구 (351~400) ───────────────────────
    IronArmor   = 351,

    // ─── 식물 (401~450) ─────────────────────────
    /// <summary>나무 묘목 — 나무를 제초하면 나오고, 파종하면 나무로 자랍니다.</summary>
    TreeSapling  = 401,
    /// <summary>베리 묘목 — 베리 덤불을 제초하면 나오고, 파종하면 베리 덤불로 자랍니다.</summary>
    BerrySapling = 402,
    RiceSeed          = 403,
    PotatoSeed        = 404,
    ErosionRiceSeed   = 405,
    ErosionPotatoSeed = 406,

    // ─── 성장 (451~500) ─────────────────────────
    /// <summary>습격 전리품 — 습격 적이 확률로 떨굼. 연구 후 직원 레벨업에 사용</summary>
    RaidTrophy        = 451,
}
