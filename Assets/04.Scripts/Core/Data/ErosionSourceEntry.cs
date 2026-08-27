using System;

/// <summary>
/// 침식이 어디서 얼마나 쌓였는지를 기록하는 내역 항목.
///
/// 침식은 정신력 모디파이어처럼 "붙었다 사라지는" 값이 아니라 <b>누적되는 수치</b>다.
/// 따라서 이 항목은 현재 적용 중인 효과가 아니라 <b>출처별 누적 기여량</b>을 보여준다:
/// <code>
/// 침식 62.0
///   자연 침식          +3.0
///   제놉스 A 오라침식   +7.0
///   침식 사수 피격      +52.0
/// </code>
/// 회복(자연 회복·세척)은 각 출처에서 비례 차감되어 내역이 총량과 어긋나지 않는다.
/// </summary>
[Serializable]
public class ErosionSourceEntry
{
    /// <summary>중복 합산용 키 (같은 키면 한 줄로 누적)</summary>
    public string sourceKey;

    /// <summary>UI 표시용 이름 (예: "제놉스 A 오라침식")</summary>
    public string displayName;

    /// <summary>이 출처가 기여한 누적 침식량</summary>
    public float amount;
}

/// <summary>
/// 코드가 등록하는 침식 출처의 표준 키.
/// 개체별로 구분해야 하는 경우(제놉스 오라 등)는 접두사 뒤에 인스턴스 ID를 붙인다.
/// </summary>
public static class ErosionSource
{
    /// <summary>자연 침식 타일</summary>
    public const string NATURAL = "natural";

    /// <summary>위험 작업(발원지 제거·세척 등) — 작업 종류별로 구분됩니다</summary>
    public const string HAZARD_PREFIX = "hazard_";

    /// <summary>위험 작업의 출처 키를 만듭니다. 작업 이름이 다르면 내역도 따로 잡힙니다.</summary>
    public static string HazardKey(string hazardName) => HAZARD_PREFIX + hazardName;

    /// <summary>제놉스 오라 (개체별 구분 — AuraKey 사용)</summary>
    public const string AURA_PREFIX = "aura_";

    /// <summary>침식 직원의 전파 오라 (4단계)</summary>
    public const string PROPAGATION_PREFIX = "propagation_";

    /// <summary>침식 폭주 이상 행동 (직원별 구분 — OutburstKey 사용)</summary>
    public const string OUTBURST_PREFIX = "outburst_";

    /// <summary>원거리 피격 (스피터·침식 투사체)</summary>
    public const string PROJECTILE = "projectile";

    /// <summary>오염 구체 폭발</summary>
    public const string CONTAMINATION = "contamination";

    /// <summary>출처 미상 (치트·디버그 등)</summary>
    public const string UNKNOWN = "unknown";

    /// <summary>제놉스 개체별 오라 키를 만듭니다.</summary>
    public static string AuraKey(int instanceId) => AURA_PREFIX + instanceId;

    /// <summary>직원 전파 오라 키를 만듭니다.</summary>
    public static string PropagationKey(int instanceId) => PROPAGATION_PREFIX + instanceId;

    /// <summary>침식 폭주 직원별 키를 만듭니다.</summary>
    public static string OutburstKey(int instanceId) => OUTBURST_PREFIX + instanceId;
}
