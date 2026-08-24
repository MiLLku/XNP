using System;

/// <summary>
/// 정신력 변동 하나를 나타내는 항목.
///
/// 정신력은 절대 수치를 직접 깎고 채우는 값이 아니다. 직원마다 <b>기본값</b>(EmployeeData.baseMental, 예: 50)이 있고,
/// 현재 정신력은 거기에 활성 모디파이어를 전부 더한 결과다:
/// <code>정신력 = clamp(기본값 + Σ모디파이어, 0, 최대치)</code>
///
/// 따라서 정신력 변동은 <b>영구적이지 않다</b>. 두 방식으로 원상복구된다:
///   • <b>상태형</b>(remainingTime &lt; 0) — 조건이 참인 동안만 붙어 있다. 굶주림·탈진처럼
///     <b>상황을 해결하면</b> 자동으로 사라진다.
///   • <b>시간형</b>(remainingTime ≥ 0) — 지속 시간이 지나면 사라진다. 오락·감정 폭발 목격처럼
///     <b>효과가 끝나면</b> 원래대로 돌아온다.
/// </summary>
[Serializable]
public class MentalModifier
{
    /// <summary>중복 방지 키. 같은 키로 다시 추가하면 새 항목이 쌓이지 않고 기존 항목이 갱신된다.</summary>
    public string reasonKey;

    /// <summary>UI 표시용 이름 (예: "굶주림", "오락을 즐김")</summary>
    public string displayName;

    /// <summary>정신력 가감량. 음수면 페널티, 양수면 보너스.</summary>
    public float value;

    /// <summary>남은 지속 시간(초). 음수면 상태형(조건이 해소될 때까지 유지).</summary>
    public float remainingTime;

    /// <summary>상태형(조건 기반) 모디파이어인지 여부.</summary>
    public bool IsConditional => remainingTime < 0f;
}

/// <summary>
/// 정신력 모디파이어 저장 데이터.
/// </summary>
[Serializable]
public class MentalModifierSaveData
{
    public string reasonKey;
    public string displayName;
    public float value;
    public float remainingTime;
}

/// <summary>
/// 코드가 등록하는 모디파이어의 표준 키.
/// 문자열 오타로 중복 항목이 생기는 것을 막기 위해 상수로 모아둔다.
/// </summary>
public static class MentalReason
{
    /// <summary>굶주림 (상태형 — 배가 차면 사라짐)</summary>
    public const string STARVATION = "starvation";

    /// <summary>탈진 (상태형 — 자고 나면 사라짐)</summary>
    public const string EXHAUSTION = "exhaustion";

    /// <summary>오락을 즐김 (시간형)</summary>
    public const string RECREATION = "recreation";

    /// <summary>동료의 감정 폭발 목격 (시간형)</summary>
    public const string OUTBURST = "outburst";

    /// <summary>제놉스 효과 (시간형)</summary>
    public const string XENOPS = "xenops";

    /// <summary>이벤트 등 출처가 특정되지 않은 일시적 기분 (시간형)</summary>
    public const string GENERIC = "mood";

    /// <summary>
    /// 정신차림 (시간형) — 정신 이상을 한 차례 겪고 난 뒤 붙는 큰 폭의 안정 버프.
    /// 이게 붙어 있는 동안은 정신 비율이 임계점 위로 올라가 다시 터지지 않는다.
    /// </summary>
    public const string COMPOSURE = "composure";
}
