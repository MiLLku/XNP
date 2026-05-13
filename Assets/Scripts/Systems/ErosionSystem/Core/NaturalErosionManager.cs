using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 자연 침식 시스템 관리자.
///
/// 아키텍처 (스파스 딕셔너리 방식):
///   - 각 발원지(INaturalErosionSource)별 계산 결과를 독립 딕셔너리로 저장
///   - 타일별 최종 침식 수치 = 전체 발원지 영향력의 MAX 집계
///   - 발원지 추가(RegisterSource): 계산 후 영향력 맵에 증분 반영
///   - 발원지 제거(UnregisterSource): 해당 발원지 삭제 → 전체 재계산
///   - 장벽 변경(OnBarrierChanged): 식물형 BFS만 재계산 → 영향력 맵 재빌드
///
/// 전파 방식:
///   식물형 (IgnoresBarriers=false):
///     BFS 경로 탐색 — AIR/LADDER 타일만 통과, 벽/문에서 차단
///   광물형 (IgnoresBarriers=true):
///     원형 거리 기반 — 유클리드 거리로 강도 계산, 장벽 완전 무시
///
/// 직원 연동:
///   EmployeeMovement.MoveToTileCoroutine → GetInfluenceAt(tile)
///   → EmployeeErosionController.ApplyNaturalErosion(intensity)
///   (워터마크 방식: 이전 최대치 초과분만 침식 수치에 추가)
///
/// 세이브:
///   ISaveModule 구현 (SaveOrder = 65).
///   영향력 맵은 파생 데이터이므로 저장 불필요.
///   PostRestore에서 전체 재계산하여 장벽 최종 상태를 반영합니다.
/// </summary>
public class NaturalErosionManager : DestroySingleton<NaturalErosionManager>, ISaveModule
{
    #region 내부 상태

    /// <summary>발원지별 계산 결과 (발원지 → 타일 → 침식 수치)</summary>
    private readonly Dictionary<INaturalErosionSource, Dictionary<Vector2Int, float>> _sourceInfluences
        = new Dictionary<INaturalErosionSource, Dictionary<Vector2Int, float>>();

    /// <summary>집계된 MAX 영향력 맵 [x, y]</summary>
    private float[,] _influenceMap;

    private GameMap _gameMap;

    #endregion

    #region BFS 방향 오프셋 (4방향)

    private static readonly Vector2Int[] NeighborOffsets =
    {
        new Vector2Int( 1,  0),
        new Vector2Int(-1,  0),
        new Vector2Int( 0,  1),
        new Vector2Int( 0, -1)
    };

    #endregion

    #region 초기화

    protected override void Awake()
    {
        base.Awake();
        _influenceMap = new float[GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT];
    }

    private void Start()
    {
        EnsureGameMap();
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 발원지를 등록하고 영향력을 계산합니다.
    /// NaturalErosionEmitter.OnEnable에서 호출됩니다.
    /// </summary>
    public void RegisterSource(INaturalErosionSource source)
    {
        if (source == null) return;
        if (_sourceInfluences.ContainsKey(source)) return;

        EnsureGameMap();
        if (_gameMap == null) return; // PostRestore RecalculateInfluenceMap()에서 커버

        var result = ComputeInfluence(source);
        _sourceInfluences[source] = result;
        IncrementalAddToMap(result);
    }

    /// <summary>
    /// 발원지를 제거하고 영향력 맵을 전체 재계산합니다.
    /// NaturalErosionEmitter.OnDisable에서 호출됩니다.
    /// </summary>
    public void UnregisterSource(INaturalErosionSource source)
    {
        if (source == null) return;
        if (!_sourceInfluences.ContainsKey(source)) return;

        _sourceInfluences.Remove(source);
        RecalculateInfluenceMap();
    }

    /// <summary>
    /// 지정 타일의 자연 침식 영향력을 반환합니다.
    /// EmployeeMovement에서 직원이 타일에 도달할 때마다 호출됩니다.
    /// </summary>
    public float GetInfluenceAt(int x, int y)
    {
        if (x < 0 || x >= GameMap.MAP_WIDTH || y < 0 || y >= GameMap.MAP_HEIGHT) return 0f;
        return _influenceMap[x, y];
    }

    /// <inheritdoc cref="GetInfluenceAt(int, int)"/>
    public float GetInfluenceAt(Vector2Int tile) => GetInfluenceAt(tile.x, tile.y);

    /// <summary>
    /// 벽/문 등 장벽이 변경되었을 때 호출합니다.
    /// 식물형(BFS) 발원지만 재계산합니다. 광물형은 장벽 무관이므로 유지됩니다.
    /// </summary>
    public void OnBarrierChanged()
    {
        EnsureGameMap();
        if (_gameMap == null) return;

        var sources = new List<INaturalErosionSource>(_sourceInfluences.Keys);

        // 식물형만 BFS 재계산 (광물형은 그대로)
        foreach (var src in sources)
        {
            if (src == null || !src.IsActive) continue;
            if (!src.IgnoresBarriers)
                _sourceInfluences[src] = ComputeInfluence(src);
        }

        RecalculateInfluenceMap();
        Debug.Log($"[NaturalErosionManager] 장벽 변경 후 영향력 맵 재계산 완료 (발원지 {_sourceInfluences.Count}개)");
    }

    #endregion

    #region 영향력 계산 (분기)

    /// <summary>
    /// 발원지 타입에 따라 적절한 계산 방식을 선택합니다.
    ///   광물형 (IgnoresBarriers=true) → ComputeCircular  (원형, 장벽 무시)
    ///   식물형 (IgnoresBarriers=false) → ComputeBFS      (BFS, 장벽 차단)
    /// </summary>
    private Dictionary<Vector2Int, float> ComputeInfluence(INaturalErosionSource source)
    {
        return source.IgnoresBarriers
            ? ComputeCircular(source)
            : ComputeBFS(source);
    }

    // ──────────────────────────────────────────────
    // 광물형: 원형 거리 기반 (장벽 무시)
    // ──────────────────────────────────────────────

    /// <summary>
    /// 원형 범위로 침식 영향력을 계산합니다 (광물형).
    /// 유클리드 거리 기반. 벽/문 등 모든 장벽을 관통합니다.
    /// 직원이 밟을 수 있는 AIR/LADDER 타일에만 영향력을 기록합니다.
    /// </summary>
    private Dictionary<Vector2Int, float> ComputeCircular(INaturalErosionSource source)
    {
        var result = new Dictionary<Vector2Int, float>();
        if (_gameMap == null) return result;

        float maxIntensity = source.MaxIntensity;
        float decay        = source.DecayPerTile;
        if (maxIntensity <= 0f || decay <= 0f) return result;

        int maxRadius = Mathf.CeilToInt(maxIntensity / decay);
        Vector2Int origin = source.TilePosition;

        for (int dx = -maxRadius; dx <= maxRadius; dx++)
        {
            for (int dy = -maxRadius; dy <= maxRadius; dy++)
            {
                int x = origin.x + dx;
                int y = origin.y + dy;

                if (x < 0 || x >= GameMap.MAP_WIDTH)  continue;
                if (y < 0 || y >= GameMap.MAP_HEIGHT) continue;

                // 직원이 실제로 있을 수 있는 타일에만 기록
                if (!_gameMap.IsPassableTile(x, y)) continue;

                float dist      = Mathf.Sqrt(dx * dx + dy * dy);
                float intensity = maxIntensity - dist * decay;
                if (intensity <= 0f) continue;

                result[new Vector2Int(x, y)] = intensity;
            }
        }

        return result;
    }

    // ──────────────────────────────────────────────
    // 식물형: BFS 경로 탐색 (장벽 차단)
    // ──────────────────────────────────────────────

    /// <summary>
    /// BFS로 침식 영향력을 계산합니다 (식물형).
    /// AIR/LADDER 타일만 통과하며 벽/문에서 차단됩니다.
    /// 발원지 타일 자체가 고체여도 인접 통과 가능 타일로 방출합니다.
    /// </summary>
    private Dictionary<Vector2Int, float> ComputeBFS(INaturalErosionSource source)
    {
        var result = new Dictionary<Vector2Int, float>();
        if (_gameMap == null) return result;

        float maxIntensity = source.MaxIntensity;
        float decay        = source.DecayPerTile;
        if (maxIntensity <= 0f || decay <= 0f) return result;

        int maxRadius = Mathf.CeilToInt(maxIntensity / decay);

        var queue   = new Queue<Vector2Int>();
        var distMap = new Dictionary<Vector2Int, int>();

        Vector2Int origin = source.TilePosition;
        queue.Enqueue(origin);
        distMap[origin] = 0;

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            int   dist      = distMap[current];
            float intensity = maxIntensity - dist * decay;

            if (intensity <= 0f) continue;

            // 직원이 밟을 수 있는 타일에만 영향력 기록
            if (_gameMap.IsPassableTile(current.x, current.y))
                result[current] = intensity;

            if (dist >= maxRadius) continue;

            foreach (var offset in NeighborOffsets)
            {
                Vector2Int neighbor = current + offset;
                if (distMap.ContainsKey(neighbor)) continue;
                if (!CanBFSPassThrough(neighbor.x, neighbor.y)) continue;

                distMap[neighbor] = dist + 1;
                queue.Enqueue(neighbor);
            }
        }

        return result;
    }

    /// <summary>
    /// BFS 침식이 해당 타일을 통과해 전파될 수 있는지 확인합니다 (식물형 전용).
    /// AIR/LADDER이고 벽과 이동 차단 설치물이 없어야 합니다.
    /// </summary>
    private bool CanBFSPassThrough(int x, int y)
    {
        if (x < 0 || x >= GameMap.MAP_WIDTH)  return false;
        if (y < 0 || y >= GameMap.MAP_HEIGHT) return false;
        if (!_gameMap.IsPassableTile(x, y))   return false; // AIR/LADDER만
        if (_gameMap.WallGrid[x, y] != 0)     return false; // 벽 차단
        if (_gameMap.DoesTileBlockMovement(x, y)) return false; // 문/설치물 차단
        return true;
    }

    #endregion

    #region 영향력 맵 관리

    private void IncrementalAddToMap(Dictionary<Vector2Int, float> result)
    {
        foreach (var kvp in result)
        {
            int x = kvp.Key.x;
            int y = kvp.Key.y;
            if (x < 0 || x >= GameMap.MAP_WIDTH)  continue;
            if (y < 0 || y >= GameMap.MAP_HEIGHT) continue;

            if (kvp.Value > _influenceMap[x, y])
                _influenceMap[x, y] = kvp.Value;
        }
    }

    private void RecalculateInfluenceMap()
    {
        _influenceMap = new float[GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT];
        foreach (var kvp in _sourceInfluences)
            IncrementalAddToMap(kvp.Value);
    }

    private void EnsureGameMap()
    {
        if (_gameMap == null && MapGenerator.instance != null)
            _gameMap = MapGenerator.instance.GameMapInstance;
    }

    #endregion

    #region ISaveModule

    /// <summary>Employee(50), WorkOrder(60) 이후 / DroppedItem(70) 이전</summary>
    public int SaveOrder => 65;

    public void Capture(SaveData data) { }
    public void Restore(SaveData data) { }

    /// <summary>
    /// 모든 세이브 모듈 복원 후 영향력 맵을 재계산합니다.
    /// 장벽(벽/문) 상태가 BuildingSystem 복원(SaveOrder=30) 이후 확정되므로
    /// 이 시점에서 전체 재계산하여 식물형 BFS 정확성을 보장합니다.
    /// </summary>
    public void PostRestore(SaveData data)
    {
        EnsureGameMap();
        if (_gameMap == null) return;

        var sources = new List<INaturalErosionSource>(_sourceInfluences.Keys);
        _sourceInfluences.Clear();

        foreach (var src in sources)
        {
            if (src != null && src.IsActive)
                _sourceInfluences[src] = ComputeInfluence(src);
        }

        RecalculateInfluenceMap();
        Debug.Log($"[NaturalErosionManager] PostRestore 영향력 맵 재계산 완료 (발원지 {_sourceInfluences.Count}개)");
    }

    #endregion
}
