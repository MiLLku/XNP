using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 도달 가능성 맵
/// Chunk 기반으로 이동 가능한 영역을 관리하고,
/// 타일 변경 시 해당 영역만 갱신합니다.
/// </summary>
public class ReachabilityMap
{
    #region 설정
    
    public const int CHUNK_SIZE = 16;
    private const int EMPLOYEE_HEIGHT = 2;
    
    #endregion
    
    #region 데이터
    
    private GameMap gameMap;
    
    // 청크별 도달 가능 노드 캐시
    // Key: ChunkCoord, Value: 해당 청크 내에서 서 있을 수 있는 위치들
    private Dictionary<Vector2Int, HashSet<Vector2Int>> chunkReachableNodes;
    
    // 전체 연결 그래프 (어느 위치에서 어느 위치로 이동 가능한지)
    // 성능을 위해 청크 단위로 연결성만 저장
    private Dictionary<Vector2Int, HashSet<Vector2Int>> chunkConnections;
    
    // 더티 플래그 (갱신이 필요한 청크)
    private HashSet<Vector2Int> dirtyChunks;
    
    #endregion
    
    #region 이벤트
    
    public delegate void ReachabilityChangedHandler(Vector2Int chunkCoord);
    public event ReachabilityChangedHandler OnReachabilityChanged;
    
    #endregion
    
    #region 초기화
    
    public ReachabilityMap(GameMap gameMap)
    {
        this.gameMap = gameMap;
        this.chunkReachableNodes = new Dictionary<Vector2Int, HashSet<Vector2Int>>();
        this.chunkConnections = new Dictionary<Vector2Int, HashSet<Vector2Int>>();
        this.dirtyChunks = new HashSet<Vector2Int>();
        
        // 초기 빌드
        BuildInitialMap();
    }
    
    private void BuildInitialMap()
    {
        int chunksX = Mathf.CeilToInt((float)GameMap.MAP_WIDTH / CHUNK_SIZE);
        int chunksY = Mathf.CeilToInt((float)GameMap.MAP_HEIGHT / CHUNK_SIZE);
        
        for (int cx = 0; cx < chunksX; cx++)
        {
            for (int cy = 0; cy < chunksY; cy++)
            {
                Vector2Int chunkCoord = new Vector2Int(cx, cy);
                BuildChunk(chunkCoord);
            }
        }
        
        Debug.Log($"[ReachabilityMap] 초기 빌드 완료: {chunksX * chunksY}개 청크");
    }
    
    #endregion
    
    #region 청크 빌드
    
    /// <summary>
    /// 특정 청크의 도달 가능 노드를 빌드합니다.
    /// </summary>
    private void BuildChunk(Vector2Int chunkCoord)
    {
        int startX = chunkCoord.x * CHUNK_SIZE;
        int startY = chunkCoord.y * CHUNK_SIZE;
        int endX = Mathf.Min(startX + CHUNK_SIZE, GameMap.MAP_WIDTH);
        int endY = Mathf.Min(startY + CHUNK_SIZE, GameMap.MAP_HEIGHT);
        
        HashSet<Vector2Int> reachableNodes = new HashSet<Vector2Int>();
        
        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                if (CanStandAt(x, y))
                {
                    reachableNodes.Add(new Vector2Int(x, y));
                }
            }
        }
        
        chunkReachableNodes[chunkCoord] = reachableNodes;
    }
    
    /// <summary>
    /// 특정 위치에 직원이 서 있을 수 있는지 확인
    /// pos는 발 위치 (발이 있는 빈 공간)입니다.
    /// </summary>
    private bool CanStandAt(int x, int y)
    {
        if (x < 0 || x >= GameMap.MAP_WIDTH || y < 0 || y >= GameMap.MAP_HEIGHT)
            return false;
        
        // 1. 발 아래 타일(y-1)에 지지대가 있어야 함
        int groundY = y - 1;
        if (groundY < 0)
            return false;
        
        int groundTileId = gameMap.TileGrid[x, groundY];
        bool hasGround = groundTileId != 0 || FloorTile.HasFloorTileAt(new Vector2Int(x, groundY));
        
        if (!hasGround)
        {
            // 사다리 체크
            FloorTile ladder = FloorTile.GetFloorTileAt(new Vector2Int(x, groundY));
            if (ladder == null || !ladder.AllowsVerticalMovement())
            {
                // 현재 위치에 사다리가 있는지도 확인
                FloorTile currentLadder = FloorTile.GetFloorTileAt(new Vector2Int(x, y));
                if (currentLadder == null || !currentLadder.AllowsVerticalMovement())
                    return false;
            }
        }
        
        // 2. 몸통 공간(발 위치 + 발 위 1칸)이 비어있어야 함
        for (int i = 0; i < EMPLOYEE_HEIGHT; i++)
        {
            int checkY = y + i;
            if (checkY >= GameMap.MAP_HEIGHT)
                continue;
            
            int tileId = gameMap.TileGrid[x, checkY];
            if (tileId != 0)
            {
                // 바닥 타일(사다리 등)이 통과 가능한지 체크
                FloorTile floorTile = FloorTile.GetFloorTileAt(new Vector2Int(x, checkY));
                if (floorTile == null || !floorTile.IsPassable())
                    return false;
            }
        }
        
        return true;
    }
    
    #endregion
    
    #region 타일 변경 처리
    
    /// <summary>
    /// 타일이 변경되었을 때 호출 (OnTileChanged 이벤트)
    /// </summary>
    public void OnTileChanged(Vector2Int tilePos)
    {
        // 해당 타일이 속한 청크와 주변 청크를 더티로 마킹
        Vector2Int chunkCoord = GetChunkCoord(tilePos);
        
        // 중심 청크
        MarkChunkDirty(chunkCoord);
        
        // 주변 8방향 청크 (경계에 있을 수 있으므로)
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                MarkChunkDirty(chunkCoord + new Vector2Int(dx, dy));
            }
        }
    }
    
    /// <summary>
    /// 여러 타일이 변경되었을 때 호출
    /// </summary>
    public void OnTilesChanged(IEnumerable<Vector2Int> tilePositions)
    {
        foreach (var pos in tilePositions)
        {
            OnTileChanged(pos);
        }
    }
    
    private void MarkChunkDirty(Vector2Int chunkCoord)
    {
        if (chunkCoord.x < 0 || chunkCoord.y < 0)
            return;
        
        int maxChunkX = Mathf.CeilToInt((float)GameMap.MAP_WIDTH / CHUNK_SIZE);
        int maxChunkY = Mathf.CeilToInt((float)GameMap.MAP_HEIGHT / CHUNK_SIZE);
        
        if (chunkCoord.x >= maxChunkX || chunkCoord.y >= maxChunkY)
            return;
        
        dirtyChunks.Add(chunkCoord);
    }
    
    /// <summary>
    /// 더티 청크들을 갱신합니다.
    /// 매 프레임 또는 필요할 때 호출
    /// </summary>
    public void UpdateDirtyChunks()
    {
        if (dirtyChunks.Count == 0)
            return;
        
        foreach (var chunkCoord in dirtyChunks)
        {
            BuildChunk(chunkCoord);
            OnReachabilityChanged?.Invoke(chunkCoord);
        }
        
        Debug.Log($"[ReachabilityMap] {dirtyChunks.Count}개 청크 갱신");
        dirtyChunks.Clear();
    }
    
    /// <summary>
    /// 즉시 갱신이 필요한 경우 (작업 할당 직전 등)
    /// </summary>
    public void ForceUpdate()
    {
        UpdateDirtyChunks();
    }
    
    #endregion
    
    #region 도달 가능성 쿼리
    
    /// <summary>
    /// 시작점에서 목표점까지 도달 가능한지 확인
    /// </summary>
    public bool IsReachable(Vector2Int from, Vector2Int to)
    {
        // 더티 청크가 있으면 먼저 갱신
        if (dirtyChunks.Count > 0)
        {
            ForceUpdate();
        }
        
        // 같은 위치면 true
        if (from == to)
            return true;
        
        // 목표 위치에 서 있을 수 있는지 먼저 확인
        Vector2Int toChunk = GetChunkCoord(to);
        if (!chunkReachableNodes.ContainsKey(toChunk))
            return false;
        
        if (!chunkReachableNodes[toChunk].Contains(to))
            return false;
        
        // 시작 위치가 유효한지
        Vector2Int fromChunk = GetChunkCoord(from);
        if (!chunkReachableNodes.ContainsKey(fromChunk))
            return false;
        
        if (!chunkReachableNodes[fromChunk].Contains(from))
            return false;
        
        // 실제 경로 존재 여부는 Pathfinder에 위임
        // (청크 연결성으로 빠른 필터링 가능하지만, 정확도를 위해 Pathfinder 사용)
        return true;
    }
    
    /// <summary>
    /// 특정 위치에서 도달 가능한 모든 작업 가능 위치 반환
    /// </summary>
    public List<Vector2Int> GetReachableWorkPositions(Vector2Int from, Vector2Int targetTile)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        
        // 타겟 주변 작업 가능 위치들
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -3; dy <= 1; dy++)
            {
                Vector2Int candidate = new Vector2Int(targetTile.x + dx, targetTile.y + dy);
                
                if (candidate == targetTile)
                    continue;
                
                if (IsReachable(from, candidate))
                {
                    result.Add(candidate);
                }
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// 특정 위치에 서 있을 수 있는지 빠르게 확인
    /// </summary>
    public bool CanStandAtPosition(Vector2Int pos)
    {
        Vector2Int chunkCoord = GetChunkCoord(pos);
        
        if (!chunkReachableNodes.ContainsKey(chunkCoord))
            return false;
        
        return chunkReachableNodes[chunkCoord].Contains(pos);
    }
    
    #endregion
    
    #region 유틸리티
    
    private Vector2Int GetChunkCoord(Vector2Int worldPos)
    {
        return new Vector2Int(
            worldPos.x / CHUNK_SIZE,
            worldPos.y / CHUNK_SIZE
        );
    }
    
    /// <summary>
    /// 디버그: 청크 정보 출력
    /// </summary>
    public void DebugPrintChunkInfo(Vector2Int chunkCoord)
    {
        if (!chunkReachableNodes.ContainsKey(chunkCoord))
        {
            Debug.Log($"[ReachabilityMap] 청크 {chunkCoord}: 데이터 없음");
            return;
        }
        
        var nodes = chunkReachableNodes[chunkCoord];
        Debug.Log($"[ReachabilityMap] 청크 {chunkCoord}: {nodes.Count}개 노드");
    }
    
    #endregion
}