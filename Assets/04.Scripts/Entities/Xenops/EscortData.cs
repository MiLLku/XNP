using UnityEngine;

/// <summary>
/// 침식 호위병 데이터 — 원거리 침식체(침식 사수) 곁을 지키는 근접 침식체.
/// 이동·공격 스탯은 hostileStats를 쓰고, 여기엔 호위 거리만 둡니다.
/// </summary>
[CreateAssetMenu(fileName = "NewEscort", menuName = "StampSystem/Escort Xenops Data")]
public class EscortData : XenopsData
{
    [Header("호위")]
    [Tooltip("호위할 원거리 침식체를 찾는 반경 (칸)")]
    [Min(1)] public float chargeSearchRadius = 25f;

    [Tooltip("평소 호위 대상과 벌려 두는 가로 거리 (칸) — 이 안이면 제자리에 선다")]
    [Min(0)] public float followDistance = 1.5f;

    [Tooltip("호위 대상에서 이 반경 안으로 들어온 직원만 막아선다 (칸)")]
    [Min(0)] public float guardRadius = 4f;

    [Tooltip("막아서더라도 호위 대상에서 이 이상 멀어지면 추격을 멈추고 돌아간다 (칸)")]
    [Min(0)] public float leashRadius = 6f;
}
