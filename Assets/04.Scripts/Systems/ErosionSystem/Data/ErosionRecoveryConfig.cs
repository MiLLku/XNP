using UnityEngine;

/// <summary>
/// 침식 회복 파라미터 ScriptableObject.
///
/// <b>침식 회복 경로는 셋뿐이다 (2026-07-29 확정):</b>
///   ① <b>매우 느린 자연 회복</b> — 단, 하한 아래로는 내려가지 않는다
///   ② <b>세척 시설</b>에서 세척 작업 수행 → 침식 0
///   ③ 무작위 이벤트 (기존 EventSystem의 효과로 처리)
/// 휴식·포스트레이드 보너스 회복은 이 원칙에 따라 제거됐다.
///
/// 하한이 있으므로 <b>세척 시설 없이는 침식을 완전히 지울 수 없다.</b>
/// 하한은 게임 진행에 따라 낮아진다 (연구 + 런타임 감소량 — ErosionManager.EffectiveRecoveryFloor 참고).
///
/// 생성: Create > StampSystem > Erosion Recovery Config
/// </summary>
[CreateAssetMenu(fileName = "ErosionRecoveryConfig", menuName = "StampSystem/Erosion Recovery Config")]
public class ErosionRecoveryConfig : ScriptableObject
{
    #region 자연 회복

    [Header("자연 회복 (매우 느림)")]
    [Tooltip("오라 범위 밖에서 이 시간(초) 경과 후 자연 회복 시작")]
    [Min(0)] public float outOfAuraDuration = 33f;

    [Tooltip("자연 회복 속도 (침식 / 초).\n" +
             "0.1이면 게임 하루(600초)에 60 회복 — 침식 200을 빼는 데 3일 이상 걸린다.")]
    [Min(0f)] public float naturalRecoveryPerSecond = 0.06f;

    [Tooltip("자연 회복 하한 — 이 수치 아래로는 자연 회복이 내려가지 않는다.\n" +
             "여기서 더 지우려면 세척 시설이 필요하다.\n" +
             "게임 진행에 따라 연구·런타임 감소량만큼 낮아진다 (ErosionManager가 계산).")]
    [Min(0f)] public float naturalRecoveryFloor = 50f;

    #endregion

    #region 아이템 즉시 회복

    [Header("실외 기본 침식")]
    [Tooltip("바깥 세상의 기본 침식 수치. 평상시에는 고정이며 이벤트로만 바뀝니다. 밀폐된 방은 여기서 출발하고, 그 뒤로는 실외와 완전히 분리됩니다(전도 없음).")]
    [Min(0f)] public float outdoorErosionBase = 10f;

    [Header("환경 노출 (방 침식 + 타일 침식)")]
    [Tooltip("유효 침식 1당 초당 받는 침식량. 예: 0.01이면 침식 50인 방에서 초당 0.5씩 오릅니다.")]
    [Min(0f)] public float exposurePerErosionPoint = 0.01f;

    [Tooltip("환경 노출 판정 주기(초). 짧을수록 즉각적이지만 총량은 같습니다.")]
    [Min(0.1f)] public float exposureCheckInterval = 1f;

    [Header("아이템 즉시 회복")]
    [Tooltip("정화 약품 사용 시 즉시 회복되는 침식 수치 (아이템 미구현 — 향후 소모품 경로용)")]
    [Min(0)] public float purificationItemAmount = 80f;

    #endregion

    #region 전파 오라

    [Header("침식 전파 오라 (4단계)")]
    [Tooltip("4단계 직원이 주변 직원에게 침식을 옮기는 판정 주기 (초).\n" +
             "초당 전파량(ErosionStageConfig.auraErosionPerSecond)에 이 값을 곱해 한 번에 적용하므로,\n" +
             "주기를 늘려도 시간당 총 전파량은 같고 판정 빈도만 줄어든다.")]
    [Min(0.1f)] public float propagationCheckInterval = 8f;

    #endregion
}
