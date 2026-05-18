using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 레이드 시스템 매니저.
/// DestroySingleton + ISaveModule 패턴을 따릅니다.
///
/// 담당 기능:
///   - RaidData 기반 웨이브 스폰 관리
///   - 레이드 전멸/철수 감지 및 완료 처리
///   - 레이드 완료 시 ErosionManager.StartPostRaidRecovery() 호출
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

    /// <summary>이번 레이드에서 스폰된 제놉스 목록</summary>
    private readonly List<Xenops> spawnedEntities = new List<Xenops>();

    private Coroutine activeRaidCoroutine;

    #endregion

    #region 이벤트

    /// <summary>레이드가 시작될 때 발행됩니다.</summary>
    public event Action<RaidData> OnRaidStarted;

    /// <summary>새 웨이브가 시작될 때 발행됩니다. (웨이브 인덱스)</summary>
    public event Action<int> OnWaveStarted;

    /// <summary>레이드가 완료(전멸 또는 철수)될 때 발행됩니다.</summary>
    public event Action<RaidData> OnRaidCompleted;

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
            waveTimer   = waveTimer
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
            activeRaidCoroutine = StartCoroutine(MonitorRaidCompletion());
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

        if (IsRaidActive)
        {
            Debug.LogWarning("[RaidManager] 이미 레이드가 진행 중입니다. 새 레이드를 시작할 수 없습니다.");
            return;
        }

        activeRaid = raid;
        currentWaveIndex = 0;
        spawnedEntities.Clear();

        if (showDebugLogs)
            Debug.Log($"[RaidManager] 레이드 시작: {raid.raidName}");

        if (activeRaidCoroutine != null)
            StopCoroutine(activeRaidCoroutine);

        activeRaidCoroutine = StartCoroutine(RunRaid(raid));
    }

    /// <summary>
    /// 현재 레이드를 강제로 종료합니다.
    /// </summary>
    public void ForceEndRaid()
    {
        if (activeRaidCoroutine != null)
        {
            StopCoroutine(activeRaidCoroutine);
            activeRaidCoroutine = null;
        }
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

    #region 레이드 코루틴

    private IEnumerator RunRaid(RaidData raid)
    {
        // 접근 연출
        currentState = RaidState.Approaching;
        if (showDebugLogs) Debug.Log($"[RaidManager] {raid.raidName} 접근 중... ({raid.approachDuration}초)");
        yield return new WaitForSeconds(raid.approachDuration);

        // 레이드 시작
        currentState = RaidState.InProgress;
        OnRaidStarted?.Invoke(raid);

        // 웨이브 순차 스폰
        for (int i = 0; i < raid.waves.Count; i++)
        {
            currentWaveIndex = i;
            var wave = raid.waves[i];

            if (showDebugLogs) Debug.Log($"[RaidManager] 웨이브 {i + 1}/{raid.waves.Count} 대기 ({wave.delayBeforeWave}초)");
            yield return new WaitForSeconds(wave.delayBeforeWave);

            OnWaveStarted?.Invoke(i);
            yield return StartCoroutine(SpawnWave(wave));
        }

        // 전멸 또는 철수 대기
        if (raid.retreatDelay > 0f)
            yield return new WaitForSeconds(raid.retreatDelay);

        // 모든 웨이브 소진 후 전멸 대기 모니터링
        yield return StartCoroutine(MonitorRaidCompletion());
    }

    private IEnumerator SpawnWave(RaidWave wave)
    {
        foreach (var entry in wave.entries)
        {
            for (int i = 0; i < entry.count; i++)
            {
                SpawnRaidXenops(entry);

                if (entry.spawnInterval > 0f)
                    yield return new WaitForSeconds(entry.spawnInterval);
            }
        }
    }

    private IEnumerator MonitorRaidCompletion()
    {
        // 모든 스폰 개체가 제거될 때까지 대기
        while (true)
        {
            spawnedEntities.RemoveAll(x => x == null);
            if (spawnedEntities.Count == 0) break;
            yield return new WaitForSeconds(1f);
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
        Vector3 basePos = defaultSpawnPoint != null
            ? defaultSpawnPoint.position
            : Vector3.zero;

        Vector2 offset = UnityEngine.Random.insideUnitCircle * entry.spawnRadius;
        return basePos + new Vector3(offset.x, offset.y, 0f);
    }

    private void CompleteRaid()
    {
        currentState = RaidState.Completed;

        if (showDebugLogs && activeRaid != null)
            Debug.Log($"[RaidManager] 레이드 완료: {activeRaid.raidName}");

        OnRaidCompleted?.Invoke(activeRaid);

        // 포스트 레이드 회복 시작
        ErosionManager.instance?.StartPostRaidRecovery();

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
