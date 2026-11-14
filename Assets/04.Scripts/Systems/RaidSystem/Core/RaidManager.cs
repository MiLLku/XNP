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

    [Tooltip("일반·땅굴·브리칭·분산·침식된 인간 습격이 이 게임일 넘게 끝나지 않으면 남은 적이 물러난다 (0이면 끔).\n" +
             "땅굴로 기지 바닥 밑에 들어간 적처럼 서로 닿지 못하는 적 때문에 습격이 영영 '진행 중'으로 남아 다음 습격이 오지 않던 문제를 막는다. 거점·스포너·위장체는 제외")]
    [Min(0f)] [SerializeField] private float raidRetreatAfterDays = 1f;

    [Tooltip("초반 습격 마리 수 상한 — x일차까지 최대 y마리 (일차 오름차순). 마지막 x 이후는 위협 계산 그대로. 위협 점수·스탯 배율은 그대로 (임시 수치)")]
    [SerializeField] private Vector2Int[] earlyEnemyCaps = { new Vector2Int(5, 2), new Vector2Int(8, 3), new Vector2Int(11, 4) };
    [Min(1f)] [SerializeField] private float tunnelPointsPerEnemy = 12f;
    [Min(1f)] [SerializeField] private float breachPointsPerEnemy = 12f;

    [Tooltip("성능을 위한 기술적 최대 마리 수 (난이도 상한이 아님)")]
    [Min(1)] [SerializeField] private int maxEnemiesTechnical = 30;

    [Header("습격 종류 — 습격마다 무작위 (임시 가중치)")]
    [Min(0f)] [SerializeField] private float normalWeight = 60f;
    [Min(0f)] [SerializeField] private float tunnelWeight = 20f;
    [Min(0f)] [SerializeField] private float breachWeight = 20f;

    [Header("습격 후보 풀 — 위협 점수가 이 이상이어야 후보에 든다 (임시 수치, 일수 조건 대체)")]
    [Min(0f)] [SerializeField] private float normalMinThreat = 0f;
    [Min(0f)] [SerializeField] private float tunnelMinThreat = 35f;
    [Min(0f)] [SerializeField] private float breachMinThreat = 35f;
    [Min(0f)] [SerializeField] private float outpostMinThreat = 50f;
    [Min(0f)] [SerializeField] private float spawnerMinThreat = 50f;
    [Min(0f)] [SerializeField] private float splitMinThreat = 90f;
    [Min(0f)] [SerializeField] private float disguiseMinThreat = 40f;
    [Min(0f)] [SerializeField] private float erodedHumansMinThreat = 40f;

    [Header("습격 후보 풀 — 이 일차 전에는 위협이 높아도 후보에서 빠진다 (임시 수치)")]
    [Min(0f)] [SerializeField] private float tunnelMinDay = 8f;
    [Min(0f)] [SerializeField] private float breachMinDay = 8f;
    [Min(0f)] [SerializeField] private float outpostMinDay = 14f;
    [Min(0f)] [SerializeField] private float spawnerMinDay = 14f;
    [Min(0f)] [SerializeField] private float disguiseMinDay = 14f;
    [Min(0f)] [SerializeField] private float erodedHumansMinDay = 14f;
    [Min(0f)] [SerializeField] private float splitMinDay = 20f;

    [Tooltip("바로 직전 습격과 같은 종류의 가중치 배율")]
    [Range(0f, 1f)] [SerializeField] private float repeatLastFactor = 0.25f;
    [Tooltip("그 전 습격과 같은 종류의 가중치 배율")]
    [Range(0f, 1f)] [SerializeField] private float repeatPrevFactor = 0.6f;

    [Header("거점 습격 — 구조물을 짓고 지킨다 (임시 수치)")]
    [Min(0f)] [SerializeField] private float outpostWeight = 15f;
    [Tooltip("구조물 하나가 먼저 차지하는 위협 점수 — 나머지로 경비 마리 수")]
    [Min(0f)] [SerializeField] private float outpostStructurePoints = 25f;
    [Tooltip("구조물 XenopsData ID (무작위 하나): 침식 송신탑, 공포 토템")]
    [SerializeField] private int[] outpostStructureIds = { 6201, 6202 };
    [Min(1f)] [SerializeField] private float outpostGuardRadius = 7f;
    [Tooltip("경비로 못 쓰는 적(자체 조준 사격) → 이 ID로 바꿔 보냄")]
    [SerializeField] private int outpostShooterId = 6100;
    [SerializeField] private int outpostShooterReplacementId = 6101;

    [Header("스포너 습격 — 부수기 전까지 적을 계속 내보낸다 (임시 수치)")]
    [Min(0f)] [SerializeField] private float spawnerWeight = 15f;
    [Tooltip("스포너가 차지하는 위협 점수 (호위 없이 스포너만 — 알림 표시용)")]
    [Min(0f)] [SerializeField] private float spawnerPoints = 30f;
    [SerializeField] private int spawnerXenopsId = 6301;
    [Tooltip("스포너 패턴 후보 적 ID — 심층 단계부터 deepXenopsId도 섞인다")]
    [SerializeField] private int[] spawnerPatternIds = { 6101, 6102, 6100 };
    [SerializeField] private Vector2 spawnerDistance = new Vector2(15f, 30f);

    [Header("위장체 습격 — 알림 없이 기지 안에 직원 모습으로 하나 (임시 수치)")]
    [Min(0f)] [SerializeField] private float disguiseWeight = 10f;

    [Header("침식된 인간 습격 — 무작위 장비를 입은 침식된 사람들 (임시 수치)")]
    [Min(0f)] [SerializeField] private float erodedHumansWeight = 12f;
    [Min(1f)] [SerializeField] private float erodedHumanPointsPerEnemy = 15f;

    [Header("분산 습격 — 여러 곳에서 나눠 오는 대신 규모가 작다 (임시 수치)")]
    [Min(0f)] [SerializeField] private float splitWeight = 15f;
    [Tooltip("위협 점수에 곱해 마리 수를 줄인다")]
    [Range(0.1f, 1f)] [SerializeField] private float splitThreatFactor = 0.8f;
    [Tooltip("스폰 지점 수 (최소~최대, 무작위)")]
    [SerializeField] private Vector2Int splitPointCount = new Vector2Int(3, 4);
    [Tooltip("스폰 지점끼리 최소 거리(칸)")]
    [Min(0f)] [SerializeField] private float splitMinSeparation = 15f;
    [Tooltip("기지(가장 가까운 앵커)에서 이보다 먼 후보는 버린다 — 근처 안개 후보가 없으면 맵 반대편까지 잡혔다")]
    [Min(1f)] [SerializeField] private float splitMaxFromBase = 40f;


    [Tooltip("땅굴 경고 뒤 적이 솟아 나오기까지 (초)")]
    [Min(0f)] [SerializeField] private float tunnelWarningSeconds = 4f;

    [Tooltip("브리칭 습격에 섞이는 자폭체 ID")]
    [SerializeField] private int bomberXenopsId = 6103;

    [Tooltip("브리칭 습격 — 전체 마리 수 중 자폭체 비율 (최소 1마리)")]
    [Range(0f, 1f)] [SerializeField] private float bomberShare = 0.33f;

    [Header("위협 단계 — 보정 전 총 가치(직원+자원+건물)가 문턱을 넘을 때마다 한 단계, 회차 최대 유지 (RunStatsManager.ThreatStage, 임시 수치)")]
    [Tooltip("Ⅱ·Ⅲ·Ⅳ·Ⅴ단계가 되는 총 가치 문턱 (오름차순)")]
    [SerializeField] private float[] threatStageValues = { 1500f, 3000f, 5000f, 8000f };

    /// <summary>총 가치에 해당하는 위협 단계 (0 = Ⅰ)</summary>
    public int StageForValue(float value)
    {
        int s = 0;
        if (threatStageValues != null) foreach (var t in threatStageValues) if (value >= t) s++;
        return s;
    }

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

    /// <summary>최근 습격 종류 (0 = 바로 직전, 1 = 그 전), 세이브됨 — 같은 종류가 연달아 나오지 않게</summary>
    private readonly List<RaidType> recentRaidTypes = new List<RaidType>();

    // 거점 습격 — 자리·구조물 종류(스폰 전)와 지어지는 구조물(스폰 후). 스포너 습격도 자리(activeOutpostSite)를 같이 쓴다
    private Vector3? activeOutpostSite;
    private int activeOutpostId;
    private Xenops activeOutpost;
    private bool outpostGuardReleased;

    // 분산 습격 — 이번 습격의 스폰 지점들 (스폰 순서대로 돌아가며 쓴다)
    private readonly List<Vector3> splitPoints = new List<Vector3>();

    /// <summary>이번 습격의 위협 단계 (시작 시점) · 그 단계로 늘어난 적 수</summary>
    private int activeThreatStage;
    private readonly List<List<RaidSpawnEntry>> activePlan = new List<List<RaidSpawnEntry>>();

    /// <summary>마지막으로 계산한 위협 점수와 항목 내역 (디버그·개발용 표시)</summary>
    public ThreatScore.Breakdown LastThreat { get; private set; }
    private int currentWaveIndex;
    private int wavesSpawned = -1;   // 다 내보낸 웨이브 수 (-1 = 웨이브 방식이 아님)
    private int spawnedInWave;       // 진행 중 웨이브에서 이미 내보낸 마리 수
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
    public static string TypeLabel(RaidType t) => t == RaidType.Tunnel ? "땅굴 습격" : t == RaidType.Breach ? "브리칭 습격" : t == RaidType.Outpost ? "거점 습격" : t == RaidType.Spawner ? "스포너 습격" : t == RaidType.Split ? "분산 습격" : t == RaidType.Disguise ? "위장체 습격" : t == RaidType.ErodedHumans ? "침식된 인간 습격" : "일반 습격";
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
            wavesSpawned = currentState == RaidState.InProgress ? wavesSpawned : -1,
            spawnedInWave = spawnedInWave,
            activeMultiplier = activeMultiplier,
            raidType = (int)activeRaidType,
            threatStage = activeThreatStage,
            recentRaidTypes = recentRaidTypes.ConvertAll(t => (int)t),
            disguises = DisguisedEmployeeSaves(),
            lastRaidGameDay = lastRaidGameDay,
            nextRaidGameDay = nextRaidGameDay
        };

        foreach (var xenops in spawnedEntities)
        {
            if (xenops != null)
                save.spawnedEntityIds.Add(xenops.InstanceId);
        }

        // 아직 나오지 않은 웨이브의 스폰 계획 (진행 중 웨이브는 이미 나온 마리까지 포함 — 불러올 때 건너뛴다)
        if (save.wavesSpawned >= 0)
            for (int w = save.wavesSpawned; w < activePlan.Count; w++)
                foreach (var e in activePlan[w]) { save.planEntries.Add(e); save.planWave.Add(w); }

        data.raidSystem = save;
    }

    public void Restore(SaveData data)
    {
        // 불러오기 전 습격의 감시 작업이 복원 도중(개체가 비었을 때) 습격을 끝내지 않게 먼저 끊는다
        CancelRaidTask();
        if (data.raidSystem == null) return;

        currentState     = (RaidState)data.raidSystem.raidState;
        currentWaveIndex = data.raidSystem.currentWaveIndex;
        wavesSpawned     = data.raidSystem.wavesSpawned;
        spawnedInWave    = data.raidSystem.spawnedInWave;
        waveTimer        = data.raidSystem.waveTimer;
        activeMultiplier = data.raidSystem.activeMultiplier > 0f ? data.raidSystem.activeMultiplier : 1f;
        lastRaidGameDay  = data.raidSystem.lastRaidGameDay;
        nextRaidGameDay  = data.raidSystem.nextRaidGameDay;
        activeRaidType   = (RaidType)data.raidSystem.raidType;
        activeThreatStage = data.raidSystem.threatStage;
        recentRaidTypes.Clear();
        if (data.raidSystem.recentRaidTypes != null)
            foreach (var t in data.raidSystem.recentRaidTypes) recentRaidTypes.Add((RaidType)t);

        int raidId = data.raidSystem.activeRaidId;
        if (raidId >= 0)
            activeRaid = GetRaidById(raidId);

        // 남은 스폰 계획 복원
        activePlan.Clear();
        if (activeRaid != null && wavesSpawned >= 0)
        {
            foreach (var _ in activeRaid.waves) activePlan.Add(new List<RaidSpawnEntry>());
            var entries = data.raidSystem.planEntries;
            var waves = data.raidSystem.planWave;
            for (int i = 0; entries != null && waves != null && i < entries.Count && i < waves.Count; i++)
                if (waves[i] >= 0 && waves[i] < activePlan.Count) activePlan[waves[i]].Add(entries[i]);
        }
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
                obj.ApplyThreatScaling(StageMultiplier, true); // 체력 비율은 유지한 채 단계 배율로 최대 체력 다시 맞춤
            }
        }

        // 위장 중인 위장체 (직원 목록 밖이라 직원 세이브에 없다)
        foreach (var d in UnityEngine.Object.FindObjectsByType<DisguisedEmployee>(FindObjectsSortMode.None)) Destroy(d.gameObject);
        if (data.raidSystem.disguises != null)
            foreach (var d in data.raidSystem.disguises) DisguisedEmployee.Restore(d.employee, d.sourceEmployeeId, d.alertCooldown);

        // 거점 습격: 남은 구조물을 다시 잡고, 경비는 그 자리를 지킨다
        activeOutpost = activeRaidType == RaidType.Outpost ? spawnedEntities.Find(x => x != null && x.GetComponent<OutpostStructure>() != null) : null;
        outpostGuardReleased = activeRaidType == RaidType.Outpost && activeOutpost == null;
        activeOutpostSite = activeOutpost != null ? activeOutpost.transform.position : (Vector3?)null;
        foreach (var x in spawnedEntities) ApplyRaidTypeTo(x);

        // InProgress 상태로 저장됐다면 — 남은 웨이브가 있으면 이어서 내보내고, 없으면 감시만 재시작
        if (currentState == RaidState.InProgress && activeRaid != null)
        {
            if (wavesSpawned >= 0 && wavesSpawned < activeRaid.waves.Count)
                ResumeWavesAsync(activeRaid, RestartRaidTask()).Forget();
            else
                MonitorRaidCompletionAsync(RestartRaidTask()).Forget();
        }
        // 접근 중에 저장됐다면 — 스폰 계획(자리·마리 수)은 저장하지 않으므로 같은 습격·종류로 다시 시작 (전엔 영영 멈춰 있었다)
        else if (currentState == RaidState.Approaching && activeRaid != null)
        {
            var raid = activeRaid;
            var type = activeRaidType;
            if (recentRaidTypes.Count > 0) recentRaidTypes.RemoveAt(0); // StartRaid가 다시 기록한다
            currentState = RaidState.None;
            StartRaid(raid, type);
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
        currentWaveIndex = 0; wavesSpawned = -1; spawnedInWave = 0;
        spawnedEntities.Clear();
        activeMultiplier = 1f; // 구 배수 방식 — 세이브 호환용으로만 남김
        LastThreat = CurrentThreat();
        int total = type == RaidType.Outpost
            ? Mathf.Clamp(Mathf.RoundToInt((LastThreat.total - outpostStructurePoints) / PointsPerEnemy(type)), 1, maxEnemiesTechnical)
            : type == RaidType.Spawner || type == RaidType.Disguise ? 0
            : type == RaidType.Split ? EnemyCountFor(LastThreat.total * splitThreatFactor, type)
            : EnemyCountFor(LastThreat.total, type);
        activeOutpost = null;
        outpostGuardReleased = false;
        activeOutpostSite = null;
        if (type == RaidType.Outpost)
        {
            activeOutpostId = outpostStructureIds[UnityEngine.Random.Range(0, outpostStructureIds.Length)];
            if (RaidSpawnPlacer.TryFindOutpostPosition(out var site)) activeOutpostSite = site;
            else { type = RaidType.Normal; activeRaidType = type; total = EnemyCountFor(LastThreat.total, type); } // 자리가 없으면 일반 습격으로
        }
        if (type == RaidType.Spawner)
        {
            if (RaidSpawnPlacer.TryFindOutpostPosition(out var site, spawnerDistance.x, spawnerDistance.y)) activeOutpostSite = site;
            else { type = RaidType.Normal; activeRaidType = type; total = EnemyCountFor(LastThreat.total, type); }
        }
        splitPoints.Clear();
        if (type == RaidType.Split && !PickSplitPoints(raid))
        {
            type = RaidType.Normal; activeRaidType = type; total = EnemyCountFor(LastThreat.total, type); // 한 곳밖에 못 찾으면 일반 습격으로
        }
        if (type == RaidType.Split) total = Mathf.Max(total, splitPoints.Count); // 지점마다 최소 1마리
        recentRaidTypes.Insert(0, type); // 실제로 나온 종류 (자리가 없어 일반으로 바뀐 것까지)
        if (recentRaidTypes.Count > 2) recentRaidTypes.RemoveRange(2, recentRaidTypes.Count - 2);
        if (lastRaidGameDay < 0f && total > 1) total = 1; // 첫 습격은 1마리 — 장비·방어 시설이 하나도 없을 때다
        total = Mathf.Min(total, EarlyEnemyCap());
        if (type == RaidType.Spawner || type == RaidType.Disguise) activePlan.Clear(); // 처음엔 스포너만 — 적은 스포너가 내보낸다
        else BuildSpawnPlan(raid, type, total);
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
        // 위협 점수 산출 → 그 점수로 가능한 습격 종류 풀에서 고른다
        var rs = RunStatsManager.instance;
        float threat = ThreatScore.Compute(rs != null ? rs.Difficulty : null, rs != null ? rs.ThreatAdapt : 1f).total;
        StartRaid(pool[UnityEngine.Random.Range(0, pool.Count)], RollRaidType(threat));
    }

    /// <summary>
    /// 습격 종류를 고른다 — 위협 점수가 종류별 최소 점수 이상인 것만 후보, 가중치로 뽑되
    /// 바로 직전과 같은 종류는 ×repeatLastFactor, 그 전과 같으면 ×repeatPrevFactor. 땅굴은 깊이 단계마다 가중치 +.
    /// </summary>
    public RaidType RollRaidType(float threat)
    {
        var types = new[] { RaidType.Normal, RaidType.Tunnel, RaidType.Breach, RaidType.Outpost, RaidType.Spawner, RaidType.Split, RaidType.Disguise, RaidType.ErodedHumans };
        var weights = new float[types.Length];
        float total = 0f;
        float day = DayCycle.instance != null ? DayCycle.instance.Day : float.MaxValue;
        for (int i = 0; i < types.Length; i++)
        {
            if (threat < MinThreat(types[i]) || day < MinDay(types[i])) continue;
            float w = BaseWeight(types[i]);
            if (recentRaidTypes.Count > 0 && recentRaidTypes[0] == types[i]) w *= repeatLastFactor;
            if (recentRaidTypes.Count > 1 && recentRaidTypes[1] == types[i]) w *= repeatPrevFactor;
            weights[i] = w;
            total += w;
        }
        if (total <= 0f) return RaidType.Normal;
        float r = UnityEngine.Random.value * total;
        for (int i = 0; i < types.Length; i++)
        {
            if (r < weights[i]) return types[i];
            r -= weights[i];
        }
        return RaidType.Normal;
    }

    private float BaseWeight(RaidType t)
        => t == RaidType.Tunnel ? tunnelWeight + tunnelWeightPerStage * CurrentThreatStage
         : t == RaidType.Breach ? breachWeight : t == RaidType.Outpost ? outpostWeight
         : t == RaidType.Spawner ? spawnerWeight : t == RaidType.Split ? splitWeight
         : t == RaidType.Disguise ? disguiseWeight : t == RaidType.ErodedHumans ? erodedHumansWeight : normalWeight;

    private float MinThreat(RaidType t)
        => t == RaidType.Tunnel ? tunnelMinThreat : t == RaidType.Breach ? breachMinThreat
         : t == RaidType.Outpost ? outpostMinThreat : t == RaidType.Spawner ? spawnerMinThreat
         : t == RaidType.Split ? splitMinThreat : t == RaidType.Disguise ? disguiseMinThreat : t == RaidType.ErodedHumans ? erodedHumansMinThreat : normalMinThreat;

    private float MinDay(RaidType t)
        => t == RaidType.Tunnel ? tunnelMinDay : t == RaidType.Breach ? breachMinDay
         : t == RaidType.Outpost ? outpostMinDay : t == RaidType.Spawner ? spawnerMinDay
         : t == RaidType.Split ? splitMinDay : t == RaidType.Disguise ? disguiseMinDay : t == RaidType.ErodedHumans ? erodedHumansMinDay : 0f;

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

        // 위장체: 알림·정지·화면 이동 없이 하나만 두고 습격은 조용히 닫는다 — 위장체는 처치될 때까지 남는다
        // (습격으로 남겨 두면 찾지 못하는 동안 다음 습격이 영영 오지 않는다)
        if (activeRaidType == RaidType.Disguise) { StartDisguise(); ResetRaidState(); return; }

        // 레이드 시작
        currentState = RaidState.InProgress;
        GameMessageBus.Publish(new RaidStartedMessage(raid));

        // 위협 레터 + 강제 일시정지 (확인 시 NotificationManager가 재개)
        NotificationManager.instance?.PushLetter(new Letter
        {
            title = $"침공 발생 — {TypeLabel(activeRaidType)}{OutpostSuffix}{(activeRaidType == RaidType.Split ? $" · {splitPoints.Count}곳" : "")} · {StageLabel(activeThreatStage)}",
            onClick = activeOutpostSite.HasValue ? () => CameraFocus(activeOutpostSite.Value)
                    : activeRaidType == RaidType.Split && splitPoints.Count > 0 ? () => CameraFocus(splitPoints[0]) : (Action)null,
            body = $"{raid.raidName} — {TypeDescription(activeRaidType)} 직원을 소집해 방어하세요.\n(위협 점수 {LastThreat.total:F0}{(activeRaidType == RaidType.Spawner ? $" · 스포너 {spawnerPoints:F0}점" : "")})",
            type = LetterType.Threat,
            pauseUntilRead = true
        });
        TimeManager.instance?.ForcePause();

        // 스포너: 웨이브 대신 스포너 하나 — 부수고 남은 적까지 처치해야 끝난다
        if (activeRaidType == RaidType.Spawner) { StartSpawner(); await MonitorRaidCompletionAsync(ct); return; }

        // 웨이브 순차 스폰
        wavesSpawned = 0;
        spawnedInWave = 0;
        await SpawnRemainingWavesAsync(raid, ct);
    }

    /// <summary>불러온 진행 중 습격 — 아직 나오지 않은 웨이브를 이어서 내보낸다 (진행 중이던 웨이브는 대기 없이 남은 마리부터)</summary>
    private async UniTaskVoid ResumeWavesAsync(RaidData raid, CancellationToken ct)
    {
        if (showDebugLogs) Debug.Log($"[RaidManager] 불러온 습격 이어가기: 웨이브 {wavesSpawned + 1}/{raid.waves.Count}부터 ({spawnedInWave}마리 나온 뒤)");
        await SpawnRemainingWavesAsync(raid, ct);
    }

    private async UniTask SpawnRemainingWavesAsync(RaidData raid, CancellationToken ct)
    {
        for (int i = wavesSpawned; i < raid.waves.Count; i++)
        {
            currentWaveIndex = i;
            var wave = raid.waves[i];

            if (spawnedInWave == 0)
            {
                if (showDebugLogs) Debug.Log($"[RaidManager] 웨이브 {i + 1}/{raid.waves.Count} 대기 ({wave.delayBeforeWave}초)");
                await UniTask.Delay(TimeSpan.FromSeconds(wave.delayBeforeWave), cancellationToken: ct);
                GameMessageBus.Publish(new RaidWaveStartedMessage(i));
            }
            await SpawnWaveAsync(wave, ct);
            wavesSpawned = i + 1;
            spawnedInWave = 0;
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
         : t == RaidType.Outpost ? "기지 바깥에 해로운 구조물을 세우고 지키려 합니다. 다 지어지기 전에 건설자를 막으세요."
         : t == RaidType.Spawner ? "기지 바깥에 스포너가 생겼습니다. 부수기 전까지 적을 계속 내보냅니다."
         : t == RaidType.Split ? "적이 여러 방향에서 나눠 접근합니다. 수는 적지만 방어를 나눠야 합니다."
         : t == RaidType.ErodedHumans ? "완전히 침식된 사람들이 무기와 방어구를 갖추고 접근합니다. 쓰러뜨린 뒤 정화 혈청을 쓰면 되돌릴 수 있습니다."
         : "적대 개체가 접근했습니다.";

    /// <summary>알림 제목에 붙는 구조물 종류 (' · 침식 송신탑')</summary>
    private string OutpostSuffix
    {
        get
        {
            if (activeRaidType != RaidType.Outpost) return "";
            var d = GameDatabase.Instance != null ? GameDatabase.Instance.GetXenopsData(activeOutpostId) : null;
            var s = d != null && d.prefab != null ? d.prefab.GetComponent<OutpostStructure>() : null;
            return s != null ? " · " + OutpostStructure.KindLabel(s.Kind) : "";
        }
    }

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

        // 거점: 모두 그 자리에 — 첫 경비가 건설자, 구조물은 그 곁에 짓기 시작
        if (activeRaidType == RaidType.Outpost && activeOutpostSite.HasValue) tunnelPos = activeOutpostSite;

        // 계획된 적을 차례로 (StartRaid에서 위협 점수로 정해 둔 마리 수 — BuildSpawnPlan)
        if (currentWaveIndex < activePlan.Count)
        {
            var plan = activePlan[currentWaveIndex];
            for (int n = spawnedInWave; n < plan.Count; n++) // 불러온 습격이면 이미 나온 마리는 건너뛴다
            {
                var spawn = plan[n];
                // 분산: 스폰 지점을 돌아가며 (지점 주변으로 흩뿌림)
                if (activeRaidType == RaidType.Split && splitPoints.Count > 0)
                {
                    Vector2 off = UnityEngine.Random.insideUnitCircle * spawn.spawnRadius;
                    tunnelPos = splitPoints[splitIndex++ % splitPoints.Count] + new Vector3(off.x, off.y, 0f);
                }
                var x = SpawnRaidXenops(spawn, spawn.xenopsDataId == bomberXenopsId && activeRaidType != RaidType.Split ? null : tunnelPos);
                spawnedInWave = n + 1;
                if (x != null && activeRaidType == RaidType.Outpost && activeOutpost == null && !outpostGuardReleased && activeOutpostSite.HasValue)
                    StartOutpost(x);
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

    /// <summary>
    /// 습격 종류에 따른 개체 설정 — 땅굴 적은 직원만 노린다(건물만 있으면 부수러 가지 않음),
    /// 거점 경비는 구조물이 서 있는 동안 그 자리를 지킨다.
    /// </summary>
    private void ApplyRaidTypeTo(Xenops xenops)
    {
        if (xenops == null || xenops.GetComponent<OutpostStructure>() != null || xenops.GetComponent<SpawnerStructure>() != null) return;
        bool tunnel = activeRaidType == RaidType.Tunnel;
        bool guard = activeRaidType == RaidType.Outpost && !outpostGuardReleased && activeOutpostSite.HasValue;
        if (!tunnel && !guard) return;
        var nav = xenops.GetComponent<EnemyNavigator>() ?? xenops.gameObject.AddComponent<EnemyNavigator>();
        if (tunnel) nav.StaffOnly = true;
        if (guard) { nav.GuardAnchor = activeOutpostSite.Value; nav.GuardRadius = outpostGuardRadius; }
    }

    /// <summary>거점 구조물을 건설자 곁에 세운다 (진행도 0에서 시작)</summary>
    private void StartOutpost(Xenops builder)
    {
        var site = activeOutpostSite.Value;
        // 구조물 그림은 피벗이 좌하단 — 칸 모서리에 맞춘다
        var structure = XenopsManager.instance != null ? XenopsManager.instance.SpawnXenops(activeOutpostId, new Vector3(Mathf.Floor(site.x), Mathf.Floor(site.y), 0f)) : null;
        if (structure == null) { outpostGuardReleased = true; return; }
        spawnedEntities.Add(structure);
        structure.ApplyThreatScaling(StageMultiplier, true);
        var s = structure.GetComponent<OutpostStructure>();
        if (s != null) s.Builder = builder;
        activeOutpost = structure;
        // 안개 속에 지어도 보이게 — 구조물 주변을 밝힌다
        FogOfWarManager.instance?.PermanentlyRevealArea(new Vector2Int(Mathf.FloorToInt(site.x) - 4, Mathf.FloorToInt(site.y) - 2), new Vector2Int(9, 7));
        Debug.Log($"[RaidManager] 거점 구조물 건설 시작: {structure.name} @ {site}, 건설자 {builder.name}");
    }

    private int splitIndex;

    /// <summary>
    /// 분산 습격 스폰 지점 3~4곳 — 기존 지상·지하(안개) 스폰 후보에서 뽑되, 기지 중심 기준 방향(좌·우·지하)이
    /// 다른 곳을 먼저 고르고, 지점끼리 splitMinSeparation 이상 떨어지게 한다. 2곳 미만이면 false. 지점마다 안개를 밝힌다.
    /// </summary>
    private bool PickSplitPoints(RaidData raid)
    {
        int want = UnityEngine.Random.Range(splitPointCount.x, Mathf.Max(splitPointCount.x, splitPointCount.y) + 1);
        splitIndex = 0;

        var anchors = RaidSpawnPlacer.CollectBaseAnchors();
        Vector2 center = Vector2.zero;
        foreach (var a in anchors) center += a;
        if (anchors.Count > 0) center /= anchors.Count;
        // 방향: 0 왼쪽 지상, 1 오른쪽 지상, 2 지하
        int Dir(Vector3 p, bool under) => under ? 2 : (p.x < center.x ? 0 : 1);

        // 지상·지하 후보를 한 번씩만 훑고(맵 전체 스캔이라 비싸다), 기지에서 먼 칸은 버린 뒤 섞는다
        var pool = new List<(Vector3 p, int dir)>();
        foreach (bool under in new[] { false, true })
        {
            var cells = RaidSpawnPlacer.FindCandidates(under ? RaidSpawnLocation.FogUnderground : RaidSpawnLocation.FogGround);
            if (cells == null) continue;
            foreach (var c in cells)
            {
                var p = new Vector3(c.x + 0.5f, c.y + 0.5f, 0f);
                if (anchors.Count > 0 && !anchors.Exists(a => Vector2.Distance(a, p) <= splitMaxFromBase)) continue;
                pool.Add((p, Dir(p, under)));
            }
        }
        for (int i = pool.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); var t = pool[i]; pool[i] = pool[j]; pool[j] = t; }
        var usedDirs = new HashSet<int>();
        for (int pass = 0; pass < 2 && splitPoints.Count < want; pass++) // 1회차: 새 방향만, 2회차: 아무 방향
            foreach (var c in pool)
            {
                if (splitPoints.Count >= want) break;
                if (pass == 0 && usedDirs.Contains(c.dir)) continue;
                if (!splitPoints.TrueForAll(q => Vector2.Distance(q, c.p) >= splitMinSeparation)) continue;
                splitPoints.Add(c.p);
                usedDirs.Add(c.dir);
            }

        foreach (var p in splitPoints)
            FogOfWarManager.instance?.PermanentlyRevealArea(new Vector2Int(Mathf.FloorToInt(p.x) - 3, Mathf.FloorToInt(p.y) - 2), new Vector2Int(7, 5));
        Debug.Log($"[RaidManager] 분산 습격 스폰 지점 {splitPoints.Count}곳 (목표 {want}): {string.Join(" ", splitPoints)}");
        return splitPoints.Count >= 2;
    }

    /// <summary>위장체 — 살아 있는 직원 하나를 복제해 화면 밖 기지 안에 둔다 (자리·직원이 없으면 아무것도 나오지 않는다)</summary>
    private void StartDisguise()
    {
        var alive = EmployeeManager.instance != null
            ? EmployeeManager.instance.AllEmployees.FindAll(e => e != null && e.State != EmployeeState.Dead) : new List<Employee>();
        if (alive.Count == 0 || !RaidSpawnPlacer.TryFindHiddenBasePosition(out var pos))
        {
            Debug.Log("[RaidManager] 위장체 자리·원본 직원이 없어 나오지 않았습니다");
            return;
        }
        DisguisedEmployee.Spawn(alive[UnityEngine.Random.Range(0, alive.Count)], pos);
    }

    private static List<DisguiseSaveData> DisguisedEmployeeSaves()
    {
        var list = new List<DisguiseSaveData>();
        foreach (var d in DisguisedEmployee.All)
            if (d != null && d.Body != null)
                list.Add(new DisguiseSaveData { employee = d.Body.CreateSaveData(), sourceEmployeeId = d.SourceEmployeeId, alertCooldown = d.AlertCooldown });
        return list;
    }

    /// <summary>위협 단계의 체력·피해 배율 (정체를 드러낸 위장체용)</summary>
    public float StageMultiplierFor(int stage) => (1f + statBonusPerStage * stage) * EarlyDayStatFactor;

    // shortcut: 임시 수치 — 초반 적 약화(체력·피해 배율): 3일차 0.5 → 12일차 1.0 선형. 위협 점수(마리 수)는 그대로
    private const float EARLY_FACTOR_MIN = 0.5f;
    private const float EARLY_FACTOR_START_DAY = 3f, EARLY_FACTOR_FULL_DAY = 12f;

    /// <summary>초반 적 체력·피해 배율 — 습격 적과 이벤트로 나오는 적 공통</summary>
    public static float EarlyDayStatFactor
    {
        get
        {
            float day = DayCycle.instance != null ? DayCycle.instance.Day + DayCycle.instance.TimeNormalized : EARLY_FACTOR_FULL_DAY;
            return Mathf.Lerp(EARLY_FACTOR_MIN, 1f, Mathf.InverseLerp(EARLY_FACTOR_START_DAY, EARLY_FACTOR_FULL_DAY, day));
        }
    }

    /// <summary>스포너를 자리에 세우고 패턴을 정한다</summary>
    private void StartSpawner()
    {
        if (!activeOutpostSite.HasValue || XenopsManager.instance == null) return;
        var site = activeOutpostSite.Value;
        // 구조물 그림은 피벗이 좌하단 — 칸 모서리에 맞춘다
        var spawner = XenopsManager.instance.SpawnXenops(spawnerXenopsId, new Vector3(Mathf.Floor(site.x), Mathf.Floor(site.y), 0f));
        if (spawner == null) return;
        spawnedEntities.Add(spawner);
        spawner.ApplyThreatScaling(StageMultiplier, true);
        var ids = new List<int>(spawnerPatternIds);
        if (activeThreatStage >= deepEnemyMinStage) ids.Add(deepXenopsId);
        spawner.GetComponent<SpawnerStructure>()?.RollPattern(ids);
        FogOfWarManager.instance?.PermanentlyRevealArea(new Vector2Int(Mathf.FloorToInt(site.x) - 4, Mathf.FloorToInt(site.y) - 2), new Vector2Int(9, 7));
        Debug.Log($"[RaidManager] 스포너 생성 @ {site}");
    }

    /// <summary>스포너가 내보낸 적 중 살아 있는 수 (스포너 습격은 호위가 없어 스포너 외 전부)</summary>
    public int SpawnerChildrenAlive
    {
        get
        {
            int n = 0;
            foreach (var x in spawnedEntities) if (x != null && x.GetComponent<SpawnerStructure>() == null) n++;
            return n;
        }
    }

    /// <summary>스포너가 적 하나를 내보낸다 — 습격 적으로 등록(단계 배율·전리품·완료 판정)</summary>
    public Xenops SpawnFromSpawner(int xenopsId, Vector3 at)
        => SpawnRaidXenops(new RaidSpawnEntry { xenopsDataId = xenopsId, count = 1 }, at);

    /// <summary>구조물이 사라지면(부서짐·건설 실패) 남은 경비는 일반 습격처럼 행동한다</summary>
    private void ReleaseOutpostGuards()
    {
        outpostGuardReleased = true;
        foreach (var x in spawnedEntities)
        {
            var nav = x != null ? x.GetComponent<EnemyNavigator>() : null;
            if (nav != null) nav.GuardAnchor = null;
        }
        Debug.Log("[RaidManager] 거점 구조물이 사라져 경비가 풀렸습니다");
    }

    private async UniTask MonitorRaidCompletionAsync(CancellationToken ct)
    {
        // 모든 스폰 개체가 제거될 때까지 대기
        while (true)
        {
            spawnedEntities.RemoveAll(x => x == null);
            if (spawnedEntities.Count == 0) break;
            if (ShouldRetreat()) { Retreat(); break; }
            await UniTask.Delay(TimeSpan.FromSeconds(1f), cancellationToken: ct);
        }

        CompleteRaid();
    }

    #endregion

    #region 내부 유틸

    /// <summary>습격으로 온 적인지 (처치 시 전리품 드롭 판정)</summary>
    public bool IsRaider(Xenops xenops) => xenops != null && spawnedEntities.Contains(xenops);

    private Xenops SpawnRaidXenops(RaidSpawnEntry entry, Vector3? at)
    {
        if (XenopsManager.instance == null) return null;

        // 땅굴 자리는 좁은 굴이라 흩뿌리지 않는다
        Vector3 spawnPos = SnapToStandable(at.HasValue ? at.Value + new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 0f, 0f) : GetSpawnPosition(entry));
        // 거점 경비로 못 쓰는 적(자체 조준 사격 — 지키기 모드가 없다)은 다른 적으로 바꿔 보낸다
        int id = activeRaidType == RaidType.Outpost && entry.xenopsDataId == outpostShooterId ? outpostShooterReplacementId : entry.xenopsDataId;

        // 침식된 인간: 계획의 모든 자리를 사람으로 — 장비의 무기 종류가 몸(근접·원거리)을 정한다
        EmployeeData person = null;
        Dictionary<EquipmentSlot, EquipmentData> loadout = null;
        if (activeRaidType == RaidType.ErodedHumans)
        {
            person = ErodedHuman.RollPerson();
            loadout = ErodedHuman.RollLoadout(person);
            id = ErodedHuman.BodyIdFor(loadout);
        }
        var xenops = XenopsManager.instance.SpawnXenops(id, spawnPos);
        if (xenops != null && person != null) xenops.GetComponent<ErodedHuman>()?.Setup(person, loadout);

        if (xenops != null)
        {
            spawnedEntities.Add(xenops);
            ApplyRaidTypeTo(xenops);
            xenops.ApplyThreatScaling(StageMultiplier, true);

            if (showDebugLogs)
                Debug.Log($"[RaidManager] 제놉스 스폰: ID {id} at {spawnPos}");
        }
        return xenops;
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
        // 거점 구조물이 사라졌으면 경비를 푼다 (Unity null — 파괴된 개체)
        if (activeRaidType == RaidType.Outpost && !outpostGuardReleased && !ReferenceEquals(activeOutpost, null) && activeOutpost == null)
            ReleaseOutpostGuards();

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
        return ThreatScore.Compute(rs != null ? rs.Difficulty : null, rs != null ? rs.ThreatAdapt : 1f);
    }

    /// <summary>습격 종류의 한 마리당 점수</summary>
    private float PointsPerEnemy(RaidType type)
        => type == RaidType.Tunnel ? tunnelPointsPerEnemy : type == RaidType.Breach ? breachPointsPerEnemy
         : type == RaidType.ErodedHumans ? erodedHumanPointsPerEnemy : normalPointsPerEnemy;

    /// <summary>오늘의 초반 마리 수 상한 (상한 구간이 지났으면 int.MaxValue)</summary>
    public int EarlyEnemyCap()
    {
        int day = DayCycle.instance != null ? DayCycle.instance.Day : int.MaxValue;
        foreach (var c in earlyEnemyCaps) if (day <= c.x) return Mathf.Max(1, c.y);
        return int.MaxValue;
    }

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
    private float StageMultiplier => StageMultiplierFor(activeThreatStage);

    private bool ShouldRetreat()
        => raidRetreatAfterDays > 0f && lastRaidGameDay >= 0f && GameDayNow - lastRaidGameDay > raidRetreatAfterDays
           && activeRaidType != RaidType.Outpost && activeRaidType != RaidType.Spawner && activeRaidType != RaidType.Disguise;

    /// <summary>오래 끈 습격 — 남은 적이 전리품 없이 물러난다</summary>
    private void Retreat()
    {
        int n = 0;
        foreach (var x in spawnedEntities.ToArray())
        {
            if (x == null) continue;
            XenopsManager.instance?.RemoveXenops(x);
            n++;
        }
        spawnedEntities.Clear();
        Debug.Log($"[RaidManager] 습격이 {raidRetreatAfterDays}일 넘게 끝나지 않아 남은 적 {n}마리가 물러났습니다");
    }

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

        ResetRaidState();
    }

    /// <summary>습격 상태를 비운다 (완료 알림 없음)</summary>
    private void ResetRaidState()
    {
        activeRaid = null;
        activeRaidType = RaidType.Normal;
        activeOutpost = null;
        activeOutpostSite = null;
        currentWaveIndex = 0; wavesSpawned = -1; spawnedInWave = 0;
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
