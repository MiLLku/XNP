using UnityEngine;

/// <summary>
/// 세이브 파일 버전 마이그레이션
/// 구버전 세이브 파일을 현재 버전으로 변환합니다.
/// </summary>
public static class SaveMigration
{
    // 현재 지원하는 최신 버전
    public const int CURRENT_VERSION = 2;

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
}
