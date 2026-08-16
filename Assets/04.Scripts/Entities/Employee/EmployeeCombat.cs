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
/// 전투 능력치는 <b>전적으로 무기(EquipmentData)가 보유</b>하고, 직원의 근접/원거리 숙련과
/// 전투 특성이 그 값을 조정한다 (직원 자체는 공격력을 갖지 않는다):
///   데미지   = 무기.baseDamage(+장비 가산) × 숙련 × 특성 × 연구
///   명중률   = 무기.accuracy × 숙련 × 특성   → 빗나가면 데미지 0
///   공격간격 = 무기.attackInterval ÷ 숙련 × 특성
///   사거리   = 무기.attackRange + 특성(특수)
///   관통력   = 무기.penetration + 특성(특수)  → 대상 방어율에서 차감
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
    private EmployeeGrowth growth;
    private EmployeeStatsController stats;

    [SerializeField] private CombatStance stance = CombatStance.Guard;

    /// <summary>경계/점거 기준점 (소집 또는 태세 변경 시 현재 위치)</summary>
    private Vector3 anchor;
    private float scanTimer;
    private float attackTimer;
    private Xenops target;
    private bool movingToTarget;
    private bool projectilePoolReady;

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
        growth    = GetComponent<EmployeeGrowth>();
        stats     = GetComponent<EmployeeStatsController>();

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
                    // 적 반대 방향으로 후퇴 (후퇴 중에도 사격은 계속)
                    float dir = Mathf.Sign(transform.position.x - target.transform.position.x);
                    if (dir == 0f) dir = 1f;
                    Approach(target.transform.position + new Vector3(dir * prefer, 0f, 0f));
                }
                else if (dist <= range) StopApproach();
                else Approach(target.transform.position);

                if (dist <= range) TryAttack(target);
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

    #region 전투 능력치 계산 — 기본값은 무기, 조정은 숙련·특성

    /// <summary>현재 장착 무기 (없으면 null = 맨손).</summary>
    private EquipmentData Weapon
        => equipment != null ? equipment.GetItemInSlot(EquipmentSlot.Weapon) : null;

    /// <summary>현재 무기에 대응하는 숙련 종류. 맨손은 근접.</summary>
    private CombatSkillType CurrentSkill
    {
        get
        {
            var w = Weapon;
            return CombatAptitude.FromWeaponClass(w != null ? w.weaponClass : WeaponClass.Melee);
        }
    }

    /// <summary>현재 무기 숙련 레벨 (1~10).</summary>
    private int CurrentSkillLevel
        => growth != null ? growth.GetCombatLevel(CurrentSkill) : 1;

    /// <summary>
    /// 숙련 레벨을 배율로 환산합니다. Lv.1 = 1.0배, Lv.MAX = (1 + bonusAtMax)배.
    /// 항목(데미지·명중률·공격속도)마다 다른 계수를 쓸 수 있습니다.
    /// </summary>
    private static float SkillFactor(int level, float bonusAtMax)
    {
        int max = CombatAptitude.MAX_LEVEL;
        if (max <= 1) return 1f;

        float t = Mathf.Clamp01((level - 1f) / (max - 1f));
        return 1f + bonusAtMax * t;
    }

    /// <summary>공격 사거리 = 무기 사거리 + 특성 가감.</summary>
    private float GetAttackRange()
    {
        var w = Weapon;
        float baseRange = w != null ? w.attackRange : (Cfg != null ? Cfg.unarmedRange : 1.5f);
        float traitBonus = stats != null ? stats.CachedAttackRangeBonus : 0f;
        return Mathf.Max(0.5f, baseRange + traitBonus);
    }

    /// <summary>공격 간격 = 무기 간격 ÷ 숙련 배율 × 특성 배율. 짧을수록 빠름.</summary>
    private float GetAttackInterval()
    {
        var w = Weapon;
        float baseInterval = w != null ? w.attackInterval : (Cfg != null ? Cfg.unarmedInterval : 1.2f);

        float speedFactor = SkillFactor(CurrentSkillLevel,
            Cfg != null ? Cfg.attackSpeedBonusAtMaxLevel : 0.5f);
        float traitMult = stats != null ? stats.CachedAttackIntervalMult : 1f;

        return Mathf.Max(0.05f, baseInterval / Mathf.Max(0.01f, speedFactor) * traitMult);
    }

    /// <summary>데미지 = 무기 데미지 × 숙련 배율 × 특성 배율 × (1 + 특성 가산%) × (1 + 연구 보너스).</summary>
    private float GetAttackDamage()
    {
        var w = Weapon;
        float baseDamage = w != null ? w.baseDamage : (Cfg != null ? Cfg.unarmedDamage : 3f);

        // 무기가 아닌 장비(장갑·반지 등)의 데미지 가산
        if (equipment != null) baseDamage += equipment.GetTotalDamageBonus();

        float skillFactor = SkillFactor(CurrentSkillLevel,
            Cfg != null ? Cfg.damageBonusAtMaxLevel : 0.5f);

        float traitMult = 1f;
        if (stats != null)
        {
            traitMult = CurrentSkill == CombatSkillType.Ranged
                ? stats.CachedRangedDamageMult
                : stats.CachedMeleeDamageMult;
            traitMult *= 1f + stats.GetTraitDamageModifier();
        }

        var rt = ResearchTreeManager.instance;
        float researchBonus = rt != null ? rt.GetStatBonus(ResearchStatType.EmployeeAttackPowerBonus) : 0f;

        return Mathf.Max(0f, baseDamage * skillFactor * traitMult * (1f + researchBonus));
    }

    /// <summary>명중률 = 무기 명중률 × 숙련 배율 × 특성 배율 (0~1 클램프).</summary>
    private float GetAccuracy()
    {
        var w = Weapon;
        float baseAccuracy = w != null ? w.accuracy : (Cfg != null ? Cfg.unarmedAccuracy : 0.8f);

        float skillFactor = SkillFactor(CurrentSkillLevel,
            Cfg != null ? Cfg.accuracyBonusAtMaxLevel : 0.5f);
        float traitMult = stats != null ? stats.CachedAccuracyMult : 1f;

        return Mathf.Clamp01(baseAccuracy * skillFactor * traitMult);
    }

    /// <summary>관통력 = 무기 관통력 + 특성 가산 (0~1 클램프).</summary>
    private float GetPenetration()
    {
        var w = Weapon;
        float basePen = w != null ? w.penetration : 0f;
        float traitBonus = stats != null ? stats.CachedPenetrationBonus : 0f;
        return Mathf.Clamp01(basePen + traitBonus);
    }

    #endregion

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

        // 명중 판정 — 빗나가면 데미지 0. 근접·원거리 동일하게 '헛친다'로 처리한다.
        bool hitSuccess = Random.value <= GetAccuracy();
        float damage = hitSuccess ? GetAttackDamage() : 0f;

        GainCombatExp(hitSuccess);

        var weapon = Weapon;
        bool ranged = weapon != null && weapon.weaponClass == WeaponClass.Ranged;

        if (ranged && TryFireProjectile(victim, damage, hitSuccess))
        {
            equipment?.NotifyAttackPerformed();           // 무기 내구도는 발사 시점에 소모
            return;                                       // 피해·OnHit 능력은 투사체 명중 시
        }

        // 근접 (원거리인데 투사체 프리팹 미설정이면 즉시 타격 폴백)
        equipment?.NotifyAttackPerformed();               // 무기 내구도 소모

        if (!hitSuccess)
        {
            Debug.Log($"[Combat] {employee.DisplayName}: 공격이 빗나감");
            return;
        }

        health.TakeDamage(damage, GetPenetration());
        equipment?.TriggerAbilities(AbilityTriggerType.OnHit);

        if (health.IsDead) target = null;
    }

    /// <summary>공격 결과에 따라 전투 숙련 경험치를 지급합니다.</summary>
    private void GainCombatExp(bool hitSuccess)
    {
        if (growth == null) return;

        var cfg = Cfg;
        int exp = hitSuccess
            ? (cfg != null ? cfg.expPerHit : 3)
            : (cfg != null ? cfg.expPerMiss : 1);

        if (exp > 0) growth.GainCombatExperience(CurrentSkill, exp);
    }

    /// <summary>
    /// 아군 투사체를 발사합니다. 프리팹/풀 미비 시 false (근접 폴백).
    /// 빗나간 공격도 투사체는 정상적으로 날아가 충돌 시 사라지되 피해를 주지 않습니다.
    /// </summary>
    private bool TryFireProjectile(Xenops victim, float damage, bool hitSuccess)
    {
        var cfg = Cfg;
        var prefab = cfg != null ? cfg.allyProjectilePrefab : null;
        if (prefab == null || PoolManager.instance == null) return false;

        if (!projectilePoolReady)
        {
            PoolManager.instance.RegisterPool(prefab, prewarm: 8, maxSize: 64);
            projectilePoolReady = true;
        }

        // 발밑 타일 콜라이더에 즉시 명중하지 않게 살짝 위에서 발사
        Vector3 origin = transform.position + new Vector3(0f, 0.3f, 0f);
        Vector2 dir = victim.transform.position - origin;

        var proj = PoolManager.instance.Spawn<AllyProjectile>(prefab, origin);
        if (proj == null) return false;

        proj.Init(dir, cfg.allyProjectileSpeed, damage, GetPenetration(), cfg.allyProjectileLifetime,
            hit =>
            {
                if (this == null) return;                 // 발사자 사망/파괴 후 명중
                if (!hitSuccess) return;                  // 빗나간 사격은 OnHit 능력도 발동하지 않는다

                equipment?.TriggerAbilities(AbilityTriggerType.OnHit);
                if (hit == target)
                {
                    var h = hit != null ? hit.GetComponent<XenopsHealth>() : null;
                    if (h == null || h.IsDead) target = null;
                }
            });
        return true;
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

    #region 저장/로드

    /// <summary>EmployeeSaveData에 전투 태세를 기록합니다.</summary>
    public void PopulateSaveData(EmployeeSaveData data)
    {
        data.combatStance = (int)stance;
    }

    /// <summary>EmployeeSaveData에서 전투 태세를 복원합니다 (범위 밖 값은 Guard).</summary>
    public void RestoreFromSaveData(EmployeeSaveData data)
    {
        stance = System.Enum.IsDefined(typeof(CombatStance), data.combatStance)
            ? (CombatStance)data.combatStance
            : CombatStance.Guard;
    }

    #endregion
}
