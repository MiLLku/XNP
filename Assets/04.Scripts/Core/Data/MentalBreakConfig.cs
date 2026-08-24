using UnityEngine;

/// <summary>
/// 정신 이상 발생 판정 기준값 (ScriptableObject).
///
/// 역할 분담(2026-07-29 개편):
///   • <b>정신 수치</b> — 정신 이상이 <b>발생할 확률</b>을 결정한다.
///   • <b>침식 수치</b> — 발생한 정신 이상이 <b>'침식 계열'일 확률</b>을 높인다. 발생 확률에는 관여하지 않는다.
///   • <b>임계점</b> — 직원마다 다르다. 공통 기본값을 개인 저항 배율로 나눠 보정한다.
///
/// 발생 판정은 림월드식이다. 매 프레임이 아니라 checkIntervalSeconds 주기로만 검사하고,
/// 각 검사에서 "평균 발생 간격(MTB)"을 확률로 환산해 굴린다:
///   p = 1 - exp(-checkInterval / mtbSeconds)
/// 임계점 아래로 깊이 내려갈수록 MTB가 짧아져 더 자주 터진다.
///
/// 미할당 시 EmployeeMental이 이 클래스의 필드 기본값과 동일한 상수로 동작합니다.
/// 메뉴: StampSystem ▶ Mental Break Config
/// </summary>
[CreateAssetMenu(fileName = "MentalBreakConfig", menuName = "StampSystem/Mental Break Config")]
public class MentalBreakConfig : ScriptableObject
{
    #region 판정 주기

    [Header("판정 주기")]
    [Tooltip("정신 이상 발생 여부를 검사하는 간격(초). 림월드의 150틱 주기 검사에 대응합니다.\n" +
             "짧을수록 판정이 촘촘해지지만 발생 빈도 자체는 MTB가 결정하므로 크게 달라지지 않습니다.")]
    [Range(0.5f, 15f)]
    public float checkIntervalSeconds = 2.5f;

    [Tooltip("정신 이상이 끝난 뒤 다음 판정까지의 유예 시간(초).\n" +
             "이게 없으면 정신이 바닥인 직원이 연속으로 터져 손쓸 틈이 없습니다.")]
    [Min(0f)]
    public float breakGraceSeconds = 40f;

    #endregion

    #region 임계점 (정신 비율)

    [Header("정신 이상 임계점 (정신력 비율)")]
    [Tooltip("정신 비율이 이 값 미만이면 정신 이상이 발생할 수 있습니다. 심각도 등급은 두지 않습니다 — 얼마나 위험한 이상이 나오는지는 침식 수치가 계열 확률로 정합니다.")]
    [Range(0f, 1f)]
    public float breakThreshold = 0.50f;

    #endregion

    #region 평균 발생 간격 (MTB)

    [Header("평균 발생 간격 (게임일)")]
    [Tooltip("임계점 바로 아래에서 정신 이상이 평균 몇 게임일에 한 번 발생하는지. 게임 1일 = DayCycle.DayLengthInSeconds(기본 600초) 기준으로 환산됩니다.")]
    [Min(0.01f)]
    public float mtbDays = 0.75f;

    [Tooltip("임계점 아래로 얼마나 깊이 내려갔는지에 따른 MTB 단축 계수. 정신이 0에 닿았을 때 MTB가 1/(1+계수)로 줄어듭니다. 심각도 등급을 없앤 뒤로는 이 값이 '정신이 낮을수록 자주 터진다'를 혼자 담당합니다.")]
    [Range(0f, 8f)]
    public float depthMtbFactor = 4f;

    #endregion

    #region 정신차림 (회복 버프)

    [Header("정신차림")]
    [Tooltip("정신 이상이 끝난 뒤 붙는 정신력 보너스. 기본값 50에 더해 상한(100)에 닿으므로, 욕구가 정상인 직원은 이 구간 동안 사실상 완전히 안전합니다. 굶주림(-25)·탈진(-20)이 붙어 있으면 그만큼 깎여 안전이 보장되지 않습니다.")]
    [Min(0f)]
    public float composureBonus = 50f;

    [Tooltip("정신차림 버프 지속 시간(초). 300초 = 12 게임시간 = 0.5 게임일(1일 600초 기준). 오락 버프(180초)보다 길고 평균 발생 간격(450초)보다는 짧게 두어, 한 번 터뜨렸다고 무한히 안전해지지는 않게 합니다.")]
    [Min(0f)]
    public float composureDurationSeconds = 1000f;

    #endregion

    #region 침식 계열 가중

    [Header("침식 계열 선택")]
    [Tooltip("이 수치에서 침식 계열 선택 확률이 100%가 됩니다 (완전 침식 수치와 동일하게 두는 것을 권장).")]
    [Min(1f)]
    public float erosionFullLevel = 200f;

    [Tooltip("침식 비율에 곱해지는 가중치. 1이면 침식 100/200일 때 침식 계열 확률 50%(선형).\n" +
             "1보다 크면 낮은 침식에서도 침식 계열이 잘 나옵니다.")]
    [Range(0f, 3f)]
    public float erosionWeightMultiplier = 1f;

    [Tooltip("침식 계열 정신 이상의 재발생 대기 시간(초). 같은 종류가 연속으로 나오는 것을 막습니다.")]
    [Min(0f)]
    public float erosionEventCooldown = 75f;

    #endregion

}
