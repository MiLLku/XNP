using UnityEngine;

/// <summary>
/// 재미(오락) 시스템 기준값(SO). 숫자만 보관한다 — 평가·적용은 코드가 담당.
/// (NotificationSystem에서 확립한 Config=숫자만/코드=로직 분리 패턴)
///
/// <b>재미의 역할은 '정신 이상 임계점 조절' 하나뿐이다 (2026-07-29 확정).</b>
/// 재미가 높다고 작업이 빨라지지 않는다 — 정신 이상을 얼마나 잘 버티는지만 달라진다.
/// (구 workBonusThreshold/workBonusMultiplier는 이 원칙에 따라 제거됨)
///
/// 재미는 <b>기준점(baseline, 기본 50)을 중심으로 오르내린다</b>:
///   • 작업하면 기준점 아래로 밀려나 정신 이상에 취약해지고
///   • 쉬면 기준점으로 돌아오며
///   • 오락하면 기준점 위로 올라가 더 잘 버틴다
/// 임계점 조절은 구간형이 아니라 <b>연속형</b>이라 기준점에서 멀어진 만큼 선형으로 반영된다.
///
/// 참조 경로: EmployeeManager.FunConfig → EmployeeStatsController가 읽는다.
/// 에셋이 없으면 모든 효과는 중립(1.0)으로 동작한다.
/// </summary>
[CreateAssetMenu(fileName = "FunConfig", menuName = "XNP/Fun Config")]
public class FunConfig : ScriptableObject
{
    [Header("기준점 (정신 이상 임계점 계산의 기준)")]
    [Tooltip("정신 이상 저항 배율이 정확히 1.0이 되는 재미 수치.\n" +
             "이 위로 올라가면 잘 버티고, 아래로 내려가면 취약해진다.\n" +
             "※ 수렴 지점이 아니다 — 재미는 오락으로만 차오르고 그 외에는 항상 감소한다.")]
    [Range(0f, 100f)] public float baseline = 50f;

    [Header("감소 속도 (포인트/초)")]
    [Tooltip("오락 중이 아닐 때 항상 적용되는 감소 속도.\n" +
             "DayCycle 기본 600초=1일 기준 0.06 ≈ 하루 36 감소")]
    public float decayPerSecond = 0.06f;

    [Header("정신이상 임계점 조절 (연속형)")]
    [Tooltip("재미 1포인트당 저항 배율 변화량.\n" +
             "저항배율 = 1 + (재미 - 기준점) × 이 값\n" +
             "0.01이면 재미 100 → ×1.5(잘 버팀) / 50 → ×1.0 / 30 → ×0.8 / 10 → ×0.6 / 0 → ×0.5")]
    [Range(0f, 0.05f)] public float resistPerFunPoint = 0.01f;

    [Tooltip("저항 배율 하한 — 재미가 바닥이어도 이 아래로는 안 떨어진다")]
    [Range(0.1f, 1f)] public float minResistFactor = 0.5f;

    [Tooltip("저항 배율 상한 — 재미가 만점이어도 이 위로는 안 올라간다")]
    [Range(1f, 3f)] public float maxResistFactor = 1.5f;

    [Header("정신이상 취약 (피로 — 수면 부족)")]
    [Tooltip("피로가 이 수치 미만이면 취약 1단계. 수면 관리 실패도 정신 이상 임계점을 올린다")]
    [Range(0f, 100f)] public float fatigueVulnerableThreshold = 30f;

    [Tooltip("피로 취약 1단계 저항 배율 (0.85 = 임계점 ~18% 상승)")]
    public float fatigueVulnerableFactor = 0.85f;

    [Tooltip("피로가 이 수치 미만이면 취약 2단계 (심각한 수면 부족)")]
    [Range(0f, 100f)] public float fatigueSevereThreshold = 10f;

    [Tooltip("피로 취약 2단계 저항 배율 (0.7 = 임계점 ~43% 상승)")]
    public float fatigueSevereFactor = 0.7f;

    [Header("AI 행동 기준")]
    [Tooltip("오락 활동 시 이 수치에 도달하면 종료 (충분히 즐김)")]
    [Range(0f, 100f)] public float recreationTargetFun = 90f;

    [Tooltip("자유 시간에 이 수치 미만이면 스스로 오락거리를 찾음")]
    [Range(0f, 100f)] public float freeTimeFunThreshold = 40f;

    [Header("약물 (소모품 오락)")]
    [Tooltip("약물 복용의 선택 우선순위. 시설(IFunSource.Priority)과 같은 축에서 비교 — " +
             "기본 -10: 시설이 없거나 전부 사용 불가일 때만 폴백. 양수로 올리면 약물을 선호")]
    public int drugPriority = -10;
}
