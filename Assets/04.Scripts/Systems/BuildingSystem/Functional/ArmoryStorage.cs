using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 장비 보관소 건물 컴포넌트. 직원이 장비를 장착/해제하려면 이 건물로 이동해야 한다.
/// 장비 재고 자체는 EquipmentStorageManager의 전역 풀 — 이 건물은 '접근 지점'이자 <b>보관 칸</b> 역할.
/// 보관소마다 capacity벌을 보관하며, 전체 보관소의 합이 차면 장비를 더 만들 수 없습니다.
/// </summary>
public class ArmoryStorage : MonoBehaviour, IBuildingFunction, IStorageView
{
    [Tooltip("이 보관소가 보관할 수 있는 장비 수")]
    [Min(0)] [SerializeField] private int capacity = 4;

    private bool buildingEnabled = true;

    /// <summary>이 보관소가 보관할 수 있는 장비 수</summary>
    public int Capacity => capacity;

    public bool IsOperating => buildingEnabled;

    public void OnBuildingDisabled() => buildingEnabled = false;

    public void OnBuildingEnabled() => buildingEnabled = true;

    private void OnEnable()
    {
        EquipmentStorageManager.instance?.RegisterArmory(this);
    }

    private void Start()
    {
        // OnEnable 시점에 매니저가 아직 없을 수 있으므로 재시도
        EquipmentStorageManager.instance?.RegisterArmory(this);
    }

    private void OnDisable()
    {
        EquipmentStorageManager.instance?.UnregisterArmory(this);
    }

    /// <summary>보관소를 클릭하면 보관함 창을 엽니다.</summary>
    private void OnMouseDown()
    {
        // UI 위 클릭은 월드로 흘리지 않는다 (OnMouseDown은 EventSystem을 거치지 않는다)
        if (UIManager.PointerOverUI) return;
        if (!IsOperating) return;

        StoragePanel.Show(this);
    }

    #region IStorageView — 보관함 창

    /// <summary>보관소 이름 (장비의 보관 위치 표시용). 플레이어가 정한 건물 이름, 없으면 건물 종류 이름(여러 채면 번호가 붙음).</summary>
    public string StorageName
        => TryGetComponent(out Building building) && building.HasCustomName ? building.DisplayName
         : EquipmentStorageManager.instance != null ? EquipmentStorageManager.instance.GetDefaultName(this)
         : "장비 보관소";

    public int SlotCount => capacity;

    public void CollectSlots(List<StorageSlot> into)
    {
        var storage = EquipmentStorageManager.instance;
        var db = GameDatabase.Instance;
        if (storage == null || db == null) return;

        foreach (var inst in storage.GetInstancesIn(this))
        {
            var data = db.GetEquipmentData(inst.equipmentId);
            if (data != null && data.itemData != null) into.Add(new StorageSlot { item = data.itemData, count = 1 });
        }
    }

    #endregion
}
