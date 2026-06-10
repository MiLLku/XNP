using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

/// <summary>
/// TileType과 Tilemap에 쓰일 TileBase 에셋을 연결하는 엔트리.
/// </summary>
[System.Serializable]
public class TileEntry
{
    [Tooltip("이 엔트리가 매칭할 타일 종류")]
    [FormerlySerializedAs("id")]
    public TileType tile = TileType.Air;

    [Tooltip("Tilemap에 사용될 TileBase 에셋")]
    public TileBase tileAsset;
}

/// <summary>
/// EntityType과 개체 프리팹을 연결하는 엔트리.
/// </summary>
[System.Serializable]
public class EntityEntry
{
    [Tooltip("이 엔트리가 매칭할 개체 종류")]
    [FormerlySerializedAs("id")]
    public EntityType entity = EntityType.None;

    [Tooltip("개체 프리팹")]
    public GameObject prefab;
}

/// <summary>
/// 채광될 TileType과 드롭될 ItemData를 연결하는 엔트리.
/// 시각 prefab은 ItemData.dropPrefab에서 가져옵니다.
/// </summary>
[System.Serializable]
public class DropEntry
{
    [Tooltip("이 타일을 채광하면 itemData가 드롭됩니다")]
    [FormerlySerializedAs("tileId")]
    public TileType tile = TileType.Dirt;

    [Tooltip("채광 시 드롭될 ItemData (dropPrefab 포함)")]
    public ItemData itemData;
}

/// <summary>
/// 리소스 매니저 ScriptableObject.
/// 타일 ID → 타일 에셋, 개체 ID → 프리팹, 타일 ID → 드롭 아이템 매핑을 관리합니다.
/// </summary>
[CreateAssetMenu(fileName = "ResourceManager", menuName = "StampSystem/ResourceManager")]
public class ResourceManager : ScriptableObject
{
    #region 필드

    [Header("타일 시각 정보")]
    [SerializeField] private List<TileEntry> tileEntries;

    [Header("개체(건물, 식물) 프리팹")]
    [SerializeField] private List<EntityEntry> entityEntries;

    [Header("타일 드랍 아이템")]
    [SerializeField] private List<DropEntry> dropEntries;

    [Header("전역 아이템 카탈로그")]
    [Tooltip("채광·가공·드롭 등에서 ID로 조회 가능한 모든 ItemData. 가공 자원도 여기에 등록하세요.")]
    [SerializeField] private List<ItemData> allItems;

    private Dictionary<TileType, TileBase> _tileLookup;
    private Dictionary<EntityType, GameObject> _entityLookup;
    private Dictionary<TileType, ItemData> _dropLookup;
    private Dictionary<ItemType, ItemData> _itemLookup;

    #endregion

    #region 초기화

private void OnEnable()
    {
        _tileLookup = new Dictionary<TileType, TileBase>();
        if (tileEntries != null)
        {
            foreach (var entry in tileEntries)
            {
                if (entry != null)
                    _tileLookup[entry.tile] = entry.tileAsset;
            }
        }

        _entityLookup = new Dictionary<EntityType, GameObject>();
        if (entityEntries != null)
        {
            foreach (var entry in entityEntries)
            {
                if (entry != null)
                    _entityLookup[entry.entity] = entry.prefab;
            }
        }

        _dropLookup = new Dictionary<TileType, ItemData>();
        if (dropEntries != null)
        {
            foreach (var entry in dropEntries)
            {
                if (entry?.itemData != null)
                    _dropLookup[entry.tile] = entry.itemData;
            }
        }

        _itemLookup = new Dictionary<ItemType, ItemData>();
        if (allItems != null)
        {
            foreach (var item in allItems)
            {
                if (item != null)
                    _itemLookup[item.itemType] = item;
            }
        }

        // dropEntries의 ItemData도 자동으로 카탈로그에 포함 (편의)
        foreach (var kv in _dropLookup)
        {
            if (kv.Value != null && !_itemLookup.ContainsKey(kv.Value.itemType))
                _itemLookup[kv.Value.itemType] = kv.Value;
        }
    }

    #endregion

    #region 조회

    /// <summary>
    /// 타일 ID에 해당하는 타일 에셋을 반환합니다.
    /// </summary>
    /// <param name="id">타일 ID</param>
    /// <returns>타일 에셋 (없으면 null)</returns>
public TileBase GetTileAsset(TileType tile)
    {
        if (_tileLookup == null) return null;
        _tileLookup.TryGetValue(tile, out TileBase asset);
        return asset;
    }

    /// <summary>호환용 int 오버로드 — 내부적으로 (TileType)id 캐스트.</summary>
    public TileBase GetTileAsset(int id) => GetTileAsset((TileType)id);

    /// <summary>
    /// 개체 종류에 해당하는 프리팹을 반환합니다.
    /// </summary>
    /// <param name="entity">개체 종류</param>
    /// <returns>프리팹 (없으면 null)</returns>
    public GameObject GetEntityPrefab(EntityType entity)
    {
        if (_entityLookup == null) return null;
        _entityLookup.TryGetValue(entity, out GameObject prefab);
        return prefab;
    }

    /// <summary>호환용 int 오버로드 — 내부적으로 (EntityType)id 캐스트.</summary>
    public GameObject GetEntityPrefab(int id) => GetEntityPrefab((EntityType)id);

    /// <summary>
    /// 타일 ID에 해당하는 드롭 아이템 프리팹을 반환합니다.
    /// </summary>
    /// <param name="tileId">타일 ID</param>
    /// <returns>드롭 프리팹 (없으면 null)</returns>
public GameObject GetDropPrefab(TileType tile)
    {
        ItemData data = GetTileDropItem(tile);
        return data != null ? data.dropPrefab : null;
    }

    /// <summary>호환용 int 오버로드.</summary>
    public GameObject GetDropPrefab(int tileId) => GetDropPrefab((TileType)tileId);

    /// <summary>
    /// 타일 ID에 해당하는 드롭 ItemData를 반환합니다.
    /// 가공 자원 등 모든 ItemData는 GetItemByID(itemId)로 조회하세요.
    /// </summary>
public ItemData GetTileDropItem(TileType tile)
    {
        if (_dropLookup == null) return null;
        _dropLookup.TryGetValue(tile, out ItemData data);
        return data;
    }

    /// <summary>호환용 int 오버로드.</summary>
    public ItemData GetTileDropItem(int tileId) => GetTileDropItem((TileType)tileId);

    /// <summary>
    /// 아이템 ID로 ItemData를 조회합니다 (가공 자원·채광 자원 모두 포함).
    /// </summary>
public ItemData GetItemByID(ItemType type)
    {
        if (_itemLookup == null) return null;
        _itemLookup.TryGetValue(type, out ItemData data);
        return data;
    }

    /// <summary>호환용 int 오버로드 — itemID를 ItemType으로 캐스트해 조회.</summary>
    public ItemData GetItemByID(int itemId) => GetItemByID((ItemType)itemId);

    #endregion
}
