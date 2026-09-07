using UnityEngine;

/// <summary>
/// 게임 내 모든 ID를 중앙 관리하는 레지스트리.
/// ScriptableObject 생성 시 ID 범위를 참고하여 충돌을 방지합니다.
///
/// ID 범위:
///   타일(0~99), 바닥 타일(0~99), 개체(0~7999),
///   아이템(2000~2999), 건물(3000~3999),
///   직원(4000~4999), 레시피(5000~5999), 제노프스(6000~6999)
///
/// 타일·바닥 타일·개체 ID는 <see cref="DefinitionDatabase"/>의 정의 에셋이 소유하며,
/// 여기 있는 범위 상수는 정의 에디터가 중복·범위 이탈을 검증할 때 씁니다.
/// 아이템 이하 대역은 여전히 각 ScriptableObject가 직접 들고 있습니다.
/// </summary>
public static class GameIDRegistry
{
    #region 타일 ID (0~99)

    /// <summary>
    /// 지형 타일 ID 범위.
    ///
    /// GameMap.TileGrid에 그대로 저장되는 raw int 값이라 1000번대가 아니라 0부터 씁니다.
    /// 실제 값은 TileDefinition 에셋이 정하고 TileType enum으로 생성됩니다.
    /// </summary>
    public static class Tiles
    {
        /// <summary>TileGrid raw 값의 하한</summary>
        public const int RAW_MIN = 0;

        /// <summary>TileGrid raw 값의 상한 (TileRules의 배열 크기와 함께 관리)</summary>
        public const int RAW_MAX = 99;

        public const int MIN = RAW_MIN;
        public const int MAX = RAW_MAX;

        public static bool IsValid(int id) => id >= MIN && id <= MAX;
    }

    #endregion

    #region 바닥 타일 ID (0~99)

    /// <summary>
    /// 건설되는 바닥 타일 ID 범위.
    /// FloorTileDefinition 에셋이 정하고 FloorTileType enum으로 생성됩니다.
    /// FloorTile 프리팹에 직렬화된 값이므로 기존 번호는 바꾸지 마세요.
    /// </summary>
    public static class FloorTiles
    {
        public const int MIN = 0;
        public const int MAX = 99;

        public static bool IsValid(int id) => id >= MIN && id <= MAX;
    }

    #endregion

    #region 개체 ID (0~7999)

    /// <summary>
    /// 맵에 배치되는 개체(식생물·건물·바닥 프리팹) ID 범위.
    /// EntityDefinition 에셋이 정하고 EntityType enum으로 생성됩니다.
    ///
    /// <b>주의</b>: 이 대역은 역사적으로 뒤섞여 있습니다 — 침식 식물은 20·21,
    /// 나머지는 2001~2016으로, 아이템 대역(2000~2999)과 숫자가 겹칩니다.
    /// MapEntity.id와 StampElement.id로 세이브·스탬프 에셋에 이미 박혀 있어
    /// 재번호는 마이그레이션이 필요하므로 그대로 두었습니다.
    /// <b>새 개체는 <see cref="RECOMMENDED_MIN"/> 이상에서 발급하세요.</b>
    /// </summary>
    public static class Entities
    {
        public const int MIN = 0;
        public const int MAX = 7999;

        /// <summary>신규 개체 권장 대역의 시작 (기존 대역과 겹치지 않음)</summary>
        public const int RECOMMENDED_MIN = 7000;

        /// <summary>신규 개체 권장 대역의 끝</summary>
        public const int RECOMMENDED_MAX = 7999;

        public static bool IsValid(int id) => id >= MIN && id <= MAX;
    }

    #endregion

    #region 아이템 ID (2000~2999)

    /// <summary>
    /// 아이템 ID 범위 (2000~2999)
    /// </summary>
    public static class Items
    {
        // 기본 자원 (2000~2099)
        public const int WOOD = 2000;
        public const int STONE = 2001;
        public const int IRON_ORE = 2002;
        public const int COAL = 2003;
        public const int WATER = 2004;

        // 가공 자원 (2100~2199)  ※ 실제 ItemData의 itemID는 ItemType enum 값(101~200)을 씁니다
        public const int WOODEN_PLANK = 2100;
        public const int WOODEN_BEAM = 2101;
        public const int IRON_INGOT = 2102;
        public const int STEEL_BAR = 2103;
        public const int LEATHER = 2104;

        // 도구 (2200~2299)
        public const int WOODEN_PICKAXE = 2200;
        public const int STONE_PICKAXE = 2201;
        public const int IRON_PICKAXE = 2202;
        public const int AXE = 2203;
        public const int HAMMER = 2204;

        // 음식 (2300~2399)
        public const int BREAD = 2300;
        public const int COOKED_MEAT = 2301;
        public const int SOUP = 2302;

        // 포션 (2400~2499)
        public const int HEALTH_POTION = 2400;
        public const int MANA_POTION = 2401;
        public const int STAMINA_POTION = 2402;

        public const int MIN = 2000;
        public const int MAX = 2999;

        public static bool IsValid(int id) => id >= MIN && id <= MAX;
    }

    #endregion

    #region 건물 ID (3000~3999)

    /// <summary>
    /// 건물 ID 범위 (3000~3999)
    /// </summary>
    public static class Buildings
    {
        // 생산 건물 (3000~3099)
        public const int SAWMILL = 3000;
        public const int FORGE = 3001;
        public const int SMELTER = 3002;
        public const int ALCHEMY_TABLE = 3003;
        public const int WINDMILL = 3004;
        public const int LOOM = 3005;
        public const int TANNERY = 3006;

        // 저장 건물 (3100~3199)
        public const int WOODEN_CHEST = 3100;
        public const int IRON_CHEST = 3101;
        public const int WAREHOUSE = 3102;
        public const int ARMORY = 3103;             // 장비 보관소 (2x2, 장착/해제 접근 지점)

        // 주거 건물 (3200~3299)
        public const int WOODEN_HOUSE = 3200;
        public const int STONE_HOUSE = 3201;
        public const int BARRACKS = 3202;

        // 농업 건물 (3300~3399)
        public const int FARM = 3300;
        public const int BARN = 3301;
        public const int GREENHOUSE = 3302;
        public const int HYDROPONICS = 3303;        // 수경 재배기 (전력 소비, 4x1)

        // 전력 건물 (3400~3499) — PowerBuildingType enum과 값 일치
        public const int WIND_GENERATOR = 3400;     // 풍력 발전기 (무한, 4x2)
        public const int POWER_BATTERY = 3401;      // 축전기 (1x2)
        public const int POWER_WIRE = 3402;         // 전선 (1x1, 겹쳐 설치)
        public const int WOOD_GENERATOR = 3403;     // 나무 화력 발전기 (연료, 2x2)
        public const int EROSION_GENERATOR = 3404;  // 침식 융해 발전기 (2x2)

        // 오락 건물 (3500~3599) — RecreationBuildingType enum과 값 일치
        public const int DART_BOARD = 3500;         // 다트판 (1x2, 무전력)
        public const int ARCADE_MACHINE = 3501;     // 게임기 (2x2, 전력 소비)

        // 위생 건물 (3600~3699) — WashBuildingType enum과 값 일치
        public const int SMALL_WASH_STATION  = 3600; // 간이 세척대 (4x3, 무전력, 동시 1명)
        public const int MEDIUM_WASH_STATION = 3601; // 세척실 (6x3, 전력 소비, 동시 2명)
        public const int LARGE_WASH_STATION  = 3602; // 정화 세척실 (8x3, 전력 소비, 동시 4명)

        public const int MIN = 3000;
        public const int MAX = 3999;

        public static bool IsValid(int id) => id >= MIN && id <= MAX;
    }

    #endregion

    #region 직원 ID (4000~4999)

    /// <summary>
    /// 직원/유닛 ID 범위 (4000~4999)
    /// </summary>
    public static class Employees
    {
        public const int WORKER = 4000;
        public const int BUILDER = 4001;
        public const int MINER = 4002;
        public const int FARMER = 4003;

        public const int MIN = 4000;
        public const int MAX = 4999;

        public static bool IsValid(int id) => id >= MIN && id <= MAX;
    }

    #endregion

    #region 레시피 ID (5000~5999)

    /// <summary>
    /// 레시피 ID 범위 (5000~5999)
    /// </summary>
    public static class Recipes
    {
        // 목재 가공 (5000~5099)
        public const int WOODEN_PLANK = 5000;
        public const int WOODEN_BEAM = 5001;
        public const int WOODEN_DOOR = 5002;

        // 금속 가공 (5100~5199)
        public const int IRON_INGOT = 5100;
        public const int STEEL_BAR = 5101;
        public const int IRON_PICKAXE = 5102;

        // 음식 조리 (5300~5399)
        public const int BREAD = 5300;
        public const int COOKED_MEAT = 5301;
        public const int SOUP = 5302;

        // 연금술 (5400~5499)
        public const int HEALTH_POTION = 5400;
        public const int MANA_POTION = 5401;
        public const int STAMINA_POTION = 5402;

        public const int MIN = 5000;
        public const int MAX = 5999;

        public static bool IsValid(int id) => id >= MIN && id <= MAX;
    }

    #endregion

    #region 제노프스 ID (6000~6999)

    /// <summary>
    /// 제노프스 ID 범위 (6000~6999)
    /// </summary>
    public static class Xenops
    {
        // 환경 간섭 (6000~6099)
        public const int ENVIRONMENTAL_MIN = 6000;
        public const int ENVIRONMENTAL_MAX = 6099;

        // 적대적 생명체 (6100~6199)
        public const int HOSTILE_MIN = 6100;
        public const int HOSTILE_MAX = 6199;

        // 잠입체 (6200~6299)
        public const int INFILTRATOR_MIN = 6200;
        public const int INFILTRATOR_MAX = 6299;

        // 장비형 (6300~6399)
        public const int EQUIPMENT_MIN = 6300;
        public const int EQUIPMENT_MAX = 6399;

        public const int MIN = 6000;
        public const int MAX = 6999;

        public static bool IsValid(int id) => id >= MIN && id <= MAX;
    }

    #endregion

    #region 유틸리티

    /// <summary>
    /// ID가 어떤 ScriptableObject 대역에 속하는지 문자열로 반환합니다.
    ///
    /// 타일·바닥 타일·개체는 raw 값 대역(0~)이 서로 겹치므로 여기서 판정하지 않습니다.
    /// 그쪽은 <see cref="DefinitionDatabase.Validate"/>가 목록 단위로 검증합니다.
    /// </summary>
    /// <param name="id">확인할 ID</param>
    /// <returns>타입 문자열 (Item, Building, Employee, Recipe, Xenops, Unknown)</returns>
    public static string GetIDType(int id)
    {
        if (Items.IsValid(id)) return "Item";
        if (Buildings.IsValid(id)) return "Building";
        if (Employees.IsValid(id)) return "Employee";
        if (Recipes.IsValid(id)) return "Recipe";
        if (Xenops.IsValid(id)) return "Xenops";

        return "Unknown";
    }

    /// <summary>
    /// ID가 유효한 범위에 속하는지 검증합니다.
    /// </summary>
    /// <param name="id">검증할 ID</param>
    /// <param name="errorMessage">유효하지 않은 경우 에러 메시지</param>
    /// <returns>유효한 경우 true</returns>
    public static bool ValidateID(int id, out string errorMessage)
    {
        string type = GetIDType(id);

        if (type == "Unknown")
        {
            errorMessage = $"ID {id}는 유효한 범위에 속하지 않습니다!";
            return false;
        }

        errorMessage = "";
        return true;
    }

    /// <summary>
    /// 각 ID 범위의 사용 가능 범위를 콘솔에 출력합니다 (디버그용).
    /// </summary>
    public static void LogNextAvailableIDs()
    {
        Debug.Log("=== 다음 사용 가능한 ID ===");
        Debug.Log($"타일 (Tile, 정의 에셋): {Tiles.MIN} ~ {Tiles.MAX}");
        Debug.Log($"바닥 타일 (FloorTile, 정의 에셋): {FloorTiles.MIN} ~ {FloorTiles.MAX}");
        Debug.Log($"개체 (Entity, 정의 에셋, 신규 권장): {Entities.RECOMMENDED_MIN} ~ {Entities.RECOMMENDED_MAX}");
        Debug.Log($"아이템 (Item): {Items.MIN} ~ {Items.MAX}");
        Debug.Log($"건물 (Building): {Buildings.MIN} ~ {Buildings.MAX}");
        Debug.Log($"직원 (Employee): {Employees.MIN} ~ {Employees.MAX}");
        Debug.Log($"레시피 (Recipe): {Recipes.MIN} ~ {Recipes.MAX}");
        Debug.Log($"제노프스 (Xenops): {Xenops.MIN} ~ {Xenops.MAX}");
    }

    #endregion
}
