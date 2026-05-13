using System;

/// <summary>
/// 건물 저장 데이터 (완성된 건물).
/// </summary>
[Serializable]
public class BuildingSaveData
{
    /// <summary>런타임 고유 ID</summary>
    public int instanceId;

    /// <summary>BuildingData ScriptableObject ID</summary>
    public int dataId;

    /// <summary>그리드 X 좌표 (피벗 기준, 왼쪽 아래)</summary>
    public int gridX;

    /// <summary>그리드 Y 좌표 (피벗 기준, 왼쪽 아래)</summary>
    public int gridY;

    /// <summary>현재 체력</summary>
    public int currentHealth;

    /// <summary>기능 활성화 여부</summary>
    public bool isFunctional;

    /// <summary>건물별 추가 데이터 (창고 내용물 등, JSON 직렬화)</summary>
    public string extraData;
}

/// <summary>
/// 건설 현장 저장 데이터 (건설 중인 건물).
/// </summary>
[Serializable]
public class ConstructionSiteSaveData
{
    /// <summary>런타임 고유 ID</summary>
    public int instanceId;

    /// <summary>건설 중인 BuildingData ID</summary>
    public int buildingDataId;

    /// <summary>그리드 X 좌표 (피벗 기준)</summary>
    public int gridX;

    /// <summary>그리드 Y 좌표 (피벗 기준)</summary>
    public int gridY;

    /// <summary>건설 상태 (ConstructionState enum)</summary>
    public int state;

    /// <summary>건설 진행률 (0~1)</summary>
    public float progress;

    /// <summary>연결된 작업 명령 ID (-1이면 없음)</summary>
    public int workOrderId;

    /// <summary>자원 예약 ID (-1이면 예약 없음)</summary>
    public int reservationId;

    public ConstructionSiteSaveData()
    {
        workOrderId = -1;
        reservationId = -1;
    }
}
