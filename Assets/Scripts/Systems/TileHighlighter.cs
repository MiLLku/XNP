using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using System.Linq;

/// <summary>
/// 타일맵에 하이라이트 효과를 표시하는 시스템
/// 채광/수확/철거 모드에서 선택된 타일을 시각적으로 표시합니다.
/// </summary>
public class TileHighlighter : DestroySingleton<TileHighlighter>
{
    [Header("Tilemap References")]
    [SerializeField] private Tilemap highlightTilemap; // 하이라이트 전용 타일맵
    [SerializeField] private TileBase highlightTile; // 하이라이트용 타일
    
    [Header("Highlight Colors")]
    [SerializeField] private Color hoverColor = new Color(1f, 1f, 0.5f, 0.5f); // 호버 시 노란색
    [SerializeField] private Color selectedColor = new Color(1f, 0.8f, 0.3f, 0.7f); // 선택 시 주황색
    [SerializeField] private Color assignedColor = new Color(0.8f, 0.8f, 0.3f, 0.5f); // 작업 배정 시 어두운 노란색
    
    [Header("Auto-cleanup Settings")]
    [SerializeField] private float cleanupCheckInterval = 0.5f; // 타일 상태 체크 간격
    
    // 하이라이트 상태 관리
    private HashSet<Vector3Int> hoveredTiles = new HashSet<Vector3Int>();
    private HashSet<Vector3Int> selectedTiles = new HashSet<Vector3Int>();
    private Dictionary<Vector3Int, Color> assignedTiles = new Dictionary<Vector3Int, Color>();
    
    private GameMap gameMap;
    
    protected override void Awake()
    {
        base.Awake();
        
        if (highlightTilemap == null)
        {
            Debug.LogError("[TileHighlighter] Highlight Tilemap이 설정되지 않았습니다!");
        }
        
        if (highlightTile == null)
        {
            Debug.LogWarning("[TileHighlighter] Highlight Tile이 설정되지 않았습니다.");
        }
    }
    
    void Start()
    {
        if (MapGenerator.instance != null)
        {
            gameMap = MapGenerator.instance.GameMapInstance;
        }
        
        // 주기적으로 타일 상태 체크
        InvokeRepeating(nameof(CleanupInvalidTiles), cleanupCheckInterval, cleanupCheckInterval);
    }
    
    void OnDestroy()
    {
        CancelInvoke();
    }
    
    #region Hover (호버)
    
    /// <summary>
    /// 호버 타일 설정
    /// </summary>
    public void SetHoveredTiles(List<Vector3Int> tiles)
    {
        // 이전 호버 제거
        ClearHovered();
        
        // 새 호버 적용
        foreach (var tile in tiles)
        {
            // 이미 선택되거나 배정된 타일은 호버하지 않음
            if (selectedTiles.Contains(tile) || assignedTiles.ContainsKey(tile))
                continue;
            
            // 타일이 실제로 존재하는지 확인
            if (!IsTileValid(tile))
                continue;
            
            hoveredTiles.Add(tile);
            SetTileColor(tile, hoverColor);
        }
    }
    
    /// <summary>
    /// 호버 제거
    /// </summary>
    public void ClearHovered()
    {
        foreach (var tile in hoveredTiles.ToList())
        {
            // 선택되거나 배정된 타일이 아니면 제거
            if (!selectedTiles.Contains(tile) && !assignedTiles.ContainsKey(tile))
            {
                highlightTilemap.SetTile(tile, null);
            }
        }
        hoveredTiles.Clear();
    }
    
    #endregion
    
    #region Selected (선택됨)
    
    /// <summary>
    /// 선택된 타일 추가
    /// </summary>
    public void AddSelectedTiles(List<Vector3Int> tiles)
    {
        foreach (var tile in tiles)
        {
            // 타일이 유효한지 확인
            if (!IsTileValid(tile))
                continue;
            
            if (!selectedTiles.Contains(tile))
            {
                selectedTiles.Add(tile);
                SetTileColor(tile, selectedColor);
            }
        }
    }
    
    /// <summary>
    /// 선택 제거
    /// </summary>
    public void ClearSelected()
    {
        foreach (var tile in selectedTiles.ToList())
        {
            // 배정된 타일이 아니면 제거
            if (!assignedTiles.ContainsKey(tile))
            {
                highlightTilemap.SetTile(tile, null);
            }
        }
        selectedTiles.Clear();
    }
    
    #endregion
    
    #region Assigned (작업 배정됨)
    
    /// <summary>
    /// 작업이 배정된 타일로 전환 (선택 → 배정)
    /// </summary>
    public void ConvertSelectedToAssigned()
    {
        foreach (var tile in selectedTiles.ToList())
        {
            if (IsTileValid(tile))
            {
                assignedTiles[tile] = assignedColor;
                SetTileColor(tile, assignedColor);
            }
        }
        selectedTiles.Clear();
    }
    
    /// <summary>
    /// 특정 타일을 배정됨으로 표시
    /// </summary>
    public void AddAssignedTiles(List<Vector3Int> tiles, Color? customColor = null)
    {
        Color color = customColor ?? assignedColor;
        
        foreach (var tile in tiles)
        {
            if (IsTileValid(tile))
            {
                assignedTiles[tile] = color;
                SetTileColor(tile, color);
            }
        }
    }
    
    /// <summary>
    /// 배정된 타일 제거
    /// </summary>
    public void RemoveAssignedTile(Vector3Int tile)
    {
        if (assignedTiles.ContainsKey(tile))
        {
            assignedTiles.Remove(tile);
            highlightTilemap.SetTile(tile, null);
        }
    }
    
    /// <summary>
    /// 모든 배정 제거
    /// </summary>
    public void ClearAllAssigned()
    {
        foreach (var tile in assignedTiles.Keys.ToList())
        {
            highlightTilemap.SetTile(tile, null);
        }
        assignedTiles.Clear();
    }
    
    #endregion
    
    #region Tile Validation & Cleanup
    
    /// <summary>
    /// 타일이 유효한지 확인 (공기가 아닌 실제 타일인지)
    /// </summary>
    private bool IsTileValid(Vector3Int position)
    {
        if (gameMap == null) return false;
        
        // 맵 범위 확인
        if (position.x < 0 || position.x >= GameMap.MAP_WIDTH ||
            position.y < 0 || position.y >= GameMap.MAP_HEIGHT)
        {
            return false;
        }
        
        // 타일이 공기(AIR)가 아닌지 확인
        int tileID = gameMap.TileGrid[position.x, position.y];
        return tileID != 0; // 0 = AIR_ID
    }
    
    /// <summary>
    /// 주기적으로 유효하지 않은 타일 제거
    /// </summary>
    private void CleanupInvalidTiles()
    {
        // 호버된 타일 정리
        foreach (var tile in hoveredTiles.ToList())
        {
            if (!IsTileValid(tile))
            {
                hoveredTiles.Remove(tile);
                highlightTilemap.SetTile(tile, null);
            }
        }
        
        // 선택된 타일 정리
        foreach (var tile in selectedTiles.ToList())
        {
            if (!IsTileValid(tile))
            {
                selectedTiles.Remove(tile);
                highlightTilemap.SetTile(tile, null);
            }
        }
        
        // 배정된 타일 정리
        foreach (var tile in assignedTiles.Keys.ToList())
        {
            if (!IsTileValid(tile))
            {
                assignedTiles.Remove(tile);
                highlightTilemap.SetTile(tile, null);
            }
        }
    }
    
    #endregion
    
    #region Clear All
    
    /// <summary>
    /// 모든 하이라이트 제거
    /// </summary>
    public void ClearAll()
    {
        ClearHovered();
        ClearSelected();
        ClearAllAssigned();
    }
    
    #endregion
    
    #region Helper Methods
    
    private void SetTileColor(Vector3Int position, Color color)
    {
        if (highlightTilemap == null) return;
        
        // 타일 설정
        highlightTilemap.SetTile(position, highlightTile);
        
        // 색상 설정
        highlightTilemap.SetTileFlags(position, TileFlags.None);
        highlightTilemap.SetColor(position, color);
    }
    
    /// <summary>
    /// 특정 위치가 하이라이트되어 있는지 확인
    /// </summary>
    public bool IsHighlighted(Vector3Int position)
    {
        return hoveredTiles.Contains(position) || 
               selectedTiles.Contains(position) || 
               assignedTiles.ContainsKey(position);
    }
    
    /// <summary>
    /// 특정 위치가 배정되어 있는지 확인
    /// </summary>
    public bool IsAssigned(Vector3Int position)
    {
        return assignedTiles.ContainsKey(position);
    }
    
    #endregion
    
    // Public 프로퍼티
    public int HoveredCount => hoveredTiles.Count;
    public int SelectedCount => selectedTiles.Count;
    public int AssignedCount => assignedTiles.Count;
}