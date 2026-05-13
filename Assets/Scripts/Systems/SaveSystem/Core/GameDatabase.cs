using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// ScriptableObject 데이터베이스
/// ID로 각종 데이터(EmployeeData, BuildingData, ItemData 등)를 조회할 수 있습니다.
///
/// 사용법:
/// 1. Project에서 Create > SaveSystem > Game Database 생성
/// 2. 각 리스트에 ScriptableObject 드래그
/// 3. 게임 시작 시 자동으로 초기화됨
/// </summary>
[CreateAssetMenu(fileName = "GameDatabase", menuName = "SaveSystem/Game Database")]
public class GameDatabase : ScriptableObject
{
    public static GameDatabase Instance { get; private set; }

    [Header("직원 데이터")]
    [Tooltip("모든 EmployeeData ScriptableObject")]
    public List<EmployeeData> allEmployeeData;

    [Header("건물 데이터")]
    [Tooltip("모든 BuildingData ScriptableObject")]
    public List<BuildingData> allBuildingData;

    [Header("아이템 데이터")]
    [Tooltip("모든 ItemData ScriptableObject")]
    public List<ItemData> allItemData;

    [Header("레시피 데이터")]
    [Tooltip("모든 CraftingRecipe ScriptableObject")]
    public List<CraftingRecipe> allRecipeData;

    [Header("제노프스 데이터")]
    [Tooltip("모든 XenopsData ScriptableObject")]
    public List<XenopsData> allXenopsData;

    [Header("장비 데이터")]
    [Tooltip("모든 EquipmentData ScriptableObject")]
    public List<EquipmentData> allEquipmentData;

    // ID → 데이터 캐시 (런타임)
    private Dictionary<int, EmployeeData> _employeeDataMap;
    private Dictionary<int, BuildingData> _buildingDataMap;
    private Dictionary<int, ItemData> _itemDataMap;
    private Dictionary<int, CraftingRecipe> _recipeDataMap;
    private Dictionary<int, XenopsData> _xenopsDataMap;
    private Dictionary<int, EquipmentData> _equipmentDataMap;

    private bool _initialized = false;

    /// <summary>
    /// 데이터베이스를 초기화합니다.
    /// SaveManager.Awake()에서 호출됩니다.
    /// </summary>
    public void Initialize()
    {
        if (_initialized) return;

        Instance = this;
        BuildCaches();
        _initialized = true;

        Debug.Log($"[GameDatabase] 초기화 완료 - 직원: {_employeeDataMap.Count}, 건물: {_buildingDataMap.Count}, 아이템: {_itemDataMap.Count}, 레시피: {_recipeDataMap.Count}, 제노프스: {_xenopsDataMap.Count}, 장비: {_equipmentDataMap.Count}");
    }

    private void BuildCaches()
    {
        // Employee
        _employeeDataMap = new Dictionary<int, EmployeeData>();
        if (allEmployeeData != null)
        {
            foreach (var data in allEmployeeData)
            {
                if (data != null)
                {
                    if (_employeeDataMap.ContainsKey(data.employeeID))
                    {
                        Debug.LogWarning($"[GameDatabase] 중복 EmployeeID: {data.employeeID} ({data.employeeName})");
                    }
                    else
                    {
                        _employeeDataMap[data.employeeID] = data;
                    }
                }
            }
        }

        // Building
        _buildingDataMap = new Dictionary<int, BuildingData>();
        if (allBuildingData != null)
        {
            foreach (var data in allBuildingData)
            {
                if (data != null)
                {
                    if (_buildingDataMap.ContainsKey(data.buildingID))
                    {
                        Debug.LogWarning($"[GameDatabase] 중복 BuildingID: {data.buildingID} ({data.buildingName})");
                    }
                    else
                    {
                        _buildingDataMap[data.buildingID] = data;
                    }
                }
            }
        }

        // Item
        _itemDataMap = new Dictionary<int, ItemData>();
        if (allItemData != null)
        {
            foreach (var data in allItemData)
            {
                if (data != null)
                {
                    if (_itemDataMap.ContainsKey(data.itemID))
                    {
                        Debug.LogWarning($"[GameDatabase] 중복 ItemID: {data.itemID} ({data.itemName})");
                    }
                    else
                    {
                        _itemDataMap[data.itemID] = data;
                    }
                }
            }
        }

        // Recipe
        _recipeDataMap = new Dictionary<int, CraftingRecipe>();
        if (allRecipeData != null)
        {
            foreach (var data in allRecipeData)
            {
                if (data != null)
                {
                    if (_recipeDataMap.ContainsKey(data.recipeID))
                    {
                        Debug.LogWarning($"[GameDatabase] 중복 RecipeID: {data.recipeID} ({data.outputItem?.itemName})");
                    }
                    else
                    {
                        _recipeDataMap[data.recipeID] = data;
                    }
                }
            }
        }

        // Xenops
        _xenopsDataMap = new Dictionary<int, XenopsData>();
        if (allXenopsData != null)
        {
            foreach (var data in allXenopsData)
            {
                if (data != null)
                {
                    if (_xenopsDataMap.ContainsKey(data.xenopsID))
                    {
                        Debug.LogWarning($"[GameDatabase] 중복 XenopsID: {data.xenopsID} ({data.xenopsName})");
                    }
                    else
                    {
                        _xenopsDataMap[data.xenopsID] = data;
                    }
                }
            }
        }

        // Equipment (ItemData.itemID 기준 캐시)
        _equipmentDataMap = new Dictionary<int, EquipmentData>();
        if (allEquipmentData != null)
        {
            foreach (var data in allEquipmentData)
            {
                if (data != null && data.itemData != null)
                {
                    if (_equipmentDataMap.ContainsKey(data.itemData.itemID))
                    {
                        Debug.LogWarning($"[GameDatabase] 중복 EquipmentID: {data.itemData.itemID} ({data.itemData.itemName})");
                    }
                    else
                    {
                        _equipmentDataMap[data.itemData.itemID] = data;
                    }
                }
            }
        }
    }

    #region 조회 메서드

    public EmployeeData GetEmployeeData(int id)
    {
        if (_employeeDataMap != null && _employeeDataMap.TryGetValue(id, out var data))
        {
            return data;
        }
        Debug.LogWarning($"[GameDatabase] EmployeeData 없음: ID {id}");
        return null;
    }

    public BuildingData GetBuildingData(int id)
    {
        if (_buildingDataMap != null && _buildingDataMap.TryGetValue(id, out var data))
        {
            return data;
        }
        Debug.LogWarning($"[GameDatabase] BuildingData 없음: ID {id}");
        return null;
    }

    public ItemData GetItemData(int id)
    {
        if (_itemDataMap != null && _itemDataMap.TryGetValue(id, out var data))
        {
            return data;
        }
        Debug.LogWarning($"[GameDatabase] ItemData 없음: ID {id}");
        return null;
    }

    public CraftingRecipe GetRecipeData(int id)
    {
        if (_recipeDataMap != null && _recipeDataMap.TryGetValue(id, out var data))
        {
            return data;
        }
        Debug.LogWarning($"[GameDatabase] CraftingRecipe 없음: ID {id}");
        return null;
    }

    public XenopsData GetXenopsData(int id)
    {
        if (_xenopsDataMap != null && _xenopsDataMap.TryGetValue(id, out var data))
        {
            return data;
        }
        Debug.LogWarning($"[GameDatabase] XenopsData 없음: ID {id}");
        return null;
    }

    /// <summary>아이템 ID로 장비 데이터 조회</summary>
    public EquipmentData GetEquipmentData(int itemId)
    {
        if (_equipmentDataMap != null && _equipmentDataMap.TryGetValue(itemId, out var data))
        {
            return data;
        }
        Debug.LogWarning($"[GameDatabase] EquipmentData 없음: ItemID {itemId}");
        return null;
    }

    #endregion

    #region 전체 목록 조회

    public List<EmployeeData> GetAllEmployeeData() => allEmployeeData ?? new List<EmployeeData>();
    public List<BuildingData> GetAllBuildingData() => allBuildingData ?? new List<BuildingData>();
    public List<ItemData> GetAllItemData() => allItemData ?? new List<ItemData>();
    public List<CraftingRecipe> GetAllRecipeData() => allRecipeData ?? new List<CraftingRecipe>();
    public List<XenopsData> GetAllXenopsData() => allXenopsData ?? new List<XenopsData>();
    public List<EquipmentData> GetAllEquipmentData() => allEquipmentData ?? new List<EquipmentData>();

    #endregion

    #region 유틸리티

    /// <summary>
    /// 에디터에서 캐시 재빌드 (데이터 변경 시)
    /// </summary>
    [ContextMenu("Rebuild Caches")]
    public void RebuildCaches()
    {
        _initialized = false;
        BuildCaches();
        _initialized = true;
        Debug.Log("[GameDatabase] 캐시 재빌드 완료");
    }

    /// <summary>
    /// ID 중복 검사
    /// </summary>
    [ContextMenu("Validate IDs")]
    public void ValidateIDs()
    {
        int errors = 0;

        // Employee
        var employeeIds = new HashSet<int>();
        foreach (var data in allEmployeeData)
        {
            if (data == null) continue;
            if (!employeeIds.Add(data.employeeID))
            {
                Debug.LogError($"[GameDatabase] 중복 EmployeeID: {data.employeeID} ({data.employeeName})");
                errors++;
            }
        }

        // Building
        var buildingIds = new HashSet<int>();
        foreach (var data in allBuildingData)
        {
            if (data == null) continue;
            if (!buildingIds.Add(data.buildingID))
            {
                Debug.LogError($"[GameDatabase] 중복 BuildingID: {data.buildingID} ({data.buildingName})");
                errors++;
            }
        }

        // Item
        var itemIds = new HashSet<int>();
        foreach (var data in allItemData)
        {
            if (data == null) continue;
            if (!itemIds.Add(data.itemID))
            {
                Debug.LogError($"[GameDatabase] 중복 ItemID: {data.itemID} ({data.itemName})");
                errors++;
            }
        }

        // Xenops
        if (allXenopsData != null)
        {
            var xenopsIds = new HashSet<int>();
            foreach (var data in allXenopsData)
            {
                if (data == null) continue;
                if (!xenopsIds.Add(data.xenopsID))
                {
                    Debug.LogError($"[GameDatabase] 중복 XenopsID: {data.xenopsID} ({data.xenopsName})");
                    errors++;
                }
            }
        }

        // Equipment
        if (allEquipmentData != null)
        {
            var equipIds = new HashSet<int>();
            foreach (var data in allEquipmentData)
            {
                if (data == null || data.itemData == null) continue;
                if (!equipIds.Add(data.itemData.itemID))
                {
                    Debug.LogError($"[GameDatabase] 중복 EquipmentID: {data.itemData.itemID} ({data.itemData.itemName})");
                    errors++;
                }
            }
        }

        if (errors == 0)
        {
            Debug.Log("[GameDatabase] ID 검증 통과!");
        }
        else
        {
            Debug.LogError($"[GameDatabase] ID 검증 실패: {errors}개의 중복 발견");
        }
    }

    #endregion
}
