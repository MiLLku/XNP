using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using static RuntimeUI;

/// <summary>
/// 생산 계획 창 — <see cref="IPlanHost"/>(스토브 등) 하나의 계획 목록을 편집합니다.
///
/// [+ 레시피] 로 계획 추가 → 줄마다 [▲▼ 순서] [반복 방식] [− 목표 +] [정지/재개] [재료] [×].
/// [재료]를 누르면 아래에 그 계획의 재료 필터(침식·채소·육류 + 재료별 직접 지정)가 열립니다.
/// 자식 UI는 전부 코드로 만듭니다 — 씬에는 이 컴포넌트가 붙은 빈 패널만 있으면 됩니다.
/// </summary>
public class ProductionPlanUI : BasePanel
{
    private const float WIDTH = 600f;
    private const float REFRESH = 0.5f;

    private IPlanHost host;
    private ProductionPlan filterPlan;

    private TextMeshProUGUI titleText;
    private TextMeshProUGUI statusText;
    private Transform addRow;
    private Transform listRoot;
    private Transform filterRoot;
    private readonly List<(ProductionPlan plan, TextMeshProUGUI mode)> modeLabels = new List<(ProductionPlan, TextMeshProUGUI)>();
    private float timer;

    /// <summary>건물을 클릭했을 때 — 창을 열고 그 건물의 계획을 보여 줍니다.</summary>
    public static void Open(IPlanHost target)
    {
        if (UIManager.instance == null || target == null) return;
        var panel = UIManager.instance.GetPanel<ProductionPlanUI>(UIPanelType.ProductionPlanUI);
        if (panel == null) { Debug.LogError("[ProductionPlanUI] UIManager에 ProductionPlanUI 패널이 등록되지 않았습니다."); return; }

        UIManager.instance.ShowPanel(UIPanelType.ProductionPlanUI); // 처음 열 때 Awake(Build)가 여기서 돈다
        panel.host = target;
        panel.filterPlan = null;
        panel.Rebuild();
    }

    private void Awake() => Build();

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        if (host == null || (host is UnityEngine.Object o && o == null)) { Close(); return; }

        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = REFRESH;
        statusText.text = host.DescribeStatus();
        foreach (var (plan, label) in modeLabels) label.text = plan.DescribeMode();
    }

    private void Close()
    {
        if (UIManager.instance != null) UIManager.instance.HidePanel(panelType);
        else OnClose();
    }

    public override void OnClose()
    {
        host = null;
        filterPlan = null;
        base.OnClose();
    }

    #region 틀

    private void Build()
    {
        SetupPanel(this, WIDTH);

        var header = Row(transform, 0);
        titleText = Text(header, "", 20, flexible: true);
        titleText.fontStyle = FontStyles.Bold;
        Btn(header, "×", 34, Close, BTN_OFF);

        statusText = Text(transform, "", 15);
        statusText.color = DIM;

        addRow = Row(transform, 6);
        listRoot = Column(transform, 4);
        filterRoot = Column(transform, 4);
    }

    #endregion

    #region 내용

    private void Rebuild()
    {
        if (host == null || titleText == null) return;
        titleText.text = $"{host.HostName} — 계획";
        statusText.text = host.DescribeStatus();

        Clear(addRow);
        Clear(listRoot);
        Clear(filterRoot);
        modeLabels.Clear();

        // 계획 추가 버튼 — 레시피마다
        for (int i = 0; i < host.Recipes.Count; i++)
        {
            int index = i;
            Btn(addRow, $"+ {host.Recipes[i].DisplayName}", 170, () => AddPlan(index), BTN_ON);
        }

        var plans = host.Plans;
        if (plans.Count == 0)
            Text(listRoot, "계획이 없습니다 — 위에서 추가하세요", 15).color = DIM;

        for (int i = 0; i < plans.Count; i++) BuildPlanRow(plans[i], i);

        if (filterPlan != null && plans.Contains(filterPlan)) BuildFilter(filterPlan);
        else filterPlan = null;
    }

    private void BuildPlanRow(ProductionPlan plan, int index)
    {
        var plans = host.Plans;
        var recipe = plan.recipeIndex >= 0 && plan.recipeIndex < host.Recipes.Count ? host.Recipes[plan.recipeIndex] : null;

        var row = Row(listRoot, 3);
        row.gameObject.AddComponent<Image>().color = plan == filterPlan ? ROW_SEL : ROW_BG;

        Btn(row, "▲", 26, () => Move(index, -1), index > 0 ? BTN : BTN_DIM);
        Btn(row, "▼", 26, () => Move(index, +1), index < plans.Count - 1 ? BTN : BTN_DIM);

        var name = Text(row, recipe != null ? recipe.DisplayName : "(없는 레시피)", 16, flexible: true);
        if (plan.paused) name.color = DIM;

        var modeBtn = Btn(row, plan.DescribeMode(), 130, () => { plan.mode = Next(plan.mode); if (plan.mode == PlanRepeatMode.Count) plan.done = 0; Rebuild(); }, BTN);
        modeLabels.Add((plan, modeBtn.GetComponentInChildren<TextMeshProUGUI>()));

        bool hasTarget = plan.mode != PlanRepeatMode.Forever;
        Btn(row, "-", 26, () => AdjustTarget(plan, -1), hasTarget ? BTN : BTN_DIM);
        Btn(row, "+", 26, () => AdjustTarget(plan, +1), hasTarget ? BTN : BTN_DIM);

        Btn(row, plan.paused ? "재개" : "정지", 46, () => { plan.paused = !plan.paused; Rebuild(); }, plan.paused ? BTN_OFF : BTN);
        if (recipe != null && recipe.UsesIngredientFilter)
            Btn(row, "재료", 46, () => { filterPlan = filterPlan == plan ? null : plan; Rebuild(); }, plan == filterPlan ? BTN_ON : BTN);
        Btn(row, "×", 26, () => { plans.Remove(plan); if (filterPlan == plan) filterPlan = null; Rebuild(); }, BTN_OFF);
    }

    private void BuildFilter(ProductionPlan plan)
    {
        var f = plan.filter;
        Text(filterRoot, $"재료 필터 — {host.Plans.IndexOf(plan) + 1}번 계획", 16).fontStyle = FontStyles.Bold;

        var cats = Row(filterRoot, 6);
        Btn(cats, $"침식 재료: {OnOff(f.allowErosion)}", 170, () => { f.allowErosion = !f.allowErosion; Rebuild(); }, f.allowErosion ? BTN_ON : BTN_OFF);
        Btn(cats, $"채소: {OnOff(f.allowVegetable)}", 130, () => { f.allowVegetable = !f.allowVegetable; Rebuild(); }, f.allowVegetable ? BTN_ON : BTN_OFF);
        Btn(cats, $"육류: {OnOff(f.allowMeat)}", 130, () => { f.allowMeat = !f.allowMeat; Rebuild(); }, f.allowMeat ? BTN_ON : BTN_OFF);

        Text(filterRoot, "직접 지정 — 눌러서 재료별로 켜고 끄기 (회색 = 위 분류에서 꺼짐)", 13).color = DIM;

        var grid = new GameObject("Ingredients", typeof(RectTransform), typeof(GridLayoutGroup)).GetComponent<GridLayoutGroup>();
        grid.transform.SetParent(filterRoot, false);
        grid.cellSize = new Vector2(138, ROW_H);
        grid.spacing = new Vector2(4, 4);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;

        foreach (var item in IngredientFilter.AllIngredients())
        {
            var it = item;
            bool excluded = f.IsExcluded(it);
            bool usable = f.Allows(it);
            var b = Btn(grid.transform, excluded ? $"{it.itemName} (제외)" : it.itemName, 0, () => { f.SetExcluded(it, !f.IsExcluded(it)); Rebuild(); },
                        excluded ? BTN_OFF : usable ? BTN_ON : BTN_DIM);
            if (!usable && !excluded) b.GetComponentInChildren<TextMeshProUGUI>().color = DIM;
        }
    }

    private void AddPlan(int recipeIndex)
    {
        host.Plans.Add(new ProductionPlan { recipeIndex = recipeIndex });
        Rebuild();
    }

    private void Move(int index, int delta)
    {
        var plans = host.Plans;
        int to = index + delta;
        if (to < 0 || to >= plans.Count) return;
        (plans[index], plans[to]) = (plans[to], plans[index]);
        Rebuild();
    }

    /// <summary>목표 조절 — Shift를 누르면 10씩</summary>
    private void AdjustTarget(ProductionPlan plan, int delta)
    {
        if (plan.mode == PlanRepeatMode.Forever) return;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) delta *= 10;
        plan.target = Mathf.Clamp(plan.target + delta, 1, ProductionPlan.MAX_TARGET);
        Rebuild();
    }

    private static PlanRepeatMode Next(PlanRepeatMode m)
        => m == PlanRepeatMode.Count ? PlanRepeatMode.UntilStock : m == PlanRepeatMode.UntilStock ? PlanRepeatMode.Forever : PlanRepeatMode.Count;

    private static string OnOff(bool on) => on ? "사용" : "미사용";

    #endregion
}
