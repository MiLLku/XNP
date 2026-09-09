using UnityEngine;

/// <summary>개체 정의의 큰 갈래. 인스펙터 정리와 검증에만 쓰입니다.</summary>
public enum EntityKind
{
    /// <summary>빈 값 — EntityType.None 처럼 '개체 없음'을 나타내는 예약 정의</summary>
    None = 0,
    /// <summary>식생물 (나무·덤불·침식 식물)</summary>
    Plant = 1,
    /// <summary>건물·구조물</summary>
    Building = 2,
    /// <summary>건설되는 바닥 타일 프리팹</summary>
    FloorPrefab = 3,
}

/// <summary>맵 생성 시 개체를 실제로 놓는 방법.</summary>
public enum PlacementMode
{
    /// <summary>MapEntity를 직접 추가 (1칸 개체)</summary>
    Entity = 0,
    /// <summary>StampLibrary의 스탬프를 찍어 배치 (여러 칸 개체)</summary>
    Stamp = 1,
}

/// <summary>배치 후보 지점을 훑는 방식.</summary>
public enum SurfaceScanMode
{
    /// <summary>열마다 지표면 한 칸만 검사 (나무·덤불)</summary>
    GroundLevel = 0,
    /// <summary>위가 공기인 모든 노출면을 검사 (동굴 안까지 — 침식 식물)</summary>
    AnyExposedSurface = 1,
}

/// <summary>
/// 맵에 배치되는 개체(식생물·건물·바닥 프리팹) 한 종류를 정의하는 에셋.
///
/// 이 에셋이 개체의 <b>진실의 원천</b>입니다. 코드의 EntityType enum은
/// DefinitionEnumGenerator가 이 에셋들을 훑어 자동 생성하므로 직접 수정하지 마세요.
///
/// 식생물의 경우 프리팹 연결뿐 아니라 <b>맵 생성 시 배치 규칙</b>까지 여기서 정합니다.
/// 예전에는 MapGenerator에 하드코딩되어 있던 값들입니다.
/// </summary>
[CreateAssetMenu(fileName = "Entity_New", menuName = "XNP/정의/개체 정의", order = 1)]
public class EntityDefinition : ScriptableObject
{
    #region 식별자

    [Header("식별자")]
    [Tooltip("생성될 EntityType enum 멤버 이름. 영문으로 시작하는 PascalCase만 허용합니다 (예: BerryBush).")]
    public string codeName = "NewEntity";

    [Tooltip("MapEntity.id / StampElement.id에 저장되는 raw int 값.\n세이브 데이터와 직결되므로 한 번 정한 값은 바꾸지 마세요.")]
    public int id = 0;

    [Tooltip("UI·로그에 표시할 이름")]
    public string displayName = "새 개체";

    [Tooltip("enum 멤버 위에 붙일 설명 주석 (비워도 됩니다)")]
    [TextArea(1, 3)]
    public string description = "";

    [Tooltip("인스펙터 정리·검증용 분류")]
    public EntityKind kind = EntityKind.Plant;

    #endregion

    #region 프리팹

    [Header("프리팹")]
    [Tooltip("맵에 인스턴스화될 프리팹")]
    public GameObject prefab;

    #endregion

    #region 맵 생성 배치 (식생물)

    [Header("맵 생성 배치")]
    [Tooltip("체크하면 맵 생성 시 아래 규칙대로 자동 배치됩니다. 건물·바닥 프리팹은 꺼두세요.")]
    public bool spawnOnMapGeneration = false;

    [Tooltip("배치 순서. 작을수록 먼저 자리를 잡습니다 (나무 → 덤불 순).")]
    public int placementOrder = 0;

    [Tooltip("배치 방법. 여러 칸을 차지하면 Stamp를 쓰세요.")]
    public PlacementMode placementMode = PlacementMode.Entity;

    [Tooltip("PlacementMode가 Stamp일 때 사용할 StampLibrary 키 (대소문자 일치 필요)")]
    public string stampKey = "";

    [Tooltip("후보 지점을 훑는 방식")]
    public SurfaceScanMode scanMode = SurfaceScanMode.GroundLevel;

    [Tooltip("가로로 차지하는 칸 수. 2 이상이면 그만큼 평지가 이어져야 배치됩니다.")]
    [Min(1)]
    public int footprintWidth = 1;

    [Tooltip("타일당 배치 확률 (0이면 배치하지 않음)")]
    [Range(0f, 1f)]
    public float spawnChance = 0f;

    [Tooltip("같은 종류끼리 최소 가로 간격. 0이면 제한 없음.")]
    [Min(0)]
    public int minSpacing = 0;

    [Tooltip("이 타일 위에만 배치합니다. 비워두면 스폰 가능한 지표면(TileDefinition.isSpawnableSurface) 전체.")]
    public TileType[] allowedGroundTiles = new TileType[0];

    [Tooltip("시작 지점 주변(MapGenerator.spawnAreaPadding)을 피합니다.")]
    public bool avoidSpawnArea = true;

    [Tooltip("기지에서 이 거리(칸) 이상 떨어진 곳에만 배치합니다. 0이면 제한 없음.\n" +
             "실외 침식이 기지에서 멀수록 오르므로, 이 값으로 '위험 지대에만 나는 식물'을 만듭니다.")]
    [Min(0)] public int minDistanceFromBase = 0;

    [Tooltip("기지에서 이 거리(칸) 이내에만 배치합니다. 0이면 제한 없음.\n" +
             "minDistanceFromBase와 함께 쓰면 특정 거리 띠에만 나는 식생을 만들 수 있습니다.")]
    [Min(0)] public int maxDistanceFromBase = 0;

    #endregion

    #region 세이브

    [Header("세이브")]
    [Tooltip("체크하면 세이브에 위치가 기록되고 로드 시 복원됩니다.")]
    public bool persistInSave = false;

    [Tooltip("성장 진행도를 함께 저장·복원합니다 (ChoppableTree 등).")]
    public bool hasGrowthState = false;

    #endregion

    #region 프로퍼티

    /// <summary>이 정의에 대응하는 enum 값.</summary>
    public EntityType Type => (EntityType)id;

    /// <summary>UI 표기용 이름 (비어 있으면 codeName).</summary>
    public string Label => string.IsNullOrWhiteSpace(displayName) ? codeName : displayName;

    #endregion
}
