using System;
using UnityEngine;

/// <summary>
/// 동시 이용 인원이 제한된 시설의 슬롯 점유표 (세척 시설·요양 시설 공용).
///
/// 흐름: TryReserve(예약) → 이동 → MarkArrived(도착) → 이용 → Release(반납)
///
/// 슬롯은 반드시 반납되어야 합니다. AI가 명시적으로 Release를 부르지만,
/// 어떤 경로로든 새는 경우에 대비해 소유 시설이 Update에서 Sweep을 주기적으로 불러
/// 파괴·사망·예약 후 미도착·자리 이탈을 회수합니다. 이게 없으면 시설이 영구 만석으로 굳습니다.
/// </summary>
public class FacilitySlots
{
    /// <summary>예약만 하고 이 시간 안에 도착하지 않으면 슬롯을 회수합니다 (초).</summary>
    private const float RESERVE_TIMEOUT = 90f;

    /// <summary>도착한 직원이 이 거리 밖으로 나가면 반납으로 간주합니다 (타일).</summary>
    private const float ABANDON_DISTANCE = 3f;

    private readonly Employee[] occupants;
    private readonly float[] reservedAt;
    private readonly bool[] arrived;

    public FacilitySlots(int capacity)
    {
        capacity   = Mathf.Max(1, capacity);
        occupants  = new Employee[capacity];
        reservedAt = new float[capacity];
        arrived    = new bool[capacity];
    }

    public int Capacity => occupants.Length;

    /// <summary>현재 슬롯을 잡고 있는 직원 수.</summary>
    public int OccupiedCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < occupants.Length; i++)
                if (occupants[i] != null) count++;
            return count;
        }
    }

    public bool HasFreeSlot => OccupiedCount < occupants.Length;

    public int IndexOf(Employee employee)
    {
        if (employee == null) return -1;
        for (int i = 0; i < occupants.Length; i++)
            if (occupants[i] == employee) return i;
        return -1;
    }

    /// <summary>
    /// 슬롯을 예약합니다. 성공하면 슬롯 인덱스, 만석이면 -1.
    /// 이미 잡고 있으면 그 인덱스를 그대로 돌려줍니다 (멱등).
    /// </summary>
    public int TryReserve(Employee employee)
    {
        if (employee == null) return -1;

        int existing = IndexOf(employee);
        if (existing >= 0) return existing;

        for (int i = 0; i < occupants.Length; i++)
        {
            if (occupants[i] != null) continue;

            occupants[i]  = employee;
            reservedAt[i] = Time.time;
            arrived[i]    = false;
            return i;
        }

        return -1;
    }

    /// <summary>슬롯 위치에 도착했음을 표시합니다 (예약 타임아웃 해제).</summary>
    public void MarkArrived(Employee employee)
    {
        int idx = IndexOf(employee);
        if (idx >= 0) arrived[idx] = true;
    }

    /// <summary>슬롯을 반납합니다. 잡고 있지 않아도 안전합니다 (멱등).</summary>
    public void Release(Employee employee)
    {
        int idx = IndexOf(employee);
        if (idx >= 0) Clear(idx);
    }

    public void ReleaseAll()
    {
        for (int i = 0; i < occupants.Length; i++) Clear(i);
    }

    /// <summary>
    /// 새는 슬롯을 회수합니다 (파괴된 직원 / 사망 / 예약 후 미도착 / 도착 후 이탈).
    /// </summary>
    /// <param name="slotPosition">i번째 슬롯의 월드 좌표 (이탈 판정용)</param>
    public void Sweep(Func<int, Vector3> slotPosition)
    {
        float now = Time.time;
        for (int i = 0; i < occupants.Length; i++)
        {
            Employee emp = occupants[i];
            if (emp == null) { Clear(i); continue; }          // 파괴된 직원 (Unity null 비교)

            if (emp.State == EmployeeState.Dead) { Clear(i); continue; }

            if (!arrived[i])
            {
                if (now - reservedAt[i] > RESERVE_TIMEOUT) Clear(i);
                continue;
            }

            if (Vector2.Distance(emp.transform.position, slotPosition(i)) > ABANDON_DISTANCE) Clear(i);
        }
    }

    private void Clear(int i)
    {
        occupants[i]  = null;
        arrived[i]    = false;
        reservedAt[i] = 0f;
    }
}
