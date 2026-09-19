using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MessagePipe;

/// <summary>
/// 구역 바. 하단 바의 "구역" 버튼으로 열립니다.
///
/// 열면 <b>배정 표</b>가 뜹니다 — 직원 한 줄에 구역 칸이 나열되고, 칸을 누르면 그 직원이
/// 그 구역 하나에 배정됩니다. 전원의 배정 상태를 한 화면에서 봅니다.
/// (배정은 원래 직원 관리창에 있었지만 여기로 옮겼습니다.)
///
/// 표 우상단:
///   구역 관리 — 오른쪽에 관리 창을 엽니다. 구역마다 확장/축소/삭제
///   구역 생성 — 새 구역을 만들고 곧바로 확장 편집으로 들어갑니다
///
/// 편집 중 맵 조작:
///   좌드래그 — '확장'을 눌렀으면 넣고, '축소'를 눌렀으면 뺍니다
///   우드래그 — 언제나 뺍니다
///
/// 목록이 창을 넘치면 스냅 스크롤로 넘깁니다 (SnapScrollRect).
///
/// 루트는 항상 활성이고 content만 토글합니다 (CombatStanceBarUI와 동일).
/// 루트를 비활성으로 두면 Awake가 첫 활성화까지 지연되어, 그 Awake가 다시 자신을 끄는
/// 순서 문제가 생깁니다.
/// </summary>
public class ZoneModeBarUI : MonoBehaviour
{
    #region 인스펙터

    [Header("토글되는 실제 패널 (루트는 항상 활성)")]
    [Tooltip("이 오브젝트만 켜고 끕니다. 루트를 끄면 Awake가 지연되어 최초 토글이 먹지 않습니다.")]
    [SerializeField] private GameObject content;

    [Header("구역 배정 표")]
    [Tooltip("직원 행이 생성될 부모 (VerticalLayoutGroup)")]
    [SerializeField] private Transform assignListContainer;

    [Tooltip("직원 1명 행 템플릿 (비활성). TMP_Text = 이름, GridLayoutGroup = 구역 칸 자리")]
    [SerializeField] private RectTransform employeeRowTemplate;

    [Tooltip("행 안에 복제되는 구역 칸 템플릿 (비활성)")]
    [SerializeField] private Button zoneCellTemplate;

    [Header("구역 관리 (표 우상단)")]
    [Tooltip("누르면 오른쪽 관리 창을 켜고 끕니다")]
    [SerializeField] private Button manageButton;

    [Tooltip("새 구역을 만듭니다 — 관리 버튼 바로 아래")]
    [SerializeField] private Button createButton;

    [Tooltip("오른쪽 관리 창 (기본은 꺼둡니다)")]
    [SerializeField] private GameObject managePanel;

    [Tooltip("관리 행이 생성될 부모 (VerticalLayoutGroup)")]
    [SerializeField] private Transform manageListContainer;

    [Tooltip("구역 1개 관리 행 템플릿 (비활성)")]
    [SerializeField] private ZoneManageRow manageRowTemplate;

    [Header("공통")]
    [Tooltip("구역 모드를 끄고 바를 닫음")]
    [SerializeField] private Button cancelButton;

    [Tooltip("현재 상태 표시")]
    [SerializeField] private TextMeshProUGUI statusText;

    #endregion

    #region 상태

    private readonly List<GameObject> assignRows = new List<GameObject>();
    private readonly List<GameObject> manageRows = new List<GameObject>();

    /// <summary>구역·모드 메시지 구독 핸들</summary>
    private IDisposable subscriptions;

    internal static readonly Color ColActive   = new Color(0.25f, 0.55f, 1.00f);
    internal static readonly Color ColInactive = new Color(0.14f, 0.14f, 0.18f);

    #endregion

    #region 생명주기

    private void Awake()
    {
        manageButton?.onClick.AddListener(ToggleManagePanel);
        createButton?.onClick.AddListener(OnCreateClicked);
        cancelButton?.onClick.AddListener(Hide);

        if (employeeRowTemplate != null) employeeRowTemplate.gameObject.SetActive(false);
        if (zoneCellTemplate != null) zoneCellTemplate.gameObject.SetActive(false);
        if (manageRowTemplate != null) manageRowTemplate.gameObject.SetActive(false);
        if (managePanel != null) managePanel.SetActive(false);
        if (content != null) content.SetActive(false);
    }

    private void Start()
    {
        subscriptions = DisposableBag.Create(
            GameMessageBus.Subscribe<InteractionModeChangedMessage>(m => OnModeChanged(m.mode)),
            GameMessageBus.Subscribe<EditingZoneChangedMessage>(m => RefreshStatus()),
            GameMessageBus.Subscribe<ZoneTilesChangedMessage>(m => OnZoneTilesChanged()),
            GameMessageBus.Subscribe<ZoneCreatedMessage>(m => OnZoneSetChanged()),
            GameMessageBus.Subscribe<ZoneDeletedMessage>(m => OnZoneSetChanged()));

        RefreshStatus();
    }

    private void OnDestroy()
    {
        subscriptions?.Dispose();
        subscriptions = null;
    }

    private void Update()
    {
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Hide();
    }

    /// <summary>바가 열려 있는지</summary>
    public bool IsOpen => content != null && content.activeSelf;

    #endregion

    #region 열기/닫기

    /// <summary>하단 바 버튼에서 호출 — 바를 열고 닫습니다.</summary>
    public void Toggle()
    {
        if (IsOpen) Hide();
        else
        {
            if (content != null) content.SetActive(true);
            InteractionManager.instance?.SetMode(InteractionManager.InteractMode.Zone);
            if (managePanel != null) managePanel.SetActive(false);
            RebuildAssignList();
            RefreshStatus();
        }
    }

    private void Hide()
    {
        ClearAssignRows();
        ClearManageRows();
        if (managePanel != null) managePanel.SetActive(false);

        if (InteractionManager.instance != null &&
            InteractionManager.instance.GetCurrentMode() == InteractionManager.InteractMode.Zone)
        {
            InteractionManager.instance.SetMode(InteractionManager.InteractMode.Normal);
        }

        if (content != null) content.SetActive(false);
    }

    #endregion

    #region 구역 배정 표

    /// <summary>
    /// 직원 × 구역 배정 표를 다시 그립니다.
    ///
    /// 한 줄이 직원 한 명이고, 줄 안의 칸이 배정 가능한 구역입니다.
    /// 지금 배정된 칸만 밝게 칠합니다 — 직원은 언제나 구역 하나에만 속합니다.
    /// 구역이 많아 줄을 넘치면 칸이 다음 줄로 접힙니다 (GridLayoutGroup이 처리).
    /// </summary>
    private void RebuildAssignList()
    {
        ClearAssignRows();

        if (assignListContainer == null || employeeRowTemplate == null || zoneCellTemplate == null) return;

        var em = EmployeeManager.instance;
        if (em == null) return;

        var zm = ZoneManager.instance;
        var zones = zm != null ? zm.GetAllZones() : new List<Zone>();

        foreach (var emp in em.AllEmployees)
        {
            if (emp == null || emp.State == EmployeeState.Dead) continue;

            var assignment = emp.GetComponent<EmployeeZoneAssignment>();
            if (assignment == null) continue;

            var row = Instantiate(employeeRowTemplate, assignListContainer);
            row.gameObject.SetActive(true);
            assignRows.Add(row.gameObject);

            var nameLabel = row.GetComponentInChildren<TMP_Text>(true);
            if (nameLabel != null) nameLabel.text = emp.DisplayName;

            // 칸이 들어갈 자리 — 없으면 행 자체에 붙인다 (템플릿이 단순할 때)
            var grid = row.GetComponentInChildren<GridLayoutGroup>(true);
            Transform cellParent = grid != null ? grid.transform : row;

            int current = assignment.AssignedZoneId;

            // 첫 칸은 항상 일반(맵 전체) — 지울 수 없는 기본 선택지
            AddCell(cellParent, "일반", EmployeeZoneAssignment.GENERAL_ZONE_ID, current, assignment, emp);

            foreach (var zone in zones)
                AddCell(cellParent, zone.zoneName, zone.zoneId, current, assignment, emp);
        }
    }

    private void AddCell(Transform parent, string label, int zoneId, int currentZoneId,
                         EmployeeZoneAssignment assignment, Employee employee)
    {
        var cell = Instantiate(zoneCellTemplate, parent);
        cell.gameObject.SetActive(true);

        var text = cell.GetComponentInChildren<TMP_Text>(true);
        if (text != null) text.text = label;

        var img = cell.GetComponent<Image>();
        if (img != null) img.color = zoneId == currentZoneId ? ColActive : ColInactive;

        cell.onClick.AddListener(() => Assign(assignment, employee, zoneId));
    }

    /// <summary>칸 클릭 — 이 직원을 이 구역에 배정합니다 (-1 = 일반/맵 전체).</summary>
    private void Assign(EmployeeZoneAssignment assignment, Employee employee, int zoneId)
    {
        if (assignment == null) return;

        assignment.AssignZone(zoneId);

        // 배정이 바뀌면 지금 하던 행동을 다시 판단해야 한다
        // (예: 구역이 좁아졌는데 구역 밖 작업을 계속하고 있으면 안 됨)
        employee?.GetComponent<EmployeeAI>()?.ForceReevaluate();

        RebuildAssignList();   // 하이라이트 갱신
    }

    private void ClearAssignRows() => ClearRows(assignRows);

    #endregion

    #region 구역 관리 창

    /// <summary>'구역 관리' — 오른쪽 관리 창을 켜고 끕니다.</summary>
    private void ToggleManagePanel()
    {
        if (managePanel == null) return;

        bool on = !managePanel.activeSelf;
        managePanel.SetActive(on);

        if (on) RebuildManageList();
        else    ClearManageRows();

        RefreshStatus();
    }

    /// <summary>만든 구역을 전부 한 줄씩 나열합니다 — 확장 / 축소 / 삭제.</summary>
    private void RebuildManageList()
    {
        ClearManageRows();

        if (manageListContainer == null || manageRowTemplate == null) return;

        var zm = ZoneManager.instance;
        if (zm == null) return;

        int editingId = InteractionManager.instance != null
            ? InteractionManager.instance.EditingZoneId : -1;
        var intent = InteractionManager.instance != null
            ? InteractionManager.instance.ZoneIntent
            : InteractionManager.ZoneEditIntent.Expand;

        foreach (var zone in zm.GetAllZones())
        {
            int id = zone.zoneId;

            var row = Instantiate(manageRowTemplate, manageListContainer);
            row.gameObject.SetActive(true);
            manageRows.Add(row.gameObject);

            row.Bind($"{zone.zoneName} ({zone.TileCount}칸)", zone.zoneName,
                     newName => Rename(id, newName),
                     () => StartEdit(id, InteractionManager.ZoneEditIntent.Expand),
                     () => StartEdit(id, InteractionManager.ZoneEditIntent.Shrink),
                     () => RemoveZone(id));

            bool editing = id == editingId;
            row.SetEditing(editing && intent == InteractionManager.ZoneEditIntent.Expand,
                           editing && intent == InteractionManager.ZoneEditIntent.Shrink);
        }
    }

    /// <summary>
    /// 이름 변경 — 배정 표의 구역 칸도 같은 이름을 쓰므로 함께 다시 그립니다.
    /// </summary>
    private void Rename(int zoneId, string newName)
    {
        var zm = ZoneManager.instance;
        if (zm == null) return;

        zm.RenameZone(zoneId, newName);

        RebuildManageList();
        if (IsOpen) RebuildAssignList();
        RefreshStatus();
    }

    /// <summary>확장/축소 — 이 구역을 편집 대상으로 잡고 드래그의 뜻을 정합니다.</summary>
    private void StartEdit(int zoneId, InteractionManager.ZoneEditIntent intent)
    {
        var im = InteractionManager.instance;
        if (im == null) return;

        im.SetMode(InteractionManager.InteractMode.Zone);
        im.SetEditingZone(zoneId);
        im.SetZoneEditIntent(intent);

        RebuildManageList();   // 어느 줄이 편집 중인지 다시 칠한다
        RefreshStatus();
    }

    private void RemoveZone(int zoneId)
    {
        var zm = ZoneManager.instance;
        if (zm == null) return;

        var zone = zm.GetZone(zoneId);
        string name = zone != null ? zone.zoneName : $"#{zoneId}";

        // 지금 편집 중이던 구역이면 편집 상태도 같이 푼다
        if (InteractionManager.instance != null &&
            InteractionManager.instance.EditingZoneId == zoneId)
        {
            InteractionManager.instance.SetEditingZone(-1);
        }

        zm.DeleteZone(zoneId);
        Debug.Log($"[ZoneModeBar] '{name}' 제거");
    }

    /// <summary>새 구역을 만들고 곧바로 확장 편집으로 들어갑니다.</summary>
    private void OnCreateClicked()
    {
        var zm = ZoneManager.instance;
        if (zm == null) return;

        Zone zone = zm.CreateZone();
        StartEdit(zone.zoneId, InteractionManager.ZoneEditIntent.Expand);

        Debug.Log($"[ZoneModeBar] '{zone.zoneName}' 생성 — 맵을 드래그해 영역을 지정하세요");
    }

    private void ClearManageRows() => ClearRows(manageRows);

    #endregion

    #region 목록 행 정리

    /// <summary>
    /// 목록 행을 치웁니다.
    ///
    /// Destroy는 프레임 끝에야 실제로 지워지므로, 그때까지 남은 낡은 행이
    /// <b>여전히 클릭 가능</b>합니다. 지우기 전에 즉시 비활성화합니다.
    /// </summary>
    private static void ClearRows(List<GameObject> rows)
    {
        foreach (var go in rows)
        {
            if (go == null) continue;
            go.SetActive(false);
            Destroy(go);
        }
        rows.Clear();
    }

    #endregion

    #region 갱신

    private void OnModeChanged(InteractionManager.InteractMode mode)
    {
        if (mode != InteractionManager.InteractMode.Zone)
        {
            ClearAssignRows();
            ClearManageRows();
            if (managePanel != null) managePanel.SetActive(false);
            if (content != null) content.SetActive(false);
        }
        else RefreshStatus();
    }

    /// <summary>칸 수가 바뀌면 관리 창의 '몇 칸' 표시가 낡는다.</summary>
    private void OnZoneTilesChanged()
    {
        if (managePanel != null && managePanel.activeSelf) RebuildManageList();
        RefreshStatus();
    }

    /// <summary>구역이 생기거나 사라지면 배정 표의 칸 구성까지 달라진다.</summary>
    private void OnZoneSetChanged()
    {
        if (IsOpen) RebuildAssignList();
        if (managePanel != null && managePanel.activeSelf) RebuildManageList();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        SetHighlight(manageButton, managePanel != null && managePanel.activeSelf);

        if (statusText == null) return;

        var im = InteractionManager.instance;
        var zm = ZoneManager.instance;
        if (im == null || zm == null) { statusText.text = ""; return; }

        Zone editing = zm.GetZone(im.EditingZoneId);
        if (editing != null)
        {
            string what = im.ZoneIntent == InteractionManager.ZoneEditIntent.Expand
                ? "좌드래그 넣기 / 우드래그 빼기"
                : "좌드래그 빼기 (축소 중)";
            statusText.text = $"편집 중: {editing.zoneName} · {editing.TileCount}칸   ({what})";
        }
        else
        {
            int count = zm.GetAllZones().Count;
            statusText.text = count == 0
                ? "구역 없음 — '구역 생성'으로 새 구역을 만드세요"
                : $"구역 {count}개 — '구역 관리'에서 확장·축소·삭제";
        }
    }

    private static void SetHighlight(Button btn, bool active)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = active ? ColActive : ColInactive;
    }

    #endregion
}
