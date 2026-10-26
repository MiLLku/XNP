using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 레이드 시스템 매니저.
/// DestroySingleton + ISaveModule 패턴을 따릅니다.
///
/// 담당 기능:
///   - RaidData 기반 웨이브 스폰 관리
///   - 레이드 전멸/철수 감지 및 완료 처리
///   - 레이드 완료 시 격퇴 레터 발행 (포스트 레이드 침식 회복은 v10에서 제거)
///   - RaidSystemSaveData 저장/복원
///
/// 확장 방법:
///   - RaidData ScriptableObject를 추가하여 새 레이드 시나리오를 만드세요.
///   - availableRaids 리스트에 등록하면 StartRaid(id)로 발동할 수 있습니다.
///   - EventManager의 EffectType.StartRaid 효과와 연동하세요.
/// </summary>
public class RaidManager : DestroySingleton<RaidManager>, ISaveModule
{
    #region 인스펙터 설정

    [Header("레이드 풀")]
    [Tooltip("사용 가능한 레이드 데이터 목록")]
    [SerializeField] private List<RaidData> availableRaids = new List<RaidData>();

    [Header("스폰 설정")]
    [Tooltip("레이드 개체 스폰 기준점. 미설정 시 (0,0,0) 사용.")]
    [SerializeField] private Transform defaultSpawnPoint;

    [Header("자동 습격 일정 — 잦은 소규모, 텀이 길었으면 크게")]
    [Tooltip("켜면 RaidManager가 직접 습격을 잡습니다 (이벤트 풀의 침공 이벤트와 별개)")]
    [SerializeField] private bool autoSchedule = true;

    [Tooltip("첫 습격이 올 수 있는 가장 이른 날")]
    [Min(0)] [SerializeField] private int firstRaidDay = 3;

    [Tooltip("습격 사이 간격 (게임일, 최소~최대 무작위)")]
    [SerializeField] private Vector2 raidIntervalDays = new Vector2(0.7f, 1.5f);

    [Tooltip("지난 습격 뒤 이 일수를 넘기면 '강한 습격'(RaidData.isStrong)도 뽑힐 수 있다")]
    [Min(0f)] [SerializeField] private float strongRaidAfterDays = 1.5f;

    [Header("습격 규모 — 마리 수 = 위협 점수 ÷ 습격 종류의 한 마리당 점수 (ThreatScore, 임시 수치)")]
    [Min(1f)] [SerializeField] private float normalPointsPerEnemy = 10f;
    [Min(1f)] [SerializeField] private float tunnelPointsPerEnemy = 12f;
    [Min(1f)] [SerializeField] private float breachPointsPerEnemy = 12f;

    [Tooltip("성능을 위한 기술적 최대 마리 수 (난이도 상한이 아님)")]
    [Min(1)] [SerializeField] private int maxEnemiesTechnical = 30;

    [Header("습격 종류 — 습격마다 무작위 (임시 가중치)")]
    [Min(0f)] [SerializeField] private float normalWeight = 60f;
    [Min(0f)] [SerializeField] private float tunnelWeight = 20f;
    [Min(0f)] [SerializeField] private float breachWeight = 20f;

    [Tooltip("땅굴·브리칭 습격이 나올 수 있는 가장 이른 날")]
    [Min(0)] [SerializeField] private int specialRaidMinDay = 5;

    [Tooltip("땅굴 경고 뒤 적이 솟아 나오기까지 (초)")]
    [Min(0f)] [SerializeField] private float tunnelWarningSeconds = 4f;

    [Tooltip("브리칭 습격에 섞이는 자폭체 ID")]
    [SerializeField] private int bomberXenopsId = 6103;

    [Tooltip("브리칭 습격 — 전체 마리 수 중 자폭체 비율 (최소 1마리)")]
    [Range(0f, 1f)] [SerializeField] private float bomberShare = 0.33f;

    [Header("깊이 위협 — 단계 = 최대 깊이가 지표에서 몇 지층 아래인지 (RunStatsManager.ThreatStage, 임시 수치)")]
    [Tooltip("심층 전용 적 비율 — 단계당 (deepEnemyMinStage부터, 최대 deepShareMax)")]
    [Range(0f, 1f)] [SerializeField] private float deepSharePerStage = 0.15f;
    [Range(0f, 1f)] [SerializeField] private float deepShareMax = 0.5f;

    [Tooltip("단계마다 적 체력·피해 +이 비율")]
    [Min(0f)] [SerializeField] private float statBonusPerStage = 0.2f;

    [Tooltip("단계마다 땅굴 습격 가중치 +이만큼")]
    [Min(0f)] [SerializeField] private float tunnelWeightPerStage = 10f;

    [Tooltip("심층 전용 적 ID — 이 단계부터 늘어난 적 자리를 채운다")]
    [SerializeField] private int deepXenopsId = 6104;
    [Min(0)] [SerializeField] private int deepEnemyMinStage = 1;

    [Header("디버그")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    #region 상태

    private RaidState currentState = RaidState.None;
    private RaidData activeRaid;
    private RaidType activeRaidType = RaidType.Normal;

    /// <summary>이번 습격의 위협 단계 (시작 시점) · 그 단계로 늘어난 적 수</summary>
    private int activeThreatStage;
    private readonly List<List<RaidSpawnEntry>> activePlan = new List<List<RaidSpawnEntry>>();

    /// <summary>마지막으로 계산한 위협 점수와 항목 내역 (디버그·개발용 표시)</summary>
    public ThreatScore.Breakdown LastThreat { get; private set; }
    private int currentWaveIndex;
    private float waveTimer;
    private bool waitingForNextWave;

    /// <summary>이번 레이드의 스폰 수량 배수 (시작 시점 진행 상황으로 계산)</summary>
    private float activeMultiplier = 1f;

    /// <summary>지난 습격이 시작된 시각과 다음 자동 습격 시각 (게임일 단위, 세이브됨). -1 = 아직 없음</summary>
    private float lastRaidGameDay = -1f;
    private float nextRaidGameDay = -1f;

    /// <summary>이번 레이드에서 스폰된 제놉스 목록</summary>
    private readonly List<Xenops> spawnedEntities = new List<Xenops>();

    /// <summary>진행 중인 레이드 비동기 작업의 취소원 (한 번에 하나만)</summary>
    private CancellationTokenSource raidCts;

    #endregion

    #region 프로퍼티

    public RaidState CurrentState => currentState;
    public RaidData ActiveRaid => activeRaid;
    public RaidType ActiveRaidType => activeRaidType;

    public int ActiveThreatStage => activeThreatStage;

    /// <summary>지금 런의 위협 단계 (런 기록이 없으면 0)</summary>
    private static int CurrentThreatStage => RunStatsManager.instance != null ? RunStatsManager.instance.ThreatStage : 0;

    /// <summary>알림용 단계 이름 — 0단계부터 Ⅰ단계</summary>
    public static string StageLabel(int stage)
    {
        string[] roman = { "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ", "Ⅶ", "Ⅷ" };
        return (stage >= 0 && stage < roman.Length ? roman[stage] : (stage + 1).ToString()) + "단계";
    }

    /// <summary>알림·UI용 습격 종류 이름</summary>
    public static string TypeLabel(RaidType t) => t == RaidType.Tunnel ? "땅굴 습격" : t == RaidType.Breach ? "브리칭 습격" : "일반 습격";
    public bool IsRaidActive => currentState == RaidState.InProgress || currentState == RaidState.Approaching;

    #endregion

    #region ISaveModule

    /// <summary>Event(80)보다 뒤에 복원</summary>
    public int SaveOrder => 90;

    public void Capture(SaveData data)
    {
        var save = new RaidSystemSaveData
        {
            raidState   = (int)currentState,
            activeRaidId = activeRaid != null ? activeRaid.raidId : -1,
            currentWaveIndex = currentWaveIndex,
            waveTimer   = waveTimer,
            activeMultiplier = activeMultiplier,
            raidType = (int)activeRaidType,
            threatStage = activeThreatStage,
            lastRaidGameDay = lastRaidGameDay,
            nextRaidGameDay = nextRaidGameDay
        };

        foreach (var xenops in spawnedEntities)
        {
            if (xenops != null)
                save.spawnedEntityIds.Add(xenops.InstanceId);
        }

        data.raidSystem = save;
    }

    public void Restore(SaveData data)
    {
        if (data.raidSystem == null) return;

        currentState     = (RaidState)data.raidSystem.raidState;
        currentWaveIndex = data.raidSystem.currentWaveIndex;
        waveTimer        = data.raidSystem.waveTimer;
        activeMultiplier = data.raidSystem.activeMultiplier > 0f ? data.raidSystem.activeMultiplier : 1f;
        lastRaidGameDay  = data.raidSystem.lastRaidGameDay;
        nextRaidGameDay  = data.raidSystem.nextRaidGameDay;
        activeRaidType   = (RaidType)data.raidSystem.raidType;
        activeThreatStage = data.raidSystem.threatStage;

        int raidId = data.raidSystem.activeRaidId;
        if (raidId >= 0)
            activeRaid = GetRaidById(raidId);
    }

    public void PostRestore(SaveData data)
    {
        if (data.raidSystem == null) return;

        // 스폰된 제놉스 참조 복원
        spawnedEntities.Clear();
        foreach (var id in data.raidSystem.spawnedEntityIds)
        {
            if (RuntimeIDRegistry.instance == null) continue;
            var obj = RuntimeIDRegistry.instance.ResolveComponent<Xenops>(id);
            if (obj != null)
            {
                spawnedEntities.Add(obj);
                ApplyRaidTypeTo(obj);
                obj.ApplyThreatScaling(StageMultiplier, true); // 개체 체력은 저장되지 않아 가득 찬 채로 복원된다 — 단계 배율로 다시 채움
            }
        }

        // InProgress 상태로 저장됐다면 감시 재시작
        if (currentState == RaidState.InProgress && activeRaid != null)
        {
            MonitorRaidCompletionAsync(RestartRaidTask()).Forget();
        }
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// ID로 레이드를 시작합니다.
    /// </summary>
    public void StartRaid(int raidId)
    {
        var raid = GetRaidById(raidId);
        if (raid == null)
        {
            Debug.LogWarning($"[RaidManager] raidId {raidId}를 찾을 수 없습니다.");
            return;
        }
        StartRaid(raid);
    }

    /// <summary>
    /// RaidData로 레이드를 시작합니다.
    /// </summary>
    public void StartRaid(RaidData raid) => StartRaid(raid, RaidType.Normal);

    /// <summary>RaidData와 습격 종류로 레이드를 시작합니다.</summary>
    public void StartRaid(RaidData raid, RaidType type)
    {
        if (raid == null) return;

        // 디버그: 외부 침략 차단 — StartRaid(int)/StartRandomRaid/이벤트 효과가 모두 이 지점을 거친다
        if (DebugManager.IsBlocked(DebugFlag.Raid))
        {
            Debug.Log("[RaidManager] 디버그 차단으로 레이드를 시작하지 않습니다.");
            return;
        }

        if (IsRaidActive)
        {
            Debug.LogWarning("[RaidManager] 이미 레이드가 진행 중입니다. 새 레이드를 시작할 수 없습니다.");
            return;
        }

        activeRaid = raid;
        activeRaidType = type;
        activeThreatStage = CurrentThreatStage;
        currentWaveIndex = 0;
        spawnedEntities.Clear();
        activeMultiplier = 1f; // 구 배수 방식 — 세이브 호환용으로만 남김
        LastThreat = CurrentThreat();
        int total = EnemyCountFor(LastThreat.total, type);
        BuildSpawnPlan(raid, type, total);
        Debug.Log($"[RaidManager] {raid.raidName} {TypeLabel(type)} · {StageLabel(activeThreatStage)} — {LastThreat} → {total}마리 (한 마리당 {PointsPerEnemy(type)})");
        lastRaidGameDay = GameDayNow;
        ScheduleNextRaid();

        if (showDebugLogs)
            Debug.Log($"[RaidManager] 레이드 시작: {raid.raidName} ({TypeLabel(type)}, 위치 {raid.spawnLocation})");

        RunRaidAsync(raid, RestartRaidTask()).Forget();
    }

    /// <summary>
    /// 현재 레이드를 강제로 종료합니다.
    /// </summary>
    public void ForceEndRaid()
    {
        CancelRaidTask();
        CompleteRaid();
    }

    /// <summary>
    /// 랜덤 레이드를 선택하여 시작합니다.
    /// 지난 습격 뒤 strongRaidAfterDays가 지났으면 강한 습격도 후보에 넣고, 아니면 약한 습격만 뽑습니다.
    /// </summary>
    public void StartRandomRaid()
    {
        bool overdue = lastRaidGameDay < 0f || GameDayNow - lastRaidGameDay >= strongRaidAfterDays;
        int day = DayCycle.instance != null ? DayCycle.instance.Day : 0;
        var pool = availableRaids.FindAll(r => r != null && r.includeInRandomPool && day >= r.minDayNumber && (overdue || !r.isStrong));
        if (pool.Count == 0) pool = availableRaids.FindAll(r => r != null && r.includeInRandomPool);
        if (pool.Count == 0)
        {
            Debug.LogWarning("[RaidManager] 랜덤 레이드 풀이 비어 있습니다.");
            return;
        }
        StartRaid(pool[UnityEngine.Random.Range(0, pool.Count)], RollRaidType(day));
    }

    /// <summary>습격 종류를 가중치로 뽑는다 — 땅굴·브리칭은 specialRaidMinDay부터</summary>
    private RaidType RollRaidType(int day)
    {
        bool special = day >= specialRaidMinDay;
        float t = special ? tunnelWeight + tunnelWeightPerStage * CurrentThreatStage : 0f, b = special ? breachWeight : 0f;
        float total = normalWeight + t + b;
        if (total <= 0f) return RaidType.Normal;
        float r = UnityEngine.Random.value * total;
        if (r < normalWeight) return RaidType.Normal;
        return r < normalWeight + t ? RaidType.Tunnel : RaidType.Breach;
    }

    #endregion

    #region 레이드 비동기 진행

    /// <summary>
    /// 진행 중인 레이드 작업을 끊고 새 취소 토큰을 발급합니다.
    /// 오브젝트가 파괴되면 함께 취소되도록 파괴 토큰에 묶습니다.
    /// </summary>
    private CancellationToken RestartRaidTask()
    {
        CancelRaidTask();
        raidCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        return raidCts.Token;
    }

    /// <summary>진행 중인 레이드 작업을 취소합니다.</summary>
    private void CancelRaidTask()
    {
        if (raidCts == null) return;

        raidCts.Cancel();
        raidCts.Dispose();
        raidCts = null;
    }

    private void OnDestroy()
    {
        CancelRaidTask();
    }

    private async UniTaskVoid RunRaidAsync(RaidData raid, CancellationToken ct)
    {
        // 접근 연출
        currentState = RaidState.Approaching;
        if (showDebugLogs) Debug.Log($"[RaidManager] {raid.raidName} 접근 중... ({raid.approachDuration}초)");
        await UniTask.Delay(TimeSpan.FromSeconds(raid.approachDuration), cancellationToken: ct);

        // 레이드 시작
        currentState = RaidState.InProgress;
        GameMessageBus.Publish(new RaidStartedMessage(raid));

        // 위협 레터 + 강제 일시정지 (확인 시 NotificationManager가 재개)
        NotificationManager.instance?.PushLetter(new Letter
        {
            title = $"침공 발생 — {TypeLabel(activeRaidType)} · {StageLabel(activeThreatStage)}",
            body = $"{raid.raidName} — {TypeDescription(activeRaidType)} 직원을 소집해 방어하세요.\n(위협 점수 {LastThreat.total:F0})",
            type = LetterType.Threat,
            pauseUntilRead = true
        });
        TimeManager.instance?.ForcePause();

        // 웨이브 순차 스폰
        for (int i = 0; i < raid.waves.Count; i++)
        {
            currentWaveIndex = i;
            var wave = raid.waves[i];

            if (showDebugLogs) Debug.Log($"[RaidManager] 웨이브 {i + 1}/{raid.waves.Count} 대기 ({wave.delayBeforeWave}초)");
            await UniTask.Delay(TimeSpan.FromSeconds(wave.delayBeforeWave), cancellationToken: ct);

            GameMessageBus.Publish(new RaidWaveStartedMessage(i));
            await SpawnWaveAsync(wave, ct);
        }

        // 전멸 또는 철수 대기
        if (raid.retreatDelay > 0f)
            await UniTask.Delay(TimeSpan.FromSeconds(raid.retreatDelay), cancellationToken: ct);

        // 모든 웨이브 소진 후 전멸 대기 모니터링
        await MonitorRaidCompletionAsync(ct);
    }

    private static string TypeDescription(RaidType t)
        => t == RaidType.Tunnel ? "땅 밑을 파고 기지 근처로 직원을 노리는 적이 옵니다."
         : t == RaidType.Breach ? "벽을 터뜨리는 자폭체가 섞여 접근합니다."
         : "적대 개체가 접근했습니다.";

    private async UniTask SpawnWaveAsync(RaidWave wave, CancellationToken ct)
    {
        // 땅굴: 웨이브마다 한 자리에서 — 경고를 띄우고 잠시 뒤 솟아 나온다 (자리가 없으면 일반 위치)
        Vector3? tunnelPos = null;
        if (activeRaidType == RaidType.Tunnel && RaidSpawnPlacer.TryFindTunnelPosition(out var p))
        {
            tunnelPos = p;
            NotificationManager.instance?.PushLetter(new Letter
            {
                title = "땅이 울립니다",
                body = "기지 근처 땅 밑에서 무언가 파고 올라옵니다.",
                type = LetterType.Threat,
                onClick = () => CameraFocus(p)
            });
            await UniTask.Delay(TimeSpan.FromSeconds(tunnelWarningSeconds), cancellationToken: ct);
        }

        // 계획된 적을 차례로 (StartRaid에서 위협 점수로 정해 둔 마리 수 — BuildSpawnPlan)
        if (currentWaveIndex < activePlan.Count)
        {
            foreach (var spawn in activePlan[currentWaveIndex])
            {
                SpawnRaidXenops(spawn, spawn.xenopsDataId == bomberXenopsId ? null : tunnelPos);
                if (spawn.spawnInterval > 0f)
                    await UniTask.Delay(TimeSpan.FromSeconds(spawn.spawnInterval), cancellationToken: ct);
            }
        }
    }

    private static void CameraFocus(Vector3 p)
    {
        var cam = Camera.main;
        if (cam != null) cam.transform.position = new Vector3(p.x, p.y, cam.transform.position.z);
    }

    /// <summary>습격 종류에 따른 개체 설정 — 땅굴 적은 직원만 노린다 (건물만 있으면 부수러 가지 않음)</summary>
    private void ApplyRaidTypeTo(Xenops xenops)
    {
        if (activeRaidType != RaidType.Tunnel || xenops == null) return;
        var nav = xenops.GetComponent<EnemyNavigator>() ?? xenops.gameObject.AddComponent<EnemyNavigator>();
        nav.StaffOnly = true;
    }

    private async UniTask MonitorRaidCompletionAsync(CancellationToken ct)
    {
        // 모든 스폰 개체가 제거될 때까지 대기
        while (true)
        {
            spawnedEntities.RemoveAll(x => x == null);
            if (spawnedEntities.Count == 0) break;
            await UniTask.Delay(TimeSpan.FromSeconds(1f), cancellationToken: ct);
        }

        CompleteRaid();
    }

    #endregion

    #region 내부 유틸

    /// <summary>습격으로 온 적인지 (처치 시 전리품 드롭 판정)</summary>
    public bool IsRaider(Xenops xenops) => xenops != null && spawnedEntities.Contains(xenops);

    private void SpawnRaidXenops(RaidSpawnEntry entry, Vector3? at)
    {
        if (XenopsManager.instance == null) return;

        // 땅굴 자리는 좁은 굴이라 흩뿌리지 않는다
        Vector3 spawnPos = SnapToStandable(at.HasValue ? at.Value + new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 0f, 0f) : GetSpawnPosition(entry));
        var xenops = XenopsManager.instance.SpawnXenops(entry.xenopsDataId, spawnPos);

        if (xenops != null)
        {
            spawnedEntities.Add(xenops);
            ApplyRaidTypeTo(xenops);
            xenops.ApplyThreatScaling(StageMultiplier, true);

            if (showDebugLogs)
                Debug.Log($"[RaidManager] 제놉스 스폰: ID {entry.xenopsDataId} at {spawnPos}");
        }
    }

    private TilePathfinder standChecker;
    private GameMap standCheckerMap;

    /// <summary>
    /// 흩뿌린 스폰 지점이 지형 속이면 가까운(4칸 안) 설 수 있는 칸으로 옮긴다 —
    /// 바위 속에 낀 적은 길을 못 잡아 제자리에 멈췄다. 못 찾으면 그대로.
    /// </summary>
    private Vector3 SnapToStandable(Vector3 pos)
    {
        var map = MapGenerator.instance != null ? MapGenerator.instance.GameMapInstance : null;
        if (map == null) return pos;
        if (standCheckerMap != map) { standCheckerMap = map; standChecker = new TilePathfinder(map); }

        var foot = new Vector2Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y));
        if (standChecker.IsValidPosition(foot)) return pos;
        for (int r = 1; r <= 4; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                    var c = foot + new Vector2Int(dx, dy);
                    if (standChecker.IsValidPosition(c)) return new Vector3(c.x + 0.5f, c.y + 0.3f, pos.z);
                }
        return pos;
    }

    private Vector3 GetSpawnPosition(RaidSpawnEntry entry)
    {
        // 레이드 설정 위치 유형(안개 지상/지하, 기지 내부)에서 후보 선정
        Vector3 basePos;
        if (activeRaid == null || !RaidSpawnPlacer.TryFindPosition(activeRaid.spawnLocation, out basePos))
        {
            // 후보 없음(맵 전부 밝혀짐 등) → 인스펙터 기준점 폴백
            if (showDebugLogs && activeRaid != null)
                Debug.Log($"[RaidManager] {activeRaid.spawnLocation} 스폰 후보 없음 → 기준점 폴백");

            basePos = defaultSpawnPoint != null ? defaultSpawnPoint.position : Vector3.zero;
        }

        Vector2 offset = UnityEngine.Random.insideUnitCircle * entry.spawnRadius;
        return basePos + new Vector3(offset.x, offset.y, 0f);
    }

    #region 자동 습격 일정

    /// <summary>지금 시각 (게임일 단위, 하루 안의 진행도 포함)</summary>
    private static float GameDayNow =>
        DayCycle.instance != null ? DayCycle.instance.Day + DayCycle.instance.TimeNormalized : 0f;

    private void ScheduleNextRaid()
    {
        float from = Mathf.Max(GameDayNow, firstRaidDay);
        nextRaidGameDay = from + UnityEngine.Random.Range(raidIntervalDays.x, Mathf.Max(raidIntervalDays.x, raidIntervalDays.y));
    }

    private void Update()
    {
        if (!autoSchedule || IsRaidActive || DayCycle.instance == null) return;
        if (nextRaidGameDay < 0f) { ScheduleNextRaid(); return; }
        if (GameDayNow < nextRaidGameDay) return;

        StartRandomRaid();
        if (!IsRaidActive) ScheduleNextRaid(); // 디버그 차단 등으로 안 시작했으면 다음으로 미룬다
    }

    #endregion

    /// <summary>지금 런의 위협 점수 (난이도·적응 보정은 런 기록에서)</summary>
    private ThreatScore.Breakdown CurrentThreat()
    {
        var rs = RunStatsManager.instance;
        return ThreatScore.Compute(rs != null ? rs.Difficulty : null, rs != null ? rs.ThreatAdapt : 1f, activeThreatStage);
    }

    /// <summary>습격 종류의 한 마리당 점수</summary>
    private float PointsPerEnemy(RaidType type)
        => type == RaidType.Tunnel ? tunnelPointsPerEnemy : type == RaidType.Breach ? breachPointsPerEnemy : normalPointsPerEnemy;

    /// <summary>마리 수 = 위협 점수 ÷ 한 마리당 점수 (반올림, 최소 1, 기술적 최대)</summary>
    public int EnemyCountFor(float threat, RaidType type)
        => Mathf.Clamp(Mathf.RoundToInt(threat / PointsPerEnemy(type)), 1, maxEnemiesTechnical);

    /// <summary>
    /// 이번 습격의 스폰 계획 — 정해진 마리 수를 RaidData 웨이브 항목의 count 비율대로 나누고(큰 나머지 순),
    /// 깊이 단계면 그 비율만큼 심층 전용 적, 브리칭이면 자폭체로 첫 웨이브 몫을 바꾼다.
    /// </summary>
    private void BuildSpawnPlan(RaidData raid, RaidType type, int total)
    {
        activePlan.Clear();
        foreach (var _ in raid.waves) activePlan.Add(new List<RaidSpawnEntry>());
        if (activePlan.Count == 0) return;

        int deep = activeThreatStage >= deepEnemyMinStage
            ? Mathf.Max(1, Mathf.RoundToInt(total * Mathf.Min(deepShareMax, deepSharePerStage * activeThreatStage))) : 0;
        int bombers = type == RaidType.Breach ? Mathf.Max(1, Mathf.RoundToInt(total * bomberShare)) : 0;
        if (deep + bombers > total) deep = Mathf.Max(0, total - bombers);
        int regular = total - deep - bombers;

        // 일반 적 — 항목 count 비율대로 (큰 나머지 순)
        var slots = new List<(int wave, RaidSpawnEntry entry)>();
        int weightSum = 0;
        for (int w = 0; w < raid.waves.Count; w++)
            foreach (var e in raid.waves[w].entries) { slots.Add((w, e)); weightSum += Mathf.Max(1, e.count); }
        if (slots.Count > 0 && regular > 0)
        {
            var counts = new int[slots.Count];
            var rem = new float[slots.Count];
            int given = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                float exact = regular * Mathf.Max(1, slots[i].entry.count) / (float)weightSum;
                counts[i] = Mathf.FloorToInt(exact);
                rem[i] = exact - counts[i];
                given += counts[i];
            }
            for (; given < regular; given++)
            {
                int best = 0;
                for (int i = 1; i < rem.Length; i++) if (rem[i] > rem[best]) best = i;
                counts[best]++; rem[best] = -1f;
            }
            for (int i = 0; i < slots.Count; i++)
                for (int k = 0; k < counts[i]; k++) activePlan[slots[i].wave].Add(slots[i].entry);
        }

        float radius = slots.Count > 0 ? slots[0].entry.spawnRadius : 2f;
        for (int i = 0; i < deep; i++)
            activePlan[0].Add(new RaidSpawnEntry { xenopsDataId = deepXenopsId, count = 1, spawnInterval = 0.5f, spawnRadius = radius });
        for (int i = 0; i < bombers; i++)
            activePlan[0].Add(new RaidSpawnEntry { xenopsDataId = bomberXenopsId, count = 1, spawnInterval = 0.5f, spawnRadius = 2f });
    }

    /// <summary>위협 단계의 체력·피해 배율</summary>
    private float StageMultiplier => 1f + statBonusPerStage * activeThreatStage;

    private void CompleteRaid()
    {
        currentState = RaidState.Completed;

        if (showDebugLogs && activeRaid != null)
            Debug.Log($"[RaidManager] 레이드 완료: {activeRaid.raidName}");

        GameMessageBus.Publish(new RaidCompletedMessage(activeRaid));

        // 격퇴 레터 (일반 — 정지 없음)
        if (activeRaid != null)
        {
            NotificationManager.instance?.PushLetter(new Letter
            {
                title = "침공 격퇴",
                body = $"{activeRaid.raidName}을(를) 격퇴했습니다.",
                type = LetterType.Positive
            });
        }

        // 레이드 종료 후 가속 회복은 v10에서 제거됐다 —
        // 침식 회복 경로는 자연 회복(하한까지)·세척 시설·무작위 이벤트 셋뿐이다.

        // 상태 리셋
        activeRaid = null;
        activeRaidType = RaidType.Normal;
        currentWaveIndex = 0;
        waveTimer = 0f;
        spawnedEntities.Clear();
        currentState = RaidState.None;
    }

    private RaidData GetRaidById(int id)
    {
        return availableRaids.Find(r => r != null && r.raidId == id);
    }

    #endregion

    #region 컨텍스트 메뉴 (디버그)

    [ContextMenu("랜덤 레이드 시작 (테스트)")]
    private void DebugStartRandomRaid() => StartRandomRaid();

    [ContextMenu("현재 레이드 강제 종료 (테스트)")]
    private void DebugForceEnd() => ForceEndRaid();

    #endregion
}
