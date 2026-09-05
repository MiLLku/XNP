using UnityEngine;

/// <summary>
/// 세척 시설 밸런스 기준값(SO). 숫자만 보관한다 — 판정·적용은 코드가 담당.
/// (NotificationSystem에서 확립한 Config=숫자만/코드=로직 분리 패턴)
///
/// 세척은 침식 회복 경로 셋(느린 자연 회복·세척 시설·무작위 이벤트) 중 하나이므로
/// ErosionManager가 StageConfig·RecoveryConfig와 함께 들고 있습니다.
///
/// 참조 경로: ErosionManager.WashConfig → WashStation이 읽는다.
/// 에셋이 없으면 이 클래스의 기본값이 그대로 쓰인다.
///
/// 티어별 차등이 필요한 값(동시 인원·세척 속도·수용 상한)은 프리팹 인스펙터에 둡니다.
/// 여기에는 <b>전 티어 공통 밸런스</b>만 넣으세요.
/// </summary>
[CreateAssetMenu(fileName = "WashConfig", menuName = "XNP/Wash Config")]
public class WashConfig : ScriptableObject
{
    [Header("부산물 산출")]
    [Tooltip("씻어낸 침식 1당 산출되는 침식 결정체 수 (전 티어 공통 기본값).\n" +
             "예: 0.2면 침식 100을 씻어낼 때 결정체 20개.\n" +
             "프리팹의 '결정체 비율 오버라이드'가 0 이상이면 그 값이 우선합니다.")]
    [Min(0f)] public float crystalPerErosion = 0.2f;

    /// <summary>기본값 폴백 — 에셋이 없을 때 쓰는 값.</summary>
    public const float DEFAULT_CRYSTAL_PER_EROSION = 0.2f;
}
