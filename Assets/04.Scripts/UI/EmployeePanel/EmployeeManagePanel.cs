using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 직원 관리 패널. 좌측 직원 목록 + 우측 상세:
///   - 현재 상태 (체력/침식/재미/피로)
///   - 장비 슬롯 (무기/방어구 — 클릭 → 보관소 보유 장비 리스트 → 클릭 장착 지시)
///   - 구역 배정 (클릭 → 구역 리스트 → 클릭 배정. 기본값은 일반 = 맵 전체)
///   - 필수 소지 설정 (식량/약물 개수 — AI 선제 확보가 이 값을 따름)
///
/// 하단의 선택 리스트는 장비와 구역이 공용으로 씁니다 (PoolMode로 구분).
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

    [Header("구역 배정")]
    [Tooltip("클릭 시 아래 선택 리스트에 구역 목록이 뜹니다")]
    [SerializeField] private Button zoneButton;

    [Header("선택 리스트 (장비/구역 공용)")]
    [SerializeField] private TMP_Text poolTitleText;
    [Tooltip("선택 행 템플릿 (비활성)")]
    [SerializeField] private Button poolItemTemplate;

    private const float REFRESH_INTERVAL = 0.5f;

    /// <summary>선택 리스트가 지금 무엇을 고르는 중인지</summary>
    private enum PoolMode { None, Equipment, Zone }

    private Employee selected;
    private EquipmentSlot activeSlot;
    private PoolMode poolMode = PoolMode.None;
    private float refreshTimer;

    private readonly List<GameObject> listItems = new List<GameObject>();
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

        zoneButton?.onClick.AddListener(OpenZonePool);

        foodMinusButton?.onClick.AddListener(() => AdjustCarry(isFood: true, delta: -1));
        foodPlusButton?.onClick.AddListener(() => AdjustCarry(isFood: true, delta: +1));
        drugMinusButton?.onClick.AddListener(() => AdjustCarry(isFood: false, delta: -1));
        drugPlusButton?.onClick.AddListener(() => AdjustCarry(isFood: false, delta: +1));
    }

    public override void OnOpen()
    {
        base.OnOpen();
        RebuildList();
        RefreshDetail();
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
        }

        // 선택 직원이 죽었거나 없으면 첫 직원 선택
        if (selected == null || selected.State == EmployeeState.Dead)
            selected = firstAlive;
    }

    private void Select(Employee emp)
    {
        selected = emp;
        ClosePool();
        RefreshDetail();
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
        RefreshZoneLabels();
        RefreshCarryLabels();
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

    /// <summary>
    /// 구역 버튼에 현재 배정 상태를 씁니다.
    /// 예: "활동 구역: 구역 2 (48칸)" / "활동 구역: 일반 (맵 전체)"
    /// </summary>
    private void RefreshZoneLabels()
    {
        var label = zoneButton != null ? zoneButton.GetComponentInChildren<TMP_Text>() : null;
        if (label == null || selected == null) return;

        var assignment = selected.GetComponent<EmployeeZoneAssignment>();

        // AssignedZone은 삭제된 구역을 자동으로 일반으로 되돌리므로 별도 정리가 필요 없다
        Zone zone = assignment != null ? assignment.AssignedZone : null;

        label.text = zone == null
            ? $"활동 구역: {EmployeeZoneAssignment.GENERAL_ZONE_NAME}"
            : $"활동 구역: {zone.zoneName} ({zone.TileCount}칸)";
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

    #region 선택 리스트 (장비/구역 공용)

    private void OpenPool(EquipmentSlot slot)
    {
        if (selected == null) return;
        activeSlot = slot;
        poolMode = PoolMode.Equipment;
        RebuildPool();
    }

    /// <summary>구역 버튼 클릭 — 선택 리스트에 배정 가능한 구역을 띄웁니다.</summary>
    private void OpenZonePool()
    {
        if (selected == null) return;
        poolMode = PoolMode.Zone;
        RebuildPool();
    }

    private void ClosePool()
    {
        poolMode = PoolMode.None;
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

    private void RebuildPool()
    {
        ClearPoolRows();

        if (poolItemTemplate == null) return;

        switch (poolMode)
        {
            case PoolMode.Equipment: RebuildEquipmentPool(); break;
            case PoolMode.Zone:      RebuildZonePool();      break;
        }
    }

    /// <summary>
    /// 이 직원에게 배정할 구역 목록.
    /// 첫 줄은 항상 일반(맵 전체) — 삭제할 수 없는 기본 선택지입니다.
    /// </summary>
    private void RebuildZonePool()
    {
        var zm = ZoneManager.instance;
        var assignment = selected != null ? selected.GetComponent<EmployeeZoneAssignment>() : null;

        var zones = zm != null ? zm.GetAllZones() : new List<Zone>();

        if (poolTitleText != null)
            poolTitleText.text = zones.Count > 0
                ? "활동 구역 선택 — 배정하면 작업·취침·오락·세척을 그 안에서 해결합니다"
                : "구역이 없습니다 (하단 바 구역 > 생성에서 먼저 만드세요)";

        if (assignment == null) return;

        int currentId = assignment.AssignedZoneId;

        // 첫 행: 일반 (맵 전체) — 항상 존재하고 지울 수 없는 기본값
        AddPoolRow(currentId < 0
                ? $"[ {EmployeeZoneAssignment.GENERAL_ZONE_NAME} ] ◄ 현재"
                : $"[ {EmployeeZoneAssignment.GENERAL_ZONE_NAME} ]",
            () => ApplyZone(EmployeeZoneAssignment.GENERAL_ZONE_ID), true);

        foreach (var zone in zones)
        {
            int capturedId = zone.zoneId;
            string mark = capturedId == currentId ? "  ◄ 현재" : "";
            AddPoolRow($"{zone.zoneName} ({zone.TileCount}칸){mark}", () => ApplyZone(capturedId), true);
        }
    }

    /// <summary>구역 배정을 적용합니다 (-1 = 일반/맵 전체).</summary>
    private void ApplyZone(int zoneId)
    {
        var assignment = selected != null ? selected.GetComponent<EmployeeZoneAssignment>() : null;
        if (assignment == null) { ClosePool(); return; }

        assignment.AssignZone(zoneId);

        // 할당이 바뀌면 지금 하던 행동을 다시 판단해야 한다
        // (예: 작업 구역이 좁아졌는데 구역 밖 작업을 계속하고 있으면 안 됨)
        selected.GetComponent<EmployeeAI>()?.ForceReevaluate();

        ClosePool();
        RefreshDetail();
    }

    private void RebuildEquipmentPool()
    {
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
