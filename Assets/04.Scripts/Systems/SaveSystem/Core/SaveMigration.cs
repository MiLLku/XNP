using UnityEngine;

/// <summary>
/// 세이브 파일 버전 마이그레이션
/// 구버전 세이브 파일을 현재 버전으로 변환합니다.
/// </summary>
public static class SaveMigration
{
    // 현재 지원하는 최신 버전
    public const int CURRENT_VERSION = 5;

    /// <summary>
    /// 세이브 데이터를 현재 버전으로 마이그레이션합니다.
    /// </summary>
    public static SaveData Migrate(SaveData data)
    {
        if (data == null)
        {
            Debug.LogError("[SaveMigration] 마이그레이션할 데이터가 없습니다.");
            return null;
        }

        int originalVersion = data.saveVersion;

        // 버전별 순차 마이그레이션
        while (data.saveVersion < CURRENT_VERSION)
        {
            switch (data.saveVersion)
            {
                case 0:
                    data = MigrateV0ToV1(data);
                    break;
                case 1:
                    data = MigrateV1ToV2(data);
                    break;
                case 2:
                    data = MigrateV2ToV3(data);
                    break;
                case 3:
                    data = MigrateV3ToV4(data);
                    break;
                case 4:
                    data = MigrateV4ToV5(data);
                    break;
                default:
                    Debug.LogError($"[SaveMigration] 알 수 없는 버전: {data.saveVersion}");
                    return null;
            }
        }

        if (originalVersion != data.saveVersion)
        {
            Debug.Log($"[SaveMigration] 마이그레이션 완료: v{originalVersion} → v{data.saveVersion}");
        }

        return data;
    }

    /// <summary>
    /// v0 → v1 마이그레이션
    /// </summary>
    private static SaveData MigrateV0ToV1(SaveData data)
    {
        Debug.Log("[SaveMigration] v0 → v1 마이그레이션 시작...");

        // v1에서 추가된 필드들에 기본값 설정
        if (data.employees != null)
        {
            foreach (var employee in data.employees)
            {
                // 예: 새로운 필드가 추가되었을 때 기본값 설정
                if (employee.workPriorities == null)
                {
                    employee.workPriorities = new System.Collections.Generic.List<WorkPrioritySaveData>();
                }
            }
        }

        if (data.nextInstanceId <= 0)
        {
            data.nextInstanceId = 1;
        }

        data.saveVersion = 1;
        return data;
    }

    /// <summary>
    /// v1 → v2 마이그레이션
    /// ISaveModule 기반 구조 전환, 예약 데이터 추가
    /// </summary>
    private static SaveData MigrateV1ToV2(SaveData data)
    {
        Debug.Log("[SaveMigration] v1 → v2 마이그레이션 시작...");

        // InventorySaveData에 예약 필드 추가
        if (data.inventory != null)
        {
            if (data.inventory.reservations == null)
            {
                data.inventory.reservations = new System.Collections.Generic.List<ReservationSaveData>();
            }
            if (data.inventory.nextReservationId <= 0)
            {
                data.inventory.nextReservationId = 1;
            }
        }

        // ConstructionSiteSaveData에 reservationId 기본값
        if (data.constructionSites != null)
        {
            foreach (var site in data.constructionSites)
            {
                if (site.reservationId == 0)
                {
                    site.reservationId = -1;
                }
            }
        }

        data.saveVersion = 2;
        return data;
    }

    /// <summary>
    /// v2 → v3 마이그레이션
    /// TileType enum 재배열 대응: 맵 타일 그리드의 raw int 값을 새 번호 체계로 변환.
    ///
    /// v2 시절 번호: Air=0, Dirt=1, GrassDirt=2, Stone=3, IronOre=4, CopperOre=5, SilverOre=6, GoldOre=7
    /// v3(현재) 번호: Air=0, Dirt=1, Stone=2, CopperOre=3, IronOre=4, GoldOre=5, GrassDirt=6,
    ///               ProcessedDirt=7, Ladder=8, Coal=9, SilverOre=10, Crystal=11
    ///
    /// 주의: enum 재배열과 버전 스탬프 사이 기간에 만들어진 세이브는 이미 새 번호를 쓰고
    /// 있을 수 있다. 새 체계에서만 존재하는 값(8~11)이 발견되면 이미 변환된 것으로 보고
    /// 리매핑을 건너뛴다 (은 광석=10은 일반 맵 생성에 거의 항상 포함되므로 신뢰할 만한 지표).
    /// </summary>
    private static SaveData MigrateV2ToV3(SaveData data)
    {
        Debug.Log("[SaveMigration] v2 → v3 마이그레이션 시작 (TileType 재배열)...");

        if (data.map != null)
        {
            bool looksLikeNewScheme =
                ContainsValueInRange(data.map.tileGrid, 8, 11) ||
                ContainsValueInRange(data.map.wallGrid, 8, 11);

            if (looksLikeNewScheme)
            {
                Debug.LogWarning("[SaveMigration] 타일 값 8~11 발견 — 이미 새 TileType 체계로 저장된 세이브로 판단, 리매핑 생략");
            }
            else
            {
                RemapTileValues(data.map.tileGrid);
                RemapTileValues(data.map.wallGrid);
                Debug.Log("[SaveMigration] 타일 그리드 리매핑 완료 (구 enum → 신 enum)");
            }
        }

        data.saveVersion = 3;
        return data;
    }

    /// <summary>
    /// v3 → v4 마이그레이션
    /// 재미(fun) 욕구 추가: 구 세이브에는 필드가 없어 JsonUtility가 0으로 채우므로
    /// 기본값 70으로 보정합니다 (로드 직후 전 직원이 침식 취약 상태가 되는 것 방지).
    /// </summary>
    private static SaveData MigrateV3ToV4(SaveData data)
    {
        Debug.Log("[SaveMigration] v3 → v4 마이그레이션 시작 (재미 욕구 추가)...");

        if (data.employees != null)
        {
            foreach (var employee in data.employees)
            {
                employee.fun = 70f;
            }
        }

        data.saveVersion = 4;
        return data;
    }

    /// <summary>
    /// v4 → v5 마이그레이션
    /// 장비 시스템 확장: 필수 소지 식량 기본 1 보정(기존 동작 유지),
    /// 장착 장비 내구도는 EmployeeEquipment 복원부에서 0 이하 → 최대치로 보정하므로 별도 처리 불필요.
    /// </summary>
    private static SaveData MigrateV4ToV5(SaveData data)
    {
        Debug.Log("[SaveMigration] v4 → v5 마이그레이션 시작 (장비/필수 소지)...");

        if (data.employees != null)
        {
            foreach (var employee in data.employees)
            {
                employee.desiredFoodCount = 1; // 기존 '식량 1개 유도' 동작 유지
                employee.desiredDrugCount = 0;
            }
        }

        data.saveVersion = 5;
        return data;
    }

    /// <summary>v2 TileType 값 → v3 TileType 값 변환표를 grid 전체에 적용합니다.</summary>
    private static void RemapTileValues(int[] grid)
    {
        if (grid == null) return;

        for (int i = 0; i < grid.Length; i++)
        {
            switch (grid[i])
            {
                case 2:  grid[i] = 6;  break; // GrassDirt
                case 3:  grid[i] = 2;  break; // Stone
                case 5:  grid[i] = 3;  break; // CopperOre
                case 6:  grid[i] = 10; break; // SilverOre
                case 7:  grid[i] = 5;  break; // GoldOre
                // 0(Air), 1(Dirt), 4(IronOre), 99(Special)는 동일
            }
        }
    }

    /// <summary>grid에 [min, max] 범위의 값이 하나라도 있는지 확인합니다.</summary>
    private static bool ContainsValueInRange(int[] grid, int min, int max)
    {
        if (grid == null) return false;

        for (int i = 0; i < grid.Length; i++)
        {
            if (grid[i] >= min && grid[i] <= max) return true;
        }
        return false;
    }
}
