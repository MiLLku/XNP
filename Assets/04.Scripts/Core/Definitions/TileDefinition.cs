using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 블록(지형 타일) 한 종류를 정의하는 에셋.
///
/// 이 에셋이 타일의 <b>진실의 원천</b>입니다. 코드의 TileType enum은
/// DefinitionEnumGenerator가 이 에셋들을 훑어 자동 생성하므로 직접 수정하지 마세요.
/// 새 블록을 추가하려면 이 에셋을 만들고 DefinitionDatabase에 등록한 뒤
/// 메뉴 [XNP/정의/enum 재생성]을 실행하면 됩니다.
///
/// 예전에 코드에 흩어져 있던 TileHardness / TileConductivity / TileHeatOutput /
/// MiningSkillGate의 switch 값들이 모두 이 에셋의 필드로 들어왔습니다.
/// </summary>
[CreateAssetMenu(fileName = "Tile_New", menuName = "XNP/정의/타일 정의", order = 0)]
public class TileDefinition : ScriptableObject
{
    #region 식별자

    [Header("식별자")]
    [Tooltip("생성될 TileType enum 멤버 이름. 영문으로 시작하는 PascalCase만 허용합니다 (예: IronOre).")]
    public string codeName = "NewTile";

    [Tooltip("GameMap.TileGrid에 저장되는 raw int 값.\n세이브 데이터와 직결되므로 한 번 정한 값은 바꾸지 마세요.")]
    public int id = 0;

    [Tooltip("UI·로그에 표시할 이름")]
    public string displayName = "새 타일";

    [Tooltip("enum 멤버 위에 붙일 설명 주석 (비워도 됩니다)")]
    [TextArea(1, 3)]
    public string description = "";

    #endregion

    #region 시각

    [Header("시각")]
    [Tooltip("Tilemap에 그려질 TileBase 에셋. 비워두면 그려지지 않습니다(공기).")]
    public TileBase tileAsset;

    #endregion

    #region 통행

    [Header("통행")]
    [Tooltip("체크 해제하면 직원이 통과할 수 있습니다 (공기·사다리).")]
    public bool isSolid = true;

    [Tooltip("사다리처럼 수직 이동이 가능한 타일")]
    public bool isClimbable = false;

    [Tooltip("직원 스폰·식생물 배치가 가능한 지표면 타일 (흙·잔디흙)")]
    public bool isSpawnableSurface = false;

    #endregion

    #region 채광

    [Header("채광")]
    [Tooltip("채광 지시를 내릴 수 있는 타일인지")]
    public bool isMineable = true;

    [Tooltip("채광 소요 시간 배율. 돌 = 1.0 기준.\n깊은 층 광물일수록 높게 두어 시간 비용으로 진행도를 만듭니다.")]
    [Min(0.01f)]
    public float hardness = 1f;

    [Tooltip("채광에 필요한 스킬 ID (SkillTreeConfig 기준). 0이면 제한 없음.\n채광 I=5, 채광 II=6, 채광 III=7")]
    public int requiredMiningSkillId = 0;

    [Tooltip("채광 시 드롭될 ItemData. 시각 프리팹은 ItemData.dropPrefab을 씁니다.")]
    public ItemData dropItem;

    #endregion

    #region 광맥 생성

    [Header("광맥 생성")]
    [Tooltip("체크하면 맵 생성 시 돌 속에 이 광물의 군집이 생성됩니다.\n" +
             "예전에 MapGenerator.PlaceMineralClusters에 하드코딩되어 있던 값들입니다.")]
    public bool generateAsVein = false;

    [Tooltip("지표면 기준 얕은 한계 (음수). -3이면 지표에서 3칸 아래부터 나옵니다.")]
    public int veinShallowOffset = -3;

    [Tooltip("지표면 기준 깊은 한계 (음수). shallowOffset보다 작아야 합니다.")]
    public int veinDeepOffset = -20;

    [Tooltip("씨앗 위치를 고르는 Perlin noise 스케일. 클수록 잘게 흩어집니다.")]
    [Range(0.01f, 0.3f)]
    public float veinNoiseScale = 0.12f;

    [Tooltip("씨앗이 되는 noise 임계값. 높을수록 희귀해집니다.")]
    [Range(0f, 1f)]
    public float veinThreshold = 0.65f;

    [Tooltip("광물마다 다른 패턴이 나오도록 주는 noise 오프셋. 광물끼리 서로 다른 값이면 됩니다.")]
    public float veinSeedOffset = 0f;

    #endregion

    #region 온도

    [Header("온도")]
    [Tooltip("열 전도율 배율 — 방의 벽을 통해 열이 새는 속도. 낮을수록 단열이 잘 됩니다. 돌 = 1.0")]
    [Min(0f)]
    public float thermalConductivity = 1f;

    [Tooltip("접촉면 하나가 방에 넣는 초당 열량. 0이면 발열하지 않습니다.")]
    public float heatOutput = 0f;

    [Tooltip("목표 온도를 쓸지. 끄면 상한 없이 계속 데웁니다. heatOutput이 0이면 의미 없습니다.")]
    public bool useHeatTarget = false;

    [Tooltip("이 타일이 방을 데울 수 있는 한계 온도(℃). 방이 이 온도에 닿으면 더는 올리지 않습니다.")]
    public float heatTargetTemperature = 60f;

    #endregion

    #region 프로퍼티

    /// <summary>이 정의에 대응하는 enum 값.</summary>
    public TileType Type => (TileType)id;

    /// <summary>UI 표기용 이름 (비어 있으면 codeName).</summary>
    public string Label => string.IsNullOrWhiteSpace(displayName) ? codeName : displayName;

    #endregion
}
