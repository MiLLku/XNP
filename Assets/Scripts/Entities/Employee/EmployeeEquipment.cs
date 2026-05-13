using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 직원 장비 관리 서브 컴포넌트.
/// 4개 슬롯(슈트, 헬멧, 무기, 다용도구)에 일반 장비 또는 장비형 제노프스를 장착합니다.
///
/// 장비 효과 3계층:
///   1. EquipmentStatModifier — 스탯 보정 (체력, 정신력, 속도 등)
///   2. EquipmentPassiveEffects — 패시브 특성 (야간작업, 피해감소 등)
///   3. EquipmentAbility — 액티브/트리거 기반 스킬
/// </summary>
public class EmployeeEquipment : MonoBehaviour
{
    #region 필드

    private Employee employee;
    private EmployeeStatsController statsController;

    /// <summary>슬롯별 장비 출처 타입</summary>
    private Dictionary<EquipmentSlot, EquipmentSourceType> slotTypes;

    /// <summary>일반 장비 슬롯</summary>
    private Dictionary<EquipmentSlot, EquipmentData> itemSlots;

    /// <summary>제노프스 장비 슬롯</summary>
    private Dictionary<EquipmentSlot, Xenops> xenopsSlots;

    /// <summary>능력 쿨다운 추적 (abilityName → 남은 쿨다운)</summary>
    private Dictionary<string, float> abilityCooldowns;

    #endregion

    #region 초기화

    void Awake()
    {
        employee = GetComponent<Employee>();
        statsController = GetComponent<EmployeeStatsController>();

        slotTypes = new Dictionary<EquipmentSlot, EquipmentSourceType>();
        itemSlots = new Dictionary<EquipmentSlot, EquipmentData>();
        xenopsSlots = new Dictionary<EquipmentSlot, Xenops>();
        abilityCooldowns = new Dictionary<string, float>();

        // 모든 슬롯을 빈 상태로 초기화
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            slotTypes[slot] = EquipmentSourceType.None;
        }
    }

    void Update()
    {
        UpdateAbilityCooldowns();
        UpdatePeriodicAbilities();
    }

    #endregion

    #region 장착/해제 — 일반 장비

    /// <summary>
    /// 일반 장비를 장착합니다.
    /// </summary>
    /// <returns>장착 성공 여부</returns>
    public bool EquipItem(EquipmentSlot slot, EquipmentData data)
    {
        if (data == null) return false;
        if (data.slot != slot) return false;

        // 기존 장비 해제
        if (!IsSlotEmpty(slot))
        {
            if (GetSlotType(slot) == EquipmentSourceType.Item)
                UnequipItem(slot);
            else if (GetSlotType(slot) == EquipmentSourceType.Xenops)
                UnequipXenops(slot);
        }

        itemSlots[slot] = data;
        slotTypes[slot] = EquipmentSourceType.Item;

        // 스탯 보정 적용
        if (!data.statModifier.IsEmpty)
        {
            statsController?.AddEquipmentModifier(slot, data.statModifier);
        }

        Debug.Log($"[EmployeeEquipment] {employee?.DisplayName}: {data.itemData?.itemName} 장착 ({slot})");
        return true;
    }

    /// <summary>
    /// 일반 장비를 해제합니다.
    /// </summary>
    /// <returns>해제된 장비 데이터 (없으면 null)</returns>
    public EquipmentData UnequipItem(EquipmentSlot slot)
    {
        if (GetSlotType(slot) != EquipmentSourceType.Item) return null;
        if (!itemSlots.TryGetValue(slot, out var data)) return null;

        // 스탯 보정 제거
        statsController?.RemoveEquipmentModifier(slot);

        // 쿨다운 정리
        if (data.abilities != null)
        {
            foreach (var ability in data.abilities)
            {
                abilityCooldowns.Remove(ability.abilityName);
            }
        }

        itemSlots.Remove(slot);
        slotTypes[slot] = EquipmentSourceType.None;

        Debug.Log($"[EmployeeEquipment] {employee?.DisplayName}: {data.itemData?.itemName} 해제 ({slot})");
        return data;
    }

    #endregion

    #region 장착/해제 — 제노프스

    /// <summary>
    /// 장비형 제노프스를 장착합니다.
    /// </summary>
    /// <returns>장착 성공 여부</returns>
    public bool EquipXenops(EquipmentSlot slot, Xenops xenops)
    {
        if (xenops == null) return false;
        if (xenops.Data == null || xenops.Data.xenopsType != XenopsType.Equipment) return false;

        // 기존 장비 해제
        if (!IsSlotEmpty(slot))
        {
            if (GetSlotType(slot) == EquipmentSourceType.Item)
                UnequipItem(slot);
            else if (GetSlotType(slot) == EquipmentSourceType.Xenops)
                UnequipXenops(slot);
        }

        xenopsSlots[slot] = xenops;
        slotTypes[slot] = EquipmentSourceType.Xenops;

        // Xenops 측에 장착 통보 (상태 변경 + EquipmentBehavior 활성화)
        xenops.EquipTo(employee);

        Debug.Log($"[EmployeeEquipment] {employee?.DisplayName}: {xenops.DisplayName} 제노프스 장착 ({slot})");
        return true;
    }

    /// <summary>
    /// 장비형 제노프스를 해제합니다.
    /// </summary>
    /// <returns>해제된 제노프스 (없으면 null)</returns>
    public Xenops UnequipXenops(EquipmentSlot slot)
    {
        if (GetSlotType(slot) != EquipmentSourceType.Xenops) return null;
        if (!xenopsSlots.TryGetValue(slot, out var xenops)) return null;

        // 스탯 보정 제거 (EquipmentBehavior에서 AddEquipmentModifier 했을 수 있음)
        statsController?.RemoveEquipmentModifier(slot);

        // Xenops 측에 해제 통보
        xenops.Unequip();

        xenopsSlots.Remove(slot);
        slotTypes[slot] = EquipmentSourceType.None;

        Debug.Log($"[EmployeeEquipment] {employee?.DisplayName}: {xenops.DisplayName} 제노프스 해제 ({slot})");
        return xenops;
    }

    #endregion

    #region 조회

    /// <summary>슬롯이 비어있는지 확인</summary>
    public bool IsSlotEmpty(EquipmentSlot slot)
    {
        return !slotTypes.ContainsKey(slot) || slotTypes[slot] == EquipmentSourceType.None;
    }

    /// <summary>슬롯의 장비 출처 타입 반환</summary>
    public EquipmentSourceType GetSlotType(EquipmentSlot slot)
    {
        return slotTypes.TryGetValue(slot, out var type) ? type : EquipmentSourceType.None;
    }

    /// <summary>슬롯의 일반 장비 반환</summary>
    public EquipmentData GetItemInSlot(EquipmentSlot slot)
    {
        return itemSlots.TryGetValue(slot, out var data) ? data : null;
    }

    /// <summary>슬롯의 제노프스 반환</summary>
    public Xenops GetXenopsInSlot(EquipmentSlot slot)
    {
        return xenopsSlots.TryGetValue(slot, out var xenops) ? xenops : null;
    }

    #endregion

    #region 패시브 합산 조회

    /// <summary>야간 작업 가능 여부 (장비 중 하나라도 enableNightWork이면 true)</summary>
    public bool CanWorkAtNight()
    {
        foreach (var kvp in itemSlots)
        {
            if (kvp.Value.passiveEffects != null && kvp.Value.passiveEffects.enableNightWork)
                return true;
        }
        return false;
    }

    /// <summary>비 보호 여부</summary>
    public bool HasRainProtection()
    {
        foreach (var kvp in itemSlots)
        {
            if (kvp.Value.passiveEffects != null && kvp.Value.passiveEffects.rainProtection)
                return true;
        }
        return false;
    }

    /// <summary>전체 장비의 피해 감소 합산 (%)</summary>
    public float GetTotalDamageReduction()
    {
        float total = 0f;
        foreach (var kvp in itemSlots)
        {
            if (kvp.Value.passiveEffects != null)
                total += kvp.Value.passiveEffects.damageReduction;
        }
        return Mathf.Clamp(total, 0f, 100f);
    }

    /// <summary>
    /// 전체 장비의 침식 오라 무시 수치 합산.
    /// HostileErosionAura에서 개체의 aoeErosionPerSecond와 비교합니다.
    /// </summary>
    public float GetTotalErosionIgnore()
    {
        float total = 0f;
        foreach (var kvp in itemSlots)
        {
            if (kvp.Value.passiveEffects != null)
                total += kvp.Value.passiveEffects.erosionIgnoreValue;
        }
        return total;
    }

    /// <summary>전체 장비의 추가 산출 확률 합산</summary>
    public float GetTotalBonusYieldChance()
    {
        float total = 0f;
        foreach (var kvp in itemSlots)
        {
            if (kvp.Value.passiveEffects != null)
                total += kvp.Value.passiveEffects.bonusYieldChance;
        }
        return Mathf.Clamp01(total);
    }

    /// <summary>특정 작업 타입에 대한 장비 속도 보정 합산 (%)</summary>
    public float GetWorkTypeSpeedModifier(WorkType type)
    {
        float total = 0f;
        foreach (var kvp in itemSlots)
        {
            if (kvp.Value.passiveEffects?.workTypeModifiers == null) continue;
            foreach (var mod in kvp.Value.passiveEffects.workTypeModifiers)
            {
                if (mod.workType == type)
                    total += mod.speedModifier;
            }
        }
        return total;
    }

    #endregion

    #region 능력 시스템

    /// <summary>
    /// 특정 트리거 조건에 해당하는 모든 능력을 발동합니다.
    /// </summary>
    public void TriggerAbilities(AbilityTriggerType trigger)
    {
        foreach (var kvp in itemSlots)
        {
            if (kvp.Value.abilities == null) continue;
            foreach (var ability in kvp.Value.abilities)
            {
                if (ability.triggerType != trigger) continue;
                if (trigger == AbilityTriggerType.Active) continue; // Active는 수동 발동만

                // 쿨다운 체크
                if (abilityCooldowns.TryGetValue(ability.abilityName, out float remaining) && remaining > 0f)
                    continue;

                // 발동 확률 체크
                if (ability.triggerChance < 1f && UnityEngine.Random.value > ability.triggerChance)
                    continue;

                ExecuteAbility(ability);
                abilityCooldowns[ability.abilityName] = ability.cooldown;
            }
        }
    }

    /// <summary>
    /// 능력을 수동으로 발동합니다 (Active 타입).
    /// </summary>
    /// <returns>발동 성공 여부</returns>
    public bool ActivateAbility(string abilityName)
    {
        foreach (var kvp in itemSlots)
        {
            if (kvp.Value.abilities == null) continue;
            foreach (var ability in kvp.Value.abilities)
            {
                if (ability.abilityName != abilityName) continue;
                if (ability.triggerType != AbilityTriggerType.Active) continue;

                // 쿨다운 체크
                if (abilityCooldowns.TryGetValue(abilityName, out float remaining) && remaining > 0f)
                    return false;

                ExecuteAbility(ability);
                abilityCooldowns[abilityName] = ability.cooldown;
                return true;
            }
        }
        return false;
    }

    /// <summary>UI용 전체 능력 목록 반환</summary>
    public List<EquipmentAbility> GetAllAbilities()
    {
        var result = new List<EquipmentAbility>();
        foreach (var kvp in itemSlots)
        {
            if (kvp.Value.abilities != null)
                result.AddRange(kvp.Value.abilities);
        }
        return result;
    }

    /// <summary>능력의 남은 쿨다운 반환</summary>
    public float GetAbilityCooldown(string abilityName)
    {
        return abilityCooldowns.TryGetValue(abilityName, out float remaining) ? Mathf.Max(0f, remaining) : 0f;
    }

    /// <summary>쿨다운 갱신</summary>
    private void UpdateAbilityCooldowns()
    {
        if (abilityCooldowns.Count == 0) return;

        var keys = new List<string>(abilityCooldowns.Keys);
        foreach (var key in keys)
        {
            abilityCooldowns[key] -= Time.deltaTime;
            if (abilityCooldowns[key] <= 0f)
            {
                abilityCooldowns[key] = 0f;
            }
        }
    }

    /// <summary>주기적 능력 자동 발동</summary>
    private void UpdatePeriodicAbilities()
    {
        TriggerAbilities(AbilityTriggerType.Periodic);
    }

    #endregion

    #region 능력 효과 실행

    /// <summary>
    /// 능력 효과를 실행합니다.
    /// </summary>
    private void ExecuteAbility(EquipmentAbility ability)
    {
        switch (ability.effectType)
        {
            case AbilityEffectType.AoEDamage:
                ExecuteAoEDamage(ability);
                break;
            case AbilityEffectType.TemporaryShield:
                ExecuteTemporaryShield(ability);
                break;
            case AbilityEffectType.Heal:
                ExecuteHeal(ability);
                break;
            case AbilityEffectType.SpeedBurst:
                ExecuteSpeedBurst(ability);
                break;
            case AbilityEffectType.ResourceDuplicate:
                ExecuteResourceDuplicate(ability);
                break;
            case AbilityEffectType.Stun:
                ExecuteStun(ability);
                break;
            case AbilityEffectType.Regeneration:
                ExecuteRegeneration(ability);
                break;
        }

        Debug.Log($"[EmployeeEquipment] {employee?.DisplayName}: {ability.abilityName} 발동! ({ability.effectType})");
    }

    /// <summary>범위 피해</summary>
    private void ExecuteAoEDamage(EquipmentAbility ability)
    {
        if (employee == null) return;

        Vector3 center = employee.transform.position;
        float radius = ability.effectRadius;
        float damage = ability.effectValue;

        // 범위 내 적대 Xenops에 피해
        if (XenopsManager.instance != null)
        {
            foreach (var xenops in XenopsManager.instance.GetXenopsByType(XenopsType.Hostile))
            {
                if (xenops == null || xenops.State == XenopsState.Subdued) continue;
                if (Vector3.Distance(center, xenops.transform.position) <= radius)
                {
                    // Hostile에 직접 피해 (제압 판정은 별도)
                    Debug.Log($"[AoEDamage] {xenops.DisplayName}에 {damage} 피해");
                }
            }
        }
    }

    /// <summary>일시적 보호막</summary>
    private void ExecuteTemporaryShield(EquipmentAbility ability)
    {
        // TODO: 보호막 시스템과 연동
        // ability.effectValue = 보호막 HP, ability.duration = 지속시간
        Debug.Log($"[TemporaryShield] {ability.effectValue} HP 보호막, {ability.duration}초 지속");
    }

    /// <summary>체력/정신력 회복</summary>
    private void ExecuteHeal(EquipmentAbility ability)
    {
        if (employee == null) return;

        // effectValue > 0이면 체력 회복, effectRadius > 0이면 범위 회복
        if (ability.effectRadius > 0f && EmployeeManager.instance != null)
        {
            Vector3 center = employee.transform.position;
            foreach (var emp in EmployeeManager.instance.AllEmployees)
            {
                if (emp == null || emp.State == EmployeeState.Dead) continue;
                if (Vector3.Distance(center, emp.transform.position) <= ability.effectRadius)
                {
                    emp.ModifyHealth(ability.effectValue);
                }
            }
        }
        else
        {
            employee.ModifyHealth(ability.effectValue);
        }
    }

    /// <summary>일시적 속도 증가</summary>
    private void ExecuteSpeedBurst(EquipmentAbility ability)
    {
        // TODO: 일시적 버프 시스템과 연동
        // ability.effectValue = 속도 증가 %, ability.duration = 지속시간
        Debug.Log($"[SpeedBurst] {ability.effectValue}% 속도 증가, {ability.duration}초 지속");
    }

    /// <summary>자원 2배 산출</summary>
    private void ExecuteResourceDuplicate(EquipmentAbility ability)
    {
        // TODO: 자원 산출 시스템과 연동
        // 다음 자원 산출 시 2배로 적용
        Debug.Log($"[ResourceDuplicate] 자원 2배 산출 효과 적용");
    }

    /// <summary>범위 내 적대 Xenops 기절</summary>
    private void ExecuteStun(EquipmentAbility ability)
    {
        if (employee == null) return;

        Vector3 center = employee.transform.position;
        float radius = ability.effectRadius;

        if (XenopsManager.instance != null)
        {
            foreach (var xenops in XenopsManager.instance.GetXenopsByType(XenopsType.Hostile))
            {
                if (xenops == null || xenops.State == XenopsState.Subdued) continue;
                if (Vector3.Distance(center, xenops.transform.position) <= radius)
                {
                    // TODO: Xenops에 기절 상태 추가
                    Debug.Log($"[Stun] {xenops.DisplayName} 기절 ({ability.duration}초)");
                }
            }
        }
    }

    /// <summary>지속 회복</summary>
    private void ExecuteRegeneration(EquipmentAbility ability)
    {
        if (employee == null) return;

        // effectValue = 초당 회복량, duration = 지속시간
        // TODO: 지속 효과 시스템과 연동
        employee.ModifyHealth(ability.effectValue * Time.deltaTime);
    }

    #endregion

    #region 저장/복원

    /// <summary>
    /// 저장 데이터에 장비 정보를 기록합니다.
    /// </summary>
    public void PopulateSaveData(EmployeeSaveData data)
    {
        data.equippedItems = new List<EquipmentSlotSaveData>();

        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            var slotType = GetSlotType(slot);
            if (slotType == EquipmentSourceType.None) continue;

            var slotData = new EquipmentSlotSaveData
            {
                slot = (int)slot,
                sourceType = (int)slotType
            };

            if (slotType == EquipmentSourceType.Item)
            {
                var item = GetItemInSlot(slot);
                slotData.itemId = item?.itemData?.itemID ?? 0;
            }
            else if (slotType == EquipmentSourceType.Xenops)
            {
                var xenops = GetXenopsInSlot(slot);
                slotData.xenopsInstanceId = xenops?.InstanceId ?? 0;
            }

            data.equippedItems.Add(slotData);
        }
    }

    /// <summary>
    /// 저장 데이터에서 장비를 복원합니다 (아이템만, 제노프스는 PostRestore에서).
    /// </summary>
    public void RestoreFromSaveData(EmployeeSaveData data)
    {
        if (data.equippedItems == null) return;

        foreach (var slotData in data.equippedItems)
        {
            var slot = (EquipmentSlot)slotData.slot;
            var sourceType = (EquipmentSourceType)slotData.sourceType;

            if (sourceType == EquipmentSourceType.Item && slotData.itemId > 0)
            {
                // GameDatabase에서 EquipmentData 조회
                if (GameDatabase.Instance != null)
                {
                    var equipData = GameDatabase.Instance.GetEquipmentData(slotData.itemId);
                    if (equipData != null)
                    {
                        EquipItem(slot, equipData);
                    }
                    else
                    {
                        Debug.LogWarning($"[EmployeeEquipment] 장비 복원 실패: ItemID {slotData.itemId}");
                    }
                }
            }
            // Xenops는 ResolveEquipmentReferences에서 처리
        }
    }

    /// <summary>
    /// 제노프스 장비 크로스레퍼런스를 복원합니다 (PostRestore 단계).
    /// </summary>
    public void ResolveEquipmentReferences(EmployeeSaveData data)
    {
        if (data.equippedItems == null) return;
        if (RuntimeIDRegistry.instance == null) return;

        foreach (var slotData in data.equippedItems)
        {
            var sourceType = (EquipmentSourceType)slotData.sourceType;
            if (sourceType != EquipmentSourceType.Xenops) continue;
            if (slotData.xenopsInstanceId <= 0) continue;

            var slot = (EquipmentSlot)slotData.slot;
            var xenops = RuntimeIDRegistry.instance.ResolveComponent<Xenops>(slotData.xenopsInstanceId);

            if (xenops != null)
            {
                EquipXenops(slot, xenops);
            }
            else
            {
                Debug.LogWarning($"[EmployeeEquipment] 제노프스 장비 복원 실패: InstanceID {slotData.xenopsInstanceId}");
            }
        }
    }

    #endregion
}
