using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 장비 보관소 전역 매니저.
/// 미장착 장비 인스턴스 풀을 관리한다 (전역 풀 — 인벤토리/창고 패턴과 동일).
/// 직원이 장비를 장착/해제하려면 보관소 건물(ArmoryStorage)로 이동해야 한다.
///
/// 제작 연동: 제작 산출물이 장비(GameDatabase.allEquipmentData 등록)면
/// TryAddCraftOutput이 인벤토리 대신 이 풀에 새 인스턴스를 생성한다.
/// </summary>
public class EquipmentStorageManager : DestroySingleton<EquipmentStorageManager>, ISaveModule
{
    [Header("시작 지급")]
    [Tooltip("새 게임 시작 시 지급할 장비 목록 (StartingInventoryConfig.startingEquipment)")]
    [SerializeField] private StartingInventoryConfig startingConfig;

    [Header("디버그")]
    [SerializeField] private bool showDebugLogs = false;

    /// <summary>미장착 장비 인스턴스 풀</summary>
    private readonly List<EquipmentInstance> pool = new List<EquipmentInstance>();

    /// <summary>등록된 보관소 건물들</summary>
    private readonly List<ArmoryStorage> armories = new List<ArmoryStorage>();

    private int nextInstanceId = 1;

    /// <summary>풀 변경 시 발행 (UI 갱신용)</summary>
    public event System.Action OnPoolChanged;

    #region 생명주기

    private void Start()
    {
        GrantStartingEquipment();
    }

    /// <summary>새 게임 시작 지급. 로드 시에는 Restore가 풀을 덮어쓰므로 새 게임에만 유효.</summary>
    private void GrantStartingEquipment()
    {
        if (startingConfig == null || startingConfig.startingEquipment == null) return;

        foreach (var entry in startingConfig.startingEquipment)
        {
            if (entry.equipment == null || entry.amount <= 0) continue;
            for (int i = 0; i < entry.amount; i++)
                CreateInstance(entry.equipment);
        }

        if (showDebugLogs)
            Debug.Log($"[EquipmentStorage] 시작 장비 지급 완료 (풀 {pool.Count}개)");
    }

    #endregion

    #region 보관소 등록

    public void RegisterArmory(ArmoryStorage armory)
    {
        if (armory != null && !armories.Contains(armory)) armories.Add(armory);
    }

    public void UnregisterArmory(ArmoryStorage armory)
    {
        armories.Remove(armory);
    }

    /// <summary>가동 중인 보관소가 하나라도 있는지</summary>
    public bool HasArmory()
    {
        return armories.Any(a => a != null && a.IsOperating);
    }

    /// <summary>가장 가까운 가동 중인 보관소</summary>
    public ArmoryStorage GetNearestArmory(Vector2 position)
    {
        return armories
            .Where(a => a != null && a.IsOperating)
            .OrderBy(a => Vector2.Distance(position, a.transform.position))
            .FirstOrDefault();
    }

    #endregion

    #region 풀 관리

    /// <summary>새 장비 인스턴스를 생성해 풀에 추가합니다 (내구도 = 최대).</summary>
    public EquipmentInstance CreateInstance(EquipmentData data)
    {
        if (data == null || data.itemData == null) return null;

        var inst = new EquipmentInstance
        {
            instanceId = nextInstanceId++,
            equipmentId = data.itemData.itemID,
            durability = data.maxDurability
        };
        pool.Add(inst);
        OnPoolChanged?.Invoke();
        return inst;
    }

    /// <summary>기존 인스턴스를 풀에 반납합니다 (장착 해제 시).</summary>
    public void ReturnInstance(EquipmentInstance inst)
    {
        if (inst == null) return;
        pool.Add(inst);
        OnPoolChanged?.Invoke();
    }

    /// <summary>풀에서 인스턴스를 꺼냅니다 (장착 시). 없으면 null.</summary>
    public EquipmentInstance TakeInstance(int instanceId)
    {
        var inst = pool.FirstOrDefault(i => i.instanceId == instanceId);
        if (inst != null)
        {
            pool.Remove(inst);
            OnPoolChanged?.Invoke();
        }
        return inst;
    }

    /// <summary>풀 전체 (읽기 전용)</summary>
    public IReadOnlyList<EquipmentInstance> Pool => pool;

    /// <summary>특정 슬롯에 장착 가능한 풀 인스턴스 목록</summary>
    public List<EquipmentInstance> GetPoolForSlot(EquipmentSlot slot)
    {
        var result = new List<EquipmentInstance>();
        foreach (var inst in pool)
        {
            var data = GameDatabase.Instance?.GetEquipmentData(inst.equipmentId);
            if (data != null && data.slot == slot) result.Add(inst);
        }
        return result;
    }

    /// <summary>
    /// 제작 산출물이 장비면 풀에 인스턴스로 추가합니다.
    /// CraftingTable/ProductionBuilding의 산출 지점에서 호출 — true면 인벤토리 추가를 생략할 것.
    /// </summary>
    public static bool TryAddCraftOutput(ItemData item, int amount)
    {
        if (instance == null || item == null || GameDatabase.Instance == null) return false;

        var equipData = GameDatabase.Instance.GetEquipmentData(item.itemID);
        if (equipData == null) return false;

        for (int i = 0; i < amount; i++)
            instance.CreateInstance(equipData);

        Debug.Log($"[EquipmentStorage] 제작 장비 입고: {item.itemName} x{amount}");
        return true;
    }

    #endregion

    #region ISaveModule

    public int SaveOrder => 45; // 직원(장착 복원)보다 먼저

    public void Capture(SaveData data)
    {
        data.equipmentStorage = new EquipmentStorageSaveData
        {
            instances = new List<EquipmentInstance>(pool),
            nextInstanceId = nextInstanceId
        };
    }

    public void Restore(SaveData data)
    {
        pool.Clear();
        if (data.equipmentStorage != null)
        {
            if (data.equipmentStorage.instances != null)
                pool.AddRange(data.equipmentStorage.instances);
            nextInstanceId = Mathf.Max(1, data.equipmentStorage.nextInstanceId);
        }
        OnPoolChanged?.Invoke();
    }

    public void PostRestore(SaveData data) { }

    #endregion
}
