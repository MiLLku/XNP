using UnityEngine;

/// <summary>
/// 전투 시스템 기준값(SO). 숫자만 보관 — 태세 로직은 EmployeeCombat(코드)이 담당.
/// 참조 경로: EmployeeManager.CombatConfig. 미할당 시 코드 기본값으로 동작.
/// </summary>
[CreateAssetMenu(fileName = "CombatConfig", menuName = "XNP/Combat Config")]
public class CombatConfig : ScriptableObject
{
    [Header("공통")]
    [Tooltip("무기 없음(맨손) 공격 사거리 (타일)")]
    public float unarmedRange = 1.5f;

    [Tooltip("무기 없음(맨손) 공격 간격 (초)")]
    public float unarmedInterval = 1.2f;

    [Tooltip("적 탐지 재평가 주기 (초)")]
    public float targetScanInterval = 0.5f;

    [Header("경계 (Guard)")]
    [Tooltip("경계 반경 기본값 (타일). 직원 특성 guardRangeBonus가 가감된다")]
    public float guardRadius = 6f;

    [Tooltip("경계 위치에서 이 거리 이상 벗어나면 추격을 포기하고 복귀 (leash)")]
    public float guardLeashRadius = 9f;

    [Header("카이팅 (Kiting)")]
    [Tooltip("적이 이 거리 이내로 오면 후퇴")]
    public float kitingMinDistance = 3f;

    [Tooltip("후퇴 시 확보하려는 목표 거리")]
    public float kitingPreferredDistance = 5f;

    [Header("원거리 (아군 투사체)")]
    [Tooltip("아군 투사체 프리팹 (AllyProjectile 컴포넌트 필수). 미설정 시 원거리 무기도 즉시 타격으로 폴백")]
    public GameObject allyProjectilePrefab;

    [Tooltip("아군 투사체 속도 (타일/초)")]
    public float allyProjectileSpeed = 10f;

    [Tooltip("아군 투사체 수명 (초) — 사거리 밖으로 무한히 날아가지 않게 제한")]
    public float allyProjectileLifetime = 1.5f;

    [Header("방어 (Defend)")]
    [Tooltip("방어 태세 중 방어형 장비 감쇄 증폭 배율 (1.5 = 감쇄 50% 증폭)")]
    public float defendReductionMultiplier = 1.5f;

    [Tooltip("적 타겟 선정 시 방어 태세 직원에게 곱해지는 어그로 가중치 (거리 나눗셈 — 클수록 우선 타겟)")]
    public float defendAggroWeight = 3f;
}
