using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 건물 데이터 (ScriptableObject).
/// 건물의 기본 정보, 설치 정보, 건설 비용, 게임플레이 스탯을 정의합니다.
/// StampSystem 메뉴에서 생성 가능합니다.
/// </summary>
[System.Serializable]
[CreateAssetMenu(fileName = "NewBuildingData", menuName = "StampSystem/Building Data")]
public class BuildingData : ScriptableObject
{
    #region 기본 정보

    [Header("기본 정보")]
    [Tooltip("건물을 식별하는 고유 ID (예: 3001)")]
    public int buildingID;

    [Tooltip("UI에 표시될 건물의 이름")]
    public string buildingName;

    [Tooltip("UI에 표시될 건물 아이콘")]
    public Sprite icon;

    [Tooltip("빌드 메뉴 정렬용 카테고리")]
    public BuildingCategory category;

    [TextArea(3, 5)]
    [Tooltip("UI에 표시될 건물 설명")]
    public string description;

    #endregion

    #region 설치 정보

    [Header("설치 정보")]
    [Tooltip("건설이 완료되었을 때 생성될 실제 프리팹 (예: Chest_3x2.prefab)")]
    public GameObject buildingPrefab;

    [Tooltip("건물이 차지하는 타일 크기")]
    public Vector2Int size = Vector2Int.one;

    [Tooltip("건설 기준점(Pivot)")]
    public Vector2Int pivot = Vector2Int.zero;

    #endregion

    #region 건설 비용

    [Header("건설 비용")]
    [Tooltip("건설에 걸리는 시간(초)")]
    public float constructionTime = 5f;

    [Tooltip("건설에 필요한 자원 목록")]
    public List<ResourceCost> requiredResources;

    #endregion

    #region 게임플레이 스탯

    [Header("게임플레이 스탯")]
    [Tooltip("건물의 최대 체력")]
    public int maxHealth = 100;

    [Header("이동 설정")]
    [Tooltip("직원의 이동을 막는지 여부")]
    public bool blocksMovement = true;

    [Tooltip("건설 시 아래 타일에 지면이 필요한지 여부.\n" +
             "• true  : 자연 지형 또는 바닥 건물이 바로 아래 있어야 배치 가능 (작업대 등 일반 건물)\n" +
             "• false : 공중·허공에도 배치 가능 (바닥 타일, 사다리, 다리 등 기반 시설)")]
    public bool requiresFloorSupport = true;

    #endregion

    // TODO [건물시스템]: 전력 소모량, 작업 포인트 등 추가 필드 확장 가능
}
