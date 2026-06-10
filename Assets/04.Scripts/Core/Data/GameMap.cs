using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 맵에 배치될 개체(건물, 적 등)를 표현하는 구조체.
/// </summary>
public struct MapEntity
{
    /// <summary>맵 상의 타일 좌표</summary>
    public Vector2Int position;

    /// <summary>개체 종류</summary>
    public TypeObjectTile type;

    /// <summary>개체 고유 ID</summary>
    public int id;
}

/// <summary>
/// 게임 맵 데이터 클래스.
/// 타일 그리드, 벽 그리드, 점유 상태, 이동 차단 등 맵의 핵심 데이터를 관리합니다.
///
/// 타일 ID 규칙:
///   0=AIR, 1=DIRT, 2=STONE, 3=COPPER, 4=IRON, 5=GOLD, 6=GRASS, 7=PROCESSED_DIRT, 8=LADDER
/// </summary>
public class GameMap
{
    #region 상수

    public const int MAP_WIDTH = 200;
    public const int MAP_HEIGHT = 200;
    private const int AIR_ID = 0;
    private const int DIRT_ID = 1;
    private const int GRASS_ID = 6;
    private const int LADDER_ID = 8;

    #endregion

    #region 프로퍼티

    /// <summary>타일 ID 2D 배열 (지형)</summary>
    public int[,] TileGrid { get; private set; }

    /// <summary>벽 ID 2D 배열 (배경)</summary>
    public int[,] WallGrid { get; private set; }

    /// <summary>맵에 배치된 개체 목록</summary>
    public List<MapEntity> Entities { get; private set; }

    /// <summary>타일 점유 상태 (건물 등이 차지하고 있는지)</summary>
    public bool[,] OccupiedGrid { get; private set; }

    /// <summary>이동 차단 상태 (점유된 타일이 이동을 막는지)</summary>
    public bool[,] BlocksMovementGrid { get; private set; }

    /// <summary>
    /// 바닥 지지 그리드.
    /// 완성된 바닥 건물(blocksMovement=false)이 놓인 타일만 true입니다.
    /// 건설 예정지(ConstructionSite)는 포함되지 않으므로,
    /// 경로탐색기가 건설 예정지 위를 발판으로 오인하지 않습니다.
    /// </summary>
    public bool[,] FloorSupportGrid { get; private set; }

    /// <summary>
    /// 내비게이션 버전 카운터.
    /// 지형이 변할 때마다 1씩 증가합니다.
    /// EmployeeMovement가 이 값을 감지해 이동 중 경로를 재탐색합니다.
    /// </summary>
    public int NavVersion { get; private set; } = 0;

    #endregion

    #region 초기화

    public GameMap()
    {
        TileGrid = new int[MAP_WIDTH, MAP_HEIGHT];
        WallGrid = new int[MAP_WIDTH, MAP_HEIGHT];
        Entities = new List<MapEntity>();
        OccupiedGrid = new bool[MAP_WIDTH, MAP_HEIGHT];
        BlocksMovementGrid = new bool[MAP_WIDTH, MAP_HEIGHT];
        FloorSupportGrid = new bool[MAP_WIDTH, MAP_HEIGHT];
    }

    #endregion

    #region 범위 검사

    private bool IsInBounds(int x, int y)
    {
        return x >= 0 && x < MAP_WIDTH && y >= 0 && y < MAP_HEIGHT;
    }

    #endregion

    #region 타일/벽 설정

    /// <summary>
    /// 지정 좌표의 타일 ID를 설정합니다.
    /// </summary>
    public void SetTile(int x, int y, int tileId)
    {
        if (IsInBounds(x, y))
        {
            TileGrid[x, y] = tileId;
            NavVersion++;
        }
    }

    /// <summary>
    /// 지정 좌표의 벽 ID를 설정합니다.
    /// </summary>
    public void SetWall(int x, int y, int wallId)
    {
        if (IsInBounds(x, y)) WallGrid[x, y] = wallId;
    }

    /// <summary>
    /// 맵 개체를 추가합니다.
    /// </summary>
    public void AddEntity(MapEntity entity)
    {
        if (IsInBounds(entity.position.x, entity.position.y))
        {
            Entities.Add(entity);
        }
    }

    /// <summary>
    /// 맵 개체 목록을 초기화합니다.
    /// 세이브 복원 전 기존 데이터 제거 시 사용합니다.
    /// </summary>
    public void ClearEntities()
    {
        Entities.Clear();
    }

    #endregion

    #region 타일 상태 조회

    /// <summary>
    /// 해당 좌표가 단단한 지면인지 확인합니다.
    /// 자연 지형 타일 또는 완성된 바닥 건물이 있으면 지면으로 판정합니다.
    /// 건설 예정지(ConstructionSite)는 포함되지 않습니다.
    /// </summary>
    public bool IsSolidGround(int x, int y)
    {
        if (!IsInBounds(x, y)) return false;
        // 자연 지형 타일 (공기가 아닌 모든 타일: 흙, 돌, 사다리 등)
        if (TileGrid[x, y] != AIR_ID) return true;
        // 완성된 바닥 건물 (FloorSupportGrid에 등록된 것만 — 건설 예정지 제외)
        if (FloorSupportGrid[x, y]) return true;
        // 건설된 바닥 타일도 발판으로 인정한다.
        // 바닥 타일은 blocksMovement=true로 설정되어 FloorSupportGrid에 등록되지 않으므로
        // FloorTile 정적 레지스트리로 직접 확인한다(완성된 바닥 타일만 등록되며 건설 예정지는 제외).
        if (FloorTile.HasFloorTileAt(new Vector2Int(x, y))) return true;
        return false;
    }

    /// <summary>
    /// 해당 좌표에 건물 등을 배치할 수 있는지 확인합니다.
    /// 공기 타일이고 점유되지 않은 경우에만 가능합니다.
    /// </summary>
    public bool IsSpaceAvailable(int x, int y)
    {
        if (!IsInBounds(x, y)) return false;
        return TileGrid[x, y] == AIR_ID && !IsTileOccupied(x, y);
    }

    /// <summary>
    /// 해당 좌표에 직원을 스폰할 수 있는지 확인합니다.
    /// DIRT 또는 GRASS 타일이고 점유되지 않은 경우에만 가능합니다.
    /// </summary>
    public bool IsTileSpawnable(int x, int y)
    {
        if (!IsInBounds(x, y)) return false;
        if (IsTileOccupied(x, y)) return false;

        int tileID = TileGrid[x, y];
        return (tileID == DIRT_ID || tileID == GRASS_ID);
    }

    /// <summary>
    /// 타일이 통과 가능한지 확인합니다 (AIR 또는 LADDER).
    /// </summary>
    public bool IsPassableTile(int x, int y)
    {
        if (!IsInBounds(x, y)) return false;
        int tileId = TileGrid[x, y];
        return tileId == AIR_ID || tileId == LADDER_ID;
    }

    #endregion

    #region 점유 관리

    /// <summary>
    /// 타일을 점유 상태로 표시합니다.
    /// </summary>
    /// <param name="x">타일 X 좌표</param>
    /// <param name="y">타일 Y 좌표</param>
    /// <param name="blocksMovement">이동을 차단하는지 여부</param>
    public void MarkTileOccupied(int x, int y, bool blocksMovement = true)
    {
        if (IsInBounds(x, y))
        {
            OccupiedGrid[x, y] = true;
            BlocksMovementGrid[x, y] = blocksMovement;
            NavVersion++;
        }
    }

    /// <summary>
    /// 타일의 점유 상태를 해제합니다.
    /// FloorSupportGrid도 함께 초기화됩니다.
    /// </summary>
    public void UnmarkTileOccupied(int x, int y)
    {
        if (IsInBounds(x, y))
        {
            OccupiedGrid[x, y] = false;
            BlocksMovementGrid[x, y] = false;
            FloorSupportGrid[x, y] = false;
            NavVersion++;
        }
    }

    /// <summary>
    /// 완성된 바닥 건물의 발판 지지 여부를 설정합니다.
    /// Building.RegisterToGameMap()에서 blocksMovement=false인 건물이 완공될 때 호출합니다.
    /// </summary>
    public void MarkFloorSupport(int x, int y, bool isSupport)
    {
        if (IsInBounds(x, y))
            FloorSupportGrid[x, y] = isSupport;
    }

    /// <summary>
    /// 해당 타일이 완성된 바닥 건물로 발판을 제공하는지 확인합니다.
    /// 건설 예정지는 false를 반환합니다.
    /// </summary>
    public bool IsFloorSupport(int x, int y)
    {
        if (!IsInBounds(x, y)) return false;
        return FloorSupportGrid[x, y];
    }

    /// <summary>
    /// 타일이 점유되어 있는지 확인합니다.
    /// 범위 밖은 점유된 것으로 간주합니다.
    /// </summary>
    public bool IsTileOccupied(int x, int y)
    {
        if (!IsInBounds(x, y)) return true;
        return OccupiedGrid[x, y];
    }

    /// <summary>
    /// 타일이 이동을 차단하는지 확인합니다.
    /// 범위 밖은 차단되는 것으로 간주합니다.
    /// </summary>
    public bool DoesTileBlockMovement(int x, int y)
    {
        if (!IsInBounds(x, y)) return true;
        return BlocksMovementGrid[x, y];
    }

    #endregion

    #region 사다리

    /// <summary>
    /// 사다리 타일인지 확인합니다.
    /// </summary>
    public bool IsLadder(int x, int y)
    {
        if (!IsInBounds(x, y)) return false;
        return TileGrid[x, y] == LADDER_ID;
    }

    /// <summary>
    /// 공기 타일에 사다리를 설치합니다.
    /// </summary>
    public void PlaceLadder(int x, int y)
    {
        if (!IsInBounds(x, y)) return;
        if (TileGrid[x, y] == AIR_ID)
        {
            SetTile(x, y, LADDER_ID); // SetTile 내부에서 NavVersion++ 처리
        }
    }

    /// <summary>
    /// 사다리를 제거하고 공기 타일로 되돌립니다.
    /// </summary>
    public void RemoveLadder(int x, int y)
    {
        if (!IsInBounds(x, y)) return;
        if (IsLadder(x, y))
        {
            SetTile(x, y, AIR_ID); // SetTile 내부에서 NavVersion++ 처리
        }
    }

    #endregion

    #region 디버그

    /// <summary>
    /// 지정 범위의 타일 맵을 콘솔에 출력합니다.
    /// </summary>
    /// <param name="centerX">중심 X 좌표</param>
    /// <param name="centerY">중심 Y 좌표</param>
    /// <param name="range">표시 범위</param>
    public void PrintDebugMap(int centerX, int centerY, int range)
    {
        for (int y = centerY + range; y >= centerY - range; y--)
        {
            string line = $"{y:D3} | ";
            for (int x = centerX - range * 2; x <= centerX + range * 2; x++)
            {
                if (!IsInBounds(x, y)) continue;
                int tileId = TileGrid[x, y];
                line += (tileId == 0 ? "." : tileId.ToString());
            }
            Debug.Log(line);
        }
    }

    #endregion
}
