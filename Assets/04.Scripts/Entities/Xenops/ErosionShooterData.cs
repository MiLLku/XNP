using UnityEngine;

/// <summary>
/// 침식 투사체 발사형 적 전용 데이터 (XenopsData 서브클래스).
/// Unity 에디터에서 Create > StampSystem > ErosionShooter Data 로 생성합니다.
///
/// 공통 전투 스탯은 XenopsData.hostileStats에서 읽습니다.
///   - moveSpeed           : 이동 속도
///   - attackRange         : 발사 사거리
///   - attackSpeed         : 발사 속도 (회/초)
///   - aoeErosionPerSecond / aoeErosionRange : 범위 오라 (HostileErosionAura 사용)
/// </summary>
[CreateAssetMenu(fileName = "NewErosionShooter", menuName = "StampSystem/ErosionShooter Data")]
public class ErosionShooterData : XenopsData
{
    [Header("투사체 설정")]
    [Tooltip("투사체 프리팹 (필수). ErosionProjectile 컴포넌트가 부착되어 있어야 합니다.")]
    public GameObject projectilePrefab;

    [Tooltip("투사체 이동 속도 (units/초)")]
    [Min(1f)] public float projectileSpeed = 8f;

    [Tooltip("투사체 최대 생존 시간 (초)")]
    [Min(0.5f)] public float projectileLifetime = 3f;

    [Tooltip("피격 1회당 적용되는 침식량")]
    [Min(0f)] public float erosionOnHit = 12f;

    [Tooltip("피격 1회당 체력 직접 피해량 — 메인은 침식, 체력 피해는 최소치")]
    [Min(0f)] public float healthDamageOnHit = 1f;
}
