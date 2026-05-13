using UnityEngine;

/// <summary>
/// 채광 작업 명령.
/// 지정된 타일을 채굴하고, 드롭 아이템을 인벤토리에 추가합니다.
/// 채굴 후 타일은 AIR로 변경되며, 직원 낙하는 EmployeeMovement에서 자동 처리됩니다.
/// </summary>
[System.Serializable]
public class MiningOrder : IWorkTarget
{
    #region 상수

    private const float MINING_WORK_TIME = 3f;
    private const int AIR_TILE_ID = 0;

    #endregion

    #region 필드

    /// <summary>채굴 대상 타일 좌표</summary>
    public Vector3Int position;

    /// <summary>채굴 대상 타일 ID</summary>
    public int tileID;

    /// <summary>작업 우선순위</summary>
    public int priority;

    /// <summary>완료 여부</summary>
    public bool completed;

    /// <summary>배정된 직원</summary>
    public Employee assignedWorker;

    #endregion

    #region IWorkTarget 구현

    /// <inheritdoc/>
    public Vector3 GetWorkPosition() => new Vector3(position.x, position.y, 0);

    /// <inheritdoc/>
    public WorkType GetWorkType() => WorkType.Mining;

    /// <inheritdoc/>
    public float GetWorkTime() => MINING_WORK_TIME;

    /// <inheritdoc/>
    public bool IsWorkAvailable() => !completed;

    /// <inheritdoc/>
    public void CompleteWork(Employee worker)
    {
        if (completed) return;

        Vector3Int footTile3 = worker != null ? worker.GetFootTile() : Vector3Int.zero;

        if (worker != null)
        {
            Debug.Log($"[MiningOrder] 채굴완료 직원발위치={footTile3}, 채굴타겟={position}, " +
                      $"거리: dx={Mathf.Abs(position.x - footTile3.x)}, dy={position.y - footTile3.y}");
        }

        completed = true;
        assignedWorker = null;

        ExecuteMining();

        // 채광으로 지형이 바뀌면 LOS가 달라집니다.
        // UpdateEmployeeSight의 diff 계산은 '현재' 지형으로 inFrom을 재계산하므로
        // 이전에 막혀 있던 타일이 inFrom==inTo==true가 되어 안개가 갱신되지 않는 버그를 막기 위해
        // 직원의 현재 위치에서 시야를 완전히 재계산합니다.
        if (worker != null && FogOfWarManager.instance != null)
        {
            var feet2 = new Vector2Int(footTile3.x, footTile3.y);
            FogOfWarManager.instance.RefreshEmployeeSight(feet2);
        }
    }

    /// <inheritdoc/>
    public void CancelWork(Employee worker)
    {
        assignedWorker = null;
    }

    #endregion

    #region 내부 헬퍼

    /// <summary>
    /// 실제 채광 로직을 실행합니다.
    /// 타일을 AIR로 변경하고, 드롭 아이템을 인벤토리에 추가합니다.
    /// </summary>
    private void ExecuteMining()
    {
        if (MapGenerator.instance == null)
        {
            Debug.LogError("[MiningOrder] MapGenerator.instance가 null입니다!");
            return;
        }

        GameMap gameMap = MapGenerator.instance.GameMapInstance;
        MapRenderer mapRenderer = MapGenerator.instance.MapRendererInstance;
        ResourceManager resourceManager = MapGenerator.instance.ResourceManagerInstance;

        if (gameMap == null || mapRenderer == null || resourceManager == null)
        {
            Debug.LogError("[MiningOrder] 필수 컴포넌트가 null입니다!");
            return;
        }

        if (position.x < 0 || position.x >= GameMap.MAP_WIDTH ||
            position.y < 0 || position.y >= GameMap.MAP_HEIGHT)
        {
            Debug.LogWarning($"[MiningOrder] 유효하지 않은 위치: {position}");
            return;
        }

        int currentTileID = gameMap.TileGrid[position.x, position.y];
        if (currentTileID == AIR_TILE_ID)
        {
            Debug.LogWarning($"[MiningOrder] 타일이 이미 제거됨: {position}");
            return;
        }

        DropMinedResource(resourceManager);

        gameMap.SetTile(position.x, position.y, AIR_TILE_ID);
        gameMap.UnmarkTileOccupied(position.x, position.y);
        mapRenderer.UpdateTileVisual(position.x, position.y);

        Debug.Log($"[MiningOrder] 채광 완료: {position} (TileID: {tileID})");
    }

    /// <summary>
    /// 채굴된 자원을 바닥에 드롭합니다.
    /// DroppedItemManager가 있으면 DroppedItem으로 스폰 (직원이 창고로 운반).
    /// 없으면 인벤토리에 직접 추가 (폴백).
    /// </summary>
    private void DropMinedResource(ResourceManager resourceManager)
    {
        GameObject dropPrefab = resourceManager.GetDropPrefab(tileID);
        if (dropPrefab == null) return;

        ItemData itemData = null;
        ClickableItem itemComponent = dropPrefab.GetComponent<ClickableItem>();
        if (itemComponent != null)
            itemData = itemComponent.GetItemData();

        Vector3 dropPos = new Vector3(position.x + 0.5f, position.y + 0.5f, 0f);

        // DroppedItemManager가 있으면 바닥에 드롭 → 직원이 운반
        if (DroppedItemManager.instance != null && itemData != null)
        {
            DroppedItemManager.instance.SpawnItem(itemData, 1, dropPos);
            Debug.Log($"[MiningOrder] 드롭 스폰: {itemData.itemName} @ {dropPos}");
            return;
        }

        // 폴백: 인벤토리에 직접 추가
        if (itemData != null && InventoryManager.instance != null)
        {
            InventoryManager.instance.AddItem(itemData, 1);
        }
        else
        {
            GameObject.Instantiate(dropPrefab, dropPos, Quaternion.identity);
        }
    }

    #endregion
}
