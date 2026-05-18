using UnityEngine;

/// <summary>
/// 침식 회복 파라미터 ScriptableObject.
/// 자연 회복, 휴식 보너스, 포스트 레이드 회복 등의 수치를 인스펙터에서 설정합니다.
///
/// 생성: Create > StampSystem > Erosion Recovery Config
/// </summary>
[CreateAssetMenu(fileName = "ErosionRecoveryConfig", menuName = "StampSystem/Erosion Recovery Config")]
public class ErosionRecoveryConfig : ScriptableObject
{
    [Header("자연 회복")]
    [Tooltip("오라 범위 밖에서 이 시간(초) 경과 후 자연 회복 시작")]
    [Min(0)] public float outOfAuraDuration = 20f;

    [Tooltip("자연 회복 속도 (침식 / 초)")]
    [Min(0)] public float naturalRecoveryPerSecond = 3f;

    [Header("아이템 즉시 회복")]
    [Tooltip("정화 약품 사용 시 즉시 회복되는 침식 수치")]
    [Min(0)] public float purificationItemAmount = 80f;

    [Header("포스트 레이드 회복")]
    [Tooltip("레이드 종료 후 회복 속도 (침식 / 초). 자연 회복과 중첩됩니다.")]
    [Min(0)] public float postRaidRecoveryPerSecond = 10f;

    [Tooltip("포스트 레이드 가속 회복 지속 시간 (초)")]
    [Min(0)] public float postRaidRecoveryDuration = 120f;

    [Header("휴식/수면 보너스")]
    [Tooltip("직원이 Resting 상태일 때 추가 회복 속도 (침식 / 초)")]
    [Min(0)] public float restRecoveryPerSecond = 5f;
}
