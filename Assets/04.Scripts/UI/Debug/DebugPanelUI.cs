using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 디버그 패널 UI (림월드 디버그 메뉴 형태).
/// 좌측 카테고리 → 우측 목록 구조이며, 목록의 각 줄은 프리팹 안에 들어 있는
/// <b>템플릿을 복제</b>해 만듭니다. UI 오브젝트를 코드로 생성하지 않습니다.
///
/// 실제 동작은 전부 <see cref="DebugManager"/>가 수행하고, 이 클래스는 표시만 담당합니다.
/// </summary>
public class DebugPanelUI : BasePanel
{
    /// <summary>패널의 좌측 카테고리</summary>
    public enum DebugCategory
    {
        /// <summary>차단 스위치</summary>
        Block,
        /// <summary>즉시 실행</summary>
        Action,
        /// <summary>자원 지급</summary>
        Resource,
    }

    #region 인스펙터

    [Header("공통")]
    [SerializeField] private Button closeButton;
    [SerializeField] private TextMeshProUGUI headerText;

    [Header("카테고리 탭")]
    [SerializeField] private Button blockTabButton;
    [SerializeField] private Button actionTabButton;
    [SerializeField] private Button resourceTabButton;
    [SerializeField] private Color tabNormalColor   = new Color(0.20f, 0.22f, 0.28f, 1f);
    [SerializeField] private Color tabSelectedColor = new Color(0.30f, 0.45f, 0.65f, 1f);

    [Header("목록")]
    [Tooltip("복제된 줄이 들어갈 부모 (VerticalLayoutGroup)")]
    [SerializeField] private Transform contentRoot;

    [Header("줄 템플릿 (프리팹 내부, 비활성 상태로 둘 것)")]
    [SerializeField] private DebugToggleRow toggleRowTemplate;
    [SerializeField] private DebugActionRow actionRowTemplate;
    [SerializeField] private DebugResourceRow resourceRowTemplate;

    #endregion

    #region 상태

    private DebugCategory currentCategory = DebugCategory.Block;
    private readonly List<GameObject> spawnedRows = new List<GameObject>();
    private readonly List<DebugResourceRow> resourceRows = new List<DebugResourceRow>();

    /// <summary>인벤토리 변경 메시지 구독 핸들</summary>
    private IDisposable _inventorySubscription;

    #endregion

    #region 초기화

    void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(() => UIManager.instance?.HidePanel(UIPanelType.DebugUI));

        BindTab(blockTabButton, DebugCategory.Block);
        BindTab(actionTabButton, DebugCategory.Action);
        BindTab(resourceTabButton, DebugCategory.Resource);

        // 템플릿은 항상 꺼둔 채로 복제 원본으로만 쓴다
        SetTemplateActive(toggleRowTemplate, false);
        SetTemplateActive(actionRowTemplate, false);
        SetTemplateActive(resourceRowTemplate, false);
    }

    void OnEnable()
    {
        _inventorySubscription = GameMessageBus.Subscribe<InventoryChangedMessage>(
            m => OnInventoryChanged(m.item, m.changeAmount));
    }

    void OnDisable()
    {
        _inventorySubscription?.Dispose();
        _inventorySubscription = null;
    }

    private void BindTab(Button button, DebugCategory category)
    {
        if (button == null) return;
        button.onClick.AddListener(() => SelectCategory(category));
    }

    private void SetTemplateActive(MonoBehaviour template, bool active)
    {
        if (template != null) template.gameObject.SetActive(active);
    }

    #endregion

    #region BasePanel

    public override void OnOpen()
    {
        base.OnOpen();
        SelectCategory(currentCategory);
    }

    public override void OnClose()
    {
        ClearRows();
        base.OnClose();
    }

    #endregion

    #region 목록 구성

    /// <summary>카테고리를 바꾸고 목록을 다시 그립니다.</summary>
    public void SelectCategory(DebugCategory category)
    {
        currentCategory = category;
        UpdateTabColors();
        ClearRows();

        switch (category)
        {
            case DebugCategory.Block:    BuildBlockRows();    break;
            case DebugCategory.Action:   BuildActionRows();   break;
            case DebugCategory.Resource: BuildResourceRows(); break;
        }
    }

    private void UpdateTabColors()
    {
        SetTabColor(blockTabButton,    currentCategory == DebugCategory.Block);
        SetTabColor(actionTabButton,   currentCategory == DebugCategory.Action);
        SetTabColor(resourceTabButton, currentCategory == DebugCategory.Resource);

        if (headerText != null)
        {
            switch (currentCategory)
            {
                case DebugCategory.Block:    headerText.text = "차단 스위치 — 켜면 해당 현상이 발생하지 않습니다"; break;
                case DebugCategory.Action:   headerText.text = "즉시 실행"; break;
                case DebugCategory.Resource: headerText.text = "자원 지급 — 전역 인벤토리에 추가됩니다"; break;
            }
        }
    }

    private void SetTabColor(Button button, bool selected)
    {
        if (button == null) return;

        Image image = button.GetComponent<Image>();
        if (image != null) image.color = selected ? tabSelectedColor : tabNormalColor;
    }

    /// <summary>차단 스위치 목록</summary>
    private void BuildBlockRows()
    {
        if (toggleRowTemplate == null) return;

        DebugManager manager = DebugManager.instance;
        foreach (DebugFlag flag in DebugFlagInfo.All)
        {
            DebugToggleRow row = Instantiate(toggleRowTemplate, contentRoot);
            row.gameObject.SetActive(true);
            row.Setup(flag, manager != null && manager.HasFlag(flag));
            spawnedRows.Add(row.gameObject);
        }
    }

    /// <summary>즉시 실행 목록</summary>
    private void BuildActionRows()
    {
        if (actionRowTemplate == null) return;

        DebugManager manager = DebugManager.instance;
        if (manager == null) return;

        AddAction("무작위 이벤트 발생",   "F5와 동일합니다.",                       manager.TriggerRandomEvent);
        AddAction("제놉스 등장",         "F6과 동일합니다. 차단 중이면 무시됩니다.", manager.TriggerXenopsSpawn);
        AddAction("무작위 레이드 시작",   "외부 침략을 즉시 시작합니다.",            manager.StartRandomRaid);
        AddAction("레이드 강제 종료",     "진행 중인 침략을 끝냅니다.",              manager.EndActiveRaid);
        AddAction("정신 이상 전부 해제",  "모든 직원의 진행 중인 이상을 해제합니다.", manager.ClearAllMentalBreaks);
        AddAction("침식 전부 초기화",     "모든 직원의 침식을 0으로 만듭니다.",       manager.ClearAllErosion);
        AddAction("욕구 전부 회복",       "허기·기력·재미를 가득 채웁니다.",         manager.RefillAllNeeds);
        AddAction("체력 전부 회복",       "모든 직원의 체력을 가득 채웁니다.",        manager.HealAllEmployees);
        AddAction("차단 전부 해제",       "켜둔 차단 스위치를 모두 끕니다.",          manager.ClearAllFlags);
        AddAction("방 오버레이",         "밀폐된 공간을 색으로 칠해 보여줍니다.",     manager.ToggleRoomOverlay);
        AddAction("방 재계산",           "방을 지금 다시 계산하고 소요 시간을 찍습니다.", manager.RebuildRooms);
        AddAction("오버레이 모드 전환",   "방 번호 색 ↔ 온도 색.",                    manager.CycleRoomOverlayMode);
        AddAction("방 온도 출력",         "방마다 온도와 누출계수를 콘솔에 찍습니다.",   manager.PrintRoomTemperatures);
        AddAction("방 침식 출력",         "방 침식·실외 기본 침식·발원지 수를 찍습니다.", manager.PrintRoomErosion);
        AddAction("직원 온도 출력",       "직원별 체감 온도·견딤 범위·단계를 찍습니다.", manager.PrintEmployeeTemperatures);
        AddAction("계절 정보 출력",       "계절·일교차·실외 온도를 찍습니다.",         manager.PrintSeason);
        AddAction("다음 계절로",          "다음 계절 첫날로 건너뜁니다.",              manager.SkipToNextSeason);
        AddAction("한파 on/off",         "실외 -25도 (300초). 실내는 벽을 통해 서서히 끌려갑니다.", manager.ToggleColdSnap);
        AddAction("폭염 on/off",         "실외 +25도 (300초).",                       manager.ToggleHeatWave);
    }

    private void AddAction(string label, string description, System.Action onClick)
    {
        DebugActionRow row = Instantiate(actionRowTemplate, contentRoot);
        row.gameObject.SetActive(true);
        row.Setup(label, description, onClick);
        spawnedRows.Add(row.gameObject);
    }

    /// <summary>자원 지급 목록 — GameDatabase에 등록된 모든 아이템</summary>
    private void BuildResourceRows()
    {
        if (resourceRowTemplate == null) return;

        if (GameDatabase.Instance == null)
        {
            Debug.LogWarning("[DebugPanelUI] GameDatabase가 없어 자원 목록을 만들 수 없습니다.");
            return;
        }

        foreach (ItemData item in GameDatabase.Instance.GetAllItemData())
        {
            if (item == null) continue;

            DebugResourceRow row = Instantiate(resourceRowTemplate, contentRoot);
            row.gameObject.SetActive(true);
            row.Setup(item);
            spawnedRows.Add(row.gameObject);
            resourceRows.Add(row);
        }
    }

    private void ClearRows()
    {
        foreach (GameObject row in spawnedRows)
        {
            if (row != null) Destroy(row);
        }
        spawnedRows.Clear();
        resourceRows.Clear();
    }

    private void OnInventoryChanged(ItemData item, int changeAmount)
    {
        foreach (DebugResourceRow row in resourceRows)
        {
            if (row != null) row.Refresh();
        }
    }

    #endregion
}
