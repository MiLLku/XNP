using UnityEngine;

/// <summary>
/// 타일별 이동 속도 배율과 통과 가능 여부를 정의합니다.
/// EmployeeMovement에서 타일 위 이동 속도를 결정할 때 참조합니다.
/// </summary>
[System.Serializable]
public class TileMovementData
{
    #region 타일 ID 상수

    private const int AIR_ID            = 0;
    private const int DIRT_ID           = 1;
    private const int STONE_ID          = 2;
    private const int COPPER_ORE_ID     = 3;
    private const int IRON_ORE_ID       = 4;
    private const int GOLD_ORE_ID       = 5;
    private const int GRASS_ID          = 6;
    private const int PROCESSED_DIRT_ID = 7;
    private const int LADDER_ID         = 8;
    private const int COAL_ID           = 9;
    private const int SILVER_ID         = 10;
    private const int CRYSTAL_ID        = 11;

    #endregion

    #region 이동 속도 배율

    [Header("타일 ID별 이동 속도 배율")]
    [Tooltip("AIR - 공중 이동 불가")]
    public float airSpeedMultiplier = 0f;

    [Tooltip("DIRT - 기본 속도")]
    public float dirtSpeedMultiplier = 1f;

    [Tooltip("STONE - 기본 속도")]
    public float stoneSpeedMultiplier = 1f;

    [Tooltip("COPPER_ORE - 기본 속도")]
    public float copperSpeedMultiplier = 1f;

    [Tooltip("IRON_ORE - 기본 속도")]
    public float ironSpeedMultiplier = 1f;

    [Tooltip("GOLD_ORE - 기본 속도")]
    public float goldSpeedMultiplier = 1f;

    [Tooltip("GRASS - 기본 속도")]
    public float grassSpeedMultiplier = 1f;

    [Tooltip("PROCESSED_DIRT - 평탄화된 땅, 빠른 이동")]
    public float processedDirtSpeedMultiplier = 1.2f;

    [Tooltip("LADDER - 사다리, 층간 이동용")]
    public float ladderSpeedMultiplier = 0.8f;

    [Tooltip("COAL - 석탄, 기본 속도")]
    public float coalSpeedMultiplier = 1f;

    [Tooltip("SILVER - 은, 기본 속도")]
    public float silverSpeedMultiplier = 1f;

    [Tooltip("CRYSTAL - 수정, 기본 속도")]
    public float crystalSpeedMultiplier = 1f;

    #endregion

    #region 공개 API

    /// <summary>
    /// 타일 ID에 따른 이동 속도 배율을 반환합니다.
    /// </summary>
    /// <param name="tileId">타일 ID</param>
    /// <returns>이동 속도 배율 (0이면 이동 불가)</returns>
    public float GetSpeedMultiplier(int tileId)
    {
        switch (tileId)
        {
            case AIR_ID: return airSpeedMultiplier;
            case DIRT_ID: return dirtSpeedMultiplier;
            case STONE_ID: return stoneSpeedMultiplier;
            case COPPER_ORE_ID: return copperSpeedMultiplier;
            case IRON_ORE_ID: return ironSpeedMultiplier;
            case GOLD_ORE_ID: return goldSpeedMultiplier;
            case GRASS_ID: return grassSpeedMultiplier;
            case PROCESSED_DIRT_ID: return processedDirtSpeedMultiplier;
            case LADDER_ID:  return ladderSpeedMultiplier;
            case COAL_ID:    return coalSpeedMultiplier;
            case SILVER_ID:  return silverSpeedMultiplier;
            case CRYSTAL_ID: return crystalSpeedMultiplier;
            default: return 1f;
        }
    }

    /// <summary>
    /// 타일을 통과할 수 있는지 확인합니다.
    /// AIR와 LADDER만 통과 가능합니다.
    /// </summary>
    /// <param name="tileId">타일 ID</param>
    public bool IsPassable(int tileId)
    {
        return tileId == AIR_ID || tileId == LADDER_ID;
    }

    /// <summary>
    /// 타일 위를 걸을 수 있는지 확인합니다.
    /// AIR를 제외한 모든 타일은 발판이 될 수 있습니다.
    /// </summary>
    /// <param name="tileId">타일 ID</param>
    public bool IsWalkable(int tileId)
    {
        return tileId != AIR_ID;
    }

    /// <summary>
    /// 사다리 타일인지 확인합니다.
    /// </summary>
    /// <param name="tileId">타일 ID</param>
    public bool IsLadder(int tileId)
    {
        return tileId == LADDER_ID;
    }

    #endregion
}
