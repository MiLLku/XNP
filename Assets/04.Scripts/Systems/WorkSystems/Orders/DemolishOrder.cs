using UnityEngine;

/// <summary>
/// 철거 작업 명령.
/// 건물을 철거하고 자원을 일부 반환합니다.
///
/// <b>작업량 누적 방식</b>(IProgressiveWork) — 진행도가 이 오더(=건물 하나)에 쌓이므로
/// 직원이 중간에 떠나도 철거 진행이 사라지지 않고 다른 직원이 이어받는다.
/// </summary>
[System.Serializable]
public class DemolishOrder : IWorkTarget, IProgressiveWork
{
    #region 상수

    private const float DEMOLISH_WORK_AMOUNT = 5f;
    private const int RESOURCE_RETURN_DIVISOR = 2;

    #endregion

    #region 필드

    /// <summary>철거 대상 건물</summary>
    public Building building;

    /// <summary>작업 위치</summary>
    public Vector3 position;

    /// <summary>작업 우선순위</summary>
    public int priority;

    /// <summary>완료 여부</summary>
    public bool completed;

    /// <summary>배정된 직원</summary>
    public Employee assignedWorker;

    /// <summary>이 건물에 누적된 철거 작업량 (직원이 떠나도 유지)</summary>
    public float accumulatedWork;

    #endregion

    #region IWorkTarget 구현

    /// <inheritdoc/>
    public Vector3 GetWorkPosition() => position;

    /// <inheritdoc/>
    public WorkType GetWorkType() => WorkType.Demolish;

    /// <inheritdoc/>
    public float GetWorkTime() => DEMOLISH_WORK_AMOUNT;

    /// <inheritdoc/>
    public bool IsWorkAvailable() => !completed && building != null;

    #endregion

    #region IProgressiveWork 구현 — 진행도는 이 건물에 누적된다

    /// <inheritdoc/>
    public float GetWorkAmount() => DEMOLISH_WORK_AMOUNT;

    /// <inheritdoc/>
    public float GetAccumulatedWork() => accumulatedWork;

    /// <inheritdoc/>
    public void AddWork(float amount)
    {
        if (amount <= 0f || completed) return;
        accumulatedWork = Mathf.Min(DEMOLISH_WORK_AMOUNT, accumulatedWork + amount);
    }

    /// <inheritdoc/>
    public void ReduceWork(float amount)
    {
        if (amount <= 0f || completed) return;
        accumulatedWork = Mathf.Max(0f, accumulatedWork - amount);
    }

    /// <inheritdoc/>
    public void CompleteWork(Employee worker)
    {
        if (completed) return; // 이중 호출 방지 (자원 이중 반환 차단 — 형제 오더와 동일 패턴)
        completed = true;

        if (building != null)
        {
            string buildingName = building.buildingData?.buildingName ?? "Unknown";
            ReturnResources();
            ReleaseTileOccupation();
            GameObject.Destroy(building.gameObject);
            Debug.Log($"[DemolishOrder] 철거 완료: {buildingName}");
        }

        assignedWorker = null;
    }

    /// <inheritdoc/>
    public void CancelWork(Employee worker)
    {
        assignedWorker = null;
    }

    #endregion

    #region 내부 헬퍼

    /// <summary>
    /// 건설 비용의 절반을 인벤토리에 반환합니다.
    /// </summary>
private void ReturnResources()
    {
        // 인벤토리 직접 추가 대신 바닥 드롭으로 전환됨.
        // 직원이 운반(Hauling)해야 인벤토리에 들어옵니다.
        if (building == null) return;
        BuildingDropHelper.SpawnDestructionDrops(building.buildingData, building.transform.position);
    }

    /// <summary>
    /// 건물이 차지하던 타일의 점유 상태를 해제합니다.
    /// </summary>
    private void ReleaseTileOccupation()
    {
        if (MapGenerator.instance == null || building == null || building.buildingData == null) return;

        GameMap gameMap = MapGenerator.instance.GameMapInstance;
        if (gameMap == null) return;

        Vector2Int cellPos = new Vector2Int(
            Mathf.FloorToInt(building.transform.position.x),
            Mathf.FloorToInt(building.transform.position.y)
        );

        for (int y = 0; y < building.buildingData.size.y; y++)
        {
            for (int x = 0; x < building.buildingData.size.x; x++)
            {
                gameMap.UnmarkTileOccupied(cellPos.x + x, cellPos.y + y);
            }
        }
    }

    #endregion
}
