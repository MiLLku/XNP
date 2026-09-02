using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 도달 가능성 맵 — 연결 성분(Connected Component) 라벨링.
///
/// 맵 전체를 한 번 훑어 "서 있을 수 있는 타일"에 성분 번호를 부여합니다.
/// 두 타일의 성분 번호가 다르면 <b>A*를 돌리지 않고도</b> 도달 불가임을 O(1)에 알 수 있습니다.
/// 벽 너머 고립된 방처럼 애초에 갈 수 없는 목적지를 A* 앞단에서 걸러내는 것이 목적입니다.
///
/// ── 정확성 규약 (중요) ─────────────────────────────────────────────
/// 이 라벨링은 <b>갈 수 있는 곳을 못 간다고 말하면 안 됩니다</b>
/// (오판하면 직원이 멀쩡한 작업을 거부합니다). 반대로 못 가는 곳을 갈 수 있다고
/// 말하는 것은 안전합니다 — 그 경우 그냥 A*가 돌고 예전처럼 실패할 뿐입니다.
///
/// 그래서 두 가지 안전장치를 둡니다:
///   1. 이웃 판정을 TilePathfinder.GetMovementNeighbors에 위임 — A*와 규칙이 어긋날 수 없습니다.
///   2. 간선을 <b>무향으로</b> 취급 — Union-Find로 합치므로 a→b 한 방향만 가능해도 같은 성분이 됩니다.
///      이동 규칙은 비대칭입니다(사다리 없이 아래로는 내려갈 수 있지만 위로는 못 올라감).
///      무향으로 합치면 성분이 실제보다 커지므로 오탐(=갈 수 있다고 잘못 말함)만 생기고,
///      A*가 찾아낼 수 있는 경로는 반드시 같은 성분 안에 들어옵니다.
///
/// ── 구역과의 관계 ────────────────────────────────────────────────
/// 라벨은 <b>순수 지형</b> 기준입니다. 구역 배정(PathOptions.allowedZoneIds)은 직원마다 달라
/// 미리 라벨링할 수 없으므로, 그런 질의는 판정을 포기하고 A*에 위임합니다.
/// 따라서 구역이 바뀌어도 재빌드가 필요 없습니다 — 지형(NavVersion)만 보면 됩니다.
/// </summary>
public class ReachabilityMap
{
    #region 상수

    /// <summary>서 있을 수 없는 타일의 성분 번호.</summary>
    private const int NO_COMPONENT = -1;

    #endregion

    #region 정적 접근자

    private static ReachabilityMap _instance;
    private static GameMap _instanceMap;

    /// <summary>
    /// 해당 맵의 공용 인스턴스를 반환합니다 (맵이 바뀌면 새로 만듭니다).
    /// 라벨 배열이 맵 크기만큼이라 직원마다 만들지 말고 이것을 재사용하세요.
    /// </summary>
    public static ReachabilityMap For(GameMap map)
    {
        if (map == null) return null;

        if (_instance == null || _instanceMap != map)
        {
            _instance = new ReachabilityMap(map);
            _instanceMap = map;
        }
        return _instance;
    }

    /// <summary>MapGenerator의 현재 맵으로 인스턴스를 반환합니다 (준비 전이면 null).</summary>
    public static ReachabilityMap Current
        => MapGenerator.instance != null ? For(MapGenerator.instance.GameMapInstance) : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        _instanceMap = null;
    }

    #endregion

    #region 데이터

    private readonly GameMap gameMap;
    private TilePathfinder pathfinder;

    /// <summary>타일별 연결 성분 번호 (지형 기준).</summary>
    private int[] component;

    private int builtNavVersion = -1;

    // 재빌드용 작업 버퍼 (매 재빌드마다 할당하지 않도록 재사용)
    private int[]  unionParent;
    private bool[] traversable;

    #endregion

    #region 초기화

    public ReachabilityMap(GameMap gameMap)
    {
        this.gameMap = gameMap;
        // 빌드는 첫 질의 때 lazy로 수행합니다 (생성 시점엔 지형이 아직 준비 전일 수 있음).
    }

    #endregion

    #region 빌드

    /// <summary>지형 버전이 바뀌었으면 라벨을 다시 만듭니다.</summary>
    private void EnsureBuilt()
    {
        if (component != null && gameMap.NavVersion == builtNavVersion) return;

        Rebuild();
        builtNavVersion = gameMap.NavVersion;
    }

    private void Rebuild()
    {
        if (pathfinder == null) pathfinder = new TilePathfinder(gameMap);

        int size = GameMap.MAP_WIDTH * GameMap.MAP_HEIGHT;
        if (component == null || component.Length != size)
        {
            component   = new int[size];
            unionParent = new int[size];
            traversable = new bool[size];
        }

        float startTime = Time.realtimeSinceStartup;

        // 1) 통행 가능 타일 표시 + Union-Find 초기화
        for (int x = 0; x < GameMap.MAP_WIDTH; x++)
        {
            for (int y = 0; y < GameMap.MAP_HEIGHT; y++)
            {
                int index = Index(x, y);
                unionParent[index] = index;
                traversable[index] = pathfinder.IsValidPosition(new Vector2Int(x, y));
                component[index]   = NO_COMPONENT;
            }
        }

        // 2) 간선 합치기 — 방향을 구분하지 않으므로 정방향으로 한 번만 훑으면 충분하다
        for (int x = 0; x < GameMap.MAP_WIDTH; x++)
        {
            for (int y = 0; y < GameMap.MAP_HEIGHT; y++)
            {
                int index = Index(x, y);
                if (!traversable[index]) continue;

                foreach (var neighbor in pathfinder.GetMovementNeighbors(new Vector2Int(x, y)))
                {
                    if (!InBounds(neighbor)) continue;

                    int neighborIndex = Index(neighbor.x, neighbor.y);
                    if (!traversable[neighborIndex]) continue;

                    Union(index, neighborIndex);
                }
            }
        }

        // 3) 루트마다 성분 번호 부여
        var rootToComponent = new Dictionary<int, int>();
        int nextComponent = 0;

        for (int index = 0; index < component.Length; index++)
        {
            if (!traversable[index]) continue;

            int root = Find(index);
            if (!rootToComponent.TryGetValue(root, out int id))
            {
                id = nextComponent++;
                rootToComponent[root] = id;
            }
            component[index] = id;
        }

        float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;
        Debug.Log($"[ReachabilityMap] 재빌드 {elapsedMs:F1}ms — 성분 {nextComponent}개 (nav={gameMap.NavVersion})");
    }

    private int Find(int x)
    {
        while (unionParent[x] != x)
        {
            unionParent[x] = unionParent[unionParent[x]]; // 경로 절반 압축
            x = unionParent[x];
        }
        return x;
    }

    private void Union(int a, int b)
    {
        int rootA = Find(a);
        int rootB = Find(b);
        if (rootA != rootB) unionParent[rootA] = rootB;
    }

    #endregion

    #region 도달 가능성 질의

    /// <summary>from에서 to로 도달할 <b>가능성이 있는지</b> 확인합니다 (구역 제한 없음).</summary>
    public bool IsReachable(Vector2Int from, Vector2Int to)
        => IsReachable(from, to, null);

    /// <summary>
    /// from에서 to로 도달할 <b>가능성이 있는지</b> A* 없이 확인합니다.
    ///
    /// false면 경로가 확실히 없습니다 — A*를 돌리지 마세요.
    /// true는 "성분이 같다"는 뜻일 뿐 경로를 보장하지 않습니다. 실제 경로는 A*가 판단합니다.
    ///
    /// 판정할 수 없는 상황(라벨 없는 타일, 구역 한정 경로)에서는
    /// 안전하게 true를 반환해 A*로 넘깁니다.
    /// </summary>
    public bool IsReachable(Vector2Int from, Vector2Int to, PathOptions options)
    {
        // 구역 한정 경로는 직원마다 달라 미리 라벨링할 수 없다 → A*에 위임
        if (options != null && options.allowedZoneIds != null) return true;

        EnsureBuilt();

        if (!InBounds(from) || !InBounds(to)) return true;

        int fromLabel = component[Index(from.x, from.y)];
        int toLabel   = component[Index(to.x, to.y)];

        // 어느 한쪽이라도 라벨이 없으면 판정을 포기한다.
        //   - 출발지 미라벨: 직원이 비정상 위치(공중 등)에 있는 경우
        //   - 목적지 미라벨: A*가 FindNearestValidPosition으로 근처 유효 타일에 스냅할 수 있다
        if (fromLabel == NO_COMPONENT || toLabel == NO_COMPONENT) return true;

        return fromLabel == toLabel;
    }

    /// <summary>특정 위치에 서 있을 수 있는지 확인합니다.</summary>
    public bool CanStandAtPosition(Vector2Int pos)
    {
        EnsureBuilt();
        if (!InBounds(pos)) return false;
        return component[Index(pos.x, pos.y)] != NO_COMPONENT;
    }

    #endregion

    #region 유틸리티

    private static int Index(int x, int y) => y * GameMap.MAP_WIDTH + x;

    private static bool InBounds(Vector2Int pos)
        => pos.x >= 0 && pos.x < GameMap.MAP_WIDTH &&
           pos.y >= 0 && pos.y < GameMap.MAP_HEIGHT;

    /// <summary>디버그: 두 지점의 성분 번호를 출력합니다.</summary>
    public void DebugPrintComponents(Vector2Int from, Vector2Int to)
    {
        EnsureBuilt();
        if (!InBounds(from) || !InBounds(to)) { Debug.Log("[ReachabilityMap] 범위 밖"); return; }

        Debug.Log($"[ReachabilityMap] {from} -> {to} | " +
                  $"성분 {component[Index(from.x, from.y)]} vs {component[Index(to.x, to.y)]}");
    }

    #endregion
}
