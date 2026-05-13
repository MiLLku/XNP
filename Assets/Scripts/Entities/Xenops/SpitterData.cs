using UnityEngine;

/// <summary>
/// 스피터 전용 데이터 (XenopsData 서브클래스).
/// Unity 에디터에서 Create > StampSystem > Spitter Data 로 생성합니다.
///
/// 스피터는 개체형(Hostile) 공통 스탯(hostileStats) 외에
/// 피격 시 DoT와 피격 침식을 추가로 보유합니다.
/// </summary>
[CreateAssetMenu(fileName = "NewSpitter", menuName = "StampSystem/Spitter Data")]
public class SpitterData : XenopsData
{
    [Header("스피터 전용 — 피격 효과")]
    [Tooltip("피격 시 적용되는 침식량 (1회)")]
    [Min(0)] public float hitErosionAmount = 5f;

    [Tooltip("피격 시 지속 체력 피해량 (HP/초)")]
    [Min(0)] public float dotDamagePerSecond = 3f;

    [Tooltip("피격 시 DoT 지속 시간 (초). 재피격 시 리셋됩니다.")]
    [Min(0)] public float dotDuration = 5f;
}
