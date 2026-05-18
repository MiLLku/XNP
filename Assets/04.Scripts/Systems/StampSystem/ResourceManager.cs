using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// ID와 타일/프리팹 에셋을 연결하는 엔트리.
/// </summary>
[System.Serializable]
public class TileEntry
{
    /// <summary>타일 ID</summary>
    public int id;

    /// <summary>타일 에셋</summary>
    public TileBase tileAsset;
}

/// <summary>
/// ID와 개체 프리팹을 연결하는 엔트리.
/// </summary>
[System.Serializable]
public class EntityEntry
{
    /// <summary>개체 ID</summary>
    public int id;

    /// <summary>개체 프리팹</summary>
    public GameObject prefab;
}

/// <summary>
/// 타일 ID와 드롭 아이템 프리팹을 연결하는 엔트리.
/// </summary>
[System.Serializable]
public class DropEntry
{
    /// <summary>타일 ID (예: 1=흙, 2=돌)</summary>
    public int tileId;

    /// <summary>드롭 아이템 프리팹</summary>
    public GameObject dropPrefab;
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

    private Dictionary<int, TileBase> _tileLookup;
    private Dictionary<int, GameObject> _entityLookup;
    private Dictionary<int, GameObject> _dropLookup;

    #endregion

    #region 초기화

    private void OnEnable()
    {
        _tileLookup = new Dictionary<int, TileBase>();
        foreach (var entry in tileEntries)
        {
            _tileLookup[entry.id] = entry.tileAsset;
        }

        _entityLookup = new Dictionary<int, GameObject>();
        foreach (var entry in entityEntries)
        {
            _entityLookup[entry.id] = entry.prefab;
        }

        _dropLookup = new Dictionary<int, GameObject>();
        foreach (var entry in dropEntries)
        {
            _dropLookup[entry.tileId] = entry.dropPrefab;
        }
    }

    #endregion

    #region 조회

    /// <summary>
    /// 타일 ID에 해당하는 타일 에셋을 반환합니다.
    /// </summary>
    /// <param name="id">타일 ID</param>
    /// <returns>타일 에셋 (없으면 null)</returns>
    public TileBase GetTileAsset(int id)
    {
        _tileLookup.TryGetValue(id, out TileBase tile);
        return tile;
    }

    /// <summary>
    /// 개체 ID에 해당하는 프리팹을 반환합니다.
    /// </summary>
    /// <param name="id">개체 ID</param>
    /// <returns>프리팹 (없으면 null)</returns>
    public GameObject GetEntityPrefab(int id)
    {
        _entityLookup.TryGetValue(id, out GameObject prefab);
        return prefab;
    }

    /// <summary>
    /// 타일 ID에 해당하는 드롭 아이템 프리팹을 반환합니다.
    /// </summary>
    /// <param name="tileId">타일 ID</param>
    /// <returns>드롭 프리팹 (없으면 null)</returns>
    public GameObject GetDropPrefab(int tileId)
    {
        _dropLookup.TryGetValue(tileId, out GameObject prefab);
        return prefab;
    }

    #endregion
}
