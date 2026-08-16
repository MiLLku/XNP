using UnityEngine;

/// <summary>
/// 세이브 파일 버전 마이그레이션
/// 구버전 세이브 파일을 현재 버전으로 변환합니다.
/// </summary>
public static class SaveMigration
{
    // 현재 지원하는 최신 버전
    public const int CURRENT_VERSION = 10;

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
                case 5:
                    data = MigrateV5ToV6(data);
                    break;
                case 6:
                    data = MigrateV6ToV7(data);
                    break;
                case 7:
                    data = MigrateV7ToV8(data);
                    break;
                case 8:
                    data = MigrateV8ToV9(data);
                    break;
                case 9:
                    data = MigrateV9ToV10(data);
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

    /// <summary>
    /// v5 → v6 마이그레이션
    /// 전투 태세(combatStance) 추가: 구 세이브는 JsonUtility가 0(점거)으로 채우므로
    /// 기본 태세인 경계(Guard)로 보정합니다.
    /// </summary>
    private static SaveData MigrateV5ToV6(SaveData data)
    {
        Debug.Log("[SaveMigration] v5 → v6 마이그레이션 시작 (전투 태세)...");

        if (data.employees != null)
        {
            foreach (var employee in data.employees)
            {
                employee.combatStance = (int)CombatStance.Guard;
            }
        }

        data.saveVersion = 6;
        return data;
    }

    /// <summary>
    /// v6 → v7 마이그레이션
    /// 작업 적성·스킬 포인트 추가. 구 세이브에는 적성 기록이 없으므로 빈 목록(전부 Lv.1)으로 두고,
    /// 스킬 포인트 확장 단계도 0(미해금)에서 시작한다.
    /// 이미 해제돼 있던 스킬은 그대로 유지되며 포인트를 소급 차감하지 않는다.
    /// </summary>
    private static SaveData MigrateV6ToV7(SaveData data)
    {
        Debug.Log("[SaveMigration] v6 → v7 마이그레이션 시작 (작업 적성·스킬 포인트)...");

        if (data.employees != null)
        {
            foreach (var employee in data.employees)
            {
                if (employee.workAptitudes == null)
                    employee.workAptitudes = new System.Collections.Generic.List<WorkAptitude.Entry>();
            }
        }

        data.skillPointTierCount = 0;

        data.saveVersion = 7;
        return data;
    }

    /// <summary>
    /// v7 → v8 마이그레이션
    /// 정신 이상 통합: 침식 단계가 굴리던 이상 행동이 정신 이상(EmployeeMental)으로 흡수됐다.
    /// 구 세이브에서 진행 중이던 이상 행동을 activeMentalEvents의 '침식 계열' 항목으로 이관하고,
    /// 구 필드는 초기화한다. 재판정 유예는 0에서 시작한다.
    /// </summary>
    private static SaveData MigrateV7ToV8(SaveData data)
    {
        Debug.Log("[SaveMigration] v7 → v8 마이그레이션 시작 (침식 이상행동 → 정신 이상 통합)...");

        if (data.employees != null)
        {
            int migrated = 0;

            foreach (var employee in data.employees)
            {
                if (employee.activeMentalEvents == null)
                    employee.activeMentalEvents = new System.Collections.Generic.List<MentalEventSaveData>();

                // 진행 중이던 침식 이상 행동을 침식 계열 정신 이상 항목으로 이관
                if (employee.activeAbnormalBehavior != (int)AbnormalBehaviorType.None &&
                    employee.abnormalBehaviorRemainingTime > 0f)
                {
                    employee.activeMentalEvents.Add(new MentalEventSaveData
                    {
                        eventType = (int)MentalEventType.None,
                        abnormalType = employee.activeAbnormalBehavior,
                        remainingTime = employee.abnormalBehaviorRemainingTime,
                        cooldownRemaining = 0f
                    });
                    migrated++;
                }

                employee.activeAbnormalBehavior = (int)AbnormalBehaviorType.None;
                employee.abnormalBehaviorRemainingTime = 0f;
                employee.mentalBreakGraceRemaining = 0f;
            }

            if (migrated > 0)
                Debug.Log($"[SaveMigration] 진행 중이던 침식 이상행동 {migrated}건을 침식 계열 정신 이상으로 이관했습니다.");
        }

        data.saveVersion = 8;
        return data;
    }

    /// <summary>
    /// v8 → v9 마이그레이션
    ///
    /// 두 가지 큰 규칙 변경을 함께 반영한다.
    ///   1. <b>정신력이 기본값 기준 모디파이어 방식으로 전환</b> — 더 이상 영구 차감되는 절대 수치가 아니다.
    ///      구 세이브의 currentMental은 "그동안 깎여서 남은 값"이라 새 체계로 그대로 옮길 수 없으므로,
    ///      기본값(50)에서 모디파이어 없이 시작한다. 굶주림·탈진 상태라면 다음 프레임에 상태형
    ///      모디파이어가 자동으로 다시 붙는다.
    ///   2. <b>공격력이 직원 스탯에서 제거</b> — 전투력은 무기와 근접/원거리 숙련이 결정한다.
    ///      구 attackPower를 초기 숙련 레벨로 환산해 성장분을 일부 보존한다 (기본 10 = Lv.1).
    /// </summary>
    private static SaveData MigrateV8ToV9(SaveData data)
    {
        Debug.Log("[SaveMigration] v8 → v9 마이그레이션 시작 (정신력 모디파이어 · 전투 숙련)...");

        if (data.employees != null)
        {
            foreach (var employee in data.employees)
            {
                // ── 1. 정신력 ──
                employee.baseMental = DEFAULT_BASE_MENTAL;
                employee.mentalModifiers = new System.Collections.Generic.List<MentalModifierSaveData>();
                // currentMental은 복원 시 baseMental + 모디파이어로 재계산되므로 표시용으로만 맞춰둔다
                employee.currentMental = DEFAULT_BASE_MENTAL;

                // ── 2. 전투 숙련 ──
                // 실제 숙련 레벨은 combatAptitudes가 들고 있다 (복원 시 이 목록이 최종 반영된다).
                int meleeLevel = AttackPowerToSkillLevel(employee.attackPower);

                // 무작위 생성 직원은 템플릿을 스냅샷에서 재구성하므로 초기 레벨도 맞춰둔다
                if (employee.generated != null && employee.generated.isGenerated)
                {
                    employee.generated.initialMeleeLevel  = meleeLevel;
                    employee.generated.initialRangedLevel = 1;   // 구 세이브엔 원거리 개념이 없었다
                }

                if (employee.combatAptitudes == null)
                    employee.combatAptitudes = new System.Collections.Generic.List<CombatAptitude.Entry>();

                employee.combatAptitudes.Clear();
                employee.combatAptitudes.Add(new CombatAptitude.Entry
                {
                    skillType = CombatSkillType.Melee, level = meleeLevel, experience = 0
                });
                employee.combatAptitudes.Add(new CombatAptitude.Entry
                {
                    skillType = CombatSkillType.Ranged, level = 1, experience = 0
                });

                employee.attackPower = 0;   // 구 필드 비움
            }

            Debug.Log($"[SaveMigration] 직원 {data.employees.Count}명: 정신력을 기본값 {DEFAULT_BASE_MENTAL}으로 재설정하고 " +
                      "구 공격력을 근접 숙련으로 환산했습니다.");
        }

        data.saveVersion = 9;
        return data;
    }

    /// <summary>
    /// v9 → v10 마이그레이션
    ///
    /// 침식 회복 규칙 개편. 회복 경로가 <b>자연 회복(하한까지) · 세척 시설 · 무작위 이벤트</b> 셋으로 줄고,
    /// 휴식·포스트 레이드 가속 회복이 제거됐다. 또 침식이 어디서 왔는지 출처별 내역을 기록하기 시작한다.
    ///
    /// 이 마이그레이션이 <b>반드시 필요한 이유</b>는 자연 회복 하한 때문이다.
    /// runtimeFloorReduction이 JsonUtility 기본값 0으로 채워지는 것 자체는 맞지만,
    /// 구 세이브에는 erosionSystem 블록이 아예 없을 수 있어 명시적으로 초기화해야
    /// "하한 없음"으로 오해되는 상태를 막을 수 있다.
    /// (침식 내역은 비어 있어도 무해하다 — 총량 erosionLevel은 그대로 유지된다)
    /// </summary>
    private static SaveData MigrateV9ToV10(SaveData data)
    {
        Debug.Log("[SaveMigration] v9 → v10 마이그레이션 시작 (침식 회복 규칙 · 출처 내역)...");

        // 침식 시스템 전역 상태 — 구 포스트레이드 필드는 사라지고 하한 감소량이 들어온다
        if (data.erosionSystem == null)
            data.erosionSystem = new ErosionSystemSaveData();

        data.erosionSystem.runtimeFloorReduction = 0f;

        if (data.employees != null)
        {
            int withErosion = 0;

            foreach (var employee in data.employees)
            {
                if (employee.erosionSources == null)
                    employee.erosionSources = new System.Collections.Generic.List<ErosionSourceEntry>();

                employee.erosionSources.Clear();

                // 기존 침식 총량은 유지하되 출처를 알 수 없으므로 한 줄로 이관한다
                if (employee.erosionLevel > 0f)
                {
                    employee.erosionSources.Add(new ErosionSourceEntry
                    {
                        sourceKey = ErosionSource.UNKNOWN,
                        displayName = "이전 기록 (출처 미상)",
                        amount = employee.erosionLevel
                    });
                    withErosion++;
                }
            }

            if (withErosion > 0)
                Debug.Log($"[SaveMigration] 침식이 남아 있던 직원 {withErosion}명의 수치를 '출처 미상' 내역으로 이관했습니다.");
        }

        data.saveVersion = 10;
        return data;
    }

    /// <summary>v8 이하의 기본 최대 정신력이 100이었으므로 새 기본값은 그 절반인 50으로 잡는다.</summary>
    private const int DEFAULT_BASE_MENTAL = 50;

    /// <summary>
    /// 구 공격력을 근접 숙련 레벨로 환산합니다.
    /// 기본 공격력 10 = Lv.1이고, 5씩 오를 때마다 1레벨씩 올려 상한 10에서 멈춥니다.
    /// </summary>
    private static int AttackPowerToSkillLevel(int attackPower)
    {
        if (attackPower <= 10) return 1;
        return Mathf.Clamp(1 + (attackPower - 10) / 5, 1, CombatAptitude.MAX_LEVEL);
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
