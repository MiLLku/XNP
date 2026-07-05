using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 직원 작업 일정(24시간 스케줄) 편집 패널. 림월드식 그리드:
///   행 = 직원, 열 = 24시간. 상단 브러시(활동 5종)를 고른 뒤 셀을 클릭/드래그해 칠한다.
///
/// 열기: BottomBarUI 스케줄 버튼 → UIManager.TogglePanel(UIPanelType.ScheduleUI).
/// 현재 시간대를 칠하면 해당 직원 AI가 즉시 재평가(ForceReevaluate)된다.
/// </summary>
public class SchedulePanel : BasePanel
{
    [Header("템플릿")]
    [Tooltip("직원 1명 분 행 템플릿 (비활성 상태로 배치)")]
    [SerializeField] private ScheduleRowUI rowTemplate;

    [Header("브러시 버튼 (ScheduleActivity enum 순서: Work/Sleep/Recreation/Wash/Anything)")]
    [SerializeField] private Button[] brushButtons;

    [Header("활동별 색 (enum 순서와 동일)")]
    [SerializeField] private Color workColor       = new Color(0.25f, 0.45f, 0.75f, 1f); // 파랑
    [SerializeField] private Color sleepColor      = new Color(0.35f, 0.28f, 0.55f, 1f); // 보라
    [SerializeField] private Color recreationColor = new Color(0.30f, 0.65f, 0.30f, 1f); // 초록
    [SerializeField] private Color washColor       = new Color(0.25f, 0.60f, 0.65f, 1f); // 청록
    [SerializeField] private Color anythingColor   = new Color(0.45f, 0.45f, 0.45f, 1f); // 회색

    /// <summary>현재 선택된 브러시</summary>
    private ScheduleActivity currentBrush = ScheduleActivity.Work;

    private readonly List<ScheduleRowUI> rows = new List<ScheduleRowUI>();

    public ScheduleActivity CurrentBrush => currentBrush;

    private void Awake()
    {
        // 브러시 버튼 배선 (인덱스 = enum 정수값)
        if (brushButtons != null)
        {
            for (int i = 0; i < brushButtons.Length; i++)
            {
                if (brushButtons[i] == null) continue;
                int idx = i;
                brushButtons[i].onClick.AddListener(() => SetBrush((ScheduleActivity)idx));
            }
        }
        UpdateBrushHighlight();
    }

    public override void OnOpen()
    {
        base.OnOpen();
        Rebuild();
    }

    /// <summary>활동에 대응하는 그리드 색을 반환합니다.</summary>
    public Color GetActivityColor(ScheduleActivity activity)
    {
        switch (activity)
        {
            case ScheduleActivity.Work:       return workColor;
            case ScheduleActivity.Sleep:      return sleepColor;
            case ScheduleActivity.Recreation: return recreationColor;
            case ScheduleActivity.Wash:       return washColor;
            default:                          return anythingColor;
        }
    }

    private void SetBrush(ScheduleActivity activity)
    {
        currentBrush = activity;
        UpdateBrushHighlight();
    }

    /// <summary>선택된 브러시 버튼만 불투명, 나머지는 반투명으로 표시합니다.</summary>
    private void UpdateBrushHighlight()
    {
        if (brushButtons == null) return;

        for (int i = 0; i < brushButtons.Length; i++)
        {
            if (brushButtons[i] == null) continue;
            var img = brushButtons[i].GetComponent<Image>();
            if (img == null) continue;

            Color c = GetActivityColor((ScheduleActivity)i);
            c.a = (ScheduleActivity)i == currentBrush ? 1f : 0.45f;
            img.color = c;
        }
    }

    /// <summary>직원 목록으로 행을 다시 만듭니다 (열 때마다 최신화).</summary>
    private void Rebuild()
    {
        foreach (var row in rows)
        {
            if (row != null) Destroy(row.gameObject);
        }
        rows.Clear();

        if (rowTemplate == null || EmployeeManager.instance == null) return;

        foreach (var employee in EmployeeManager.instance.AllEmployees)
        {
            if (employee == null || employee.State == EmployeeState.Dead) continue;

            var row = Instantiate(rowTemplate, rowTemplate.transform.parent);
            row.gameObject.SetActive(true);
            row.Bind(employee, this);
            rows.Add(row);
        }
    }
}
