using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
    [Tooltip("건설에 필요한 총 작업량.\n" +
             "직원이 초당 투입하는 작업량(작업 속도)만큼 깎여 나가며, 0이 되면 완성됩니다.\n" +
             "작업 속도 1인 직원 기준으로는 이 값이 곧 초 단위 소요 시간과 같습니다.\n" +
             "진행도는 건설 현장에 누적되므로 직원이 떠나도 사라지지 않습니다.")]
    [FormerlySerializedAs("constructionTime")]
    public float workAmount = 5f;

    [Tooltip("건설에 필요한 자원 목록")]
    public List<ResourceCost> requiredResources;

    #endregion

    #region 게임플레이 스탯

    [Header("게임플레이 스탯")]
    [Tooltip("건물의 최대 체력")]
    public int maxHealth = 100;

    [Header("이동 설정")]
    [Tooltip("직원의 이동을 막는지 여부.\n" +
             "• true  : 차단형 — 직원과 절대 겹칠 수 없고, 대신 지형처럼 위를 밟고 지나갈 수 있습니다.\n" +
             "• false : 통과형 — 직원이 겹쳐 지나갈 수 있지만 위는 발판이 아닙니다 (가구·오락시설 등).\n" +
             "  통과 중 감속은 movementSpeedMultiplier로 설정합니다.\n" +
             "  (문처럼 '시간을 소모해 통과'하는 조건부 통과는 false + Door 컴포넌트 조합)")]
    public bool blocksMovement = true;

    [Tooltip("건설 시 아래 타일에 지면이 필요한지 여부.\n" +
             "• true  : 자연 지형 또는 바닥 건물이 바로 아래 있어야 배치 가능 (작업대 등 일반 건물)\n" +
             "• false : 공중·허공에도 배치 가능 (바닥 타일, 사다리, 다리 등 기반 시설)")]
    public bool requiresFloorSupport = true;

    [Tooltip("이 건물 위를 지나갈 때 적용할 이동속도 배율 (1.0=기본, <1 감속).\n" +
             "통과 가능(blocksMovement=false) 건물에만 의미 — 울타리·사다리 등.")]
    public float movementSpeedMultiplier = 1.0f;

    [Tooltip("사다리처럼 수직 이동(층 이동)을 허용하는지 여부.")]
    public bool allowsVerticalMovement = false;

    #endregion

    #region 파괴 페이백

    [Header("파괴/철거 페이백")]
    [Tooltip("건물 파괴·철거 시 바닥에 드롭되는 재료 목록. 비어있으면 requiredResources × paybackRatio로 자동 계산됩니다.")]
    public List<ResourceCost> destructionDrops = new List<ResourceCost>();

    [Tooltip("destructionDrops가 비어있을 때 사용할 페이백 비율 (0~1). 0.5 = 건설 비용의 50%")]
    [Range(0f, 1f)]
    public float paybackRatio = 0.5f;

    #endregion

    #region 전력

    [Header("전력")]
    [Tooltip("이 건물이 가동에 필요로 하는 전력(W). 0이면 전력을 소비하지 않습니다.\n" +
             "0보다 크면 프리팹에 PowerConsumer 컴포넌트를 함께 부착하세요.")]
    public int powerConsumption = 0;

    [Tooltip("기존 건축물(바닥·벽·일반 건물)과 같은 타일에 겹쳐 설치할 수 있는지 여부.\n" +
             "• true  : 점유 그리드를 사용하지 않는 오버레이 설치물 (전선 등). 별도 레지스트리로 위치를 관리합니다.\n" +
             "• false : 일반 건물 (한 타일을 단독 점유).")]
    public bool allowOverlap = false;

    #endregion
}
