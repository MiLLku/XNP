using UnityEngine;

/// <summary>
/// 지층(Strata) 정의 — 깊이 한 구간의 지형 성격.
///
/// 예전에는 맵 전체가 같은 파라미터였습니다(언덕 노이즈 1개 + 동굴 노이즈 1개 + 흙 노이즈 1개).
/// 깊이에 따라 바뀌는 것은 <b>어느 광물이 나오는가</b> 하나뿐이라, 140칸을 파 내려가도
/// 눈에 보이는 변화가 광물 색깔밖에 없었습니다.
///
/// 이 게임의 진행 주축은 깊이입니다(재료 티어·채광 스킬 게이트·지열이 전부 깊이로 걸립니다).
/// 지층은 그 축을 <b>지형으로 보이게</b> 만들고, 동시에 다른 모든 다양화의 좌표계가 됩니다 —
/// 특수 구역·벤트·강적·발원지 밀도가 전부 "몇 층에서"를 필요로 하기 때문입니다.
///
/// <b>두께는 비율로 정합니다.</b> 절대 Y를 박으면 맵 높이를 바꿀 때 전부 다시 계산해야 하므로,
/// <see cref="thicknessWeight"/>의 합으로 맵 높이를 나눠 경계를 구합니다.
/// 목록의 <b>순서가 위에서 아래</b>입니다(첫 항목이 하늘, 마지막이 최하층).
/// </summary>
[CreateAssetMenu(fileName = "Strata_New", menuName = "XNP/정의/지층 정의", order = 3)]
public class StrataDefinition : ScriptableObject
{
    #region 식별

    [Header("식별")]
    [Tooltip("코드에서 쓰는 이름. 영문/숫자/밑줄만.")]
    public string codeName = "NewStrata";

    [Tooltip("UI·디버그에 보일 이름")]
    public string displayName = "새 지층";

    [TextArea(2, 4)]
    [Tooltip("이 층의 설계 의도 메모")]
    public string description = "";

    #endregion

    #region 두께

    [Header("두께")]
    [Tooltip("전체에서 이 층이 차지하는 비율 가중치. 모든 지층의 합으로 맵 높이를 나눠 경계를 구합니다. " +
             "0이면 이 층은 생성되지 않습니다.")]
    [Min(0)] public int thicknessWeight = 4;

    #endregion

    #region 지형

    [Header("지형")]
    [Tooltip("이 층의 지형을 만드는 방식.\n" +
             "• 암반+동굴: 기반 암석을 채우고 노이즈로 동굴을 파냅니다(보통의 층).\n" +
             "• 덩굴망: 빈 공간에 굵은 줄기가 얽힌 구조를 만듭니다(최하층).")]
    public StrataTerrainMode terrainMode = StrataTerrainMode.SolidWithCaves;

    [Tooltip("이 층을 채우는 기반 암석 타일. 하늘처럼 비어 있는 층은 Air로 둡니다.\n" +
             "⚠️ 광맥은 이 타일 위에만 씨앗을 심으므로, 여기가 Stone이 아니면 그 층에는 광맥이 생기지 않습니다.")]
    public TileType baseRock = TileType.Stone;

    [Tooltip("기반 암석 대신 흙이 섞여 나오는 비율의 임계값. 낮을수록 흙이 많아집니다. " +
             "1 이상이면 흙이 전혀 섞이지 않습니다.")]
    [Range(0f, 1.01f)] public float dirtThreshold = 0.5f;

    [Tooltip("흙 덩어리 노이즈의 스케일. 클수록 잘게 흩어집니다.")]
    [Range(0.01f, 0.2f)] public float dirtNoiseScale = 0.08f;

    #endregion

    #region 동굴

    [Header("동굴")]
    [Tooltip("이 값을 넘는 노이즈 칸이 빈 공간이 됩니다. 낮출수록 동굴이 많아집니다. " +
             "1 이상이면 동굴이 생기지 않습니다.")]
    [Range(0f, 1.01f)] public float caveThreshold = 0.7f;

    [Tooltip("동굴 노이즈의 스케일. <b>작을수록 거대한 공동</b>, 클수록 잘게 흩어진 굴이 됩니다.\n" +
             "층마다 이 값만 달리해도 '얕은 층=잔 동굴 / 깊은 층=거대 공동'이 바로 체감됩니다.")]
    [Range(0.01f, 0.2f)] public float caveNoiseScale = 0.07f;

    [Tooltip("동굴을 가로로 늘이는 배율. 1이면 둥근 공동, 크면 가로로 길쭉해집니다.")]
    [Range(1f, 12f)] public float caveStretch = 1f;

    [Tooltip("눈동자 모양으로 깎는 정도(칸). 0이면 그냥 타원형 공동입니다.\n" +
             "같은 노이즈를 위아래로 이만큼 어긋나게 두 번 뽑아 <b>둘 다 열린 곳만</b> 파냅니다. " +
             "두 타원의 교집합이라 위아래가 눌리고 <b>좌우 끝이 뾰족한 렌즈</b>가 됩니다.\n" +
             "임계값만 올리면 공동이 작아질 뿐 여전히 둥그므로, 뾰족하게 만들려면 이 값을 씁니다.")]
    [Range(0f, 12f)] public float caveLensSplit = 0f;

    #endregion

    #region 덩굴망 (terrainMode = FilamentWeb 일 때만)

    [Header("덩굴망")]
    [Tooltip("줄기를 이루는 타일")]
    public TileType filamentTile = TileType.Stone;

    [Tooltip("줄기 무늬의 스케일. 작을수록 굵고 성기게, 클수록 잘고 촘촘하게 얽힙니다.")]
    [Range(0.01f, 0.2f)] public float filamentScale = 0.055f;

    [Tooltip("줄기 굵기(0~1). 높일수록 두꺼워집니다. 2~3칸을 노리면 0.2 근처입니다.")]
    [Range(0.02f, 0.8f)] public float filamentThickness = 0.2f;

    [Tooltip("줄기를 비트는 정도(칸). 0이면 매끈한 곡선, 크면 뒤엉킨 뿌리처럼 됩니다.")]
    [Range(0f, 40f)] public float filamentWarp = 14f;

    [Tooltip("줄기가 두 겹으로 교차하게 할지. 켜면 서로 다른 방향의 망이 겹쳐 더 얽혀 보입니다.")]
    public bool filamentSecondLayer = true;

    #endregion

    #region 환경

    [Header("환경")]
    [Tooltip("이 층의 기본 침식 농도. 방이 아닌 칸(실외·갱도)을 조회할 때 쓰입니다. " +
             "0이면 지층 보정 없이 전역 실외 침식을 씁니다.")]
    [Min(0f)] public float baseErosion = 0f;

    [Tooltip("이 층의 주변 온도에 더해지는 보정(℃). 보통 0으로 두고 깊이 지열에 맡깁니다.")]
    public float temperatureOffset = 0f;

    #endregion

    /// <summary>UI 표시용 이름 (displayName이 비면 codeName)</summary>
    public string Label => string.IsNullOrWhiteSpace(displayName) ? codeName : displayName;

    /// <summary>기반 암석의 raw 타일 ID</summary>
    public int BaseRockId => (int)baseRock;

    /// <summary>줄기 타일의 raw ID</summary>
    public int FilamentTileId => (int)filamentTile;

    /// <summary>광맥 씨앗을 받을 수 있는 층인지 — 덩굴망 층은 기반 암석이 없어 광맥이 생기지 않습니다.</summary>
    public bool HostsVeins => terrainMode == StrataTerrainMode.SolidWithCaves;
}

/// <summary>지층의 지형 생성 방식</summary>
public enum StrataTerrainMode
{
    /// <summary>기반 암석을 채우고 노이즈로 동굴을 파낸다 (보통의 층)</summary>
    SolidWithCaves = 0,

    /// <summary>빈 공간에 굵은 줄기가 얽힌 구조를 만든다 (최하층 덩굴 바이옴)</summary>
    FilamentWeb = 1,
}
