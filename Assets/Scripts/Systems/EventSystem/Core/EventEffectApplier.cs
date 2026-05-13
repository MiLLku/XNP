using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
        // TODO: 전역 작업 속도 수정 시스템 구현 필요
        Debug.Log($"[EventEffectApplier] 작업 속도 수정: {modifier:+0.##;-0.##}");
    }

    private static void PauseAllWork(bool pause)
    {
        // TODO: 작업 일시정지 시스템 구현 필요
        Debug.Log($"[EventEffectApplier] 작업 일시정지: {pause}");
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
        Debug.Log($"[EventEffectApplier] 알림: {message}");
        // TODO: UI 알림 시스템과 연동
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

    #endregion
}
