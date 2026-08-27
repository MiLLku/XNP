using UnityEngine;

/// <summary>
/// 세척 작업 — <b>방에 고인 침식 수치를 낮춥니다</b>.
///
/// 발원지 제거(채광)와 역할이 다릅니다.
///   · 발원지 제거 : 침식이 <b>더 오르지 않게</b> 한다. 이미 고인 것은 그대로 남는다.
///   · 세척        : 이미 고인 것을 <b>지운다</b>. 발원지가 남아 있으면 다시 찬다.
/// 그래서 보통 발원지를 먼저 캐고 세척하는 순서가 됩니다.
///
/// 환기(벽을 뚫어 실외로 만들기)와 비교하면, 세척은 공간을 지키는 대신
/// <b>노동력과 작업자의 침식</b>을 지불하는 선택지입니다.
///
/// 방은 지형이 바뀔 때마다 다시 계산되므로 Room 참조를 들고 있지 않고
/// <b>좌표로 그때그때 찾습니다</b>. 작업 도중 벽이 뚫려 실외가 되면 작업은 무효가 됩니다.
/// </summary>
[System.Serializable]
public class CleanOrder : IWorkTarget, IErosionHazardWork
{
    #region 필드

    /// <summary>세척할 방을 찾을 기준 칸</summary>
    public Vector2Int targetCell;

    /// <summary>작업 위치 (월드)</summary>
    public Vector3 position;

    /// <summary>한 번 완료할 때 지우는 침식량</summary>
    public float cleanAmount = 15f;

    /// <summary>작업 시간(초)</summary>
    public float workTime = 12f;

    /// <summary>작업자가 받는 침식량</summary>
    public float workerErosionCost = 8f;

    public int priority;
    public bool completed;
    public Employee assignedWorker;

    #endregion

    #region IWorkTarget

    public Vector3 GetWorkPosition() => position;

    public WorkType GetWorkType() => WorkType.Cleaning;

    public float GetWorkTime() => workTime;

    /// <summary>
    /// 방이 사라졌거나(환기됨) 이미 깨끗하면 작업할 것이 없습니다.
    /// </summary>
    public bool IsWorkAvailable()
    {
        if (completed) return false;

        Room room = RoomManager.instance != null ? RoomManager.instance.GetRoom(targetCell) : null;
        return room != null && room.Erosion > 0f;
    }

    public void CompleteWork(Employee worker)
    {
        if (completed) return;
        completed = true;
        assignedWorker = null;

        Room room = RoomManager.instance != null ? RoomManager.instance.GetRoom(targetCell) : null;
        if (room == null)
        {
            Debug.Log("[CleanOrder] 대상 방이 사라져 세척이 무효가 되었습니다.");
            return;
        }

        float before = room.Erosion;
        TerrainErosionManager.instance?.ReduceRoomErosion(room, cleanAmount);

        // 오염을 직접 다룬 대가 — 작업자가 침식을 뒤집어쓴다
        if (workerErosionCost > 0f && worker != null)
        {
            worker.ErosionController?.AddErosion(
                workerErosionCost, ErosionSource.HazardKey(HazardDisplayName), HazardDisplayName);
        }

        Debug.Log($"[CleanOrder] 방#{room.Id} 세척: 침식 {before:F1} → {room.Erosion:F1} (작업자 {worker?.DisplayName})");
    }

    /// <summary>배정이 취소되면 담당자만 비웁니다 — 진행도는 남기지 않습니다.</summary>
    public void CancelWork(Employee worker)
    {
        assignedWorker = null;
    }

    #endregion

    #region IErosionHazardWork

    public float WorkerErosionCost => workerErosionCost;

    public string HazardDisplayName => "세척 작업";

    #endregion
}
