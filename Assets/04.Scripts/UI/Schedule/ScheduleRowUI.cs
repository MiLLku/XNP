using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// 스케줄 그리드의 직원 1명 분 행. 이름 + 24시간 셀.
/// SchedulePanel이 직원마다 복제해 Bind()로 연결한다.
/// </summary>
public class ScheduleRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;

    [Tooltip("시간 셀 템플릿 (행 안에 비활성 상태로 배치)")]
    [SerializeField] private ScheduleCellUI cellTemplate;

    private readonly List<ScheduleCellUI> cells = new List<ScheduleCellUI>();

    private Employee employee;
    private EmployeeSchedule schedule;
    private SchedulePanel panel;

    /// <summary>직원과 연결하고 24시간 셀을 생성·채색합니다.</summary>
    public void Bind(Employee target, SchedulePanel owner)
    {
        employee = target;
        panel = owner;
        schedule = target != null ? target.GetComponent<EmployeeSchedule>() : null;

        if (nameText != null && target != null)
            nameText.text = target.DisplayName;

        // 셀 생성 (최초 1회)
        if (cells.Count == 0 && cellTemplate != null)
        {
            for (int h = 0; h < EmployeeSchedule.HOURS_PER_DAY; h++)
            {
                var cell = Instantiate(cellTemplate, cellTemplate.transform.parent);
                cell.gameObject.SetActive(true);
                cell.Init(h, this);
                cells.Add(cell);
            }
        }

        RefreshAll();
    }

    /// <summary>브러시로 특정 시간을 칠합니다 (셀 클릭/드래그에서 호출).</summary>
    public void Paint(int hour)
    {
        if (schedule == null || panel == null) return;

        schedule.SetActivity(hour, panel.CurrentBrush);
        RefreshCell(hour);

        // 현재 시간대를 바꿨으면 AI 즉시 재평가 (다음 시간까지 기다리지 않음)
        if (DayCycle.instance != null && hour == DayCycle.instance.CurrentHour && employee != null)
        {
            employee.GetComponent<EmployeeAI>()?.ForceReevaluate();
        }
    }

    /// <summary>전체 24칸을 스케줄 데이터 색으로 갱신합니다.</summary>
    public void RefreshAll()
    {
        if (schedule == null || panel == null) return;

        for (int h = 0; h < cells.Count; h++)
        {
            cells[h].SetColor(panel.GetActivityColor(schedule.GetActivityAt(h)));
        }
    }

    private void RefreshCell(int hour)
    {
        if (schedule == null || panel == null) return;
        if (hour < 0 || hour >= cells.Count) return;

        cells[hour].SetColor(panel.GetActivityColor(schedule.GetActivityAt(hour)));
    }
}
