using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 직원 채용 패널.
/// 후보 카드 3장 중 하나를 토글 선택하고, 하단 "고용" 버튼으로 채용합니다.
///
/// 저장 위치: Assets/Scripts/UI/HiringPanel.cs
///
/// [인스펙터 설정]
///   panelType          → HiringUI
///   titleText          → 제목 TMP
///   cardContainer      → 카드 3장 부모 (Horizontal Layout Group)
///   employeeCardPrefab → 카드 프리팹 (자식: Portrait/Image, NameText/TMP, StatsText/TMP, SelectionBorder/Image)
///   hireButton         → 하단 고용 버튼 (기본 비활성화)
///   closeButton        → 우측 상단 닫기 버튼
/// </summary>
public class HiringPanel : BasePanel
{
    #region UI 레퍼런스

    [Header("UI 요소")]
    [SerializeField] private TextMeshProUGUI titleText;

    [Tooltip("카드 3장이 생성될 부모 Transform (Horizontal Layout Group 권장)")]
    [SerializeField] private Transform cardContainer;

    [Tooltip("직원 카드 프리팹 (자식: Portrait, NameText, StatsText, SelectionBorder)")]
    [SerializeField] private GameObject employeeCardPrefab;

    [SerializeField] private Button hireButton;
    [SerializeField] private Button closeButton;

    [Header("카드 선택 색상")]
    [SerializeField] private Color selectedColor   = new Color(0.3f, 0.7f, 1f, 1f);
    [SerializeField] private Color unselectedColor = new Color(1f, 1f, 1f, 0f);

    #endregion

    #region 런타임 상태

    private HiringOffice           _hiringOffice;
    private List<EmployeeData>     _candidates;
    private List<GameObject>       _cards = new List<GameObject>();
    private int                    _selectedIndex = -1; // -1 = 없음

    #endregion

    #region 초기화

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseClicked);

        if (hireButton != null)
            hireButton.onClick.AddListener(OnHireClicked);
    }

    private void OnDisable()
    {
        ClearCards();
        _selectedIndex = -1;
        SetHireButtonInteractable(false);
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// InteractionManager에서 호출. UIManager.ShowPanel(HiringUI) 전에 실행할 것.
    /// </summary>
    public void Setup(HiringOffice office, List<EmployeeData> candidates)
    {
        _hiringOffice  = office;
        _candidates    = candidates;
        _selectedIndex = -1;

        Debug.Log($"[HiringPanel] Setup 호출됨. 후보 수: {candidates?.Count ?? 0}");

        if (titleText != null)
            titleText.text = "직원 채용";

        SetHireButtonInteractable(false);
        BuildCards();
    }

    #endregion

    #region 카드 생성/제거

    private void BuildCards()
    {
        ClearCards();

        if (employeeCardPrefab == null)
        {
            Debug.LogError("[HiringPanel] employeeCardPrefab이 연결되지 않았습니다.");
            return;
        }

        if (_candidates == null || _candidates.Count == 0)
        {
            Debug.LogWarning("[HiringPanel] 표시할 직원 후보가 없습니다.");
            return;
        }

        for (int i = 0; i < _candidates.Count; i++)
        {
            int capturedIndex = i;
            GameObject card = Instantiate(employeeCardPrefab, cardContainer);
            SetupCard(card, _candidates[i], capturedIndex);
            _cards.Add(card);
        }
    }

    /// <summary>
    /// 카드 프리팹 자식 이름 규칙:
    ///   "Portrait"        — Image (초상화)
    ///   "NameText"        — TextMeshProUGUI (이름)
    ///   "StatsText"       — TextMeshProUGUI (스탯 요약)
    ///   "SelectionBorder" — Image (선택 하이라이트, alpha=0 → 선택 시 활성)
    /// </summary>
    private void SetupCard(GameObject card, EmployeeData data, int index)
    {
        // 초상화
        Transform portraitTf = card.transform.Find("Portrait");
        if (portraitTf != null)
        {
            Image img = portraitTf.GetComponent<Image>();
            if (img != null && data.portrait != null)
                img.sprite = data.portrait;
        }

        // 이름
        Transform nameTf = card.transform.Find("NameText");
        if (nameTf != null)
        {
            TextMeshProUGUI lbl = nameTf.GetComponent<TextMeshProUGUI>();
            if (lbl != null) lbl.text = data.employeeName;
        }

        // 스탯 요약
        Transform statsTf = card.transform.Find("StatsText");
        if (statsTf != null)
        {
            TextMeshProUGUI lbl = statsTf.GetComponent<TextMeshProUGUI>();
            if (lbl != null)
                lbl.text = $"HP: {data.maxHealth}\n정신력: {data.maxMental}\n공격력: {data.attackPower}";
        }

        // 선택 하이라이트 초기화
        SetCardHighlight(card, false);

        // 카드 버튼 — 카드 전체를 클릭 가능하게
        Button btn = card.GetComponent<Button>();
        if (btn != null)
            btn.onClick.AddListener(() => OnCardClicked(index));
    }

    private void ClearCards()
    {
        foreach (GameObject card in _cards)
            if (card != null) Destroy(card);
        _cards.Clear();
    }

    #endregion

    #region 토글 선택

    private void OnCardClicked(int index)
    {
        // 이미 선택된 카드를 다시 클릭 → 선택 해제
        if (_selectedIndex == index)
        {
            SetCardHighlight(_cards[index], false);
            _selectedIndex = -1;
            SetHireButtonInteractable(false);
            return;
        }

        // 이전 선택 해제
        if (_selectedIndex >= 0 && _selectedIndex < _cards.Count)
            SetCardHighlight(_cards[_selectedIndex], false);

        // 새 선택
        _selectedIndex = index;
        SetCardHighlight(_cards[index], true);
        SetHireButtonInteractable(true);
    }

    private void SetCardHighlight(GameObject card, bool on)
    {
        Transform borderTf = card.transform.Find("SelectionBorder");
        if (borderTf != null)
        {
            Image border = borderTf.GetComponent<Image>();
            if (border != null)
                border.color = on ? selectedColor : unselectedColor;
        }
    }

    private void SetHireButtonInteractable(bool interactable)
    {
        if (hireButton != null)
            hireButton.interactable = interactable;
    }

    #endregion

    #region 버튼 핸들러

    private void OnHireClicked()
    {
        if (_hiringOffice == null || _hiringOffice.IsUsed) return;
        if (_selectedIndex < 0 || _selectedIndex >= _candidates.Count) return;

        EmployeeData data = _candidates[_selectedIndex];

        if (EmployeeManager.instance == null)
        {
            Debug.LogError("[HiringPanel] EmployeeManager 인스턴스가 없습니다.");
            return;
        }

        Vector3 spawnPos = _hiringOffice.GetSpawnPoint();
        Employee hired = EmployeeManager.instance.SpawnEmployee(data, spawnPos);

        if (hired != null)
            Debug.Log($"[HiringPanel] '{data.employeeName}' 채용 완료. 스폰 위치: {spawnPos}");

        _hiringOffice.MarkUsed(data); // 미선택 런타임 인스턴스 해제
        UIManager.instance?.HidePanel(UIPanelType.HiringUI);
    }

    private void OnCloseClicked()
    {
        UIManager.instance?.HidePanel(UIPanelType.HiringUI);
    }

    #endregion

    #region BasePanel 오버라이드

    public override void OnOpen()  => base.OnOpen();
    public override void OnClose() => base.OnClose();

    #endregion
}
