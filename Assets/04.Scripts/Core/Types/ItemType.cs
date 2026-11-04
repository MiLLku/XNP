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
    /// <summary>침정석 — 기지에서 좌우로 멀리(120칸+) 나가야 나오는 광물. 옛 티타늄 광석 자리(세이브 호환)</summary>
    NeedleOre   = 10,
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
    SpawnerCore   = 19, // 스포너 — 증식 핵
    DisguiseFilm  = 20, // 위장체 — 위장 피막
    /// <summary>주석 광석 — 구리 곁 얕은 층. 구리와 섞어 청동을 만든다</summary>
    TinOre        = 21,
    /// <summary>원유 — 석유 분출구 위 유정이 뽑아낸다. 정유기에서 합성수지·고급 연료로</summary>
    CrudeOil      = 22,
    /// <summary>불길한 알 — 습격 적이 떨군다. 티어(떨군 적)마다 나오는 중립 침식체 풀이 다르다</summary>
    OminousEgg1   = 23,
    OminousEgg2   = 24,
    OminousEgg3   = 25,
    /// <summary>시체 — 직원·침식된 인간이 죽으면 남는다. 어려운 종의 먹이, 2일 뒤 부패</summary>
    Corpse        = 26,
    /// <summary>중립 침식체 유체 — 부화기에서 깨어나 둥지 방 먹이통으로 운반된다</summary>
    HatchlingSlimeLarva   = 30,
    HatchlingGlandBeetle  = 31,
    HatchlingFiberSpider  = 32,
    HatchlingAcidCrawler  = 33,
    HatchlingSporeSac     = 34,
    HatchlingAbyssDevourer = 35,
    /// <summary>침식 섬유 — 섬유 거미가 뽑는다</summary>
    ErosionFiber  = 36,

    // ─── 가공 자원 (101~200) ────────────────────
    IronIngot   = 101,
    CopperIngot = 102,
    SilverIngot = 103,
    GoldIngot   = 104,

    /// <summary>침식 결정체 — 세척 시설이 직원의 침식을 씻어내며 산출합니다.</summary>
    ErosionCrystal = 105,

    /// <summary>에테리온 — 에테르를 제련한 것. 태워서 직원에게 최하층 경계 채굴 자격을 새깁니다.</summary>
    Etherion = 106,

    // 금속 테크 사다리 (Lv2·5·7) 와 석유 화학
    BronzeIngot    = 107, // 구리 광석 2 + 주석 광석 1 → 2 (제련소)
    RefinedAlloy   = 108, // 철 주괴 2 + 은 주괴 1 + 금 주괴 1 → 2 (용광로)
    NeedleAlloy    = 109, // 침정석 2 + 은 주괴 1 → 1 (용광로)
    SyntheticResin = 110, // 원유 → (정유기)
    HighGradeFuel  = 111, // 원유 → (정유기)
    /// <summary>침식 사료 — 침식 열매로 만든다. 중립 침식체의 먹이</summary>
    ErosionFeed    = 112,

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
    /// <summary>침식 열매 — 기지 밖 침식된 덤불에서 채집. 날로 먹을 수 있지만 침식 +8</summary>
    ErosionFruit = 210,

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

    /// <summary>금속 단계별 검·석궁 (EquipmentData.metalTier로 성능 배율) — 빈 단계만 신규</summary>
    CopperSword = 311, // 구리 검
    BronzeSword = 312, // 청동 검
    SilverSword = 313, // 은 검
    NeedleSword = 314, // 침정석 검
    NeedleAlloySword = 315, // 침정 합금 검
    EtherionSword = 316, // 에테리온 검
    CopperCrossbow = 317, // 구리 석궁
    SilverCrossbow = 318, // 은 석궁
    IronCrossbow = 319, // 철 석궁
    NeedleCrossbow = 320, // 침정석 석궁
    NeedleAlloyCrossbow = 321, // 침정 합금 석궁
    EtherionCrossbow = 322, // 에테리온 석궁

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
    /// <summary>위장 피막 외투 — 위장체 도감 보호구. 착용자는 적의 공격 대상에서 빠진다(광역 피해는 받음)</summary>
    DisguiseCloak = 378,

    /// <summary>금속 단계별 갑옷 — 빈 단계만 신규</summary>
    CopperArmor = 379, // 구리 갑옷
    BronzeArmor = 380, // 청동 갑옷
    SilverArmor = 381, // 은 갑옷
    NeedleArmor = 382, // 침정석 갑옷
    NeedleAlloyArmor = 383, // 침정 합금 갑옷
    EtherionArmor = 384, // 에테리온 갑옷

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
    /// <summary>정화 수류탄 — 다용도구 칸 1회용. 소집 직원에게 우클릭으로 적 구조물을 지정해 던짐 (거점 습격 파훼)</summary>
    PurifyGrenade     = 506,
    /// <summary>증식 핵 미끼 — 스포너 도감 다용도구 1회용. 소집 직원 우클릭으로 적 근처 지점에 던지면 반경 8칸 적이 10초 동안 끌려감</summary>
    SpawnerCoreLure   = 507,
    /// <summary>정화 혈청 — 다용도구 1회용. 소집 직원 우클릭으로 제압된 침식된 인간에게 써서 직원으로 되돌린다</summary>
    PurificationSerum = 508,
}
