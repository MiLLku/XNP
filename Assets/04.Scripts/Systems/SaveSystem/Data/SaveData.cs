using System;
using System.Collections.Generic;

/// <summary>
/// 게임 저장 데이터의 최상위 컨테이너.
/// 모든 게임 상태(맵, 직원, 건물, 인벤토리, 작업, 이벤트)를 담습니다.
/// </summary>
[Serializable]
public class SaveData
{
    #region 메타 정보

    /// <summary>세이브 파일 버전 (마이그레이션용)</summary>
    public int saveVersion = 1;

    /// <summary>세이브 슬롯 이름</summary>
    public string saveName;

    /// <summary>저장 시각 (Unix timestamp)</summary>
    public long saveTimestamp;

    /// <summary>총 플레이 시간 (초)</summary>
    public float playTime;

    #endregion

    #region 게임 데이터

    /// <summary>맵 데이터</summary>
    public MapSaveData map;

    /// <summary>직원 데이터 목록</summary>
    public List<EmployeeSaveData> employees;

    /// <summary>완성된 건물 데이터 목록</summary>
    public List<BuildingSaveData> buildings;

    /// <summary>건설 현장 데이터 목록</summary>
    public List<ConstructionSiteSaveData> constructionSites;

    /// <summary>작업 명령 데이터 목록</summary>
    public List<WorkOrderSaveData> workOrders;

    /// <summary>인벤토리 데이터</summary>
    public InventorySaveData inventory;

    /// <summary>바닥에 드롭된 아이템 목록</summary>
    public List<DroppedItemSaveData> droppedItems;

    /// <summary>제노프스 엔티티 목록</summary>
    public List<XenopsSaveData> xenopsEntities;

    #endregion

    #region 시스템 데이터

    /// <summary>이벤트 시스템 데이터</summary>
    public EventSystemSaveData eventSystem;

    /// <summary>침식 시스템 전역 상태</summary>
    public ErosionSystemSaveData erosionSystem;

    /// <summary>레이드 시스템 상태</summary>
    public RaidSystemSaveData raidSystem;

    /// <summary>장비 보관소 풀 (v5)</summary>
    public EquipmentStorageSaveData equipmentStorage;

    /// <summary>연구 포인트 누적량 (레거시 — researchTree로 대체됨)</summary>
    public float researchPoints;

    /// <summary>연구 트리 전체 상태 (노드별 상태·진행 중 연구·진행도)</summary>
    public ResearchTreeSaveData researchTree;

    /// <summary>다음 발급할 인스턴스 ID (로드 후 이어서 발급)</summary>
    public int nextInstanceId;

    /// <summary>다음 작업물 ID (WorkSystemManager.nextOrderId)</summary>
    public int nextOrderId;

    /// <summary>다음 작업 Task ID (WorkTask.nextTaskId)</summary>
    public int nextTaskId;

    /// <summary>구역 시스템 데이터</summary>
    public ZoneManagerSaveData zoneSystem;

    /// <summary>게임 시간 데이터</summary>
    public DayCycleSaveData dayCycle;

    /// <summary>전쟁의 안개 탐색 상태</summary>
    public FogSaveData fog;

    #endregion

    public SaveData()
    {
        employees = new List<EmployeeSaveData>();
        buildings = new List<BuildingSaveData>();
        constructionSites = new List<ConstructionSiteSaveData>();
        workOrders = new List<WorkOrderSaveData>();
        droppedItems = new List<DroppedItemSaveData>();
        xenopsEntities = new List<XenopsSaveData>();
    }
}
