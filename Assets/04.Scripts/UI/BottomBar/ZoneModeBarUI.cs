using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 구역 조작 바. 하단 바의 "구역" 버튼으로 열립니다.
///
/// 버튼 3개:
///   생성 — 새 구역을 만들고 곧바로 편집 상태로 들어갑니다 (맵을 드래그해 영역 지정)
///   조정 — 만든 구역 목록이 뜨고, '조정하기'를 누르면 그 구역을 편집합니다
///   제거 — 목록에서 '제거하기'를 누르면 삭제합니다
///
/// 편집 중 맵 조작:
///   좌드래그 — 구역 확장
///   우드래그 — 구역 축소
///
/// 구역 자체에는 용도가 없습니다. 의미는 직원 관리창에서 직원에게 배정할 때 생깁니다.
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

    [Header("조작 버튼")]
    [SerializeField] private Button createButton;
    [SerializeField] private Button adjustButton;
    [SerializeField] private Button removeButton;

    [Tooltip("구역 모드를 끄고 바를 닫음")]
    [SerializeField] private Button cancelButton;

    [Header("구역 목록")]
    [Tooltip("목록 행이 생성될 부모")]
    [SerializeField] private Transform listContainer;

    [Tooltip("복제할 목록 행 템플릿 (비활성). 행 안의 Button이 '조정하기/제거하기' 역할을 합니다.")]
    [SerializeField] private Button listItemTemplate;

    [Tooltip("현재 상태 표시")]
    [SerializeField] private TextMeshProUGUI statusText;

    #endregion

    #region 상태

    /// <summary>목록이 지금 무엇을 위한 것인지</summary>
    private enum ListMode { None, Adjust, Remove }

    private ListMode listMode = ListMode.None;

    private readonly List<GameObject> listItems = new List<GameObject>();

    private static readonly Color ColActive   = new Color(0.25f, 0.55f, 1.00f);
    private static readonly Color ColInactive = new Color(0.14f, 0.14f, 0.18f);

    #endregion

    #region 생명주기

    private void Awake()
    {
        createButton?.onClick.AddListener(OnCreateClicked);
        adjustButton?.onClick.AddListener(() => ToggleList(ListMode.Adjust));
        removeButton?.onClick.AddListener(() => ToggleList(ListMode.Remove));
        cancelButton?.onClick.AddListener(Hide);

        if (listItemTemplate != null) listItemTemplate.gameObject.SetActive(false);
        if (content != null) content.SetActive(false);
    }

    private void Start()
    {
        var im = InteractionManager.instance;
        if (im != null)
        {
            im.OnModeChanged        += OnModeChanged;
            im.OnEditingZoneChanged += OnEditingZoneChanged;
        }

        var zm = ZoneManager.instance;
        if (zm != null)
        {
            zm.OnZoneTilesChanged += OnZoneTilesChanged;
            zm.OnZoneCreated      += OnZoneCreated;
            zm.OnZoneDeleted      += OnZoneTilesChanged;
        }

        RefreshStatus();
    }

    private void OnDestroy()
    {
        var im = InteractionManager.instance;
        if (im != null)
        {
            im.OnModeChanged        -= OnModeChanged;
            im.OnEditingZoneChanged -= OnEditingZoneChanged;
        }

        var zm = ZoneManager.instance;
        if (zm != null)
        {
            zm.OnZoneTilesChanged -= OnZoneTilesChanged;
            zm.OnZoneCreated      -= OnZoneCreated;
            zm.OnZoneDeleted      -= OnZoneTilesChanged;
        }
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
            ClearList();
            RefreshStatus();
        }
    }

    private void Hide()
    {
        ClearList();

        if (InteractionManager.instance != null &&
            InteractionManager.instance.GetCurrentMode() == InteractionManager.InteractMode.Zone)
        {
            InteractionManager.instance.SetMode(InteractionManager.InteractMode.Normal);
        }

        if (content != null) content.SetActive(false);
    }

    #endregion

    #region 생성 / 조정 / 제거

    /// <summary>새 구역을 만들고 곧바로 편집 상태로 들어갑니다.</summary>
    private void OnCreateClicked()
    {
        var zm = ZoneManager.instance;
        if (zm == null) return;

        ClearList();

        Zone zone = zm.CreateZone();
        InteractionManager.instance?.SetMode(InteractionManager.InteractMode.Zone);
        InteractionManager.instance?.SetEditingZone(zone.zoneId);

        Debug.Log($"[ZoneModeBar] '{zone.zoneName}' 생성 — 맵을 드래그해 영역을 지정하세요");
        RefreshStatus();
    }

    /// <summary>같은 버튼을 다시 누르면 목록을 닫습니다.</summary>
    private void ToggleList(ListMode mode)
    {
        if (listMode == mode) { ClearList(); RefreshStatus(); return; }

        listMode = mode;
        RebuildList();
        RefreshStatus();
    }

    private void RebuildList()
    {
        ClearRows();

        if (listMode == ListMode.None || listItemTemplate == null || listContainer == null) return;

        var zm = ZoneManager.instance;
        if (zm == null) return;

        var zones = zm.GetAllZones();
        if (zones.Count == 0)
        {
            AddRow("만든 구역이 없습니다 — '생성'을 먼저 누르세요", null, null, false);
            return;
        }

        bool isAdjust = listMode == ListMode.Adjust;
        string actionLabel = isAdjust ? "조정하기" : "제거하기";

        foreach (var zone in zones)
        {
            int capturedId = zone.zoneId;
            AddRow($"{zone.zoneName}  ({zone.TileCount}칸)", actionLabel, () =>
            {
                if (isAdjust) StartAdjust(capturedId);
                else          RemoveZone(capturedId);
            }, true);
        }
    }

    private void StartAdjust(int zoneId)
    {
        InteractionManager.instance?.SetMode(InteractionManager.InteractMode.Zone);
        InteractionManager.instance?.SetEditingZone(zoneId);

        ClearList();
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

        RebuildList(); // 목록은 열어둔 채 갱신
        RefreshStatus();
    }

    #endregion

    #region 목록 행

    /// <summary>
    /// 목록 행을 하나 추가합니다.
    /// 행 전체가 Button이며, 라벨은 "구역 이름 — [조정하기]" 형태로 한 줄에 표시합니다.
    /// </summary>
    private void AddRow(string text, string actionLabel, System.Action onClick, bool interactable)
    {
        var row = Instantiate(listItemTemplate, listContainer);
        row.gameObject.SetActive(true);
        row.interactable = interactable;

        var label = row.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.text = string.IsNullOrEmpty(actionLabel) ? text : $"{text}   ▶ {actionLabel}";

        if (onClick != null) row.onClick.AddListener(() => onClick());

        listItems.Add(row.gameObject);
    }

    /// <summary>
    /// 목록 행을 치웁니다.
    ///
    /// Destroy는 프레임 끝에야 실제로 지워지므로, 그때까지 남은 낡은 행이
    /// <b>여전히 클릭 가능</b>합니다. 지우기 전에 즉시 비활성화합니다.
    /// </summary>
    private void ClearRows()
    {
        foreach (var go in listItems)
        {
            if (go == null) continue;
            go.SetActive(false);
            Destroy(go);
        }
        listItems.Clear();
    }

    private void ClearList()
    {
        listMode = ListMode.None;
        ClearRows();
    }

    #endregion

    #region 갱신

    private void OnModeChanged(InteractionManager.InteractMode mode)
    {
        if (mode != InteractionManager.InteractMode.Zone)
        {
            ClearList();
            if (content != null) content.SetActive(false);
        }
        else RefreshStatus();
    }

    private void OnEditingZoneChanged(int zoneId) => RefreshStatus();
    private void OnZoneTilesChanged(int zoneId)   { if (listMode != ListMode.None) RebuildList(); RefreshStatus(); }
    private void OnZoneCreated(Zone zone)         => RefreshStatus();

    private void RefreshStatus()
    {
        SetHighlight(adjustButton, listMode == ListMode.Adjust);
        SetHighlight(removeButton, listMode == ListMode.Remove);

        if (statusText == null) return;

        var im = InteractionManager.instance;
        var zm = ZoneManager.instance;
        if (im == null || zm == null) { statusText.text = ""; return; }

        Zone editing = zm.GetZone(im.EditingZoneId);
        if (editing != null)
        {
            statusText.text = $"편집 중: {editing.zoneName} · {editing.TileCount}칸   " +
                              $"(좌드래그 확장 / 우드래그 축소)";
        }
        else
        {
            int count = zm.GetAllZones().Count;
            statusText.text = count == 0
                ? "구역 없음 — '생성'으로 새 구역을 만드세요"
                : $"구역 {count}개 — '조정'으로 고칠 구역을 고르세요";
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
