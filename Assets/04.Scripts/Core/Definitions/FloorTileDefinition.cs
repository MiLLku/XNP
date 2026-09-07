using UnityEngine;

/// <summary>
/// 건설되는 바닥 타일 한 종류를 정의하는 에셋.
///
/// 이 에셋이 바닥 타일의 <b>진실의 원천</b>입니다. 코드의 FloorTileType enum은
/// DefinitionEnumGenerator가 이 에셋들을 훑어 자동 생성하므로 직접 수정하지 마세요.
///
/// 예전에는 FloorTile 프리팹마다 이동 속도·통과 여부를 따로 입력해야 했고,
/// 값이 어긋나도 알아챌 방법이 없었습니다. 이제 프리팹은 타입만 고르고
/// 수치는 이 에셋 하나에서 관리합니다.
/// </summary>
[CreateAssetMenu(fileName = "Floor_New", menuName = "XNP/정의/바닥 타일 정의", order = 2)]
public class FloorTileDefinition : ScriptableObject
{
    #region 식별자

    [Header("식별자")]
    [Tooltip("생성될 FloorTileType enum 멤버 이름. 영문으로 시작하는 PascalCase만 허용합니다.")]
    public string codeName = "NewFloor";

    [Tooltip("FloorTileType의 raw int 값.\n프리팹에 직렬화된 값과 직결되므로 한 번 정한 값은 바꾸지 마세요.")]
    public int id = 0;

    [Tooltip("UI·로그에 표시할 이름")]
    public string displayName = "새 바닥";

    [Tooltip("enum 멤버 위에 붙일 설명 주석 (비워도 됩니다)")]
    [TextArea(1, 3)]
    public string description = "";

    #endregion

    #region 이동

    [Header("이동")]
    [Tooltip("이동 속도 배율 (1.0 = 기본 속도)")]
    [Min(0.01f)]
    public float movementSpeedMultiplier = 1f;

    [Tooltip("통과 가능 여부 (사다리류만 true)")]
    public bool isPassable = false;

    [Tooltip("수직 이동 가능 여부 (사다리류만 true)")]
    public bool allowsVerticalMovement = false;

    #endregion

    #region 프로퍼티

    /// <summary>이 정의에 대응하는 enum 값.</summary>
    public FloorTileType Type => (FloorTileType)id;

    /// <summary>UI 표기용 이름 (비어 있으면 codeName).</summary>
    public string Label => string.IsNullOrWhiteSpace(displayName) ? codeName : displayName;

    #endregion
}
