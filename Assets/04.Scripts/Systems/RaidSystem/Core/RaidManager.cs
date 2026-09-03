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

    [Header("디버그")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    #region 상태

    private RaidState currentState = RaidState.None;
    private RaidData activeRaid;
    private int currentWaveIndex;
    private float waveTimer;
    private bool waitingForNextWave;

    /// <summary>이번 레이드의 스폰 수량 배수 (시작 시점 진행 상황으로 계산)</summary>
    private float activeMultiplier = 1f;

    /// <summary>이번 레이드에서 스폰된 제놉스 목록</summary>
    private readonly List<Xenops> spawnedEntities = new List<Xenops>();

    /// <summary>진행 중인 레이드 비동기 작업의 취소원 (한 번에 하나만)</summary>
    private CancellationTokenSource raidCts;

    #endregion

    #region 프로퍼티

    public RaidState CurrentState => currentState;
    public RaidData ActiveRaid => activeRaid;
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
            activeMultiplier = activeMultiplier
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
                spawnedEntities.Add(obj);
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
    public void StartRaid(RaidData raid)
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
        currentWaveIndex = 0;
        spawnedEntities.Clear();
        activeMultiplier = ComputeMultiplier(raid);

        if (showDebugLogs)
            Debug.Log($"[RaidManager] 레이드 시작: {raid.raidName} (배수 {activeMultiplier:F2}, 위치 {raid.spawnLocation})");

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
    /// </summary>
    public void StartRandomRaid()
    {
        var pool = availableRaids.FindAll(r => r != null && r.includeInRandomPool);
        if (pool.Count == 0)
        {
            Debug.LogWarning("[RaidManager] 랜덤 레이드 풀이 비어 있습니다.");
            return;
        }
        StartRaid(pool[UnityEngine.Random.Range(0, pool.Count)]);
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
            title = "침공 발생",
            body = $"{raid.raidName} — 적대 개체가 접근했습니다. 직원을 소집해 방어하세요.",
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

    private async UniTask SpawnWaveAsync(RaidWave wave, CancellationToken ct)
    {
        foreach (var entry in wave.entries)
        {
            // 진행 상황 배수 적용 (최소 1마리)
            int count = Mathf.Max(1, Mathf.RoundToInt(entry.count * activeMultiplier));

            for (int i = 0; i < count; i++)
            {
                SpawnRaidXenops(entry);

                if (entry.spawnInterval > 0f)
                    await UniTask.Delay(TimeSpan.FromSeconds(entry.spawnInterval), cancellationToken: ct);
            }
        }
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

    private void SpawnRaidXenops(RaidSpawnEntry entry)
    {
        if (XenopsManager.instance == null) return;

        Vector3 spawnPos = GetSpawnPosition(entry);
        var xenops = XenopsManager.instance.SpawnXenops(entry.xenopsDataId, spawnPos);

        if (xenops != null)
        {
            spawnedEntities.Add(xenops);

            if (showDebugLogs)
                Debug.Log($"[RaidManager] 제놉스 스폰: ID {entry.xenopsDataId} at {spawnPos}");
        }
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

    /// <summary>진행 상황 기반 스폰 배수: base + 일수·직원수 가산, 상한 클램프.</summary>
    private float ComputeMultiplier(RaidData raid)
    {
        int day = DayCycle.instance != null ? DayCycle.instance.Day : 0;

        int employees = 0;
        if (EmployeeManager.instance != null)
        {
            foreach (var e in EmployeeManager.instance.AllEmployees)
                if (e != null && e.State != EmployeeState.Dead) employees++;
        }

        float m = raid.baseMultiplier
                + raid.multiplierPerDay * day
                + raid.multiplierPerEmployee * employees;

        return Mathf.Clamp(m, 0.1f, raid.maxMultiplier);
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

        // 상태 리셋
        activeRaid = null;
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
