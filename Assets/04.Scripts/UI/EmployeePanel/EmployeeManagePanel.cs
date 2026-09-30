using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 직원 관리 패널. 좌측 직원 목록 + 우측 상세:
///   - 현재 상태 (체력/침식/재미/피로)
///   - 장비 슬롯 표시 (무기/방어구 — 현재 착용 장비)
///   - 장비 그리드 (모든 장비 4×4 페이지 — 칸 클릭 → 선택 직원에게 장착 지시, 자기 착용분은 해제)
///   - 작업 우선순위 (박스 드래그로 순서 지정 — WorkPriorityBar)
///   - 필수 소지 설정 (식량/약물 개수 — AI 선제 확보가 이 값을 따름)
///   - 침식 유지 수치 (이 값을 넘으면 세척 시간대에 세척 시설로 감. 세척도 이 값까지만)
///
/// 구역 배정은 이 창에 없습니다 — 하단 바 '구역'으로 옮겼습니다 (ZoneModeBarUI).
/// 직원 한 명씩 고르는 대신 전원 × 구역을 한 화면에서 배정합니다.
///
/// 장착 지시 시 직원이 하던 일을 중단하고 장비 보관소로 이동해 교체합니다.
/// 열기: BottomBar '직원' 버튼 → UIManager.TogglePanel(UIPanelType.EmployeeUI).
/// 스킬 버튼 → 이 창이 왼쪽으로 밀리고 오른쪽에 스킬 트리(SkillTreePanel)가 붙습니다.
/// ESC: 스킬 트리 → 이 창 순서로 닫힘. 이 창을 닫으면 스킬 트리도 함께 닫힘.
/// 슬롯 확장: 새 EquipmentSlot을 표시하려면 빌더에서 슬롯 버튼을 추가하고 배열에 연결.
/// </summary>
public class EmployeeManagePanel : BasePanel
{
    [Header("좌측 직원 목록")]
    [Tooltip("직원 1명 행 템플릿 (비활성)")]
    [SerializeField] private Button listItemTemplate;

    [Header("우측 상세")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text statsText;

    [Header("장비 슬롯 (표시 순서 = displaySlots 순서)")]
    [Tooltip("표시할 슬롯 종류 — 확장 시 여기와 slotButtons에 추가")]
    [SerializeField] private EquipmentSlot[] displaySlots = { EquipmentSlot.Weapon, EquipmentSlot.Suit };
    [SerializeField] private Button[] slotButtons;

    [Header("필수 소지 설정")]
    [SerializeField] private Button foodMinusButton;
    [SerializeField] private Button foodPlusButton;
    [SerializeField] private TMP_Text foodCountText;
    [Tooltip("미리 챙길 음식 기준 (조리 음식만 / 아무 음식) — 누르면 바뀜")]
    [SerializeField] private Button foodPolicyButton;
    [SerializeField] private Button drugMinusButton;
    [SerializeField] private Button drugPlusButton;
    [SerializeField] private TMP_Text drugCountText;

    [Header("침식 유지 수치")]
    [SerializeField] private Button erosionMinusButton;
    [SerializeField] private Button erosionPlusButton;
    [SerializeField] private TMP_Text erosionTargetText;

    [Header("작업 우선순위")]
    [Tooltip("박스를 드래그해 순서를 정하는 바 (WorkPriorityBar)")]
    [SerializeField] private WorkPriorityBar workPriorityBar;

    [Header("장비 그리드 (4×4 페이지)")]
    [Tooltip("칸 템플릿 (비활성) — 같은 부모 아래 GRID_SIZE개로 복제")]
    [SerializeField] private EquipmentCell cellTemplate;
    [SerializeField] private Button prevPageButton;
    [SerializeField] private Button nextPageButton;
    [SerializeField] private TMP_Text pageText;
    [SerializeField] private TMP_Text gridHintText;

    [Header("장비 그리드 — 종류 탭")]
    [SerializeField] private Button weaponTabButton;
    [SerializeField] private Button armorTabButton;

    [Header("스킬 트리")]
    [SerializeField] private Button skillButton;
    [Tooltip("스킬 트리를 붙일 때 두 창 사이 간격")]
    [SerializeField] private float skillGap = 8f;

    private const float REFRESH_INTERVAL = 0.5f;

    /// <summary>침식 유지 수치 ± 버튼 1클릭당 변화량</summary>
    private const float EROSION_TARGET_STEP = 5f;

    private static readonly Color ROW_NORMAL   = new Color(0.16f, 0.16f, 0.20f, 1f);
    private static readonly Color ROW_SELECTED = new Color(0.26f, 0.34f, 0.46f, 1f);

    private const int GRID_SIZE = 16;

    private static readonly Color ARROW_ON  = Color.white;
    private static readonly Color ARROW_OFF = new Color(0.45f, 0.45f, 0.48f, 1f);

    private static readonly Color TAB_ON  = new Color(0.086f, 0.373f, 0.502f, 1f);
    private static readonly Color TAB_OFF = new Color(0.2f, 0.22f, 0.28f, 1f);

    /// <summary>장비 그리드 종류 — 무기 / 방어구(무기 외 전부)</summary>
    private enum EquipmentTab { Weapon, Armor }

    private Employee selected;

    private readonly List<EquipmentCell> cells = new List<EquipmentCell>();
    private List<EquipmentEntry> entries = new List<EquipmentEntry>();
    private int page;
    private EquipmentTab tab = EquipmentTab.Weapon;

    private SkillTreePanel skillPanel;
    private Vector2 homePosition;

    private float refreshTimer;

    private readonly List<GameObject> listItems = new List<GameObject>();

    /// <summary>선택 하이라이트를 칠하기 위한 직원 ↔ 행 매핑</summary>
    private readonly Dictionary<Employee, Image> rowByEmployee = new Dictionary<Employee, Image>();

    #region 초기화

    private void Awake()
    {
        homePosition = ((RectTransform)transform).anchoredPosition;

        // 슬롯 버튼은 현재 착용 장비 표시 전용 — 장착·해제는 장비 그리드에서
        if (slotButtons != null)
            foreach (var b in slotButtons) if (b != null) b.interactable = false;

        if (cellTemplate != null)
        {
            cellTemplate.gameObject.SetActive(false);
            for (int i = 0; i < GRID_SIZE; i++)
            {
                var cell = Instantiate(cellTemplate, cellTemplate.transform.parent);
                cell.gameObject.SetActive(true);
                cell.name = $"Cell_{i}";
                cells.Add(cell);
            }
        }
        if (prevPageButton != null) { prevPageButton.transition = Selectable.Transition.None; prevPageButton.onClick.AddListener(() => TurnPage(-1)); }
        if (nextPageButton != null) { nextPageButton.transition = Selectable.Transition.None; nextPageButton.onClick.AddListener(() => TurnPage(+1)); }
        if (skillButton != null) skillButton.onClick.AddListener(ToggleSkillTree);
        if (weaponTabButton != null) weaponTabButton.onClick.AddListener(() => SetTab(EquipmentTab.Weapon));
        if (armorTabButton != null) armorTabButton.onClick.AddListener(() => SetTab(EquipmentTab.Armor));

        foodMinusButton?.onClick.AddListener(() => AdjustCarry(isFood: true, delta: -1));
        foodPlusButton?.onClick.AddListener(() => AdjustCarry(isFood: true, delta: +1));
        if (foodPolicyButton != null) foodPolicyButton.onClick.AddListener(CycleFoodPolicy);
        drugMinusButton?.onClick.AddListener(() => AdjustCarry(isFood: false, delta: -1));
        drugPlusButton?.onClick.AddListener(() => AdjustCarry(isFood: false, delta: +1));

        erosionMinusButton?.onClick.AddListener(() => AdjustErosionTarget(-EROSION_TARGET_STEP));
        erosionPlusButton?.onClick.AddListener(() => AdjustErosionTarget(+EROSION_TARGET_STEP));
    }

    public override void OnOpen()
    {
        base.OnOpen();
        tab = EquipmentTab.Weapon; // 열 때는 항상 무기 탭부터
        page = 0;
        RebuildList();
        RefreshDetail();
        workPriorityBar?.Show(selected);
    }

    /// <summary>이 창을 닫으면 스킬 트리도 함께 닫습니다.</summary>
    public override void OnClose()
    {
        CloseSkillTree();
        base.OnClose();
    }

    private void Update()
    {
        if (!gameObject.activeSelf) return;

        // ESC: 스킬 트리가 열려 있으면 그것만, 아니면 이 창
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (IsSkillTreeOpen) CloseSkillTree();
            else if (UIManager.instance != null) UIManager.instance.HidePanel(panelType);
            else OnClose();
            return;
        }

        // 스킬 트리가 자기 닫기 버튼으로 닫혔으면 이 창을 제자리로
        if (skillPanel != null && !skillPanel.gameObject.activeSelf && Shifted) SetShifted(false);

        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = REFRESH_INTERVAL;
            RefreshDetail();
        }
    }

    #endregion

    #region 직원 목록

    private void RebuildList()
    {
        // 목록 행도 같은 이유로 즉시 비활성화 후 파괴 (Destroy는 프레임 끝이라 그 사이 낡은 행이 클릭되지 않도록)
        foreach (var go in listItems)
        {
            if (go == null) continue;
            go.SetActive(false);
            Destroy(go);
        }
        listItems.Clear();
        rowByEmployee.Clear();

        if (listItemTemplate == null || EmployeeManager.instance == null) return;

        Employee firstAlive = null;
        foreach (var emp in EmployeeManager.instance.AllEmployees)
        {
            if (emp == null || emp.State == EmployeeState.Dead) continue;
            if (firstAlive == null) firstAlive = emp;

            var row = Instantiate(listItemTemplate, listItemTemplate.transform.parent);
            row.gameObject.SetActive(true);
            var label = row.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = emp.DisplayName;
            Employee captured = emp;
            row.onClick.AddListener(() => Select(captured));
            listItems.Add(row.gameObject);

            var rowImage = row.GetComponent<Image>();
            if (rowImage != null) rowByEmployee[captured] = rowImage;
        }

        // 선택 직원이 죽었거나 없으면 첫 직원 선택
        if (selected == null || selected.State == EmployeeState.Dead)
            selected = firstAlive;

        RefreshListHighlight();
    }

    /// <summary>선택된 직원의 행만 밝게 칠합니다.</summary>
    private void RefreshListHighlight()
    {
        foreach (var pair in rowByEmployee)
        {
            if (pair.Value == null) continue;
            pair.Value.color = pair.Key == selected ? ROW_SELECTED : ROW_NORMAL;
        }
    }

    private void Select(Employee emp)
    {
        selected = emp;
        RefreshListHighlight();
        RefreshDetail();
        if (IsSkillTreeOpen) skillPanel.Setup(selected); // 열린 스킬 트리도 새 직원으로

        // 주기 갱신(RefreshDetail)이 아니라 여기서만 다시 그린다 —
        // 0.5초마다 새로 그리면 드래그 중인 박스가 그대로 파괴된다
        workPriorityBar?.Show(selected);
    }

    #endregion

    #region 상세 표시

    private void RefreshDetail()
    {
        if (selected == null)
        {
            if (nameText != null) nameText.text = "직원 없음";
            if (statsText != null) statsText.text = "";
            RefreshEquipmentGrid();
            return;
        }

        if (nameText != null) nameText.text = $"{selected.DisplayName}  Lv.{selected.Level}";

        if (statsText != null)
        {
            var stats = selected.Stats;
            var needs = selected.Needs;
            var erosionCtrl = selected.GetComponent<EmployeeErosionController>();
            string stage = erosionCtrl != null ? erosionCtrl.CurrentStage.ToString() : "-";

            statsText.text =
                $"체력  {stats.health:F0} / {stats.maxHealth:F0}\n" +
                $"정신  {stats.mental:F0} / {stats.maxMental:F0}{BuildMentalDetail()}\n" +
                $"침식  {selected.ErosionLevel:F0} / 200  ({stage})\n" +
                BuildErosionDetail(erosionCtrl) +
                $"재미  {needs.fun:F0} / 100\n" +
                $"수면(피로)  {needs.fatigue:F0} / 100";
        }

        RefreshSlotLabels();
        RefreshCarryLabels();
        RefreshErosionTargetLabel();
        RefreshEquipmentGrid();
    }

    /// <summary>
    /// 침식이 어디서 얼마나 쌓였는지를 출처별로 펼칩니다.
    /// 예: "  자연 침식 +3.0" / "  제놉스 A 오라침식 +7.0"
    /// </summary>
    private string BuildErosionDetail(EmployeeErosionController erosionCtrl)
    {
        if (erosionCtrl == null) return string.Empty;

        var sources = erosionCtrl.ErosionSources;
        if (sources == null || sources.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var s in sources)
        {
            if (s == null || s.amount < 0.05f) continue;
            sb.Append($"    {s.displayName}  +{s.amount:F1}\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 정신력이 기본값에서 얼마나, 왜 벗어나 있는지를 보여줍니다.
    /// 예: " (기본 50 · 굶주림 -25)"
    /// </summary>
    private string BuildMentalDetail()
    {
        var statsCtrl = selected != null ? selected.StatsController : null;
        if (statsCtrl == null) return string.Empty;

        var mods = statsCtrl.MentalModifiers;
        if (mods == null || mods.Count == 0)
            return $"  (기본 {statsCtrl.BaseMental:F0})";

        var sb = new System.Text.StringBuilder();
        sb.Append($"  (기본 {statsCtrl.BaseMental:F0}");
        foreach (var m in mods)
        {
            if (m == null) continue;
            // 시간형은 남은 시간을 함께 보여준다 — '정신차림'이 언제 풀리는지가
            // 위험 작업 타이밍을 잡는 근거이므로 숫자로 보이는 편이 낫다.
            if (m.IsConditional)
                sb.Append($" · {m.displayName} {m.value:+0.#;-0.#}");
            else
                sb.Append($" · {m.displayName} {m.value:+0.#;-0.#} ({m.remainingTime:F0}초)");
        }
        sb.Append(')');
        return sb.ToString();
    }

    private void RefreshSlotLabels()
    {
        if (slotButtons == null || selected == null) return;

        var equipment = selected.GetComponent<EmployeeEquipment>();
        for (int i = 0; i < slotButtons.Length && i < displaySlots.Length; i++)
        {
            var label = slotButtons[i] != null ? slotButtons[i].GetComponentInChildren<TMP_Text>() : null;
            if (label == null) continue;

            var slot = displaySlots[i];
            string slotName = GetSlotDisplayName(slot);
            var data = equipment?.GetItemInSlot(slot);
            if (data == null)
            {
                label.text = $"{slotName}: (없음)";
            }
            else
            {
                var inst = equipment.GetInstanceInSlot(slot);
                string dura = data.indestructible ? "∞"
                    : inst != null ? $"{inst.durability:F0}/{data.maxDurability:F0}" : "?";
                label.text = $"{slotName}: {data.itemData?.itemName} [{dura}]";
            }
        }
    }

    private void RefreshCarryLabels()
    {
        var work = selected != null ? selected.GetComponent<EmployeeWork>() : null;
        if (foodCountText != null)
            foodCountText.text = work != null ? $"{work.HeldFoodCount}/{work.DesiredFoodCount}" : "-";
        if (drugCountText != null)
            drugCountText.text = work != null ? $"{work.HeldDrugCount}/{work.DesiredDrugCount}" : "-";
        if (foodPolicyButton != null)
        {
            var label = foodPolicyButton.GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.text = work == null ? "-" : work.FoodPolicy == FoodCarryPolicy.CookedOnly ? "조리 음식만" : "아무 음식";
        }
    }

    private void CycleFoodPolicy()
    {
        var work = selected != null ? selected.GetComponent<EmployeeWork>() : null;
        if (work == null) return;
        work.FoodPolicy = work.FoodPolicy == FoodCarryPolicy.CookedOnly ? FoodCarryPolicy.Any : FoodCarryPolicy.CookedOnly;
        RefreshCarryLabels();
    }

    private void AdjustCarry(bool isFood, int delta)
    {
        var work = selected != null ? selected.GetComponent<EmployeeWork>() : null;
        if (work == null) return;

        if (isFood) work.DesiredFoodCount += delta;
        else        work.DesiredDrugCount += delta;
        RefreshCarryLabels();
    }

    /// <summary>
    /// 침식 유지 수치를 표시합니다.
    ///
    /// 자연 회복 하한 이상으로 설정하면 자연 회복만으로 조건이 충족돼 직원이 세척하러
    /// 가지 않습니다 — 설정이 무력화된 것처럼 보이므로 그 사실을 라벨에 직접 알립니다.
    /// </summary>
    private void RefreshErosionTargetLabel()
    {
        if (erosionTargetText == null) return;

        var erosion = selected != null ? selected.ErosionController : null;
        if (erosion == null) { erosionTargetText.text = "침식 유지: -"; return; }

        float target = erosion.ErosionMaintainTarget;
        float floor = ErosionManager.instance != null
            ? ErosionManager.instance.EffectiveRecoveryFloor
            : 50f;

        string warn = target >= floor
            ? $"  <color=#E0A030>(자연 회복 하한 {floor:F0} 이상 — 세척하러 가지 않음)</color>"
            : string.Empty;

        erosionTargetText.text = $"침식 유지: {target:F0}{warn}";
    }

    private void AdjustErosionTarget(float delta)
    {
        var erosion = selected != null ? selected.ErosionController : null;
        if (erosion == null) return;

        erosion.ErosionMaintainTarget += delta;   // 프로퍼티가 범위를 보정
        RefreshErosionTargetLabel();

        // 다른 설정 변경과 동일하게 즉시 재평가시킨다 (지금이 세척 시간대일 수 있음)
        selected.GetComponent<EmployeeAI>()?.ForceReevaluate();
    }

    public static string GetSlotDisplayName(EquipmentSlot slot)
    {
        switch (slot)
        {
            case EquipmentSlot.Weapon:    return "무기";
            case EquipmentSlot.Suit:      return "방어구";
            case EquipmentSlot.Helmet:    return "헬멧";
            case EquipmentSlot.MultiTool: return "다용도구";
            default:                      return slot.ToString();
        }
    }

    #endregion

    #region 장비 그리드

    /// <summary>모든 장비를 다시 모아 현재 페이지를 그립니다 (주기 갱신에서 호출).</summary>
    private void RefreshEquipmentGrid()
    {
        if (cells.Count == 0) return;

        entries = EquipmentListing.CollectAll().FindAll(InTab);
        SetTabColor(weaponTabButton, tab == EquipmentTab.Weapon);
        SetTabColor(armorTabButton, tab == EquipmentTab.Armor);
        int pages = Mathf.Max(1, Mathf.CeilToInt(entries.Count / (float)GRID_SIZE));
        page = Mathf.Clamp(page, 0, pages - 1);

        var mgr = EquipmentStorageManager.instance;
        bool hasArmory = mgr != null && mgr.HasArmory();

        for (int i = 0; i < cells.Count; i++)
        {
            int idx = page * GRID_SIZE + i;
            var entry = idx < entries.Count ? entries[idx] : null;

            // 보관소 장비 → 선택 직원에게 장착 / 선택 직원이 입은 장비 → 해제 / 다른 직원이 입은 장비 → 불가
            bool mine = entry != null && entry.wearer == selected;
            bool usable = entry != null && selected != null && hasArmory && (!entry.IsWorn || mine);
            EquipmentEntry captured = entry;
            cells[i].Bind(entry, usable, () => OnCellClicked(captured), () => OnCellInfo(captured));
        }

        if (pageText != null) pageText.text = $"{page + 1}/{pages}";
        SetArrow(prevPageButton, page > 0);
        SetArrow(nextPageButton, page < pages - 1);

        if (gridHintText != null)
            gridHintText.text = hasArmory ? "" : "! 장비 보관소 없음 — 장착 불가";
    }

    /// <summary>현재 탭에 속하는 장비인지 — 무기 탭은 무기 슬롯, 방어구 탭은 그 외 슬롯.</summary>
    private bool InTab(EquipmentEntry e)
    {
        bool weapon = e.data != null && e.data.slot == EquipmentSlot.Weapon;
        return tab == EquipmentTab.Weapon ? weapon : !weapon;
    }

    private void SetTab(EquipmentTab next)
    {
        if (tab == next) return;
        tab = next;
        page = 0;
        RefreshEquipmentGrid();
    }

    private static void SetTabColor(Button button, bool on)
    {
        if (button != null && button.targetGraphic != null) button.targetGraphic.color = on ? TAB_ON : TAB_OFF;
    }

    private void TurnPage(int delta)
    {
        page += delta;
        RefreshEquipmentGrid();
    }

    private static void SetArrow(Button button, bool enabled)
    {
        if (button == null) return;
        button.interactable = enabled;
        if (button.targetGraphic != null) button.targetGraphic.color = enabled ? ARROW_ON : ARROW_OFF;
    }

    /// <summary>칸 클릭 — 보관소 장비면 장착 지시, 선택 직원이 입은 장비면 해제 지시.</summary>
    private void OnCellClicked(EquipmentEntry entry)
    {
        if (selected == null || entry == null || entry.data == null) return;
        if (entry.IsWorn && entry.wearer != selected) return;

        int instanceId = entry.IsWorn ? 0 : entry.instance.instanceId; // 0 = 해제
        if (selected.TryGetComponent(out EmployeeAI ai)) ai.RequestEquipChange(entry.data.slot, instanceId);
        RefreshDetail();
    }

    // ponytail: 장비 정보 창(이름·등급·위치·착용자 …)은 아직 없음 — 만들면 여기서 연다
    private void OnCellInfo(EquipmentEntry entry)
    {
        if (entry == null) return;
        Debug.Log($"[EmployeeManagePanel] 장비 정보: {entry.Name} (착용: {(entry.IsWorn ? entry.wearer.DisplayName : "보관소")})");
    }

    #endregion

    #region 스킬 트리 (옆에 붙이기)

    private bool IsSkillTreeOpen => skillPanel != null && skillPanel.gameObject.activeSelf;
    private bool Shifted => ((RectTransform)transform).anchoredPosition != homePosition;

    private void ToggleSkillTree()
    {
        if (IsSkillTreeOpen) { CloseSkillTree(); return; }
        if (selected == null || UIManager.instance == null) return;

        skillPanel = UIManager.instance.GetPanel<SkillTreePanel>(UIPanelType.SkillTreeUI);
        if (skillPanel == null) { Debug.LogWarning("[EmployeeManagePanel] SkillTreePanel이 UIManager에 등록되지 않았습니다."); return; }

        skillPanel.Setup(selected); // Setup이 OnOpen까지 호출
        SetShifted(true);
    }

    private void CloseSkillTree()
    {
        if (IsSkillTreeOpen) skillPanel.OnClose();
        SetShifted(false);
    }

    /// <summary>
    /// 스킬 트리가 열리면 이 창을 화면 왼쪽 끝으로 밀고, 스킬 트리를 그 오른쪽에 붙입니다.
    /// 가운데 정렬로 두면 오른쪽 끝이 우상단 알림 배너에 걸려 스킬 트리 닫기 버튼이 가려진다.
    /// 두 창 모두 가운데 앵커라 x만 옮기면 됩니다.
    /// </summary>
    private void SetShifted(bool shifted)
    {
        var me = (RectTransform)transform;
        if (!shifted || skillPanel == null) { me.anchoredPosition = homePosition; return; }

        var sp = (RectTransform)skillPanel.transform;
        float screenW = me.parent is RectTransform parent ? parent.rect.width : 1920f;
        float w1 = me.rect.width, w2 = sp.rect.width;
        float left = -screenW / 2f + skillGap * 3f;
        me.anchoredPosition = new Vector2(left + w1 / 2f, homePosition.y);
        sp.anchoredPosition = new Vector2(left + w1 + skillGap + w2 / 2f, homePosition.y);
    }

    #endregion
}
