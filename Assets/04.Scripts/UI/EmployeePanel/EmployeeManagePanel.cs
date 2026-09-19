using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 직원 관리 패널. 좌측 직원 목록 + 우측 상세:
///   - 현재 상태 (체력/침식/재미/피로)
///   - 장비 슬롯 (무기/방어구 — 클릭 → 보관소 보유 장비 리스트 → 클릭 장착 지시)
///   - 작업 우선순위 (박스 드래그로 순서 지정 — WorkPriorityBar)
///   - 필수 소지 설정 (식량/약물 개수 — AI 선제 확보가 이 값을 따름)
///   - 침식 유지 수치 (이 값을 넘으면 세척 시간대에 세척 시설로 감. 세척도 이 값까지만)
///
/// 구역 배정은 이 창에 없습니다 — 하단 바 '구역'으로 옮겼습니다 (ZoneModeBarUI).
/// 직원 한 명씩 고르는 대신 전원 × 구역을 한 화면에서 배정합니다.
///
/// 장착 지시 시 직원이 하던 일을 중단하고 장비 보관소로 이동해 교체합니다.
/// 열기: BottomBar '직원' 버튼 → UIManager.TogglePanel(UIPanelType.EmployeeUI).
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

    [Header("선택 리스트 (장비)")]
    [SerializeField] private TMP_Text poolTitleText;
    [Tooltip("선택 행 템플릿 (비활성)")]
    [SerializeField] private Button poolItemTemplate;

    private const float REFRESH_INTERVAL = 0.5f;

    /// <summary>침식 유지 수치 ± 버튼 1클릭당 변화량</summary>
    private const float EROSION_TARGET_STEP = 5f;

    private static readonly Color ROW_NORMAL   = new Color(0.16f, 0.16f, 0.20f, 1f);
    private static readonly Color ROW_SELECTED = new Color(0.26f, 0.34f, 0.46f, 1f);

    private Employee selected;
    private EquipmentSlot activeSlot;

    /// <summary>선택 리스트가 열려 있는지 (같은 슬롯 버튼을 다시 누르면 닫기 위해)</summary>
    private bool poolOpen;

    private float refreshTimer;

    private readonly List<GameObject> listItems = new List<GameObject>();

    /// <summary>선택 하이라이트를 칠하기 위한 직원 ↔ 행 매핑</summary>
    private readonly Dictionary<Employee, Image> rowByEmployee = new Dictionary<Employee, Image>();
    private readonly List<GameObject> poolItems = new List<GameObject>();

    #region 초기화

    private void Awake()
    {
        if (slotButtons != null)
        {
            for (int i = 0; i < slotButtons.Length && i < displaySlots.Length; i++)
            {
                int idx = i;
                slotButtons[i]?.onClick.AddListener(() => OpenPool(displaySlots[idx]));
            }
        }

        foodMinusButton?.onClick.AddListener(() => AdjustCarry(isFood: true, delta: -1));
        foodPlusButton?.onClick.AddListener(() => AdjustCarry(isFood: true, delta: +1));
        drugMinusButton?.onClick.AddListener(() => AdjustCarry(isFood: false, delta: -1));
        drugPlusButton?.onClick.AddListener(() => AdjustCarry(isFood: false, delta: +1));

        erosionMinusButton?.onClick.AddListener(() => AdjustErosionTarget(-EROSION_TARGET_STEP));
        erosionPlusButton?.onClick.AddListener(() => AdjustErosionTarget(+EROSION_TARGET_STEP));
    }

    public override void OnOpen()
    {
        base.OnOpen();
        RebuildList();
        RefreshDetail();
        workPriorityBar?.Show(selected);
    }

    /// <summary>닫을 때 선택 리스트를 치웁니다 (다시 열었을 때 낡은 행이 남지 않도록).</summary>
    public override void OnClose()
    {
        ClosePool();
        base.OnClose();
    }

    private void Update()
    {
        if (!gameObject.activeSelf) return;

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
        // 목록 행도 같은 이유로 즉시 비활성화 후 파괴 (ClearPoolRows 주석 참고)
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
        ClosePool();
        RefreshListHighlight();
        RefreshDetail();

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
            return;
        }

        if (nameText != null) nameText.text = selected.DisplayName;

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

    #region 선택 리스트 (장비)

    /// <summary>
    /// 슬롯 버튼 — 같은 버튼을 다시 누르면 목록을 닫습니다 (토글).
    /// 다른 슬롯을 누르면 그 슬롯 목록으로 바뀝니다.
    /// </summary>
    private void OpenPool(EquipmentSlot slot)
    {
        if (selected == null) return;

        if (poolOpen && activeSlot == slot) { ClosePool(); return; }

        activeSlot = slot;
        poolOpen = true;
        RebuildEquipmentPool();
    }

    private void ClosePool()
    {
        poolOpen = false;
        ClearPoolRows();
        if (poolTitleText != null) poolTitleText.text = "";
    }

    /// <summary>
    /// 목록 행을 치웁니다.
    ///
    /// Destroy는 프레임 끝에야 실제로 지워지므로, 그때까지 남은 낡은 행이
    /// <b>여전히 클릭 가능</b>합니다. 그 사이 클릭이 들어오면 이전 목록의 항목이
    /// 지금 열린 대상에 적용되는 오작동이 생기므로, 지우기 전에 즉시 비활성화합니다.
    /// </summary>
    private void ClearPoolRows()
    {
        foreach (var go in poolItems)
        {
            if (go == null) continue;
            go.SetActive(false);
            Destroy(go);
        }
        poolItems.Clear();
    }

    private void RebuildEquipmentPool()
    {
        ClearPoolRows();

        if (poolItemTemplate == null) return;

        var mgr = EquipmentStorageManager.instance;
        bool hasArmory = mgr != null && mgr.HasArmory();

        if (poolTitleText != null)
            poolTitleText.text = hasArmory
                ? $"{GetSlotDisplayName(activeSlot)} 선택 — 클릭 시 보관소로 이동해 장착"
                : "⚠ 장비 보관소가 없습니다 (건설 필요)";

        if (mgr == null) return;

        // 첫 행: 장착 해제
        AddPoolRow("[ 장착 해제 ]", () => ApplyEquip(0), hasArmory);

        foreach (var inst in mgr.GetPoolForSlot(activeSlot))
        {
            var data = GameDatabase.Instance?.GetEquipmentData(inst.equipmentId);
            if (data == null) continue;

            string dura = data.indestructible ? "∞" : $"{inst.durability:F0}/{data.maxDurability:F0}";
            int capturedId = inst.instanceId;
            AddPoolRow($"{data.itemData?.itemName} [{dura}]", () => ApplyEquip(capturedId), hasArmory);
        }
    }

    private void AddPoolRow(string text, System.Action onClick, bool interactable)
    {
        var row = Instantiate(poolItemTemplate, poolItemTemplate.transform.parent);
        row.gameObject.SetActive(true);
        row.interactable = interactable;
        var label = row.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = text;
        row.onClick.AddListener(() => onClick());
        poolItems.Add(row.gameObject);
    }

    private void ApplyEquip(int poolInstanceId)
    {
        if (selected == null) return;

        selected.GetComponent<EmployeeAI>()?.RequestEquipChange(activeSlot, poolInstanceId);
        ClosePool();
        RefreshDetail();
    }

    #endregion
}
