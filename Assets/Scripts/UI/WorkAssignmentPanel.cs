using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

/// <summary>
/// 작업 할당 패널 - UIManager를 통해 관리됨
/// WorkSystemManager가 Setup()을 호출하여 초기화
/// </summary>
public class WorkAssignmentPanel : BasePanel
{
    [Header("UI 요소 연결")]
    [SerializeField] private TextMeshProUGUI headerText;
    [SerializeField] private Transform listContainer; // 직원 목록이 생성될 부모 (Vertical Layout Group)
    [SerializeField] private GameObject employeeRowPrefab;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button cancelButton; // 작업 취소 버튼

    [Header("페이지 네비게이션")]
    [SerializeField] private Button prevButton; // 왼쪽 화살표 (<)
    [SerializeField] private Button nextButton; // 오른쪽 화살표 (>)
    [SerializeField] private TextMeshProUGUI pageText; // 페이지 번호 (1 / 3) - 선택사항

    [Header("색상 설정")]
    [SerializeField] private Color assignedColor = new Color(0.5f, 1f, 0.5f);
    [SerializeField] private Color unassignedColor = new Color(1f, 1f, 1f);

    // 설정 상수
    private const int ITEMS_PER_PAGE = 4; // 페이지당 직원 수

    private Action<Employee> onEmployeeClickCallback;
    private Action onCloseCallback;
    private Action onCancelWorkCallback;

    private WorkOrder currentOrder;
    private int currentPage = 0;

    void Awake()
    {
        // 버튼 기본 리스너 연결 (Setup 이전에도 동작하도록)
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(OnCloseClicked);
        }

        if (prevButton != null)
        {
            prevButton.onClick.AddListener(OnPrevPage);
        }

        if (nextButton != null)
        {
            nextButton.onClick.AddListener(OnNextPage);
        }
    }

    /// <summary>
    /// 패널이 비활성화될 때 기존 목록을 정리합니다
    /// </summary>
    void OnDisable()
    {
        ClearEmployeeList();
    }

    /// <summary>
    /// WorkSystemManager에서 호출하여 패널을 초기화합니다
    /// </summary>
    public void Setup(WorkOrder order, Action<Employee> onEmployeeClick, Action onClose, Action onCancelWork)
    {
        currentOrder = order;
        onEmployeeClickCallback = onEmployeeClick;
        onCloseCallback = onClose;
        onCancelWorkCallback = onCancelWork;

        currentPage = 0; // 항상 첫 페이지부터 시작

        // 취소 버튼 리스너 연결
        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(() => onCancelWorkCallback?.Invoke());
        }

        RefreshUI();
    }

    /// <summary>
    /// 기존 직원 목록을 정리합니다
    /// </summary>
    private void ClearEmployeeList()
    {
        if (listContainer == null) return;

        foreach (Transform child in listContainer)
        {
            Destroy(child.gameObject);
        }
    }

    private void OnCloseClicked()
    {
        onCloseCallback?.Invoke();
    }

    public void RefreshUI()
    {
        if (currentOrder == null) return;

        UpdateHeader();
        UpdateEmployeeList();
        UpdateNavigationButtons();
    }

    private void UpdateHeader()
    {
        if (headerText != null)
        {
            // 자동 픽업: 현재 작업 중 인원 표시 (자격 직원이면 누구든 가능)
            // 전용 할당: 등록 인원/최대 인원 표시
            string workerInfo = currentOrder.IsAutoPickup
                ? $"[자동] 작업중: {currentOrder.assignedWorkers.Count}명"
                : $"({currentOrder.assignedWorkers.Count}/{currentOrder.maxAssignedWorkers})";

            headerText.text = $"{currentOrder.orderName}\n{workerInfo}";
        }
    }

    private void UpdateEmployeeList()
    {
        // 1. 기존 목록 삭제
        ClearEmployeeList();

        // 2. 데이터 준비
        List<Employee> allEmployees = EmployeeManager.instance.AllEmployees;
        int totalCount = allEmployees.Count;

        // 3. 현재 페이지 범위 계산
        int startIndex = currentPage * ITEMS_PER_PAGE;
        int count = Mathf.Min(ITEMS_PER_PAGE, totalCount - startIndex);

        // 4. 해당 페이지의 직원만 생성
        for (int i = 0; i < count; i++)
        {
            int dataIndex = startIndex + i;
            if (dataIndex >= totalCount) break;

            Employee emp = allEmployees[dataIndex];
            CreateEmployeeRow(emp);
        }
    }

    private void CreateEmployeeRow(Employee emp)
    {
        GameObject row = Instantiate(employeeRowPrefab, listContainer);
        
        // 이름 설정
        TextMeshProUGUI nameText = row.GetComponentInChildren<TextMeshProUGUI>();
        if(nameText != null) nameText.text = emp.Data.employeeName;

        // 배경색 설정 (할당 여부)
        Image bg = row.GetComponent<Image>();
        bool isAssigned = currentOrder.IsWorkerAssigned(emp);
        if (bg != null) bg.color = isAssigned ? assignedColor : unassignedColor;

        // 클릭 이벤트 설정
        Button btn = row.GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.AddListener(() => onEmployeeClickCallback?.Invoke(emp));
        }
    }

    private void UpdateNavigationButtons()
    {
        int totalEmployees = EmployeeManager.instance.AllEmployees.Count;
        // 전체 페이지 수 계산 (올림 처리)
        int maxPage = Mathf.Max(0, Mathf.CeilToInt((float)totalEmployees / ITEMS_PER_PAGE) - 1);

        // 버튼 활성화/비활성화 처리
        if (prevButton != null) prevButton.interactable = (currentPage > 0);
        if (nextButton != null) nextButton.interactable = (currentPage < maxPage);

        // 페이지 텍스트 업데이트 (예: 1 / 3)
        if (pageText != null)
        {
            pageText.text = $"{currentPage + 1} / {maxPage + 1}";
        }
    }

    private void OnNextPage()
    {
        currentPage++;
        RefreshUI();
    }

    private void OnPrevPage()
    {
        currentPage--;
        RefreshUI();
    }
}