using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 침대 (간이 침낭·기본 침대·고급 침대) — 주인 한 명만 잡니다.
///
/// 주인은 플레이어가 침대를 클릭해 연 <see cref="BedAssignUI"/>에서 직접 정합니다 (자동 배정 없음).
/// 직원 하나는 침대 하나만 가질 수 있습니다 — 다른 침대에 배정하면 예전 침대는 비워집니다.
/// 주인 없는 직원·침대까지 못 가는 직원은 제자리에서 잡니다 (EmployeeAI, '바닥에서 잠' 디버프).
/// </summary>
[RequireComponent(typeof(Building))]
public class SleepingBed : MonoBehaviour, IBuildingExtraSerializable
{
    /// <summary>맵에 있는 침대 전체</summary>
    public static readonly List<SleepingBed> All = new List<SleepingBed>();

    [Tooltip("잘 때 피로 회복 배율 (바닥 = EmployeeAI의 floorRecovery)")]
    [SerializeField, Min(0.1f)] private float recoveryMultiplier = 1f;

    [Tooltip("누울 위치 (건물 좌측 하단 기준)")]
    [SerializeField] private Vector2 sleepOffset = new Vector2(0.5f, 0f);

    private int ownerId;
    private Employee owner;

    public float RecoveryMultiplier => recoveryMultiplier;
    public Vector3 SleepPosition => transform.position + (Vector3)sleepOffset;

    public string BedName
        => TryGetComponent(out Building b) && b.buildingData != null ? b.buildingData.buildingName : name;

    /// <summary>주인 — 로드 직후엔 ID만 있으므로 처음 물을 때 직원을 찾아 잇는다</summary>
    public Employee Owner
    {
        get
        {
            if (owner == null && ownerId != 0 && EmployeeManager.instance != null)
                owner = EmployeeManager.instance.AllEmployees.Find(e => e != null && e.InstanceId == ownerId);
            if (owner != null && owner.State == EmployeeState.Dead) SetOwner(null);
            return owner;
        }
    }

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    /// <summary>주인을 정합니다 (null = 비우기). 그 직원의 예전 침대는 비워집니다.</summary>
    public void SetOwner(Employee employee)
    {
        if (employee != null)
            foreach (var bed in All)
                if (bed != this && bed.Owner == employee) bed.SetOwner(null);

        owner = employee;
        ownerId = employee != null ? employee.InstanceId : 0;
    }

    /// <summary>이 직원의 침대 (없으면 null)</summary>
    public static SleepingBed FindOwnedBy(Employee employee)
    {
        if (employee == null) return null;
        foreach (var bed in All)
            if (bed != null && bed.Owner == employee) return bed;
        return null;
    }

    private void OnMouseDown()
    {
        if (UIManager.PointerOverUI || UIManager.instance == null) return;
        if (InteractionManager.instance != null &&
            InteractionManager.instance.GetCurrentMode() != InteractionManager.InteractMode.Normal) return;

        BedAssignUI.Open(this);
    }

    #region 세이브

    [System.Serializable]
    private class SaveState { public int ownerId; }

    public string SerializeExtra() => JsonUtility.ToJson(new SaveState { ownerId = ownerId });

    public void DeserializeExtra(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var s = JsonUtility.FromJson<SaveState>(json);
        ownerId = s != null ? s.ownerId : 0; // 직원은 건물보다 늦게 복원되므로 Owner가 처음 불릴 때 찾는다
        owner = null;
    }

    #endregion
}
