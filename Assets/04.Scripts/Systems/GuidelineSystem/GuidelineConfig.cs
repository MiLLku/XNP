using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>가이드라인 목표의 달성 조건 종류.</summary>
public enum GuidelineConditionKind
{
    /// <summary>직원이 amount명 이상</summary>
    EmployeeCount,
    /// <summary>주인이 배정된 침대가 amount개 이상</summary>
    BedOwnerCount,
    /// <summary>침대 수가 직원 수 이상 (직원이 1명 이상일 때)</summary>
    BedsPerEmployee,
    /// <summary>창고에 item이 amount개 이상</summary>
    ItemCount,
    /// <summary>buildings 중 어느 것이든 합쳐서 amount채 이상 (가동 중인 것만)</summary>
    BuildingCount,
    /// <summary>완료한 연구가 amount개 이상</summary>
    ResearchCompleted,
    /// <summary>직원이 지표 기준선에서 amount칸 이상 깊이 내려감</summary>
    ReachDepth,
    /// <summary>flagKey 행동을 한 번이라도 함 (GuidelineManager.MarkFlag)</summary>
    Flag,
    /// <summary>
    /// 칸 수가 amount 이상이고 온도·침식이 범위 안인 밀폐된 방을 holdSeconds초 동안 유지.
    /// 조건에 맞는 방이 하나라도 있으면 시간이 쌓이고, 없어지면 처음부터 다시 셉니다.
    /// </summary>
    RoomMaintained,
    /// <summary>equipSlot 칸에 장비를 착용한 직원이 amount명 이상</summary>
    EquipmentWorn,
}

/// <summary>가이드라인 목표 하나.</summary>
[Serializable]
public class GuidelineObjective
{
    [Tooltip("세이브에 남는 식별자. 한 번 정하면 바꾸지 마세요 (바꾸면 이미 달성한 기록이 풀립니다).")]
    public string id;

    [Tooltip("패널에 표시할 문구. 수량 조건이면 뒤에 (현재/목표)가 자동으로 붙습니다.")]
    public string text;

    public GuidelineConditionKind kind;

    [Min(1)] public int amount = 1;

    [Tooltip("ItemCount 전용")]
    public ItemData item;

    [Tooltip("BuildingCount 전용 — 이 중 어느 건물이든 셉니다")]
    public List<BuildingData> buildings = new List<BuildingData>();

    [Tooltip("Flag 전용 — 코드에서 GuidelineManager.MarkFlag로 켜는 키")]
    public string flagKey;

    [Tooltip("EquipmentWorn 전용 — 이 칸에 장비를 착용한 직원 수를 셉니다")]
    public EquipmentSlot equipSlot = EquipmentSlot.Weapon;

    [Header("RoomMaintained 전용 (amount = 방의 최소 칸 수)")]
    [Tooltip("방 온도 범위(℃). 꺼 두면 온도는 보지 않습니다.")]
    public ConditionRange roomTemperature;

    [Tooltip("방 침식 범위. 꺼 두면 침식은 보지 않습니다.")]
    public ConditionRange roomErosion;

    [Tooltip("조건을 이만큼(게임 시간, 초) 이어서 유지해야 달성됩니다. 0이면 조건이 맞는 순간 달성.")]
    [Min(0f)] public float holdSeconds = 0f;
}

/// <summary>가이드라인의 한 장 — 목표를 전부 달성하면 다음 장으로 넘어갑니다.</summary>
[Serializable]
public class GuidelineChapter
{
    public string title;
    public List<GuidelineObjective> objectives = new List<GuidelineObjective>();
}

/// <summary>
/// 가이드라인(목표 안내) 내용. 장과 목표의 문구·조건·수량을 여기서 고칩니다.
/// 판정은 <see cref="GuidelineManager"/>가 합니다.
/// </summary>
[CreateAssetMenu(fileName = "GuidelineConfig", menuName = "XNP/가이드라인 설정")]
public class GuidelineConfig : ScriptableObject
{
    public List<GuidelineChapter> chapters = new List<GuidelineChapter>();
}

/// <summary>가이드라인 저장 데이터 — 달성한 목표와 한 번 한 행동만 남깁니다.</summary>
[Serializable]
public class GuidelineSaveData
{
    public List<string> completedObjectives = new List<string>();
    public List<string> flags = new List<string>();
}
