using System;
using UnityEngine;

/// <summary>
/// 침식이 심한 방 경고 기준값.
///
/// 방 침식은 <b>스스로 줄지 않습니다</b>. 발원지를 캐거나(채광) 세척하거나 환기하지 않으면
/// 계속 고이므로, 플레이어가 놓치지 않도록 조건이 유지되는 동안 배너를 상시 표시합니다.
///
/// 기준값은 이 Config가, 판정과 출력은 Evaluator가 담당합니다(프로젝트 알림 시스템 규약).
/// </summary>
[Serializable]
public class RoomErosionAlertConfig
{
    [Tooltip("이 경고를 사용할지")]
    public bool enabled = true;

    [Header("임계값")]
    [Tooltip("방 침식이 이 값 이상이면 '주의' 배너를 띄웁니다.")]
    [Min(0f)] public float cautionThreshold = 60f;

    [Tooltip("방 침식이 이 값 이상이면 '위험' 배너로 올립니다.")]
    [Min(0f)] public float dangerThreshold = 120f;

    [Header("레터")]
    [Tooltip("위험 수준에 처음 도달할 때 레터(팝업)를 발행할지")]
    public bool pushLetter = true;

    [Tooltip("레터 재발행 간격(초). 0 이하면 한 번만 띄웁니다.")]
    [Min(0f)] public float letterRepeatInterval = 0f;

    [Tooltip("레터를 읽을 때까지 게임을 멈출지")]
    public bool pauseUntilRead = false;
}
