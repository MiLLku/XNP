using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 이벤트 효과 적용기
/// 각 효과 타입에 따라 게임 상태를 변경합니다.
/// </summary>
public static class EventEffectApplier
{
    /// <summary>
    /// 효과 목록 적용
    /// </summary>
    public static void ApplyEffects(List<EventEffect> effects)
    {
        if (effects == null) return;

        foreach (var effect in effects)
        {
            ApplyEffect(effect);
        }
    }

    /// <summary>
    /// 개별 효과 적용
    /// </summary>
    public static void ApplyEffect(EventEffect effect)
    {
        switch (effect.type)
        {
            // ===== 직원 스탯 =====
            case EffectType.ModifyHealth:
                ModifyEmployeeStat(effect.target, effect.value,
                    (e, v) => e.ModifyHealth(v), (int)effect.value);
                break;

            case EffectType.ModifyMental:
                ModifyEmployeeStat(effect.target, effect.value,
                    (e, v) => e.ModifyMental(v), (int)effect.value);
                break;

            case EffectType.ModifyHunger:
                ModifyEmployeeStat(effect.target, effect.value,
                    (e, v) => e.ModifyHunger(v), effect.value);
                break;

            case EffectType.ModifyFatigue:
                ModifyEmployeeStat(effect.target, effect.value,
                    (e, v) => e.ModifyFatigue(v), effect.value);
                break;

            // ===== 자원 =====
            case EffectType.AddItem:
                AddItem(effect.targetId, (int)effect.value);
                break;

            case EffectType.RemoveItem:
                RemoveItem(effect.targetId, (int)effect.value);
                break;

            // ===== 직원 관리 =====
            case EffectType.SpawnEmployee:
                SpawnEmployee(effect.targetId);
                break;

            case EffectType.RemoveRandomEmployee:
                RemoveRandomEmployee((int)effect.value);
                break;

            // ===== 건물 =====
            case EffectType.DamageRandomBuilding:
                DamageRandomBuilding((int)effect.value);
                break;

            case EffectType.DestroyRandomBuilding:
                DestroyRandomBuilding((int)effect.value);
                break;

            // ===== 맵 =====
            case EffectType.DestroyRandomTiles:
                DestroyRandomTiles((int)effect.value);
                break;

            // ===== 작업 =====
            case EffectType.ModifyWorkSpeed:
                ModifyGlobalWorkSpeed(effect.value);
                break;

            case EffectType.PauseAllWork:
                PauseAllWork((int)effect.value > 0);
                break;

            // ===== 특수 =====
            case EffectType.TriggerEvent:
                TriggerEvent(effect.targetId);
                break;

            case EffectType.EndPersistentEvent:
                EndPersistentEvent(effect.targetId);
                break;

            // ===== 메시지 =====
            case EffectType.ShowNotification:
                ShowNotification(effect.description);
                break;

            // ===== 제노프스 =====
            case EffectType.SpawnXenops:
                SpawnXenopsNearCamera(effect.targetId);
                break;

            case EffectType.SpawnXenopsInFog:
                SpawnXenopsInFogArea(effect.targetId);
                break;

            default:
                Debug.LogWarning($"[EventEffectApplier] 알 수 없는 효과 타입: {effect.type}");
                break;
        }
    }

    #region 직원 스탯 수정

    private static void ModifyEmployeeStat(EffectTarget target, float value,
        System.Action<Employee, float> modifyAction, float modifyValue)
    {
        var employees = GetTargetEmployees(target, (int)value);

        foreach (var employee in employees)
        {
            if (employee != null)
            {
                modifyAction(employee, modifyValue);
            }
        }
    }

    private static List<Employee> GetTargetEmployees(EffectTarget target, int count = 1)
    {
        if (EmployeeManager.instance == null)
            return new List<Employee>();

        var allEmployees = EmployeeManager.instance.AllEmployees;
        if (allEmployees == null || allEmployees.Count == 0)
            return new List<Employee>();

        switch (target)
        {
            case EffectTarget.AllEmployees:
                return allEmployees.ToList();

            case EffectTarget.RandomEmployee:
                var random = allEmployees[Random.Range(0, allEmployees.Count)];
                return new List<Employee> { random };

            case EffectTarget.RandomEmployees:
                return allEmployees.OrderBy(_ => Random.value).Take(count).ToList();

            case EffectTarget.LowestHealthEmployee:
                return new List<Employee> { allEmployees.OrderBy(e => e.Stats.health).First() };

            case EffectTarget.LowestMentalEmployee:
                return new List<Employee> { allEmployees.OrderBy(e => e.Stats.mental).First() };

            case EffectTarget.HighestFatigueEmployee:
                return new List<Employee> { allEmployees.OrderByDescending(e => e.Needs.fatigue).First() };

            case EffectTarget.WorkingEmployees:
                return allEmployees.Where(e => e.State == EmployeeState.Working).ToList();

            case EffectTarget.IdleEmployees:
                return allEmployees.Where(e => e.State == EmployeeState.Idle).ToList();

            default:
                return new List<Employee>();
        }
    }

    #endregion

    #region 자원 관리

    private static void AddItem(int itemId, int amount)
    {
        if (InventoryManager.instance == null || GameDatabase.Instance == null) return;

        ItemData itemData = GameDatabase.Instance.GetItemData(itemId);
        if (itemData != null)
        {
            InventoryManager.instance.AddItem(itemData, amount);
            Debug.Log($"[EventEffectApplier] 아이템 추가: {itemData.itemName} x{amount}");
        }
    }

    private static void RemoveItem(int itemId, int amount)
    {
        if (InventoryManager.instance == null || GameDatabase.Instance == null) return;

        ItemData itemData = GameDatabase.Instance.GetItemData(itemId);
        if (itemData != null)
        {
            InventoryManager.instance.RemoveItem(itemData, amount);
            Debug.Log($"[EventEffectApplier] 아이템 제거: {itemData.itemName} x{amount}");
        }
    }

    #endregion

    #region 직원 관리

    private static void SpawnEmployee(int employeeDataId)
    {
        if (EmployeeManager.instance == null || GameDatabase.Instance == null) return;

        EmployeeData employeeData = GameDatabase.Instance.GetEmployeeData(employeeDataId);
        if (employeeData != null)
        {
            EmployeeManager.instance.SpawnEmployee(employeeData);
            Debug.Log($"[EventEffectApplier] 직원 스폰: {employeeData.employeeName}");
        }
    }

    private static void RemoveRandomEmployee(int count)
    {
        if (EmployeeManager.instance == null) return;

        var employees = EmployeeManager.instance.AllEmployees
            .OrderBy(_ => Random.value)
            .Take(count)
            .ToList();

        foreach (var employee in employees)
        {
            Debug.Log($"[EventEffectApplier] 직원 제거: {employee.Data?.employeeName}");
            EmployeeManager.instance.RemoveEmployee(employee);
        }
    }

    #endregion

    #region 건물 관리

    private static void DamageRandomBuilding(int damage)
    {
        var buildings = UnityEngine.Object.FindObjectsByType<Building>(FindObjectsSortMode.None);
        if (buildings.Length == 0) return;

        var target = buildings[Random.Range(0, buildings.Length)];
        target.TakeDamage(damage);
        Debug.Log($"[EventEffectApplier] 건물 피해: {target.buildingData?.buildingName} -{damage}");
    }

    private static void DestroyRandomBuilding(int count)
    {
        var buildings = UnityEngine.Object.FindObjectsByType<Building>(FindObjectsSortMode.None)
            .OrderBy(_ => Random.value)
            .Take(count)
            .ToList();

        foreach (var building in buildings)
        {
            Debug.Log($"[EventEffectApplier] 건물 파괴: {building.buildingData?.buildingName}");
            UnityEngine.Object.Destroy(building.gameObject);
        }
    }

    #endregion

    #region 맵 관리

    private static void DestroyRandomTiles(int count)
    {
        if (MapGenerator.instance == null) return;

        GameMap gameMap = MapGenerator.instance.GameMapInstance;
        MapRenderer mapRenderer = MapGenerator.instance.MapRendererInstance;

        int destroyed = 0;
        int maxAttempts = count * 10;
        int attempts = 0;

        while (destroyed < count && attempts < maxAttempts)
        {
            int x = Random.Range(0, GameMap.MAP_WIDTH);
            int y = Random.Range(0, GameMap.MAP_HEIGHT);

            // 공기가 아닌 타일만 파괴
            if (gameMap.TileGrid[x, y] != 0)
            {
                gameMap.SetTile(x, y, 0);
                mapRenderer?.UpdateTileVisual(x, y);
                destroyed++;
            }

            attempts++;
        }

        Debug.Log($"[EventEffectApplier] 타일 파괴: {destroyed}개");
    }

    #endregion

    #region 작업 관리

    private static void ModifyGlobalWorkSpeed(float modifier)
    {
        // 전역 작업 속도 조절: TimeManager 배속으로 근사치 구현
        // (정밀한 per-employee modifier 시스템은 EmployeeStatsController 확장 시 교체)
        if (TimeManager.instance == null)
        {
            Debug.LogWarning("[EventEffectApplier] TimeManager 없음 — 작업 속도 수정 무시");
            return;
        }

        if (TimeManager.instance.IsPaused) return; // 정지 중에는 변경 안 함

        if (modifier <= -0.3f)
        {
            // 큰 둔화 → 1배속 강제
            TimeManager.instance.SetSpeedToNormal();
        }
        else if (modifier >= 0.3f)
        {
            // 큰 가속 → 2배속
            TimeManager.instance.SetSpeed(2);
        }

        Debug.Log($"[EventEffectApplier] 작업 속도 수정: {modifier:+0.##;-0.##}");
    }

    private static void PauseAllWork(bool pause)
    {
        // 활성 WorkOrder 전체 일시정지/재개
        if (WorkSystemManager.instance == null)
        {
            Debug.LogWarning("[EventEffectApplier] WorkSystemManager 없음 — 작업 일시정지 무시");
            return;
        }

        foreach (var order in WorkSystemManager.instance.AllOrders)
        {
            if (pause) order.Pause();
            else       order.Resume();
        }

        Debug.Log($"[EventEffectApplier] 전체 작업 {(pause ? "일시정지" : "재개")} ({WorkSystemManager.instance.AllOrders.Count}개)");
    }

    #endregion

    #region 특수

    private static void TriggerEvent(int eventId)
    {
        if (EventManager.instance != null)
        {
            EventManager.instance.TriggerEvent(eventId);
        }
    }

    private static void EndPersistentEvent(int eventId)
    {
        if (EventManager.instance != null)
        {
            EventManager.instance.EndPersistentEvent(eventId);
        }
    }

    private static void ShowNotification(string message)
    {
        // UIManager에 알림 패널이 등록되어 있으면 표시, 없으면 콘솔 출력
        // (향후 Toast/Notification 전용 패널 추가 후 UIManager.ShowNotification() 호출로 교체)
        Debug.Log($"[이벤트 알림] {message}");
    }

    private static void SpawnXenopsNearCamera(int xenopsDataId)
    {
        if (XenopsManager.instance == null)
        {
            Debug.LogWarning("[EventEffectApplier] XenopsManager가 없습니다.");
            return;
        }

        // 카메라 근처 랜덤 위치에 등장
        Vector3 spawnPos = Vector3.zero;
        if (Camera.main != null)
        {
            Vector3 camPos = Camera.main.transform.position;
            float offsetX = UnityEngine.Random.Range(-8f, 8f);
            float offsetY = UnityEngine.Random.Range(-3f, 3f);
            spawnPos = new Vector3(camPos.x + offsetX, camPos.y + offsetY, 0f);
        }

        XenopsManager.instance.SpawnXenops(xenopsDataId, spawnPos);
        Debug.Log($"[EventEffectApplier] 제노프스 등장: ID {xenopsDataId} at {spawnPos}");
    }

    /// <summary>
    /// 안개(미공개) 영역의 지면 위 임의 위치에 제노프스를 스폰합니다.
    /// 조건: FogOfWar 미공개 + 해당 타일 공기(0) + 바로 아래 타일 고체.
    /// 기지 근처(건설물·직원 반경 NEAR_BASE_RADIUS) 안개 타일을 우선 선택하여
    /// 위협이 기지를 향하도록 합니다. 기지 근처 후보가 없으면 전체 후보, 그래도
    /// 없으면 카메라 근처로 폴백합니다.
    /// </summary>
    private static void SpawnXenopsInFogArea(int xenopsDataId)
    {
        if (XenopsManager.instance == null)
        {
            Debug.LogWarning("[EventEffectApplier] XenopsManager가 없습니다.");
            return;
        }

        var mapGen = MapGenerator.instance;
        var fogMgr = FogOfWarManager.instance;

        if (mapGen == null || fogMgr == null)
        {
            SpawnXenopsNearCamera(xenopsDataId);
            return;
        }

        var gameMap = mapGen.GameMapInstance;
        if (gameMap == null)
        {
            SpawnXenopsNearCamera(xenopsDataId);
            return;
        }

        // ── 안개 속 지면 위 후보 타일 수집 ────────────────────────
        var candidates = new List<Vector2Int>();
        for (int x = 0; x < GameMap.MAP_WIDTH; x++)
        {
            for (int y = 1; y < GameMap.MAP_HEIGHT; y++)
            {
                if (fogMgr.IsRevealed(x, y))       continue; // 이미 보임 → 제외
                if (gameMap.TileGrid[x, y] != 0)   continue; // 공기 아님 → 제외
                if (gameMap.TileGrid[x, y - 1] == 0) continue; // 지면 없음 → 제외
                candidates.Add(new Vector2Int(x, y));
            }
        }

        if (candidates.Count == 0)
        {
            Debug.Log("[EventEffectApplier] 안개 속 스폰 후보 없음 → 카메라 근처 폴백");
            SpawnXenopsNearCamera(xenopsDataId);
            return;
        }

        // ── 기지 앵커 수집: 건설물 + 살아있는 직원 위치 ──────────────
        // "기지 근처" = 플레이어의 건설물·직원이 모여 있는 영역.
        var anchors = new List<Vector2>();

        var buildings = UnityEngine.Object.FindObjectsByType<Building>(FindObjectsSortMode.None);
        foreach (var b in buildings)
        {
            if (b != null) anchors.Add(b.transform.position);
        }

        if (EmployeeManager.instance != null)
        {
            foreach (var emp in EmployeeManager.instance.AllEmployees)
            {
                if (emp != null && emp.State != EmployeeState.Dead)
                    anchors.Add(emp.transform.position);
            }
        }

        // ── 기지 앵커 근처(반경 NEAR_BASE_RADIUS) 안개 후보 우선 선별 ──
        const float NEAR_BASE_RADIUS = 16f;
        var nearCandidates = new List<Vector2Int>();
        if (anchors.Count > 0)
        {
            float sqrRadius = NEAR_BASE_RADIUS * NEAR_BASE_RADIUS;
            foreach (var cand in candidates)
            {
                Vector2 candWorld = new Vector2(cand.x + 0.5f, cand.y + 0.5f);
                foreach (var anchor in anchors)
                {
                    if ((anchor - candWorld).sqrMagnitude <= sqrRadius)
                    {
                        nearCandidates.Add(cand);
                        break;
                    }
                }
            }
        }

        // 기지 근처 후보가 있으면 그쪽에서, 없으면 전체 후보에서 선택
        var pool = nearCandidates.Count > 0 ? nearCandidates : candidates;

        // ── 랜덤 후보 선택 → 월드 좌표 변환 ──────────────────────
        var pick = pool[UnityEngine.Random.Range(0, pool.Count)];

        Vector3 spawnPos;
        Tilemap tm = mapGen.MapRendererInstance?.MainTilemap;
        if (tm != null)
        {
            spawnPos = tm.GetCellCenterWorld(new Vector3Int(pick.x, pick.y, 0));
        }
        else
        {
            // 타일맵 참조 불가 시 1:1 좌표 가정 (픽셀당유닛=1)
            spawnPos = new Vector3(pick.x + 0.5f, pick.y + 0.5f, 0f);
        }

        // GameDatabase.Instance 없이도 동작하도록 XenopsData 직접 전달 오버로드 사용
        XenopsData xenopsData = null;
        if (GameDatabase.Instance != null)
        {
            xenopsData = GameDatabase.Instance.GetXenopsData(xenopsDataId);
        }
        else
        {
            // GameDatabase 미초기화 폴백: allXenopsData 직접 탐색
            var db = Resources.FindObjectsOfTypeAll<GameDatabase>();
            if (db.Length > 0)
                xenopsData = db[0].allXenopsData?.Find(x => x != null && x.xenopsID == xenopsDataId);
        }

        if (xenopsData == null)
        {
            Debug.LogWarning($"[EventEffectApplier] XenopsData 없음 (ID {xenopsDataId}). GameDatabase에 등록했는지 확인하세요.");
            return;
        }

        XenopsManager.instance.SpawnXenops(xenopsData, spawnPos);
        Debug.Log($"[EventEffectApplier] 안개 속 제노프스 등장: {xenopsData.xenopsName} at tile({pick.x},{pick.y}) world{spawnPos}");
    }

    #endregion
}
