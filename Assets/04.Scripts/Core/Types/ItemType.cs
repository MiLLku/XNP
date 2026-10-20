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
    /// <summary>물 — 물 흡수 시설이나 물 퍼내기로 얻는다</summary>
    Water       = 13,
    /// <summary>침식된 물 — 침식이 높은 곳에서 받아낸 물</summary>
    ErodedWater = 14,
    /// <summary>흑요석 — 용암이 물을 만나 굳은 타일을 캐서 얻는다. 내열 슈트 IV 재료</summary>
    Obsidian    = 15,
    /// <summary>제놉스 특수 드롭 — 처치하면 떨어진다. 그 제놉스 도감 장비의 재료</summary>
    ErosionGland  = 16, // 침식 사수
    GrapplerSinew = 17, // 그래플러
    AcidSac       = 18, // 스피터

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
    /// <summary>요리사가 만든 간단한 식사 — 먹으면 추가 기분 (사이코패스 제외)</summary>
    ChefSimpleMeal = 208,
    ChefErosionSimpleMeal = 209,

    // ─── 약물 (251~300) ─────────────────────────
    Sedative    = 251,

    // ─── 무기 (301~350) ─────────────────────────
    IronSword   = 301,
    HuntingBow  = 302,
    /// <summary>1단계 근접 — 나무·돌</summary>
    StoneSpear  = 303,
    /// <summary>2단계 원거리 — 철·구리</summary>
    Crossbow    = 304,
    /// <summary>3단계 근접 — 철·은·금</summary>
    RefinedSword = 305,
    /// <summary>3단계 원거리 — 철·은·금</summary>
    PrecisionCrossbow = 306,
    /// <summary>0단계 근접 — 시작 지급 전용 (제작 불가)</summary>
    Baton = 307,
    /// <summary>제놉스 도감 무기 — 침식 사수·그래플러·스피터</summary>
    GlandRifle   = 308,
    SinewWhip    = 309,
    AcidThrower  = 310,

    // ─── 방어구 (351~400) ───────────────────────
    IronArmor   = 351,
    /// <summary>1단계 방어구 — 나무·돌</summary>
    WoodArmor   = 352,
    /// <summary>3단계 방어구 — 철·은·금</summary>
    ReinforcedArmor = 353,
    /// <summary>침식 방어 장비 1~3단계 — 같은 단계 재료 + 침식 결정체</summary>
    ProtectiveSuit1 = 354,
    ProtectiveSuit2 = 355,
    ProtectiveSuit3 = 356,
    /// <summary>0단계 방어구 — 시작 지급 전용 (제작 불가). 방어와 침식 방어를 조금씩 겸한다</summary>
    BasicArmor = 357,
    /// <summary>방호 헬멧 I~III — 헬멧 칸 침식 방호 (견딜 수 있는 침식 10/15/25)</summary>
    ProtectiveHelmet1 = 358,
    ProtectiveHelmet2 = 359,
    ProtectiveHelmet3 = 360,
    /// <summary>내열 슈트 I~IV — 슈트 칸 내열 (견딜 수 있는 온도 +15/+30/+45/+75도)</summary>
    HeatSuit1 = 361,
    HeatSuit2 = 362,
    HeatSuit3 = 363,
    HeatSuit4 = 364,
    /// <summary>기본 옷·장갑·신발·모자 — 새 직원이 입고 오는 시작 복장 (제재소에서 연구 없이 제작)</summary>
    BasicClothes = 365,
    BasicGloves  = 366,
    BasicShoes   = 367,
    BasicHat     = 368,
    /// <summary>장갑 — 작업(채광·건설·제작 +10%) / 방호(침식 +5) / 내열(온도 +10)</summary>
    WorkGloves       = 369,
    ProtectiveGloves = 370,
    HeatGloves       = 371,
    /// <summary>신발 — 작업화(이동 +10%) / 방수 장화(물속 감속 무시 + 침식 +5) / 내열화(온도 +10)</summary>
    WorkBoots       = 372,
    WaterproofBoots = 373,
    HeatBoots       = 374,
    /// <summary>제놉스 도감 착용 장비 — 분비샘 방호복(옷)·힘줄 보호구(보호구)·산성막 장갑(장갑)</summary>
    GlandSuit     = 375,
    SinewArmor    = 376,
    AcidGloves    = 377,

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

    // ─── 도구 (501~) ────────────────────────────
    /// <summary>응급 키트 — 다용도구 칸 1회용. 체력 30% 이하에서 저절로 써서 체력 +40</summary>
    FirstAidKit       = 501,
    /// <summary>정화 앰플 — 다용도구 칸 1회용. 침식이 유지 수치+30 이상이면 저절로 써서 침식 -15</summary>
    PurifyingAmpoule  = 502,
    /// <summary>제놉스 도감 다용도구 — 정화 분비샘(1회용)·갈고리 손잡이(채광·건설 속도)·해독 주머니(1회용)</summary>
    PurifyingGland    = 503,
    GrapplingGrip     = 504,
    AntidoteSac       = 505,
}
