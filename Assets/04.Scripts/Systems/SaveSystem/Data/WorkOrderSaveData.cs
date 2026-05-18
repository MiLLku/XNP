using System;
using System.Collections.Generic;

/// <summary>
/// 작업 명령 저장 데이터.
/// </summary>
[Serializable]
public class WorkOrderSaveData
{
    /// <summary>작업 명령 ID</summary>
    public int orderId;

    /// <summary>작업 명령 이름</summary>
    public string orderName;

    /// <summary>작업 타입 (WorkType enum)</summary>
    public int workType;

    /// <summary>우선순위</summary>
    public int priority;

    /// <summary>생성 시간</summary>
    public float createdTime;

    /// <summary>최대 배정 가능 인원</summary>
    public int maxWorkers;

    /// <summary>배정된 직원 ID 목록</summary>
    public List<int> assignedWorkerIds;

    /// <summary>활성화 여부</summary>
    public bool isActive;

    /// <summary>일시정지 여부</summary>
    public bool isPaused;

    /// <summary>대기 중인 작업 목록</summary>
    public List<WorkTaskSaveData> pendingTasks;

    /// <summary>진행 중인 작업 목록</summary>
    public List<WorkTaskSaveData> assignedTasks;

    public WorkOrderSaveData()
    {
        assignedWorkerIds = new List<int>();
        pendingTasks = new List<WorkTaskSaveData>();
        assignedTasks = new List<WorkTaskSaveData>();
    }
}

/// <summary>
/// 개별 작업 저장 데이터.
/// </summary>
[Serializable]
public class WorkTaskSaveData
{
    /// <summary>작업 ID</summary>
    public int taskId;

    /// <summary>작업 상태 (TaskState enum)</summary>
    public int state;

    /// <summary>우선순위</summary>
    public int priority;

    /// <summary>배정된 직원 ID (-1이면 미할당)</summary>
    public int assignedWorkerId;

    /// <summary>생성 시간</summary>
    public float createdTime;

    /// <summary>배정 시간</summary>
    public float assignedTime;

    /// <summary>시작 시간</summary>
    public float startedTime;

    /// <summary>작업 대상 타입 (WorkTargetType enum)</summary>
    public int targetType;

    /// <summary>작업 대상 데이터 (JSON 직렬화)</summary>
    public string targetData;

    public WorkTaskSaveData()
    {
        assignedWorkerId = -1;
    }
}

/// <summary>
/// 작업 대상 타입 열거형.
/// </summary>
public enum WorkTargetType
{
    /// <summary>채광</summary>
    Mining = 0,
    /// <summary>수확</summary>
    Harvest = 1,
    /// <summary>건설</summary>
    Build = 2,
    /// <summary>철거</summary>
    Demolish = 3,
    /// <summary>운반</summary>
    Haul = 4,
    /// <summary>제작 (CraftingOrder)</summary>
    Crafting = 5
}

#region 작업 대상별 직렬화 데이터

/// <summary>
/// 채광 대상 직렬화 데이터.
/// </summary>
[Serializable]
public class MiningTargetData
{
    public int tileX;
    public int tileY;
    public int tileId;
}

/// <summary>
/// 수확 대상 직렬화 데이터.
/// </summary>
[Serializable]
public class HarvestTargetData
{
    public int entityX;
    public int entityY;
    public int entityType;
}

/// <summary>
/// 건설 대상 직렬화 데이터.
/// </summary>
[Serializable]
public class BuildTargetData
{
    public int constructionSiteId;
}

/// <summary>
/// 철거 대상 직렬화 데이터.
/// </summary>
[Serializable]
public class DemolishTargetData
{
    public int buildingInstanceId;
}

/// <summary>
/// 운반 대상 직렬화 데이터.
/// </summary>
[Serializable]
public class HaulTargetData
{
    public int itemId;
    public int amount;
    public float fromX;
    public float fromY;
    public float toX;
    public float toY;
}

/// <summary>
/// 제작 대상 직렬화 데이터.
/// CraftingOrder는 WorkOrder + IWorkTarget 이중 역할이므로 별도 데이터 필요.
/// </summary>
[Serializable]
public class CraftingTargetData
{
    /// <summary>제작 레시피 ID</summary>
    public int recipeId;

    /// <summary>제작 수량</summary>
    public int craftAmount;

    /// <summary>제작 진행도 (0~1)</summary>
    public float craftingProgress;

    /// <summary>생산 건물 인스턴스 ID</summary>
    public int buildingInstanceId;

    /// <summary>자원 예약 ID (-1이면 예약 없음)</summary>
    public int reservationId;
}

#endregion
