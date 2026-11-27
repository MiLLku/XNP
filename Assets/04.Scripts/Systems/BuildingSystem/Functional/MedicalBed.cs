using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 의료 침대 — 주인이 없는 침대. 쓰러진 직원을 '치료' 담당이 여기로 옮겨 소생시킨다 (사용자 결정).
/// 한 번에 한 명만 눕는다 — 옮기는 동안 자리를 잡아 둔다.
/// (본인 침대가 있으면 그 침대로도 옮길 수 있다 — <see cref="RescueOrder"/>)
/// </summary>
[RequireComponent(typeof(Building))]
public class MedicalBed : MonoBehaviour
{
    public static readonly List<MedicalBed> All = new List<MedicalBed>();

    [Tooltip("누울 위치 (건물 좌측 하단 기준)")]
    [SerializeField] private Vector2 lieOffset = new Vector2(1f, 0f);

    private Employee occupant;

    public Vector3 LiePosition => transform.position + (Vector3)lieOffset;

    /// <summary>지금 자리를 잡은 환자 (죽었거나 깨어나 떠났으면 비운다)</summary>
    public Employee Occupant
    {
        get
        {
            if (occupant != null && (occupant.State == EmployeeState.Dead || !occupant.IsDowned)) occupant = null;
            return occupant;
        }
    }

    public bool IsOperating => TryGetComponent(out Building b) && b.IsFunctional;

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    public bool TryReserve(Employee patient)
    {
        if (patient == null || !IsOperating) return false;
        var cur = Occupant;
        if (cur != null && cur != patient) return false;
        occupant = patient;
        return true;
    }

    public void Release(Employee patient)
    {
        if (occupant == patient) occupant = null;
    }
}
