using TMPro;
using UnityEngine;
using static RuntimeUI;

/// <summary>
/// 단련장 창 — 직원마다 [레벨·남은 스킬 포인트 | 단련 버튼 또는 진행 상태].
/// 단련을 누르면 전리품을 예약하고 그 직원 전용 '단련' 작업이 생깁니다 — 직원이 작업 우선순위에 따라 와서 단련하고,
/// 도중에 멈춰도 진행도는 남습니다 (<see cref="LevelUpStation"/>).
/// 자식 UI는 전부 코드로 만듭니다.
/// </summary>
public class LevelUpUI : BasePanel
{
    private const float WIDTH = 460f;

    private LevelUpStation station;
    private TextMeshProUGUI stockText;
    private TextMeshProUGUI progressText;
    private Transform listRoot;
    private Employee shownTrainee;
    private float shownLevelSum;

    public static void Open(LevelUpStation target)
    {
        if (UIManager.instance == null || target == null) return;
        var panel = UIManager.instance.GetPanel<LevelUpUI>(UIPanelType.LevelUpUI);
        if (panel == null) { Debug.LogError("[LevelUpUI] UIManager에 LevelUpUI 패널이 등록되지 않았습니다."); return; }

        UIManager.instance.ShowPanel(UIPanelType.LevelUpUI); // 처음 열 때 Awake(Build)가 여기서 돈다
        panel.station = target;
        panel.Rebuild();
    }

    private void Awake()
    {
        SetupPanel(this, WIDTH);

        var header = Row(transform, 0);
        var title = Text(header, "단련장", 20, flexible: true);
        title.fontStyle = FontStyles.Bold;
        Btn(header, "×", 34, Close, BTN_OFF);

        stockText = Text(transform, "", 14);
        stockText.color = DIM;
        listRoot = Column(transform, 4);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) || station == null) { Close(); return; }

        // 단련 대상이 바뀌거나(완료·취소) 레벨이 변하면 목록을 다시 그림, 아니면 진행률만 갱신
        if (station.Trainee != shownTrainee || LevelSum() != shownLevelSum) Rebuild();
        else if (progressText != null && station.Trainee != null)
            progressText.text = station.IsTrainingNow ? $"단련 중 {station.Progress * 100f:F0}%" : $"대기 {station.Progress * 100f:F0}%";
    }

    private void Close()
    {
        if (UIManager.instance != null) UIManager.instance.HidePanel(panelType);
        else OnClose();
    }

    public override void OnClose()
    {
        station = null;
        base.OnClose();
    }

    private static float LevelSum()
    {
        float sum = 0f;
        if (EmployeeManager.instance != null)
            foreach (var e in EmployeeManager.instance.AllEmployees) if (e != null) sum += e.Level;
        return sum;
    }

    private void Rebuild()
    {
        if (listRoot == null || station == null) return;
        shownTrainee = station.Trainee;
        shownLevelSum = LevelSum();
        progressText = null;

        var cfg = EmployeeManager.instance != null ? EmployeeManager.instance.LevelUpConfig : null;
        var item = cfg != null ? cfg.growthItem : null;
        int have = item != null && InventoryManager.instance != null ? InventoryManager.instance.GetAvailableAmount(item) : 0;
        stockText.text = item != null
            ? $"쓸 수 있는 {item.itemName}: {have}개 · 한 번에 한 명 · 직원이 '단련' 작업 우선순위에 따라 와서 단련\n레벨마다 스킬 포인트 +1 (직원 관리창 스킬에서 찍기)"
            : "레벨업 설정 없음";

        Clear(listRoot);
        if (EmployeeManager.instance == null) return;
        float hourSeconds = DayCycle.instance != null ? DayCycle.instance.DayLengthInSeconds / 24f : 41.67f;

        foreach (var e in EmployeeManager.instance.AllEmployees)
        {
            if (e == null || e.State == EmployeeState.Dead || e.Growth == null) continue;
            var emp = e;
            var row = Row(listRoot, 6);
            int points = emp.TryGetComponent(out EmployeeSkillState ss) ? ss.RemainingSkillPoints : 0;
            Text(row, $"{emp.DisplayName}  Lv.{emp.Level}  (남은 포인트 {points})", 15, flexible: true);

            if (station.Trainee == emp)
            {
                progressText = Btn(row, "", 130, () => { }, ROW_SEL).GetComponentInChildren<TextMeshProUGUI>();
                Btn(row, "취소", 50, () => { station.CancelTraining("플레이어 취소"); Rebuild(); }, BTN_OFF);
                continue;
            }

            string reason = station.BlockReason(emp);
            if (reason == null && !emp.CanPerformWork(WorkType.Training)) reason = "단련 작업 꺼짐";
            string hours = (emp.Growth.NextTrainingSeconds / hourSeconds).ToString("F1");
            Btn(row, reason == null ? $"단련 (전리품 {emp.Growth.NextLevelCost} · {hours}시간)" : reason, 186,
                () => { if (station.StartTraining(emp)) Rebuild(); },
                reason == null ? BTN_ON : BTN_DIM);
        }
        Update();
    }
}
