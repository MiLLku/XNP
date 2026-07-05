using UnityEngine;

/// <summary>
/// 재미(오락) 시스템 기준값(SO). 숫자만 보관한다 — 평가·적용은 코드가 담당.
/// (NotificationSystem에서 확립한 Config=숫자만/코드=로직 분리 패턴)
///
/// 참조 경로: EmployeeManager.FunConfig → EmployeeStatsController가 읽는다.
/// 에셋이 없으면 모든 효과는 중립(1.0)으로 동작한다.
/// </summary>
[CreateAssetMenu(fileName = "FunConfig", menuName = "XNP/Fun Config")]
public class FunConfig : ScriptableObject
{
    [Header("감소 속도 (포인트/초)")]
    [Tooltip("평상시 재미 감소 속도. DayCycle 기본 600초=1일 기준 0.06 ≈ 하루 36 감소")]
    public float decayPerSecond = 0.06f;

    [Tooltip("작업 중 추가 감소 속도 (평상시에 더해짐)")]
    public float workingExtraDecayPerSecond = 0.06f;

    [Header("작업 효율 보너스 (재미 높을 때)")]
    [Tooltip("이 수치 이상이면 작업 속도 보너스 적용")]
    [Range(0f, 100f)] public float workBonusThreshold = 70f;

    [Tooltip("보너스 작업 속도 배율 (1.15 = +15%)")]
    public float workBonusMultiplier = 1.15f;

    [Header("침식 취약 (재미 낮을 때)")]
    [Tooltip("이 수치 미만이면 침식 취약 1단계 — 유효 침식이 높아져 이상행동 단계에 더 빨리 도달")]
    [Range(0f, 100f)] public float vulnerableThreshold = 30f;

    [Tooltip("취약 1단계 저항 배율 (0.8 = 유효 침식 25% 증가)")]
    public float vulnerableFactor = 0.8f;

    [Tooltip("이 수치 미만이면 침식 취약 2단계 (심각)")]
    [Range(0f, 100f)] public float severeVulnerableThreshold = 10f;

    [Tooltip("취약 2단계 저항 배율 (0.6 = 유효 침식 66% 증가)")]
    public float severeVulnerableFactor = 0.6f;

    [Header("침식 취약 (피로 — 수면 부족)")]
    [Tooltip("피로가 이 수치 미만이면 침식 취약 1단계. 수면 관리 실패도 침식 임계점을 낮춘다")]
    [Range(0f, 100f)] public float fatigueVulnerableThreshold = 30f;

    [Tooltip("피로 취약 1단계 저항 배율 (0.85 = 유효 침식 ~18% 증가)")]
    public float fatigueVulnerableFactor = 0.85f;

    [Tooltip("피로가 이 수치 미만이면 침식 취약 2단계 (심각한 수면 부족)")]
    [Range(0f, 100f)] public float fatigueSevereThreshold = 10f;

    [Tooltip("피로 취약 2단계 저항 배율 (0.7 = 유효 침식 ~43% 증가)")]
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
