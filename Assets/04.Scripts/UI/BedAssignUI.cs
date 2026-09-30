using TMPro;
using UnityEngine;
using static RuntimeUI;

/// <summary>
/// 침대 주인 배정 창 — 침대를 클릭하면 열림. 직원을 누르면 주인이 되고, [비우기]로 해제.
/// 다른 침대를 가진 직원을 고르면 그 침대는 비워집니다 (직원 하나 = 침대 하나).
/// 자식 UI는 전부 코드로 만듭니다.
/// </summary>
public class BedAssignUI : BasePanel
{
    private const float WIDTH = 360f;

    private SleepingBed bed;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI ownerText;
    private Transform listRoot;

    public static void Open(SleepingBed target)
    {
        if (UIManager.instance == null || target == null) return;
        var panel = UIManager.instance.GetPanel<BedAssignUI>(UIPanelType.BedAssignUI);
        if (panel == null) { Debug.LogError("[BedAssignUI] UIManager에 BedAssignUI 패널이 등록되지 않았습니다."); return; }

        UIManager.instance.ShowPanel(UIPanelType.BedAssignUI); // 처음 열 때 Awake(Build)가 여기서 돈다
        panel.bed = target;
        panel.Rebuild();
    }

    private void Awake()
    {
        SetupPanel(this, WIDTH);

        var header = Row(transform, 0);
        titleText = Text(header, "", 20, flexible: true);
        titleText.fontStyle = FontStyles.Bold;
        Btn(header, "×", 34, Close, BTN_OFF);

        ownerText = Text(transform, "", 15);
        listRoot = Column(transform, 4);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) || bed == null) Close();
    }

    private void Close()
    {
        if (UIManager.instance != null) UIManager.instance.HidePanel(panelType);
        else OnClose();
    }

    public override void OnClose()
    {
        bed = null;
        base.OnClose();
    }

    private void Rebuild()
    {
        if (bed == null || titleText == null) return;
        titleText.text = $"{bed.BedName} — 주인";
        var owner = bed.Owner;
        ownerText.text = owner != null ? $"주인: {owner.DisplayName}" : "주인: 없음 — 아래에서 직원을 고르세요";

        Clear(listRoot);
        if (owner != null) Btn(listRoot, "비우기", 0, () => { bed.SetOwner(null); Rebuild(); }, BTN_OFF);

        if (EmployeeManager.instance == null) return;
        foreach (var e in EmployeeManager.instance.AllEmployees)
        {
            if (e == null || e.State == EmployeeState.Dead) continue;
            var emp = e;
            var other = SleepingBed.FindOwnedBy(emp);
            string label = emp.DisplayName + (other != null && other != bed ? $"  (지금: {other.BedName})" : "");
            Btn(listRoot, label, 0, () => { bed.SetOwner(emp); Rebuild(); }, emp == owner ? BTN_ON : BTN);
        }
    }
}
