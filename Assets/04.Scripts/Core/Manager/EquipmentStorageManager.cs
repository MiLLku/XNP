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

    /// <summary>가동 중인 보관소들의 보관 칸 합계</summary>
    public int Capacity
    {
        get
        {
            int total = 0;
            foreach (var armory in armories)
                if (armory != null && armory.IsOperating) total += armory.Capacity;
            return total;
        }
    }

    /// <summary>남은 보관 칸. 풀이 이미 넘쳐 있으면 0.</summary>
    public int FreeSlots => Mathf.Max(0, Capacity - pool.Count);

    /// <summary>
    /// 이 산출물을 만들 자리가 있는지. 장비가 아니면 항상 true.
    ///
    /// 보관 칸은 <b>새로 만드는 것만 막습니다</b> — 시작 지급·장착 해제·세이브 복원으로 들어오는 장비는
    /// 칸이 모자라도 받아 줍니다(장비가 사라지면 안 되므로). 그만큼 넘친 동안에는 제작이 막힙니다.
    /// </summary>
    public static bool HasRoomForCraft(ItemData output, int amount)
    {
        if (instance == null || output == null || GameDatabase.Instance == null) return true;
        if (GameDatabase.Instance.GetEquipmentData(output.itemID) == null) return true;
        // ponytail: 제작 중인 장비는 세지 않는다 — 여러 곳에서 동시에 만들면 그만큼 넘칠 수 있다. 문제가 되면 예약 수를 더할 것.
        return instance.FreeSlots >= amount;
    }

    // 장비 재고는 전역 풀이라 장비마다 보관소를 따로 기록하지 않는다. 대신 보관 칸을 세는 방식 그대로
    // "풀 순서대로 보관소를 채운다"고 보고 장비의 자리를 정한다 — 앞에서부터 보관소마다 Capacity벌씩.
    // 칸이 모자라 넘친 장비는 마지막 보관소에 '칸 초과'로 이어 붙는다.

    /// <summary>풀의 index번째 장비가 놓인 보관소. 가동 중인 보관소가 없으면 null.</summary>
    private ArmoryStorage ArmoryForIndex(int index)
    {
        ArmoryStorage last = null;
        foreach (var armory in armories)
        {
            if (armory == null || !armory.IsOperating) continue;
            last = armory;
            if (index < armory.Capacity) return armory;
            index -= armory.Capacity;
        }
        return last; // 넘친 장비는 마지막 보관소 몫
    }

    /// <summary>그 보관소에 놓인 장비들 (보관함 창 표시용).</summary>
    public List<EquipmentInstance> GetInstancesIn(ArmoryStorage armory)
    {
        var result = new List<EquipmentInstance>();
        for (int i = 0; i < pool.Count; i++)
            if (ArmoryForIndex(i) == armory) result.Add(pool[i]);
        return result;
    }

    /// <summary>이름을 따로 정하지 않은 보관소의 기본 이름 — 건물 이름, 여러 채면 번호가 붙습니다.</summary>
    public string GetDefaultName(ArmoryStorage armory)
    {
        string name = "장비 보관소";
        if (armory != null && armory.TryGetComponent(out Building building) && building.buildingData != null)
            name = building.buildingData.buildingName;

        int operating = 0, number = 0;
        foreach (var other in armories)
        {
            if (other == null || !other.IsOperating) continue;
            operating++;
            if (other == armory) number = operating;
        }
        return operating > 1 && number > 0 ? $"{name} {number}" : name;
    }

    /// <summary>보관 중인 장비가 들어 있는 곳의 이름 (장비 정보 창 표시용).</summary>
    public string GetStorageName(EquipmentInstance inst)
    {
        int index = pool.IndexOf(inst);
        if (index < 0) return "";

        ArmoryStorage armory = ArmoryForIndex(index);
        if (armory == null) return "임시 보관 (보관소 없음)";
        return index < Capacity ? armory.StorageName : $"{armory.StorageName} (칸 초과)";
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
        GameMessageBus.Publish(new EquipmentPoolChangedMessage());
        return inst;
    }

    /// <summary>
    /// 새 직원에게 시작 복장(StartingInventoryConfig.startingOutfit)을 입힙니다.
    ///   · fromStorage = false (시작 직원·이벤트 합류) — 인스턴스를 새로 만들어 바로 입힘. 보관소 칸을 차지하지 않음.
    ///   · fromStorage = true (채용) — 보관소에 같은 장비가 있을 때만 꺼내 입힘. 없으면 그 칸은 빈 채로 온다
    ///     → 채용 전에 기본 옷을 만들어 두게 하는 것이 인원 증가의 부담 중 하나다.
    /// 로드로 복원되는 직원은 이 경로를 타지 않습니다(EmployeeManager.SpawnEmployee에서만 호출).
    /// </summary>
    public void EquipStartingOutfit(Employee employee, bool fromStorage = false)
    {
        var equipment = employee != null ? employee.Equipment : null;
        if (equipment == null || startingConfig == null || startingConfig.startingOutfit == null) return;

        foreach (var data in startingConfig.startingOutfit)
        {
            if (data == null || data.itemData == null || !equipment.IsSlotEmpty(data.slot)) continue;

            EquipmentInstance inst;
            if (fromStorage)
            {
                var stored = pool.FirstOrDefault(i => i.equipmentId == data.itemData.itemID);
                if (stored == null) continue;
                inst = TakeInstance(stored.instanceId);
            }
            else
            {
                inst = new EquipmentInstance
                {
                    instanceId = nextInstanceId++,
                    equipmentId = data.itemData.itemID,
                    durability = data.maxDurability
                };
            }
            if (inst != null && !equipment.EquipInstance(data.slot, inst))
                ReturnInstance(inst); // 특성상 못 입는 시작 장비(총기광의 진압봉 등)는 보관소로
        }
    }

    /// <summary>기존 인스턴스를 풀에 반납합니다 (장착 해제 시).</summary>
    public void ReturnInstance(EquipmentInstance inst)
    {
        if (inst == null) return;
        pool.Add(inst);
        GameMessageBus.Publish(new EquipmentPoolChangedMessage());
    }

    /// <summary>풀에서 인스턴스를 꺼냅니다 (장착 시). 없으면 null.</summary>
    public EquipmentInstance TakeInstance(int instanceId)
    {
        var inst = pool.FirstOrDefault(i => i.instanceId == instanceId);
        if (inst != null)
        {
            pool.Remove(inst);
            GameMessageBus.Publish(new EquipmentPoolChangedMessage());
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
        GameMessageBus.Publish(new EquipmentPoolChangedMessage());
    }

    public void PostRestore(SaveData data) { }

    #endregion
}
