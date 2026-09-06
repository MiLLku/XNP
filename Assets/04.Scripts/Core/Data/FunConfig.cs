using UnityEngine;

/// <summary>
/// 재미(오락) 시스템 기준값(SO). 숫자만 보관한다 — 평가·적용은 코드가 담당.
/// (NotificationSystem에서 확립한 Config=숫자만/코드=로직 분리 패턴)
///
/// <b>재미의 역할은 '정신력 조절' 하나뿐이다 (2026-09-06 개편).</b>
/// 재미는 정신 이상 판정에 <b>직접 관여하지 않는다</b> — 정신력을 통해서만 영향을 준다:
/// <code>재미 → 정신력 → 정신 이상 발생 확률</code>
/// (구 resistPerFunPoint/minResistFactor/maxResistFactor는 재미가 임계점을 직접 건드리는
///  경로였기 때문에 제거됐다. 임계점을 건드리는 것은 이제 특성·스킬뿐이다.)
///
/// 재미는 <b>기준점(baseline, 기본 50)을 중심으로 오르내린다</b>:
///   • 작업하면 기준점 아래로 밀려나 정신력이 깎이고
///   • 오락하면 기준점 위로 올라가 정신력이 붙는다
/// 정신력 보정은 구간형이 아니라 <b>연속형</b>이라 기준점에서 멀어진 만큼 선형으로 반영된다.
/// 굶주림·탈진과 같은 <b>상태형 모디파이어</b>라서, 재미가 기준점으로 돌아오면 보정도 사라진다.
///
/// 참조 경로: EmployeeManager.FunConfig → EmployeeStatsController가 읽는다.
/// 에셋이 없으면 재미는 감소하지도, 정신력에 영향을 주지도 않는다(중립).
/// </summary>
[CreateAssetMenu(fileName = "FunConfig", menuName = "XNP/Fun Config")]
public class FunConfig : ScriptableObject
{
    [Header("기준점 (정신력 보정 계산의 기준)")]
    [Tooltip("정신력 보정이 정확히 0이 되는 재미 수치.\n" +
             "이 위로 올라가면 정신력 보너스, 아래로 내려가면 페널티가 붙는다.\n" +
             "※ 수렴 지점이 아니다 — 재미는 오락으로만 차오르고 그 외에는 항상 감소한다.")]
    [Range(0f, 100f)] public float baseline = 50f;

    [Header("감소 속도 (포인트/초)")]
    [Tooltip("오락 중이 아닐 때 항상 적용되는 감소 속도.\n" +
             "DayCycle 기본 600초=1일 기준 0.06 ≈ 하루 36 감소")]
    public float decayPerSecond = 0.06f;

    [Header("정신력 보정 (연속형 상태형 모디파이어)")]
    [Tooltip("재미 1포인트당 정신력 가감량.")]
    [Range(0f, 2f)] public float mentalPerFunPoint = 0.4f;

    [Tooltip("정신력 페널티 하한")]
    [Range(-100f, 0f)] public float minMentalOffset = -15f;

    [Tooltip("정신력 보너스 상한")]
    [Range(0f, 100f)] public float maxMentalOffset = 15f;

    [Header("AI 행동 기준")]
    [Tooltip("오락 활동 시 이 수치에 도달하면 종료 (충분히 즐김)")]
    [Range(0f, 100f)] public float recreationTargetFun = 100f;

    [Tooltip("자유 시간에 이 수치 미만이면 스스로 오락거리를 찾음")]
    [Range(0f, 100f)] public float freeTimeFunThreshold = 40f;
}
