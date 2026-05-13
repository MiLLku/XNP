using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제노프스 효과 적용 서브컴포넌트.
/// XenopsData의 benefits/drawbacks를 해석도에 따라 계산하고,
/// 대상(직원, 건물, 타일 등)에 적용합니다.
/// </summary>
public class XenopsEffectController : MonoBehaviour
{
    #region 필드

    /// <summary>코디네이터 참조</summary>
    private Xenops xenops;

    /// <summary>데이터 템플릿 참조</summary>
    private XenopsData data;

    /// <summary>효과 적용 간격 (초)</summary>
    private const float EFFECT_TICK_INTERVAL = 1f;

    /// <summary>다음 효과 적용까지 남은 시간</summary>
    private float effectTickTimer;

    /// <summary>초기화 완료 여부</summary>
    private bool _initialized = false;

    #endregion

    #region 초기화

    void Awake()
    {
        xenops = GetComponent<Xenops>();
    }

    /// <summary>
    /// 효과 컨트롤러를 초기화합니다.
    /// </summary>
    /// <param name="xenopsData">제노프스 데이터 템플릿</param>
    public void Initialize(XenopsData xenopsData)
    {
        data = xenopsData;
        effectTickTimer = 0f;
        _initialized = true;
    }

    #endregion

    #region 효과 적용

    void Update()
    {
        if (!_initialized || data == null) return;
        if (xenops == null || xenops.State == XenopsState.Idle || xenops.State == XenopsState.Dormant) return;

        effectTickTimer -= Time.deltaTime;
        if (effectTickTimer <= 0f)
        {
            effectTickTimer = EFFECT_TICK_INTERVAL;
            ApplyAllEffects();
        }
    }

    /// <summary>
    /// 모든 이점/해악 효과를 현재 해석도 기준으로 적용합니다.
    /// </summary>
    private void ApplyAllEffects()
    {
        float ratio = xenops.InterpretationRatio;

        // 이로운 효과 적용
        foreach (var effect in data.benefits)
        {
            ApplyEffect(effect, ratio);
        }

        // 해로운 효과 적용
        foreach (var effect in data.drawbacks)
        {
            ApplyEffect(effect, ratio);
        }
    }

    /// <summary>
    /// 개별 효과를 적용합니다.
    /// 실제 대상 탐색 및 효과 적용 로직.
    /// </summary>
    private void ApplyEffect(XenopsEffect effect, float interpretationRatio)
    {
        float value = effect.GetCurrentValue(interpretationRatio);

        switch (effect.target)
        {
            case XenopsEffectTarget.EquippedEmployee:
                ApplyToEquippedEmployee(effect.effectType, value, effect.targetId);
                break;

            case XenopsEffectTarget.NearbyEmployees:
                ApplyToNearbyEmployees(effect.effectType, value, effect.targetId);
                break;

            case XenopsEffectTarget.AllEmployees:
                ApplyToAllEmployees(effect.effectType, value, effect.targetId);
                break;

            case XenopsEffectTarget.TargetBuilding:
                ApplyToTargetBuilding(effect.effectType, value, effect.targetId);
                break;

            case XenopsEffectTarget.NearbyBuildings:
                ApplyToNearbyBuildings(effect.effectType, value, effect.targetId);
                break;

            case XenopsEffectTarget.NearbyTiles:
                // 타일 효과는 타입별 Behavior에서 처리
                break;

            case XenopsEffectTarget.Global:
                ApplyToAllEmployees(effect.effectType, value, effect.targetId);
                break;
        }
    }

    #endregion

    #region 대상별 적용

    private void ApplyToEquippedEmployee(XenopsEffectType type, float value, int targetId)
    {
        var employee = xenops.EquippedEmployee;
        if (employee == null) return;

        ApplyEffectToEmployee(employee, type, value, targetId);
    }

    private void ApplyToNearbyEmployees(XenopsEffectType type, float value, int targetId)
    {
        if (EmployeeManager.instance == null) return;

        float radius = data.effectRadius;
        Vector3 pos = transform.position;

        foreach (var employee in EmployeeManager.instance.AllEmployees)
        {
            if (employee == null) continue;
            if (Vector3.Distance(pos, employee.transform.position) <= radius)
            {
                ApplyEffectToEmployee(employee, type, value, targetId);
            }
        }
    }

    private void ApplyToAllEmployees(XenopsEffectType type, float value, int targetId)
    {
        if (EmployeeManager.instance == null) return;

        foreach (var employee in EmployeeManager.instance.AllEmployees)
        {
            if (employee == null) continue;
            ApplyEffectToEmployee(employee, type, value, targetId);
        }
    }

    private void ApplyToTargetBuilding(XenopsEffectType type, float value, int targetId)
    {
        var building = xenops.InfiltratedBuilding;
        if (building == null) return;

        ApplyEffectToBuilding(building, type, value);
    }

    private void ApplyToNearbyBuildings(XenopsEffectType type, float value, int targetId)
    {
        // 범위 내 건물 탐색 — 물리 오버랩 사용
        float radius = data.effectRadius;
        var colliders = Physics2D.OverlapCircleAll(transform.position, radius);

        foreach (var col in colliders)
        {
            var building = col.GetComponent<Building>();
            if (building != null)
            {
                ApplyEffectToBuilding(building, type, value);
            }
        }
    }

    #endregion

    #region 효과 타입별 적용

    /// <summary>
    /// 직원에게 효과를 적용합니다.
    /// </summary>
    private void ApplyEffectToEmployee(Employee employee, XenopsEffectType type, float value, int targetId)
    {
        switch (type)
        {
            case XenopsEffectType.ModifyHealth:
                employee.ModifyHealth(value * Time.deltaTime);
                break;

            case XenopsEffectType.ModifyMental:
                employee.ModifyMental(value * Time.deltaTime);
                break;

            case XenopsEffectType.ModifyHungerRate:
                // 배고픔 감소 속도 보정 — 추후 Employee에 modifier 시스템 연동
                employee.ModifyHunger(value * Time.deltaTime);
                break;

            case XenopsEffectType.ModifyFatigueRate:
                // 피로 증가 속도 보정
                employee.ModifyFatigue(value * Time.deltaTime);
                break;

            // ModifyWorkSpeed, ModifyAttackPower 등은
            // 직접 스탯을 변경하기보다 modifier로 적용하는 것이 이상적.
            // 현재는 구조만 준비하고, 추후 modifier 시스템 확장 시 연동.
            case XenopsEffectType.ModifyWorkSpeed:
            case XenopsEffectType.ModifyAttackPower:
            case XenopsEffectType.GrantWorkAbility:
            case XenopsEffectType.BlockWorkType:
                // TODO: Employee modifier 시스템과 연동
                break;
        }
    }

    /// <summary>
    /// 건물에 효과를 적용합니다.
    /// </summary>
    private void ApplyEffectToBuilding(Building building, XenopsEffectType type, float value)
    {
        switch (type)
        {
            case XenopsEffectType.CorrodeBuildingHealth:
                // 건물 체력 부식 — Building의 체력 시스템과 연동
                // TODO: Building.ModifyHealth() 메서드와 연동
                break;

            case XenopsEffectType.ModifyProductionSpeed:
                // 생산 속도 보정 — ProductionBuilding과 연동
                // TODO: ProductionBuilding modifier 시스템과 연동
                break;
        }
    }

    #endregion

    #region 조회 (UI용)

    /// <summary>
    /// 현재 해석도 기준으로 모든 효과의 요약을 반환합니다.
    /// </summary>
    public List<XenopsEffectSummary> GetEffectSummary()
    {
        var summary = new List<XenopsEffectSummary>();
        if (data == null) return summary;

        float ratio = xenops != null ? xenops.InterpretationRatio : 0f;

        foreach (var effect in data.benefits)
        {
            summary.Add(new XenopsEffectSummary
            {
                effectName = effect.effectName,
                currentValue = effect.GetCurrentValue(ratio),
                isBenefit = true,
                effectType = effect.effectType
            });
        }

        foreach (var effect in data.drawbacks)
        {
            summary.Add(new XenopsEffectSummary
            {
                effectName = effect.effectName,
                currentValue = effect.GetCurrentValue(ratio),
                isBenefit = false,
                effectType = effect.effectType
            });
        }

        return summary;
    }

    #endregion
}

/// <summary>
/// 효과 요약 데이터 (UI 표시용).
/// </summary>
public struct XenopsEffectSummary
{
    public string effectName;
    public float currentValue;
    public bool isBenefit;
    public XenopsEffectType effectType;
}
