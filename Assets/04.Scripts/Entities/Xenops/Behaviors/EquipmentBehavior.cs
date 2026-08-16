using UnityEngine;

/// <summary>
/// 장비형 제노프스 행동.
/// 특정 직원에게 장착되어 스탯을 보정합니다.
///
/// 예시:
///   - 작업 속도 증가 (이점)
///   - 정신력 지속 감소 (해악)
///
/// 해석도는 장착된 직원이 작업을 수행할 때 자동으로 경험치가 쌓입니다.
/// </summary>
public class EquipmentBehavior : MonoBehaviour, IXenopsBehavior
{
    #region 필드

    private Xenops xenops;
    private XenopsData data;
    private bool isActive = false;

    /// <summary>작업 경험치 부여 간격 (초)</summary>
    private const float WORK_EXP_INTERVAL = 10f;
    private float workExpTimer;

    #endregion

    #region IXenopsBehavior 구현

    public XenopsType BehaviorType => XenopsType.Equipment;
    public bool IsActive => isActive;

    public void OnActivated()
    {
        isActive = true;
        workExpTimer = WORK_EXP_INTERVAL;

        // 장착 시 XenopsEffect 기반 스탯 보정 → EquipmentStatModifier 변환 → 적용
        ApplyXenopsStatModifier();

        Debug.Log($"[EquipmentBehavior] {xenops?.DisplayName} 장비 활성화");
    }

    public void OnDeactivated()
    {
        isActive = false;

        // 해제 시 스탯 보정 제거
        RemoveXenopsStatModifier();

        Debug.Log($"[EquipmentBehavior] {xenops?.DisplayName} 장비 비활성화");
    }

    public void UpdateBehavior()
    {
        if (!isActive) return;
        if (xenops == null) return;

        var employee = xenops.EquippedEmployee;
        if (employee == null) return;

        // 장착된 직원이 작업 중일 때 해석 경험치 자동 획득
        if (employee.State == EmployeeState.Working)
        {
            workExpTimer -= Time.deltaTime;
            if (workExpTimer <= 0f)
            {
                workExpTimer = WORK_EXP_INTERVAL;
                GainWorkExperience();
            }
        }

        // 직원 위치 추적 (장비형은 직원을 따라다님)
        FollowEmployee(employee);
    }

    #endregion

    #region 초기화

    void Awake()
    {
        xenops = GetComponent<Xenops>();
    }

    void Start()
    {
        if (xenops != null)
        {
            data = xenops.Data;
        }
    }

    #endregion

    #region 장비 동작

    void Update()
    {
        UpdateBehavior();
    }

    /// <summary>
    /// 직원이 작업 수행 시 해석 경험치를 자동 획득합니다.
    /// </summary>
    private void GainWorkExperience()
    {
        if (xenops == null || data == null) return;

        // 작업 시 자동으로 해석 경험치의 절반만 부여 (직접 상호작용보다 적음)
        int expAmount = Mathf.Max(1, data.expPerInteraction / 2);
        xenops.GainInterpretationExp(expAmount);
    }

    /// <summary>
    /// 장착된 직원의 위치를 따라갑니다.
    /// </summary>
    private void FollowEmployee(Employee employee)
    {
        if (employee == null) return;

        // 직원 위치에 약간의 오프셋을 두고 따라감
        Vector3 targetPos = employee.transform.position + new Vector3(0.3f, 0.3f, 0f);
        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * 10f);
    }

    #endregion

    #region 스탯 보정

    /// <summary>
    /// Xenops 효과(benefits/drawbacks)를 EquipmentStatModifier로 변환하여 직원에 적용합니다.
    /// </summary>
    private void ApplyXenopsStatModifier()
    {
        if (xenops == null || data == null) return;
        var employee = xenops.EquippedEmployee;
        if (employee?.Equipment == null) return;

        var modifier = BuildStatModifier();
        var statsController = employee.GetComponent<EmployeeStatsController>();
        statsController?.AddEquipmentModifier(data.equipmentSlot, modifier);
    }

    /// <summary>
    /// 직원에서 Xenops 스탯 보정을 제거합니다.
    /// </summary>
    private void RemoveXenopsStatModifier()
    {
        if (xenops == null || data == null) return;
        var employee = xenops.EquippedEmployee;
        if (employee == null) return;

        var statsController = employee.GetComponent<EmployeeStatsController>();
        statsController?.RemoveEquipmentModifier(data.equipmentSlot);
    }

    /// <summary>
    /// 현재 해석도 기반으로 EquipmentStatModifier를 빌드합니다.
    /// </summary>
    private EquipmentStatModifier BuildStatModifier()
    {
        var modifier = new EquipmentStatModifier();
        float ratio = xenops != null ? xenops.InterpretationRatio : 0f;

        void ProcessEffects(System.Collections.Generic.List<XenopsEffect> effects)
        {
            if (effects == null) return;
            foreach (var effect in effects)
            {
                if (effect.target != XenopsEffectTarget.EquippedEmployee) continue;
                float value = effect.GetCurrentValue(ratio);

                switch (effect.effectType)
                {
                    case XenopsEffectType.ModifyMaxHealth:
                        modifier.maxHealthModifier += value;
                        break;
                    case XenopsEffectType.ModifyMaxMental:
                        modifier.maxMentalModifier += value;
                        break;
                    case XenopsEffectType.ModifyWorkSpeed:
                        modifier.workSpeedModifier += value;
                        break;
                    case XenopsEffectType.ModifyHungerRate:
                        modifier.hungerRateModifier += value;
                        break;
                    case XenopsEffectType.ModifyFatigueRate:
                        modifier.fatigueRateModifier += value;
                        break;
                    case XenopsEffectType.ModifyAttackPower:
                        modifier.damageBonus += (int)value;
                        break;
                }
            }
        }

        ProcessEffects(data.benefits);
        ProcessEffects(data.drawbacks);

        return modifier;
    }

    /// <summary>
    /// 해석도 레벨업 시 modifier를 재계산합니다.
    /// XenopsInterpretation.OnLevelUp 이벤트에 연결됩니다.
    /// </summary>
    public void RefreshStatModifier()
    {
        if (!isActive) return;
        ApplyXenopsStatModifier();
    }

    #endregion
}
