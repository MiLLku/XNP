using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 디버그 매니저.
/// 두 가지를 한곳에서 담당합니다.
///   1. <b>차단 스위치</b>(DebugFlag) — 정신 이상·외부 침략처럼 "일어나지 않게" 만드는 게이트.
///      각 시스템의 발생 지점이 <see cref="IsBlocked"/>를 물어보고 조기 반환합니다.
///   2. <b>즉시 실행</b> — 이벤트/제놉스/레이드 강제 발생, 상태 초기화, 자원 지급.
///      (구 CheatManager의 F5·F6 기능을 흡수했습니다)
///
/// 플래그는 PlayerPrefs에 저장되어 에디터를 껐다 켜도 유지되며, <b>세이브 파일과는 무관</b>합니다.
/// 씬에 이 매니저가 없거나 enableDebug가 false면 모든 게이트는 "통과"(정상 게임)로 동작합니다.
///
/// 단축키:
///   F1 — 디버그 패널 열기/닫기
///   F5 — 무작위 이벤트 즉시 발생
///   F6 — 제놉스 등장
/// </summary>
public class DebugManager : DestroySingleton<DebugManager>
{
    #region 인스펙터 설정

    [Header("전역 설정")]
    [Tooltip("디버그 기능 전체 스위치. 릴리즈 빌드에서 false로 두면 모든 차단·치트가 무력화됩니다.")]
    [SerializeField] private bool enableDebug = true;

    [Header("단축키")]
    [SerializeField] private KeyCode panelKey = KeyCode.F1;
    [SerializeField] private KeyCode randomEventKey = KeyCode.F5;
    [SerializeField] private KeyCode xenopsSpawnKey = KeyCode.F6;

    [Header("제놉스 스폰")]
    [Tooltip("직접 스폰할 XenopsData ID (0 = GameDatabase에서 무작위)")]
    [SerializeField] private int debugXenopsDataId = 0;

    #endregion

    #region 상태

    private const string PREFS_KEY = "XNP_DebugFlags";

    private DebugFlag activeFlags = DebugFlag.None;

    /// <summary>플래그가 바뀔 때 발행됩니다. (UI 갱신용)</summary>
    public event Action OnFlagsChanged;

    /// <summary>현재 켜져 있는 차단 플래그 전체</summary>
    public DebugFlag ActiveFlags => activeFlags;

    /// <summary>디버그 기능 전체 활성 여부</summary>
    public bool EnableDebug => enableDebug;

    #endregion

    #region 초기화

    protected override void Awake()
    {
        base.Awake();
        activeFlags = (DebugFlag)PlayerPrefs.GetInt(PREFS_KEY, 0);
    }

    #endregion

    #region 게이트 (각 시스템이 호출)

    /// <summary>
    /// 해당 동작이 디버그로 차단되어 있는지 확인합니다.
    /// 매니저가 없어도 안전하게 false(=차단 안 함)를 반환합니다.
    /// </summary>
    public static bool IsBlocked(DebugFlag flag)
    {
        DebugManager m = instance;
        return m != null && m.enableDebug && (m.activeFlags & flag) != 0;
    }

    #endregion

    #region 플래그 조작

    /// <summary>플래그 하나가 켜져 있는지</summary>
    public bool HasFlag(DebugFlag flag) => (activeFlags & flag) != 0;

    /// <summary>
    /// 플래그를 켜거나 끕니다. 켤 때는 이미 진행 중인 대상도 함께 정리합니다.
    /// (그렇지 않으면 막았는데도 계속 날뛰는 상태가 남습니다)
    /// </summary>
    public void SetFlag(DebugFlag flag, bool blocked)
    {
        if (blocked) activeFlags |= flag;
        else         activeFlags &= ~flag;

        PlayerPrefs.SetInt(PREFS_KEY, (int)activeFlags);

        if (blocked) ApplyImmediateCleanup(flag);

        OnFlagsChanged?.Invoke();
    }

    /// <summary>모든 차단을 해제합니다.</summary>
    public void ClearAllFlags()
    {
        activeFlags = DebugFlag.None;
        PlayerPrefs.SetInt(PREFS_KEY, 0);
        OnFlagsChanged?.Invoke();
    }

    /// <summary>플래그를 켠 시점에 이미 진행 중이던 것을 정리합니다.</summary>
    private void ApplyImmediateCleanup(DebugFlag flag)
    {
        if ((flag & DebugFlag.MentalBreak) != 0) ClearAllMentalBreaks();
        if ((flag & DebugFlag.Raid) != 0 && RaidManager.instance != null && RaidManager.instance.IsRaidActive)
            RaidManager.instance.ForceEndRaid();
    }

    #endregion

    #region 단축키

    void Update()
    {
        if (!enableDebug) return;

        if (Input.GetKeyDown(panelKey) && UIManager.instance != null)
            UIManager.instance.TogglePanel(UIPanelType.DebugUI);

        if (Input.GetKeyDown(randomEventKey)) TriggerRandomEvent();
        if (Input.GetKeyDown(xenopsSpawnKey)) TriggerXenopsSpawn();
    }

    #endregion

    #region 즉시 실행 — 발생

    /// <summary>무작위 이벤트를 즉시 발생시킵니다. (구 F5 치트)</summary>
    public void TriggerRandomEvent()
    {
        if (EventManager.instance == null)
        {
            Debug.LogWarning("[DebugManager] EventManager가 없습니다.");
            return;
        }

        EventManager.instance.TriggerRandomEvent();
        Debug.Log("[DebugManager] 무작위 이벤트 발생");
    }

    /// <summary>
    /// 제놉스 등장 이벤트를 발생시킵니다. (구 F6 치트)
    /// debugXenopsDataId가 설정돼 있으면 그 개체를 카메라 근처에 직접 스폰합니다.
    /// </summary>
    public void TriggerXenopsSpawn()
    {
        if (IsBlocked(DebugFlag.XenopsSpawn))
        {
            Debug.LogWarning("[DebugManager] 제놉스 등장 차단이 켜져 있어 스폰되지 않습니다.");
            return;
        }

        if (debugXenopsDataId > 0)
        {
            SpawnXenopsDirectly(debugXenopsDataId);
            return;
        }

        if (EventManager.instance != null)
        {
            EventManager.instance.TriggerXenopsSpawnEvent();
            Debug.Log("[DebugManager] 제놉스 등장 이벤트 발생");
        }
        else
        {
            Debug.LogWarning("[DebugManager] EventManager가 없습니다. 직접 스폰을 시도합니다.");
            SpawnRandomXenops();
        }
    }

    /// <summary>무작위 레이드를 즉시 시작합니다.</summary>
    public void StartRandomRaid()
    {
        if (RaidManager.instance == null)
        {
            Debug.LogWarning("[DebugManager] RaidManager가 없습니다.");
            return;
        }

        RaidManager.instance.StartRandomRaid();
    }

    /// <summary>진행 중인 레이드를 강제 종료합니다.</summary>
    public void EndActiveRaid()
    {
        if (RaidManager.instance == null || !RaidManager.instance.IsRaidActive)
        {
            Debug.Log("[DebugManager] 진행 중인 레이드가 없습니다.");
            return;
        }

        RaidManager.instance.ForceEndRaid();
        Debug.Log("[DebugManager] 레이드 강제 종료");
    }

    /// <summary>특정 ID의 제놉스를 카메라 근처에 직접 스폰합니다.</summary>
    private void SpawnXenopsDirectly(int xenopsDataId)
    {
        if (XenopsManager.instance == null)
        {
            Debug.LogWarning("[DebugManager] XenopsManager가 없습니다.");
            return;
        }

        Vector3 spawnPos = GetSpawnPosition();
        var xenops = XenopsManager.instance.SpawnXenops(xenopsDataId, spawnPos);
        if (xenops != null)
        {
            xenops.SetState(XenopsState.Active);
            Debug.Log($"[DebugManager] {xenops.DisplayName} 직접 스폰 at {spawnPos}");
        }
    }

    /// <summary>GameDatabase에서 무작위 제놉스를 직접 스폰합니다.</summary>
    private void SpawnRandomXenops()
    {
        if (XenopsManager.instance == null || GameDatabase.Instance == null) return;

        var allXenopsData = GameDatabase.Instance.allXenopsData;
        if (allXenopsData == null || allXenopsData.Count == 0)
        {
            Debug.LogWarning("[DebugManager] GameDatabase에 등록된 XenopsData가 없습니다.");
            return;
        }

        var data = allXenopsData[UnityEngine.Random.Range(0, allXenopsData.Count)];
        Vector3 spawnPos = GetSpawnPosition();
        var xenops = XenopsManager.instance.SpawnXenops(data, spawnPos);
        if (xenops != null)
        {
            xenops.SetState(XenopsState.Active);
            Debug.Log($"[DebugManager] {xenops.DisplayName} 무작위 스폰 at {spawnPos}");
        }
    }

    /// <summary>카메라 근처 무작위 스폰 위치를 반환합니다.</summary>
    private Vector3 GetSpawnPosition()
    {
        if (Camera.main != null)
        {
            Vector3 cam = Camera.main.transform.position;
            return new Vector3(
                cam.x + UnityEngine.Random.Range(-8f, 8f),
                cam.y + UnityEngine.Random.Range(-3f, 3f),
                0f
            );
        }
        return Vector3.zero;
    }

    #endregion

    #region 즉시 실행 — 직원 상태

    /// <summary>전 직원의 진행 중인 정신 이상을 해제합니다.</summary>
    public void ClearAllMentalBreaks()
    {
        int count = 0;
        foreach (var employee in EnumerateEmployees())
        {
            var mental = employee.GetComponent<EmployeeMental>();
            if (mental == null) continue;

            mental.ClearAllEvents();
            count++;
        }
        Debug.Log($"[DebugManager] 정신 이상 해제: {count}명");
    }

    /// <summary>전 직원의 침식 수치를 0으로 만듭니다.</summary>
    public void ClearAllErosion()
    {
        int count = 0;
        foreach (var employee in EnumerateEmployees())
        {
            employee.SetErosion(0f);
            count++;
        }
        Debug.Log($"[DebugManager] 침식 초기화: {count}명");
    }

    /// <summary>전 직원의 허기·기력·재미를 가득 채웁니다.</summary>
    public void RefillAllNeeds()
    {
        int count = 0;
        foreach (var employee in EnumerateEmployees())
        {
            var stats = employee.StatsController;
            if (stats == null) continue;

            stats.ModifyHunger(100f);
            stats.ModifyFatigue(100f);
            stats.ModifyFun(100f);
            count++;
        }
        Debug.Log($"[DebugManager] 욕구 회복: {count}명");
    }

    /// <summary>전 직원의 체력을 가득 채웁니다.</summary>
    public void HealAllEmployees()
    {
        int count = 0;
        foreach (var employee in EnumerateEmployees())
        {
            var stats = employee.StatsController;
            if (stats == null) continue;

            stats.ModifyHealth(stats.Stats.maxHealth);
            count++;
        }
        Debug.Log($"[DebugManager] 체력 회복: {count}명");
    }

    /// <summary>살아 있는 직원을 순회합니다.</summary>
    private IEnumerable<Employee> EnumerateEmployees()
    {
        var all = EmployeeManager.instance != null ? EmployeeManager.instance.AllEmployees : null;
        if (all == null) yield break;

        foreach (var employee in all)
        {
            if (employee == null || employee.State == EmployeeState.Dead) continue;
            yield return employee;
        }
    }

    #endregion

    #region 즉시 실행 — 방 시스템

    /// <summary>방 오버레이(밀폐 공간 색칠)를 켜고 끕니다.</summary>
    public void ToggleRoomOverlay()
    {
        if (RoomOverlayRenderer.instance == null)
        {
            Debug.LogWarning("[DebugManager] RoomOverlayRenderer가 없습니다.");
            return;
        }

        RoomOverlayRenderer.instance.Toggle();
        Debug.Log($"[DebugManager] 방 오버레이 {(RoomOverlayRenderer.instance.IsVisible ? "켬" : "끔")}");
    }

    /// <summary>방 오버레이 표시 내용을 방 번호 ↔ 온도로 전환합니다.</summary>
    public void CycleRoomOverlayMode()
    {
        if (RoomOverlayRenderer.instance == null)
        {
            Debug.LogWarning("[DebugManager] RoomOverlayRenderer가 없습니다.");
            return;
        }

        RoomOverlayRenderer.instance.CycleMode();
        Debug.Log($"[DebugManager] 방 오버레이 모드: {RoomOverlayRenderer.instance.Mode}");
    }

    /// <summary>방 온도를 로그로 출력합니다 (큰 방부터).</summary>
    public void PrintRoomTemperatures()
    {
        var manager = RoomManager.instance;
        var temperature = TemperatureManager.instance;
        if (manager == null || temperature == null)
        {
            Debug.LogWarning("[DebugManager] RoomManager 또는 TemperatureManager가 없습니다.");
            return;
        }

        Debug.Log($"[DebugManager] 실외 {temperature.OutdoorTemperature:F1}도 (기준 {temperature.BaseOutdoorTemperature:F1}, 모디파이어: {temperature.DescribeOutdoorModifiers()}) / 방 {manager.RoomCount}개 / 열원 {temperature.SourceCount}개");
        foreach (var pair in manager.Rooms)
        {
            Room room = pair.Value;
            Debug.Log($"  방#{room.Id} {room.CellCount}칸 @{room.Representative} — {room.Temperature:F1}도 (누출 {room.LeakConductance:F2}) · 침식 {room.Erosion:F1}");
        }
    }

    /// <summary>방 침식과 실외 기본 침식을 콘솔에 출력합니다.</summary>
    public void PrintRoomErosion()
    {
        var manager = RoomManager.instance;
        var erosion = TerrainErosionManager.instance;
        if (manager == null || erosion == null)
        {
            Debug.LogWarning("[DebugManager] RoomManager 또는 TerrainErosionManager가 없습니다.");
            return;
        }

        Debug.Log($"[DebugManager] 실외 침식 {erosion.OutdoorErosion:F1} (기본 {erosion.BaseOutdoorErosion:F1}, 모디파이어: {erosion.DescribeOutdoorModifiers()}) / 고정 발원지 {erosion.SourceCount}개 / 개체 발원지 {(EntityErosionField.instance != null ? EntityErosionField.instance.SourceCount : 0)}개");

        foreach (var pair in manager.Rooms)
        {
            Room room = pair.Value;
            if (room.Erosion <= 0f) continue;

            Debug.Log($"  방#{room.Id} {room.CellCount}칸 @{room.Representative} — 침식 {room.Erosion:F1} (활동 중 발원지 {erosion.CountActiveSourcesIn(room)}개)");
        }
    }

    /// <summary>직원별 온도 컨디션을 콘솔에 출력합니다.</summary>
    public void PrintEmployeeTemperatures()
    {
        int count = 0;
        foreach (var employee in EnumerateEmployees())
        {
            var temperature = employee.Temperature;
            if (temperature == null) continue;

            Debug.Log($"  {employee.DisplayName} — {temperature.Describe()}");
            count++;
        }

        if (count == 0) Debug.Log("[DebugManager] 직원이 없습니다.");
    }

    /// <summary>계절·시각·실외 온도를 콘솔에 출력합니다.</summary>
    public void PrintSeason()
    {
        var temperature = TemperatureManager.instance;
        if (temperature == null)
        {
            Debug.LogWarning("[DebugManager] TemperatureManager가 없습니다.");
            return;
        }

        int day = DayCycle.instance != null ? DayCycle.instance.Day : 0;
        int hour = DayCycle.instance != null ? DayCycle.instance.CurrentHour : 0;

        Debug.Log($"[DebugManager] {day}일차 {hour}시 — {temperature.DescribeSeason()}");
        Debug.Log($"  실외 온도 {temperature.OutdoorTemperature:F1}도 (기준 {temperature.BaseOutdoorTemperature:F1}, 모디파이어: {temperature.DescribeOutdoorModifiers()})");
    }

    /// <summary>다음 계절 첫날로 건너뜁니다.</summary>
    public void SkipToNextSeason()
    {
        var temperature = TemperatureManager.instance;
        if (temperature == null || DayCycle.instance == null)
        {
            Debug.LogWarning("[DebugManager] TemperatureManager 또는 DayCycle이 없습니다.");
            return;
        }

        int perSeason = temperature.Config != null ? temperature.Config.daysPerSeason : 15;
        int remaining = perSeason - temperature.DayInSeason + 1;
        int targetDay = DayCycle.instance.Day + remaining;

        DayCycle.instance.SetDayAndTime(targetDay, DayCycle.instance.TimeNormalized);
        Debug.Log($"[DebugManager] {targetDay}일차로 건너뜀 — {temperature.DescribeSeason()}");
    }

    /// <summary>한파를 걸거나 해제합니다 (실외 -25도, 300초).</summary>
    public void ToggleColdSnap() => ToggleWeather("debug_cold", "한파", -25f);

    /// <summary>폭염을 걸거나 해제합니다 (실외 +25도, 300초).</summary>
    public void ToggleHeatWave() => ToggleWeather("debug_heat", "폭염", 25f);

    private void ToggleWeather(string key, string displayName, float delta)
    {
        var temperature = TemperatureManager.instance;
        if (temperature == null)
        {
            Debug.LogWarning("[DebugManager] TemperatureManager가 없습니다.");
            return;
        }

        if (temperature.DescribeOutdoorModifiers().Contains(displayName))
        {
            temperature.RemoveOutdoorModifier(key);
            Debug.Log($"[DebugManager] {displayName} 해제 → 실외 {temperature.OutdoorTemperature:F1}도");
        }
        else
        {
            temperature.SetOutdoorModifier(key, displayName, delta, 300f);
            Debug.Log($"[DebugManager] {displayName} 발생 → 실외 {temperature.OutdoorTemperature:F1}도");
        }
    }

    /// <summary>방을 지금 즉시 다시 계산하고 결과를 로그로 남깁니다.</summary>
    public void RebuildRooms()
    {
        var manager = RoomManager.instance;
        if (manager == null)
        {
            Debug.LogWarning("[DebugManager] RoomManager가 없습니다.");
            return;
        }

        manager.Rebuild();
        Debug.Log($"[DebugManager] 방 재계산: {manager.RoomCount}개 / 실내 {manager.LastIndoorCellCount}칸 / {manager.LastRebuildMs:F2}ms");
    }

    #endregion

    #region 즉시 실행 — 자원 지급

    /// <summary>글로벌 인벤토리에 아이템을 지급합니다.</summary>
    public void GrantItem(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return;

        if (InventoryManager.instance == null)
        {
            Debug.LogWarning("[DebugManager] InventoryManager가 없습니다.");
            return;
        }

        InventoryManager.instance.AddItem(item, amount);
        Debug.Log($"[DebugManager] 자원 지급: {item.itemName} x{amount}");
    }

    #endregion
}
