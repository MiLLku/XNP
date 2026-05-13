using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 건설 가능한 바닥 타일 (사다리, 나무 바닥, 돌 바닥 등).
/// Building 컴포넌트와 함께 사용되며, 건설/철거 시스템과 연동됩니다.
/// 정적 레지스트리를 통해 위치 기반 바닥 타일 조회가 가능합니다.
/// </summary>
[RequireComponent(typeof(Building))]
public class FloorTile : MonoBehaviour
{
    #region 상수

    private const float DAMAGED_SPEED_MULTIPLIER = 0.5f;

    #endregion

    #region 필드

    [Header("바닥 타일 속성")]
    [Tooltip("이 바닥 타일의 타입")]
    [SerializeField] private FloorTileType tileType;

    [Tooltip("이동 속도 배율 (1.0 = 기본 속도)")]
    [SerializeField] private float movementSpeedMultiplier = 1.0f;

    [Tooltip("통과 가능 여부 (사다리만 true)")]
    [SerializeField] private bool isPassable = false;

    [Tooltip("수직 이동 가능 여부 (사다리만 true)")]
    [SerializeField] private bool allowsVerticalMovement = false;

    private Building building;
    private Vector2Int gridPosition;

    /// <summary>위치별 바닥 타일 정적 레지스트리</summary>
    private static Dictionary<Vector2Int, FloorTile> floorTileRegistry = new Dictionary<Vector2Int, FloorTile>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearStaticRegistry()
    {
        floorTileRegistry.Clear();
    }

    #endregion

    #region 프로퍼티

    /// <summary>바닥 타일 타입</summary>
    public FloorTileType TileType => tileType;

    /// <summary>설치된 그리드 좌표</summary>
    public Vector2Int GridPosition => gridPosition;

    /// <summary>기능 정상 여부</summary>
    public bool IsFunctional => building != null && building.IsFunctional;

    #endregion

    #region 생명주기

    void Awake()
    {
        building = GetComponent<Building>();
    }

    void Start()
    {
        gridPosition = new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y)
        );

        RegisterToRegistry();
    }

    void OnDestroy()
    {
        UnregisterFromRegistry();
    }

    #endregion

    #region 레지스트리

    private void RegisterToRegistry()
    {
        if (!floorTileRegistry.ContainsKey(gridPosition))
        {
            floorTileRegistry[gridPosition] = this;
            Debug.Log($"[FloorTile] 바닥 타일 등록: {tileType} at {gridPosition}");
        }
    }

    private void UnregisterFromRegistry()
    {
        if (floorTileRegistry.ContainsKey(gridPosition))
        {
            floorTileRegistry.Remove(gridPosition);
            Debug.Log($"[FloorTile] 바닥 타일 제거: {tileType} at {gridPosition}");
        }
    }

    /// <summary>
    /// 특정 위치의 바닥 타일을 조회합니다.
    /// </summary>
    /// <param name="position">조회할 그리드 좌표</param>
    /// <returns>바닥 타일 (없으면 null)</returns>
    public static FloorTile GetFloorTileAt(Vector2Int position)
    {
        floorTileRegistry.TryGetValue(position, out FloorTile tile);
        return tile;
    }

    /// <summary>
    /// 특정 위치에 바닥 타일이 있는지 확인합니다.
    /// </summary>
    /// <param name="position">확인할 그리드 좌표</param>
    public static bool HasFloorTileAt(Vector2Int position)
    {
        return floorTileRegistry.ContainsKey(position);
    }

    /// <summary>
    /// 레지스트리를 초기화합니다 (씬 전환 시).
    /// </summary>
    public static void ClearRegistry()
    {
        floorTileRegistry.Clear();
    }

    #endregion

    #region 타일 속성 조회

    /// <summary>
    /// 이동 속도 배율을 반환합니다.
    /// 건물이 비활성화 상태면 절반으로 감소합니다.
    /// </summary>
    public float GetMovementSpeedMultiplier()
    {
        if (building != null && !building.IsFunctional)
        {
            return movementSpeedMultiplier * DAMAGED_SPEED_MULTIPLIER;
        }

        return movementSpeedMultiplier;
    }

    /// <summary>
    /// 통과 가능 여부를 반환합니다.
    /// 건물이 비활성화 상태면 통과 불가입니다.
    /// </summary>
    public bool IsPassable()
    {
        if (building != null && !building.IsFunctional)
        {
            return false;
        }

        return isPassable;
    }

    /// <summary>
    /// 수직 이동 가능 여부를 반환합니다.
    /// 건물이 비활성화 상태면 불가입니다.
    /// </summary>
    public bool AllowsVerticalMovement()
    {
        if (building != null && !building.IsFunctional)
        {
            return false;
        }

        return allowsVerticalMovement;
    }

    #endregion
}

/// <summary>
/// 바닥 타일 타입 열거형.
/// </summary>
public enum FloorTileType
{
    /// <summary>나무 바닥 (속도: 1.0)</summary>
    WoodFloor = 0,
    /// <summary>돌 바닥 (속도: 1.0)</summary>
    StoneFloor = 1,
    /// <summary>금속 바닥 (속도: 1.2)</summary>
    MetalFloor = 2,
    /// <summary>나무 사다리 (속도: 0.8, 수직 이동 가능)</summary>
    WoodLadder = 3,
    /// <summary>금속 사다리 (속도: 0.9, 수직 이동 가능)</summary>
    MetalLadder = 4,
    /// <summary>좁은 통로 (속도: 0.9)</summary>
    Catwalk = 5,
    /// <summary>다리 (속도: 1.0)</summary>
    Bridge = 6,
    /// <summary>가공된 흙 바닥 (속도: 0.9, 기본 인프라 타일)</summary>
    DirtFloor = 7
}
