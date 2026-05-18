using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 채굴 작업 선택기
/// 가중치 기반 점수 계산 + 계층적 채굴(Top-Down) + 대각선 우선 로직
/// 
/// Score = (Priority * W_p * HeightBonus) / (Distance * W_d + AccessCost * W_a)
/// </summary>
public class MiningTaskSelector
{
    #region 가중치 설정
    
    [System.Serializable]
    public class SelectorWeights
    {
        [Header("기본 가중치")]
        public float priorityWeight = 10f;      // W_p: 우선순위 가중치
        public float distanceWeight = 1f;       // W_d: 거리 가중치
        public float accessCostWeight = 2f;     // W_a: 접근 비용 가중치
        
        [Header("계층적 채굴 가중치")]
        public float heightBonusPerTile = 0.5f; // Y좌표가 높을수록 보너스
        public float diagonalPreference = 1.5f; // 대각선 아래 채굴 선호도
        
        [Header("페널티")]
        public float standingOnTaskPenalty = 100f;  // 서 있는 타일 페널티
        public float belowFootPenalty = 2000f;      // 발 아래 타일 페널티 (낙하 방지 - 최후 수단)
        public float columnOrderPenalty = 500f;     // 같은 열에 위쪽 미완성 타일이 있을 때 페널티 (Top-Down 강제)
        public float steppingStonePreservationPenalty = 700f; // 발판 보존: 이 타일이 인접 열 상단 타일의 유일한 발판일 때
        public float unreachablePenalty = 1000f;    // 도달 불가 페널티
    }
    
    #endregion
    
    private SelectorWeights weights;
    private ReachabilityMap reachabilityMap;
    
    public MiningTaskSelector(SelectorWeights weights = null, ReachabilityMap reachabilityMap = null)
    {
        this.weights = weights ?? new SelectorWeights();
        this.reachabilityMap = reachabilityMap;
    }
    
    public void SetReachabilityMap(ReachabilityMap map)
    {
        this.reachabilityMap = map;
    }
    
    #region 작업 선택
    
    /// <summary>
    /// 직원에게 가장 적합한 작업을 선택합니다.
    /// 낙하 방지 우선순위: 발 아래를 파는 위험한 작업보다 안전한 작업을 먼저 선택합니다.
    /// </summary>
    public WorkTask SelectBestTask(Employee worker, IReadOnlyList<WorkTask> pendingTasks)
    {
        if (worker == null || pendingTasks == null || pendingTasks.Count == 0)
            return null;

        var validTasks = pendingTasks.Where(t => t != null && t.IsValid()).ToList();
        if (validTasks.Count == 0)
            return null;

        // 직원 정보 수집
        WorkerContext context = CreateWorkerContext(worker);

        // 열(Column) 순서 채굴을 위해 대기 중인 모든 타일 위치를 미리 수집
        var pendingTilePositions = new HashSet<Vector2Int>(validTasks.Select(t => GetTaskTile(t)));

        // ★ float.NegativeInfinity 초기화: float.MinValue(가장 작은 유한수)보다도 작으므로
        //   도달불가 태스크(float.MinValue 점수)도 첫 번째 비교에서 올바르게 선택됨
        WorkTask bestTask = null;
        WorkTask bestSafeTask = null;   // 낙하를 유발하지 않는 최선 작업 (발 아래가 아닌 것)
        float bestScore = float.NegativeInfinity;
        float bestSafeScore = float.NegativeInfinity;

        foreach (var task in validTasks)
        {
            float score = CalculateTaskScore(task, context, pendingTilePositions);

            if (score > bestScore)
            {
                bestScore = score;
                bestTask = task;
            }

            // 낙하 유발 타일(발 바로 아래)은 bestSafeTask 후보에서 제외
            Vector2Int tile = GetTaskTile(task);
            bool isBelowFoot = IsDirectlyBelowFoot(tile, context);
            if (!isBelowFoot && score > float.MinValue && score > bestSafeScore)
            {
                bestSafeScore = score;
                bestSafeTask = task;
            }
        }

        // 안전한 작업이 있으면 우선 선택, 없을 때만 낙하 감수하고 최선 작업 수행
        WorkTask selected = bestSafeTask ?? bestTask;

        if (selected != null)
        {
            bool isRisky = (bestSafeTask == null);
            string warningTag = isRisky ? " ⚠낙하위험(최후수단)" : "";
            float displayScore = isRisky ? bestScore : bestSafeScore;
            Debug.Log($"[MiningTaskSelector] 선택: {GetTaskTile(selected)} (점수: {displayScore:F2}{warningTag})");
        }

        return selected;
    }
    
    /// <summary>
    /// 작업 점수 계산
    /// Score = (Priority * W_p * HeightBonus) / (Distance * W_d + AccessCost * W_a + Penalties)
    /// pendingTilePositions: 열(Column) 순서 페널티 계산용 (null이면 생략)
    /// </summary>
    public float CalculateTaskScore(WorkTask task, WorkerContext context,
                                    HashSet<Vector2Int> pendingTilePositions = null)
    {
        Vector2Int taskTile = GetTaskTile(task);

        // 1. 현재 위치에서 작업 가능한지 체크 (범위 + 시야)
        bool isInWorkRange = IsInWorkRange(context.footTile, taskTile);
        bool hasLineOfSight = HasLineOfSight(context, taskTile);
        bool canWorkFromHere = isInWorkRange && hasLineOfSight;

        // 2. 직원이 차지하고 있는 타일인지 체크
        float penalty = 0f;

        if (context.occupiedTiles.Contains(taskTile))
        {
            penalty += weights.standingOnTaskPenalty;
            canWorkFromHere = false; // 서 있는 타일은 현재 위치에서 작업 불가
        }

        // 3. 발 아래 타일인지 체크 (파면 낙하 - 매우 강한 페널티로 최후 수단化)
        if (IsDirectlyBelowFoot(taskTile, context))
        {
            penalty += weights.belowFootPenalty;
        }

        // 4. 열(Column) Top-Down 순서 체크
        //    같은 X 열에 이 타일보다 위쪽에 아직 채굴 안 된 타일이 있으면 페널티
        //    → 항상 위에서 아래로 채굴하도록 강제
        if (pendingTilePositions != null && HasPendingTaskAbove(taskTile, pendingTilePositions))
        {
            penalty += weights.columnOrderPenalty;
        }

        // 4b. 발판 보존 체크 (cross-column stepping stone)
        //    이 타일(y)을 파면 인접 열의 상단 타일(y+3)에 접근할 발판(foot y+1)이 사라질 수 있음
        //    columnOrderPenalty는 같은 열만 보지만 이 체크는 인접 열까지 커버함
        //    → 인접 열 y+3에 미완성 타일이 있는 한, 이 타일 채굴을 미룸
        if (pendingTilePositions != null && IsPotentialCrossColumnSteppingStone(taskTile, pendingTilePositions))
        {
            penalty += weights.steppingStonePreservationPenalty;
        }

        // 5. 현재 위치에서 작업 불가능하면 이동해야 함 - 도달 가능성 체크
        if (!canWorkFromHere)
        {
            if (!IsTaskReachable(taskTile, context))
            {
                return float.MinValue; // 도달 불가능
            }
        }

        // 6. 기본 점수 요소 계산
        float priority = Mathf.Max(1, 10 - task.priority); // priority가 낮을수록 높은 점수
        float distance = canWorkFromHere ? 0f : CalculateDistance(context.footTile, taskTile);
        float accessCost = canWorkFromHere ? 0f : CalculateAccessCost(context, taskTile);
        float heightBonus = CalculateHeightBonus(taskTile, context);
        float diagonalBonus = CalculateDiagonalBonus(taskTile, context);

        // 7. 현재 위치에서 작업 가능하면 큰 보너스
        float inRangeBonus = canWorkFromHere ? 10f : 1f;

        // 8. 최종 점수 계산
        float numerator = priority * weights.priorityWeight * heightBonus * diagonalBonus * inRangeBonus;
        float denominator = distance * weights.distanceWeight + accessCost * weights.accessCostWeight + penalty + 0.1f;

        float score = numerator / denominator;

        return score;
    }
    
    /// <summary>
    /// 작업자 발 위치에서 타겟이 작업 범위 내인지 확인
    /// 범위: 좌우 1칸, 위로 3칸, 아래로 1칸
    /// </summary>
    private bool IsInWorkRange(Vector2Int footTile, Vector2Int targetTile)
    {
        int dx = Mathf.Abs(targetTile.x - footTile.x);
        int dy = targetTile.y - footTile.y;  // 양수면 위, 음수면 아래
        
        // 범위: dx <= 1, dy: -1 ~ +2
        return dx <= 1 && dy >= -1 && dy <= 2;
    }
    
    #endregion
    
    #region 컨텍스트 생성
    
    public struct WorkerContext
    {
        public Vector2Int footTile;           // 발 위치
        public HashSet<Vector2Int> occupiedTiles;  // 직원이 차지하는 모든 타일
        public int employeeHeight;            // 직원 높이
        public GameMap gameMap;
        public TilePathfinder pathfinder;
    }
    
    private WorkerContext CreateWorkerContext(Employee worker)
    {
        Vector3 pos = worker.transform.position;
        // X는 FloorToInt — EmployeeWork.GetFootTile()과 동일하게 맞춤.
        // RoundToInt를 쓰면 타일 중앙(x+0.5)에서 반올림되어 인접 타일(고체일 수 있음)로
        // 발 위치가 어긋나고, 경로탐색 시작점이 잘못되어 도달가능성 판단이 틀어집니다.
        Vector2Int footTile = new Vector2Int(
            Mathf.FloorToInt(pos.x),
            Mathf.FloorToInt(pos.y)
        );
        
        var context = new WorkerContext
        {
            footTile = footTile,
            // 직원이 차지하는 타일: 발 위치와 그 위 (2칸)
            occupiedTiles = new HashSet<Vector2Int>
            {
                footTile,
                new Vector2Int(footTile.x, footTile.y + 1)
            },
            employeeHeight = 2,
            gameMap = MapGenerator.instance?.GameMapInstance,
            pathfinder = null
        };
        
        if (context.gameMap != null)
        {
            context.pathfinder = new TilePathfinder(context.gameMap);
        }
        
        return context;
    }
    
    #endregion
    
    #region 도달 가능성
    
    private bool IsTaskReachable(Vector2Int taskTile, WorkerContext context)
    {
        // ReachabilityMap이 있으면 사용
        if (reachabilityMap != null)
        {
            // 작업 가능한 위치가 도달 가능한지 확인
            var workPositions = GetPotentialWorkPositions(taskTile, context);
            return workPositions.Any(pos => reachabilityMap.IsReachable(context.footTile, pos));
        }

        // 없으면 직접 경로 탐색
        if (context.pathfinder == null)
            return true;

        // ★ 직원의 현재 발 위치가 Pathfinder 기준으로 유효하지 않은 경우
        //   (예: 갓 스폰되어 공중에 있거나, 비표준 위치에 있을 때)
        //   경로를 계산할 수 없으므로 작업 위치가 존재하면 도달 가능으로 간주합니다.
        //   실제 이동은 EmployeeWork.AssignWork 에서 별도의 위치 탐색으로 처리합니다.
        if (!context.pathfinder.IsValidPosition(context.footTile))
        {
            return GetPotentialWorkPositions(taskTile, context).Count > 0;
        }

        // 작업 가능한 위치 중 하나라도 도달 가능하면 OK
        var positions = GetPotentialWorkPositions(taskTile, context);
        foreach (var pos in positions)
        {
            if (pos == context.footTile)
                return true;

            var path = context.pathfinder.FindPath(context.footTile, pos);
            if (path != null && path.Count > 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 작업 대상 타일에 대해 작업 가능한 작업자 발 위치들을 반환
    /// 타겟 기준이 아닌, "이 위치에 서면 타겟을 작업할 수 있다"는 위치들
    /// </summary>
    private List<Vector2Int> GetPotentialWorkPositions(Vector2Int taskTile, WorkerContext context)
    {
        List<Vector2Int> positions = new List<Vector2Int>();

        // 작업 범위: 작업자 기준 좌우 1칸, 위로 2칸, 아래로 1칸
        // 역으로 계산: 타겟 기준으로 작업자가 서 있을 수 있는 위치
        // 타겟이 작업자의 (dx, dy) 위치에 있으려면, 작업자는 타겟의 (-dx, -dy) 위치에 있어야 함

        for (int dx = -1; dx <= 1; dx++)
        {
            // 작업자 발 기준 dy: -1 ~ +2이면,
            // 타겟 기준으로 작업자는 dy: -2 ~ +1 위치
            for (int workerDy = -2; workerDy <= 1; workerDy++)
            {
                Vector2Int candidateFootPos = new Vector2Int(taskTile.x + dx, taskTile.y + workerDy);

                // 맵 범위 체크
                if (!IsInBounds(candidateFootPos))
                    continue;

                // 타겟 자체 위치는 제외 (타겟은 고체니까 서 있을 수 없음)
                if (candidateFootPos == taskTile)
                    continue;

                // 해당 위치에서 타겟이 작업 범위 내인지 확인
                if (!IsInWorkRange(candidateFootPos, taskTile))
                    continue;

                // 유효한 위치인지 확인 (서 있을 수 있는지)
                if (context.pathfinder != null)
                {
                    if (context.pathfinder.IsValidPosition(candidateFootPos))
                    {
                        positions.Add(candidateFootPos);
                    }
                }
            }


        }
        return positions;
    }

    #endregion
    
    #region 시야 체크
    
    private bool HasLineOfSight(WorkerContext context, Vector2Int targetTile)
    {
        if (context.gameMap == null)
            return true;
        
        Vector2Int from = context.footTile;
        int footY = from.y;
        int bodyY2 = from.y + 2;
        
        // 같은 X 좌표
        if (from.x == targetTile.x)
        {
            // 위쪽 타겟
            if (targetTile.y > bodyY2)
            {
                for (int y = bodyY2 + 1; y < targetTile.y; y++)
                {
                    if (IsSolidTile(context.gameMap, from.x, y))
                        return false;
                }
            }
            // 아래쪽 타겟
            else if (targetTile.y < footY)
            {
                // 발 밑이 고체면 그 아래로 시야 차단
                if (IsSolidTile(context.gameMap, from.x, footY))
                    return false;
                
                for (int y = footY - 1; y > targetTile.y; y--)
                {
                    if (IsSolidTile(context.gameMap, from.x, y))
                        return false;
                }
            }
        }
        // 다른 X 좌표 (옆)
        else
        {
            // 몸통 높이는 OK
            if (targetTile.y >= footY && targetTile.y <= bodyY2)
                return true;
            
            // 위쪽
            if (targetTile.y > bodyY2)
            {
                for (int y = bodyY2 + 1; y < targetTile.y; y++)
                {
                    if (IsSolidTile(context.gameMap, targetTile.x, y))
                        return false;
                }
            }
            // 아래쪽
            else if (targetTile.y < footY)
            {
                for (int y = footY; y > targetTile.y; y--)
                {
                    if (IsSolidTile(context.gameMap, targetTile.x, y))
                        return false;
                }
            }
        }
        
        return true;
    }
    
    #endregion
    
    #region 점수 계산 헬퍼
    
    private float CalculateDistance(Vector2Int from, Vector2Int to)
    {
        // 맨해튼 거리 + 약간의 유클리드 보정
        float manhattan = Mathf.Abs(to.x - from.x) + Mathf.Abs(to.y - from.y);
        float euclidean = Vector2Int.Distance(from, to);
        return manhattan * 0.7f + euclidean * 0.3f;
    }
    
    private float CalculateAccessCost(WorkerContext context, Vector2Int taskTile)
    {
        float cost = 0f;
        
        // 사다리 필요 여부
        if (Mathf.Abs(taskTile.y - context.footTile.y) > 2)
        {
            cost += 5f; // 사다리 비용
        }
        
        // 경로 복잡도 (실제 경로 길이 vs 직선 거리)
        if (context.pathfinder != null)
        {
            var workPositions = GetPotentialWorkPositions(taskTile, context);
            float minPathRatio = float.MaxValue;
            
            foreach (var pos in workPositions)
            {
                if (pos == context.footTile)
                {
                    minPathRatio = 1f;
                    break;
                }
                
                var path = context.pathfinder.FindPath(context.footTile, pos);
                if (path != null && path.Count > 0)
                {
                    float directDist = Vector2Int.Distance(context.footTile, pos);
                    float pathRatio = path.Count / Mathf.Max(1f, directDist);
                    minPathRatio = Mathf.Min(minPathRatio, pathRatio);
                }
            }
            
            if (minPathRatio < float.MaxValue)
            {
                cost += (minPathRatio - 1f) * 2f; // 우회 비용
            }
        }
        
        return cost;
    }
    
    /// <summary>
    /// 높이 보너스 계산 (Top-Down 채굴)
    /// 높은 타일일수록 높은 점수
    /// </summary>
    private float CalculateHeightBonus(Vector2Int taskTile, WorkerContext context)
    {
        // 직원 머리 높이 기준으로 상대 높이 계산
        int relativeHeight = taskTile.y - (context.footTile.y + 2);
        
        // 위쪽 타일에 보너스, 아래쪽에 페널티
        float bonus = 1f + (relativeHeight * weights.heightBonusPerTile);
        
        return Mathf.Max(0.1f, bonus);
    }
    
    /// <summary>
    /// 대각선 보너스 계산
    /// 발 바로 아래보다 대각선 아래를 선호
    /// </summary>
    private float CalculateDiagonalBonus(Vector2Int taskTile, WorkerContext context)
    {
        // 발 바로 아래인 경우
        if (taskTile.x == context.footTile.x && taskTile.y < context.footTile.y)
        {
            return 1f; // 보너스 없음
        }
        
        // 대각선 아래인 경우 (다른 X, 아래 Y)
        if (taskTile.x != context.footTile.x && taskTile.y < context.footTile.y)
        {
            return weights.diagonalPreference;
        }
        
        return 1f;
    }
    
    /// <summary>
    /// 발 바로 아래 타일인지 확인 (파면 떨어짐)
    /// </summary>
    private bool IsDirectlyBelowFoot(Vector2Int taskTile, WorkerContext context)
    {
        return taskTile.x == context.footTile.x &&
               taskTile.y == context.footTile.y - 1;
    }

    /// <summary>
    /// 같은 열(X좌표)에서 이 타일보다 위에 아직 채굴하지 않은 타일이 있는지 확인
    /// Top-Down 채굴 순서 강제: 높은 타일부터 채굴해야 낙하 없이 최대 채광 가능
    /// </summary>
    private bool HasPendingTaskAbove(Vector2Int taskTile, HashSet<Vector2Int> pendingPositions)
    {
        for (int y = taskTile.y + 1; y < GameMap.MAP_HEIGHT; y++)
        {
            var above = new Vector2Int(taskTile.x, y);
            if (!IsInBounds(above)) break;
            if (pendingPositions.Contains(above))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 이 타일이 인접 열의 상단 타일(y+3)에 접근하기 위한 발판인지 확인합니다.
    ///
    /// 작업 범위가 dy ≤ 2이므로, y_task 타일에 접근하려면 foot이 y_task-2 이상이어야 합니다.
    /// 즉, y=0 타일은 "foot y=1에 서야 닿는 y=3 타일"의 발판입니다.
    ///
    /// 이 체크는 같은 열뿐 아니라 인접 열(x±1)도 커버합니다.
    /// 예: x=3,y=0 을 파면 x=2,y=3에 접근할 foot y=1 발판이 사라질 수 있음
    ///
    /// 단순화 버전: y_t+3에 미완성 타일이 있으면 패널티 부여
    /// (y+1, y+2 빈 공간 여부를 추가 체크하면 더 정밀하지만 보수적으로 처리)
    /// </summary>
    private bool IsPotentialCrossColumnSteppingStone(Vector2Int taskTile, HashSet<Vector2Int> pendingPositions)
    {
        int yt = taskTile.y;

        // 이 타일보다 정확히 3칸 위(작업 범위 최대치 dy=2에서 발 위치 +1)에
        // 인접 열(x±1 포함)에 미완성 타일이 있는지 확인
        int targetY = yt + 3;
        if (targetY >= GameMap.MAP_HEIGHT) return false;

        for (int dx = -1; dx <= 1; dx++)
        {
            var candidate = new Vector2Int(taskTile.x + dx, targetY);
            if (IsInBounds(candidate) && pendingPositions.Contains(candidate))
                return true;
        }

        return false;
    }

    #endregion
    
    #region 유틸리티
    
    private Vector2Int GetTaskTile(WorkTask task)
    {
        Vector3 pos = task.GetPosition();
        return new Vector2Int(
            Mathf.FloorToInt(pos.x),
            Mathf.FloorToInt(pos.y)
        );
    }
    
    private bool IsSolidTile(GameMap gameMap, int x, int y)
    {
        if (!IsInBounds(x, y))
            return false;
        return gameMap.TileGrid[x, y] != 0;
    }
    
    private bool IsInBounds(Vector2Int pos)
    {
        return IsInBounds(pos.x, pos.y);
    }
    
    private bool IsInBounds(int x, int y)
    {
        return x >= 0 && x < GameMap.MAP_WIDTH && y >= 0 && y < GameMap.MAP_HEIGHT;
    }
    
    #endregion
}