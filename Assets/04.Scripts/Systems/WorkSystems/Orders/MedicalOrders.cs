using UnityEngine;

/// <summary>
/// 소생 — 쓰러진 직원을 침대(본인 침대 또는 의료 침대)로 옮겨 소생시킨다 ('치료' 작업, 사용자 결정).
/// 실제 흐름(가서 들기 → 침대로 옮기기 → 소생)은 EmployeeWork.RescueWorkAsync가 한다.
/// 저장하지 않는다 — 불러온 뒤 MedicalManager가 다시 건다.
/// </summary>
public class RescueOrder : IWorkTarget, IExpiringWorkTarget
{
    /// <summary>소생에 걸리는 작업량(초, 작업 속도 1 기준) — 임시 값</summary>
    public const float RESUSCITATE_WORK = 20f;

    public readonly Employee patient;
    private bool completed;

    public RescueOrder(Employee patient) { this.patient = patient; }

    public bool IsExpired => completed || patient == null || !patient.IsDowned;

    public Vector3 GetWorkPosition() => patient != null ? patient.transform.position : Vector3.zero;
    public WorkType GetWorkType() => WorkType.Treatment;
    public float GetWorkTime() => RESUSCITATE_WORK;

    /// <summary>쓰러져 있고, 누일 침대가 있어야 한다 (없으면 침대가 생길 때까지 기다린다)</summary>
    public bool IsWorkAvailable() => !IsExpired && FindBed(patient, out _, out _);

    public void CompleteWork(Employee worker) { completed = true; }
    public void CancelWork(Employee worker) { }

    /// <summary>
    /// 환자를 누일 침대 — 본인 침대가 먼저, 없으면 비어 있는 의료 침대 (가장 가까운 것).
    /// </summary>
    public static bool FindBed(Employee patient, out Vector3 position, out MedicalBed medical)
    {
        position = default;
        medical = null;
        if (patient == null) return false;

        var own = SleepingBed.FindOwnedBy(patient);
        if (own != null && own.TryGetComponent(out Building b) && b.IsFunctional)
        {
            position = own.SleepPosition;
            return true;
        }

        float best = float.MaxValue;
        foreach (var bed in MedicalBed.All)
        {
            if (bed == null || !bed.IsOperating) continue;
            var occ = bed.Occupant;
            if (occ != null && occ != patient) continue;
            float d = Vector2.Distance(bed.LiePosition, patient.transform.position);
            if (d < best) { best = d; medical = bed; position = bed.LiePosition; }
        }
        return medical != null;
    }
}

/// <summary>
/// 치료 — 요양 중인 직원 곁에서 돌봐 회복을 빠르게 한다 (사용자 결정, 배율은 EmployeeAI.TREATMENT_HEAL_MULTIPLIER).
/// 흐름은 EmployeeWork.TreatWorkAsync. 저장하지 않는다.
/// </summary>
public class TreatOrder : IWorkTarget, IExpiringWorkTarget
{
    public readonly Employee patient;
    private readonly EmployeeAI patientAI;
    private bool completed;

    public TreatOrder(Employee patient)
    {
        this.patient = patient;
        patientAI = patient != null ? patient.GetComponent<EmployeeAI>() : null;
    }

    public bool PatientNeedsCare =>
        patient != null && !patient.IsIncapacitated && patientAI != null && patientAI.IsRecuperating &&
        patient.Stats.health < patient.Stats.maxHealth;

    public bool IsExpired => completed || !PatientNeedsCare;

    public Vector3 GetWorkPosition() => patient != null ? patient.transform.position : Vector3.zero;
    public WorkType GetWorkType() => WorkType.Treatment;
    public float GetWorkTime() => 1f;
    public bool IsWorkAvailable() => !IsExpired;

    /// <summary>돌보는 동안 매 프레임 — 회복 배율을 켠다</summary>
    public void Tend()
    {
        if (patientAI != null) patientAI.TreatedUntil = Time.time + 0.5f;
    }

    public void CompleteWork(Employee worker) { completed = true; }
    public void CancelWork(Employee worker) { }
}
