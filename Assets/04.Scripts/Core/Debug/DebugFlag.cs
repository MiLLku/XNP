using System;
using System.Collections.Generic;

/// <summary>
/// 디버그 차단 스위치.
/// 켜져 있는 플래그는 해당 시스템의 <b>발생 지점</b>에서 조기 반환시켜 아예 일어나지 않게 만듭니다.
/// 비트 값은 PlayerPrefs에 그대로 저장되므로 <b>기존 값을 재배치하지 마세요</b>.
/// </summary>
[Flags]
public enum DebugFlag
{
    /// <summary>차단 없음</summary>
    None = 0,

    /// <summary>정신 이상 발생 자체를 차단</summary>
    MentalBreak = 1 << 0,

    /// <summary>정신 이상은 일어나되 침식 계열(위험 행동)은 뽑히지 않음</summary>
    ErosionKind = 1 << 1,

    /// <summary>침식 수치가 오르지 않음</summary>
    ErosionGain = 1 << 2,

    /// <summary>외부 침략(레이드) 시작 차단</summary>
    Raid = 1 << 3,

    /// <summary>무작위 이벤트 자동 발생 차단</summary>
    RandomEvent = 1 << 4,

    /// <summary>제놉스 스폰 차단</summary>
    XenopsSpawn = 1 << 5,

    /// <summary>욕구(허기·기력·재미) 감소 정지</summary>
    NeedsDecay = 1 << 6,

    /// <summary>직원·건물이 피해를 받지 않음</summary>
    Damage = 1 << 7,
}

/// <summary>
/// DebugFlag의 표시 정보. UI가 목록을 그릴 때 사용합니다.
/// </summary>
public static class DebugFlagInfo
{
    /// <summary>UI에 표시할 순서대로 나열한 전체 플래그 목록 (None 제외)</summary>
    public static readonly DebugFlag[] All =
    {
        DebugFlag.MentalBreak,
        DebugFlag.ErosionKind,
        DebugFlag.ErosionGain,
        DebugFlag.Raid,
        DebugFlag.RandomEvent,
        DebugFlag.XenopsSpawn,
        DebugFlag.NeedsDecay,
        DebugFlag.Damage,
    };

    private static readonly Dictionary<DebugFlag, string> labels = new Dictionary<DebugFlag, string>
    {
        { DebugFlag.MentalBreak, "정신 이상 차단" },
        { DebugFlag.ErosionKind, "침식 계열 이상 차단" },
        { DebugFlag.ErosionGain, "침식 축적 차단" },
        { DebugFlag.Raid,        "외부 침략 차단" },
        { DebugFlag.RandomEvent, "무작위 이벤트 차단" },
        { DebugFlag.XenopsSpawn, "제놉스 등장 차단" },
        { DebugFlag.NeedsDecay,  "욕구 감소 정지" },
        { DebugFlag.Damage,      "무적 (직원·건물)" },
    };

    private static readonly Dictionary<DebugFlag, string> descriptions = new Dictionary<DebugFlag, string>
    {
        { DebugFlag.MentalBreak, "발생 판정을 굴리지 않습니다. 켜는 순간 진행 중인 이상도 해제됩니다." },
        { DebugFlag.ErosionKind, "정신 이상은 터지되 항상 일반 계열(태업)만 나옵니다." },
        { DebugFlag.ErosionGain, "모든 출처의 침식 상승을 무시합니다. (회복은 정상 동작)" },
        { DebugFlag.Raid,        "레이드 시작 요청을 무시합니다. 켜는 순간 진행 중인 레이드도 종료됩니다." },
        { DebugFlag.RandomEvent, "타이머에 의한 자동 발생만 막습니다. 수동 발생은 그대로 동작합니다." },
        { DebugFlag.XenopsSpawn, "이벤트·레이드·디버그를 포함한 모든 스폰을 막습니다." },
        { DebugFlag.NeedsDecay,  "허기·기력·재미가 줄지 않습니다." },
        { DebugFlag.Damage,      "직원과 건물이 받는 피해를 0으로 만듭니다." },
    };

    /// <summary>UI 표시 이름</summary>
    public static string GetLabel(DebugFlag flag)
        => labels.TryGetValue(flag, out string s) ? s : flag.ToString();

    /// <summary>UI 툴팁용 설명</summary>
    public static string GetDescription(DebugFlag flag)
        => descriptions.TryGetValue(flag, out string s) ? s : string.Empty;
}
