using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 가이드라인(목표 안내) 진행 관리.
///
/// 매 게임 처음부터 진행됩니다. 목표는 조건을 1초마다 검사해 달성 처리하고, <b>한 번 달성하면 풀리지 않습니다</b>
/// (모은 베리를 먹어도 "베리 모으기"는 달성한 채로 남습니다). 한 장의 목표를 전부 달성하면 다음 장이 됩니다.
/// 보상은 없습니다 — 안내만 합니다.
///
/// 내용(장·목표·수량)은 <see cref="GuidelineConfig"/> 에셋에서 고칩니다. 표시는 <see cref="GuidelineUI"/>가 합니다.
/// </summary>
public class GuidelineManager : DestroySingleton<GuidelineManager>, ISaveModule
{
    [SerializeField] private GuidelineConfig config;

    private const float CHECK_INTERVAL = 1f;

    private readonly HashSet<string> completed = new HashSet<string>();
    private readonly HashSet<string> flags = new HashSet<string>();
    private IDisposable raidSubscription;
    private float timer;

    // 방 유지 목표가 조건을 이어서 지킨 시간 (게임 시간). 저장하지 않는다 — 불러오면 다시 센다.
    private readonly Dictionary<string, float> heldSeconds = new Dictionary<string, float>();
    private float gameElapsed;

    #region 조회

    public int ChapterCount => config != null ? config.chapters.Count : 0;

    /// <summary>진행 중인 장 번호(0부터). 전부 끝났으면 <see cref="ChapterCount"/>.</summary>
    public int CurrentChapterIndex
    {
        get
        {
            if (config == null) return 0;
            for (int i = 0; i < config.chapters.Count; i++)
                foreach (var objective in config.chapters[i].objectives)
                    if (!IsCompleted(objective)) return i;
            return config.chapters.Count;
        }
    }

    /// <summary>진행 중인 장. 전부 끝났으면 null.</summary>
    public GuidelineChapter CurrentChapter
    {
        get
        {
            int index = CurrentChapterIndex;
            return config != null && index < config.chapters.Count ? config.chapters[index] : null;
        }
    }

    public bool IsCompleted(GuidelineObjective objective)
        => objective != null && completed.Contains(objective.id);

    /// <summary>
    /// 목표 문구 뒤에 붙일 진행 표시. 표시할 것이 없으면 빈 문자열.
    ///   · 아이템: 지금 가진 수량 그대로 (목표를 넘겨도 자르지 않는다) — "보유 8/20"
    ///   · 방 유지: 조건을 이어서 지킨 시간 — "유지 12/30초"
    ///   · 그 밖의 수량 조건: "1/2" (목표가 1이면 생략)
    /// </summary>
    public string GetProgressLabel(GuidelineObjective objective)
    {
        if (objective == null) return "";

        switch (objective.kind)
        {
            case GuidelineConditionKind.ItemCount:
                return $"보유 {Measure(objective)}/{Target(objective)}";

            case GuidelineConditionKind.RoomMaintained:
                if (objective.holdSeconds <= 0f) return "";
                heldSeconds.TryGetValue(objective.id, out float held);
                return $"유지 {Mathf.FloorToInt(held)}/{Mathf.CeilToInt(objective.holdSeconds)}초";

            case GuidelineConditionKind.Flag:
            case GuidelineConditionKind.ReachDepth:
                return "";

            default:
                int target = Target(objective);
                return target > 1 ? $"{Mathf.Min(Measure(objective), target)}/{target}" : "";
        }
    }

    #endregion

    #region 한 번 한 행동

    /// <summary>"한 번이라도 했다"로 달성되는 목표의 깃발을 켭니다. 매니저가 없으면 아무 일도 없습니다.</summary>
    public static void MarkFlag(string key)
    {
        if (instance != null && !string.IsNullOrEmpty(key)) instance.flags.Add(key);
    }

    #endregion

    #region 판정

    private void Start()
    {
        raidSubscription = GameMessageBus.Subscribe<RaidCompletedMessage>(_ => MarkFlag("raid_completed"));
    }

    private void OnDestroy()
    {
        raidSubscription?.Dispose();
        raidSubscription = null;
    }

    private void Update()
    {
        gameElapsed += Time.deltaTime; // 방 유지 시간은 게임 시간으로 센다 (일시정지 중에는 안 쌓인다)
        timer += Time.unscaledDeltaTime;
        if (timer < CHECK_INTERVAL) return;
        timer = 0f;

        float elapsed = gameElapsed;
        gameElapsed = 0f;

        // 지금 장의 목표만 본다 — 뒤 장의 조건은 그 장이 될 때 한꺼번에 판정된다
        GuidelineChapter chapter = CurrentChapter;
        if (chapter == null) return;

        foreach (var objective in chapter.objectives)
        {
            if (objective == null || string.IsNullOrEmpty(objective.id) || completed.Contains(objective.id)) continue;

            if (objective.kind == GuidelineConditionKind.RoomMaintained)
            {
                // 조건에 맞는 방이 있는 동안만 시간이 쌓이고, 끊기면 처음부터
                heldSeconds.TryGetValue(objective.id, out float held);
                held = AnyRoomMatches(objective) ? held + elapsed : 0f;
                heldSeconds[objective.id] = held;
                if (held > 0f && held >= objective.holdSeconds) completed.Add(objective.id);
                continue;
            }

            if (Measure(objective) >= Target(objective)) completed.Add(objective.id);
        }
    }

    private static bool AnyRoomMatches(GuidelineObjective objective)
    {
        var rooms = RoomManager.instance;
        if (rooms == null) return false;

        foreach (var room in rooms.Rooms.Values)
        {
            if (room.CellCount < objective.amount) continue;
            if (!objective.roomTemperature.Contains(room.Temperature)) continue;
            if (!objective.roomErosion.Contains(room.Erosion)) continue;
            return true;
        }
        return false;
    }

    private static int Target(GuidelineObjective objective)
    {
        switch (objective.kind)
        {
            case GuidelineConditionKind.BedsPerEmployee:
                return Mathf.Max(1, EmployeeManager.instance != null ? EmployeeManager.instance.EmployeeCount : 1);
            case GuidelineConditionKind.Flag:
                return 1;
            default:
                return Mathf.Max(1, objective.amount);
        }
    }

    private int Measure(GuidelineObjective objective)
    {
        switch (objective.kind)
        {
            case GuidelineConditionKind.EmployeeCount:
                return EmployeeManager.instance != null ? EmployeeManager.instance.EmployeeCount : 0;

            case GuidelineConditionKind.BedOwnerCount:
            {
                int owned = 0;
                foreach (var bed in SleepingBed.All)
                    if (bed != null && bed.Owner != null) owned++;
                return owned;
            }

            case GuidelineConditionKind.BedsPerEmployee:
                // 직원이 없으면 달성으로 치지 않는다
                if (EmployeeManager.instance == null || EmployeeManager.instance.EmployeeCount == 0) return 0;
                return SleepingBed.All.Count;

            case GuidelineConditionKind.ItemCount:
                return objective.item != null && InventoryManager.instance != null
                    ? InventoryManager.instance.GetItemCount(objective.item)
                    : 0;

            case GuidelineConditionKind.BuildingCount:
                return CountBuildings(objective.buildings);

            case GuidelineConditionKind.ResearchCompleted:
                return CountCompletedResearch();

            case GuidelineConditionKind.ReachDepth:
                return DeepestEmployeeDepth();

            case GuidelineConditionKind.Flag:
                return flags.Contains(objective.flagKey) ? 1 : 0;

            case GuidelineConditionKind.EquipmentWorn:
            {
                if (EmployeeManager.instance == null) return 0;
                int worn = 0;
                foreach (var employee in EmployeeManager.instance.AllEmployees)
                    if (employee != null && employee.Equipment != null && !employee.Equipment.IsSlotEmpty(objective.equipSlot)) worn++;
                return worn;
            }
        }
        return 0;
    }

    // ponytail: 1초마다 씬의 건물을 전부 훑는다. 건물이 수천 채가 되면 건물 등록 이벤트로 세는 방식으로.
    private static int CountBuildings(List<BuildingData> kinds)
    {
        if (kinds == null || kinds.Count == 0) return 0;

        int count = 0;
        foreach (var building in FindObjectsByType<Building>(FindObjectsSortMode.None))
            if (building.IsFunctional && building.buildingData != null && kinds.Contains(building.buildingData)) count++;
        return count;
    }

    private static int CountCompletedResearch()
    {
        var research = ResearchTreeManager.instance;
        if (research == null || research.TreeConfig == null) return 0;

        int count = 0;
        foreach (var node in research.TreeConfig.nodes)
            if (node != null && research.GetNodeState(node.nodeId) == ResearchNodeState.Completed) count++;
        return count;
    }

    private static int DeepestEmployeeDepth()
    {
        if (EmployeeManager.instance == null || MapGenerator.instance == null) return 0;

        float surface = MapGenerator.instance.SurfaceReferenceY;
        float deepest = 0f;
        foreach (var employee in EmployeeManager.instance.AllEmployees)
            if (employee != null) deepest = Mathf.Max(deepest, surface - employee.transform.position.y);
        return Mathf.FloorToInt(deepest);
    }

    #endregion

    #region ISaveModule

    public int SaveOrder => 90;

    public void Capture(SaveData data)
    {
        data.guideline = new GuidelineSaveData
        {
            completedObjectives = new List<string>(completed),
            flags = new List<string>(flags),
        };
    }

    public void Restore(SaveData data)
    {
        completed.Clear();
        flags.Clear();
        heldSeconds.Clear();

        // 구 세이브에는 없다 — 처음부터 다시 판정한다(이미 갖춘 것은 곧바로 달성 처리된다)
        if (data.guideline == null) return;
        if (data.guideline.completedObjectives != null) completed.UnionWith(data.guideline.completedObjectives);
        if (data.guideline.flags != null) flags.UnionWith(data.guideline.flags);
    }

    public void PostRestore(SaveData data) { }

    #endregion
}
