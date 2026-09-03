using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 전투 태세 바.
/// Normal 모드에서 직원을 클릭하면 하단에 표시된다:
///   [이름] [소집/해제] | [점거] [방어] [경계] [카이팅]
///
/// - 태세 버튼은 소집 중 + 가용 태세(CanUseStance)만 활성화된다
///   (방어 = 근접+방어형 장비, 카이팅 = 원거리 무기)
/// - 우클릭 이동 명령은 InteractionManager가 처리
/// - 루트는 항상 활성(선택 이벤트 구독 유지), content만 토글
/// </summary>
public class CombatStanceBarUI : MonoBehaviour
{
    [Header("토글되는 실제 패널 (루트는 항상 활성)")]
    [SerializeField] private GameObject content;

    [Header("표시")]
    [SerializeField] private TMP_Text nameText;

    [Header("소집")]
    [SerializeField] private Button draftButton;
    [SerializeField] private TMP_Text draftButtonText;

    [Header("태세 버튼")]
    [SerializeField] private Button holdButton;
    [SerializeField] private Button defendButton;
    [SerializeField] private Button guardButton;
    [SerializeField] private Button kitingButton;

    private static readonly Color ColActive   = new Color(0.25f, 0.55f, 1.00f);
    private static readonly Color ColInactive = new Color(0.14f, 0.14f, 0.18f);
    private static readonly Color ColDisabled = new Color(0.10f, 0.10f, 0.12f);
    private static readonly Color ColDrafted  = new Color(0.80f, 0.25f, 0.20f);

    private const float REFRESH_INTERVAL = 0.25f;

    private Employee selected;
    private EmployeeCombat combat;
    private EmployeeDraft draft;
    private float refreshTimer;

    /// <summary>직원 선택 메시지 구독 핸들</summary>
    private IDisposable selectionSubscription;

    #region 초기화

    private void Awake()
    {
        draftButton?.onClick.AddListener(OnDraftClicked);
        holdButton?.onClick.AddListener(()   => OnStanceClicked(CombatStance.HoldPosition));
        defendButton?.onClick.AddListener(() => OnStanceClicked(CombatStance.Defend));
        guardButton?.onClick.AddListener(()  => OnStanceClicked(CombatStance.Guard));
        kitingButton?.onClick.AddListener(() => OnStanceClicked(CombatStance.Kiting));

        if (content != null) content.SetActive(false);
    }

    private void Start()
    {
        selectionSubscription = GameMessageBus.Subscribe<EmployeeSelectionChangedMessage>(
            m => OnEmployeeSelected(m.employee));
    }

    private void OnDestroy()
    {
        selectionSubscription?.Dispose();
        selectionSubscription = null;
    }

    #endregion

    #region 선택/갱신

    private void OnEmployeeSelected(Employee employee)
    {
        selected = employee;
        combat   = employee != null ? employee.GetComponent<EmployeeCombat>() : null;
        draft    = employee != null ? employee.GetComponent<EmployeeDraft>() : null;

        if (content != null) content.SetActive(employee != null);
        if (employee != null) Refresh();
    }

    private void Update()
    {
        // 파괴 또는 사망 → 선택 해제 (해제 이벤트가 바를 숨긴다)
        if (selected == null || selected.State == EmployeeState.Dead)
        {
            if (content != null && content.activeSelf)
            {
                content.SetActive(false);
                InteractionManager.instance?.DeselectEmployee();
            }
            return;
        }

        // unscaled — 일시정지 중에도 태세/장비 변화가 바에 반영되도록 (UI 폴링 관례)
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = REFRESH_INTERVAL;
            Refresh(); // 장비 교체·태세 변화 반영
        }
    }

    private void Refresh()
    {
        if (selected == null) return;

        bool drafted = draft != null && draft.IsDrafted;

        if (nameText != null) nameText.text = selected.DisplayName;

        if (draftButtonText != null) draftButtonText.text = drafted ? "해제" : "소집";
        SetButtonColor(draftButton, drafted ? ColDrafted : ColInactive);

        RefreshStanceButton(holdButton,   CombatStance.HoldPosition, drafted);
        RefreshStanceButton(defendButton, CombatStance.Defend,       drafted);
        RefreshStanceButton(guardButton,  CombatStance.Guard,        drafted);
        RefreshStanceButton(kitingButton, CombatStance.Kiting,       drafted);
    }

    private void RefreshStanceButton(Button btn, CombatStance stance, bool drafted)
    {
        if (btn == null) return;

        bool usable = drafted && combat != null && combat.CanUseStance(stance);
        btn.interactable = usable;

        bool current = drafted && combat != null && combat.Stance == stance;
        SetButtonColor(btn, !usable ? ColDisabled : current ? ColActive : ColInactive);
    }

    private static void SetButtonColor(Button btn, Color color)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = color;
    }

    #endregion

    #region 버튼 핸들러

    private void OnDraftClicked()
    {
        if (draft == null) return;
        draft.ToggleDraft();
        Refresh();
    }

    private void OnStanceClicked(CombatStance stance)
    {
        if (combat == null) return;
        if (!combat.SetStance(stance))
            Debug.Log($"[StanceBar] 태세 변경 불가: {stance}");
        Refresh();
    }

    #endregion
}
