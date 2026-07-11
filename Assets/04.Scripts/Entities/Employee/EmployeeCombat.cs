using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 직원 전투 태세 서브 컴포넌트. 소집(Draft) 중에만 동작한다.
///
/// 태세 (가용 여부는 무기 타입이 결정):
///   점거   — 위치 고정, 자기 사거리 내 적만 공격
///   방어   — 공격 안 함, 감쇄 증폭 + 어그로 (방어형 장비 필요)
///   경계   — 경계 반경(Config 기본 + 특성 guardRangeBonus) 내 적 교전, leash 복귀 (기본)
///   카이팅 — 원거리 전용, 최소 거리 유지 후퇴 + 사격
///
/// 공격 = XenopsHealth.TakeDamage(attackPower) + 무기 내구도 소모 + OnHit 능력 발동.
/// 제압/처치는 XenopsHealth 기존 경로 그대로 (동일 취급 — 사용자 확정).
/// 기준값: EmployeeManager.CombatConfig(SO). 태세 저장은 Phase 5(세이브 v6)에서.
/// </summary>
[RequireComponent(typeof(Employee))]
public class EmployeeCombat : MonoBehaviour
{
    private Employee employee;
    private EmployeeDraft draft;
    private EmployeeMovement movement;
    private EmployeeEquipment equipment;

    [SerializeField] private CombatStance stance = CombatStance.Guard;

    /// <summary>경계/점거 기준점 (소집 또는 태세 변경 시 현재 위치)</summary>
    private Vector3 anchor;
    private float scanTimer;
    private float attackTimer;
    private Xenops target;
    private bool movingToTarget;

    /// <summary>현재 태세</summary>
    public CombatStance Stance => stance;

    /// <summary>방어 태세 중인지 (Employee.TakeDamage 감쇄 증폭 + 적 어그로에서 참조)</summary>
    public bool IsDefending => draft != null && draft.IsDrafted && stance == CombatStance.Defend;

    private static CombatConfig Cfg => EmployeeManager.instance != null ? EmployeeManager.instance.CombatConfig : null;

    #region 초기화

    private void Awake()
    {
        employee  = GetComponent<Employee>();
        draft     = GetComponent<EmployeeDraft>();
        movement  = GetComponent<EmployeeMovement>();
        equipment = GetComponent<EmployeeEquipment>();

        // Start가 아닌 Awake에서 구독 — 스폰 직후 같은 프레임에 소집되면 이벤트를 놓친다
        if (draft != null) draft.OnDraftChanged += OnDraftChanged;
    }

    private void Start()
    {
        // 구독 이전에 이미 소집된 상태로 시작하는 경우(로드 복원 등) anchor 보정
        if (draft != null && draft.IsDrafted && anchor == Vector3.zero)
            anchor = transform.position;
    }

    private void OnDestroy()
    {
        if (draft != null) draft.OnDraftChanged -= OnDraftChanged;
    }

    private void OnDraftChanged(bool drafted)
    {
        anchor = transform.position;
        target = null;
        movingToTarget = false;
        if (drafted && !CanUseStance(stance))
            stance = CombatStance.Guard; // 무기 교체 등으로 불가해진 태세 보정
    }

    #endregion

    #region 태세 API (UI에서 호출)

    /// <summary>현재 무기/장비 기준 태세 사용 가능 여부.</summary>
    public bool CanUseStance(CombatStance s)
    {
        var weapon = equipment != null ? equipment.GetItemInSlot(EquipmentSlot.Weapon) : null;
        WeaponClass wc = weapon != null ? weapon.weaponClass : WeaponClass.Melee;

        switch (s)
        {
            case CombatStance.HoldPosition: return true;
            case CombatStance.Guard:        return true;
            case CombatStance.Defend:       return wc == WeaponClass.Melee && HasDefensiveGear();
            case CombatStance.Kiting:       return wc == WeaponClass.Ranged;
            default: return false;
        }
    }

    /// <summary>태세를 변경합니다. 불가한 태세면 false.</summary>
    public bool SetStance(CombatStance s)
    {
        if (!CanUseStance(s)) return false;
        stance = s;
        anchor = transform.position;
        target = null;
        movingToTarget = false;
        return true;
    }

    /// <summary>
    /// 플레이어 이동 명령 시 호출 (EmployeeDraft.CommandMoveTo).
    /// 경계/점거 기준점을 목적지로 옮기고 현재 교전을 해제해 이동 명령이 우선되게 한다.
    /// 도착 후에는 새 기준점에서 태세대로 재교전한다.
    /// </summary>
    public void OnPlayerMoveCommand(Vector3 destination)
    {
        anchor = destination;
        target = null;
        movingToTarget = false;
    }

    /// <summary>방어형 장비(isDefensiveGear) 보유 여부 — 슬롯 전체 검사.</summary>
    public bool HasDefensiveGear()
    {
        if (equipment == null) return false;
        foreach (EquipmentSlot slot in System.Enum.GetValues(typeof(EquipmentSlot)))
        {
            var d = equipment.GetItemInSlot(slot);
            if (d != null && d.isDefensiveGear) return true;
        }
        return false;
    }

    #endregion

    #region 전투 루프

    private void Update()
    {
        if (draft == null || !draft.IsDrafted) return;
        if (employee == null || employee.State == EmployeeState.Dead) return;
        if (stance == CombatStance.Defend) return; // 방어: 공격 안 함 (효과는 TakeDamage/어그로 측)

        attackTimer -= Time.deltaTime;

        scanTimer -= Time.deltaTime;
        if (scanTimer <= 0f)
        {
            scanTimer = Cfg != null ? Cfg.targetScanInterval : 0.5f;
            EvaluateCombat();
        }
    }

    private void EvaluateCombat()
    {
        float range = GetAttackRange();

        // 타겟 유효성 재확인
        if (target != null && (target.State == XenopsState.Subdued || target.gameObject == null))
            target = null;
        if (target == null) target = FindNearestHostile(GetDetectRadius());
        if (target == null) { ReturnToAnchorIfNeeded(); return; }

        float dist = Vector2.Distance(transform.position, target.transform.position);

        switch (stance)
        {
            case CombatStance.HoldPosition:
                // 이동 없음 — 사거리 내일 때만 공격
                if (dist <= range) TryAttack(target);
                break;

            case CombatStance.Guard:
                float leash = (Cfg != null ? Cfg.guardLeashRadius : 9f);
                if (Vector2.Distance(anchor, target.transform.position) > leash)
                { target = null; ReturnToAnchorIfNeeded(); return; }

                if (dist <= range) { StopApproach(); TryAttack(target); }
                else Approach(target.transform.position);
                break;

            case CombatStance.Kiting:
                float minDist = Cfg != null ? Cfg.kitingMinDistance : 3f;
                float prefer  = Cfg != null ? Cfg.kitingPreferredDistance : 5f;
                if (dist < minDist)
                {
                    // 적 반대 방향으로 후퇴
                    float dir = Mathf.Sign(transform.position.x - target.transform.position.x);
                    if (dir == 0f) dir = 1f;
                    Approach(target.transform.position + new Vector3(dir * prefer, 0f, 0f));
                }
                else if (dist <= range) { StopApproach(); TryAttack(target); }
                else Approach(target.transform.position);
                break;
        }
    }

    /// <summary>탐지 반경 — 점거는 자기 사거리, 경계/카이팅은 경계 반경(+특성 가감).</summary>
    private float GetDetectRadius()
    {
        if (stance == CombatStance.HoldPosition) return GetAttackRange();

        float baseRadius = Cfg != null ? Cfg.guardRadius : 6f;
        return Mathf.Max(1f, baseRadius + GetTraitGuardBonus());
    }

    /// <summary>특성 경계 반경 가감 합산 (flat).</summary>
    private float GetTraitGuardBonus()
    {
        float bonus = 0f;
        var data = employee.Data;
        if (data?.traits != null)
            foreach (var t in data.traits)
                if (t != null) bonus += t.effects.guardRangeBonus;
        return bonus;
    }

    private float GetAttackRange()
    {
        var weapon = equipment != null ? equipment.GetItemInSlot(EquipmentSlot.Weapon) : null;
        if (weapon != null) return weapon.attackRange;
        return Cfg != null ? Cfg.unarmedRange : 1.5f;
    }

    private float GetAttackInterval()
    {
        var weapon = equipment != null ? equipment.GetItemInSlot(EquipmentSlot.Weapon) : null;
        if (weapon != null) return weapon.attackInterval;
        return Cfg != null ? Cfg.unarmedInterval : 1.2f;
    }

    private Xenops FindNearestHostile(float radius)
    {
        if (XenopsManager.instance == null) return null;

        Xenops best = null;
        float bestDist = float.MaxValue;
        Vector3 origin = stance == CombatStance.HoldPosition ? transform.position : anchor;

        foreach (var x in XenopsManager.instance.GetXenopsByType(XenopsType.Hostile))
        {
            if (x == null || x.State == XenopsState.Subdued) continue;
            float d = Vector2.Distance(origin, x.transform.position);
            if (d <= radius && d < bestDist) { best = x; bestDist = d; }
        }
        return best;
    }

    private void TryAttack(Xenops victim)
    {
        if (attackTimer > 0f) return;
        attackTimer = GetAttackInterval();

        var health = victim.GetComponent<XenopsHealth>();
        if (health == null || health.IsDead) { target = null; return; }

        health.TakeDamage(employee.Stats.attackPower);
        equipment?.NotifyAttackPerformed();               // 무기 내구도 소모
        equipment?.TriggerAbilities(AbilityTriggerType.OnHit);

        if (health.IsDead) target = null;
    }

    private void Approach(Vector3 pos)
    {
        if (movement == null || movingToTarget) return;
        movingToTarget = true;
        movement.MoveTo(pos,
            onComplete: () => movingToTarget = false,
            onFailed:   () => movingToTarget = false);
    }

    private void StopApproach()
    {
        if (movingToTarget && movement != null)
        {
            movement.StopMoving();
            movingToTarget = false;
        }
    }

    private void ReturnToAnchorIfNeeded()
    {
        if (stance != CombatStance.Guard && stance != CombatStance.Kiting) return;
        if (movingToTarget) return;
        if (Vector2.Distance(transform.position, anchor) > 1.5f)
            Approach(anchor);
    }

    #endregion
}
