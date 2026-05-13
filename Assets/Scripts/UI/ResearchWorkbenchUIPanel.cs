using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 연구 작업대 상태 및 제어 패널.
/// 화면 우측 50%를 차지하며, 연구 진행/중단 버튼을 제공합니다.
///
/// 사용 흐름:
///   ResearchWorkbench.OnMouseDown()
///     → UIManager.ShowPanel(ResearchWorkbenchUI)
///     → ResearchWorkbenchUIPanel.Setup(bench)
///     → 플레이어가 버튼 클릭 → 연구 시작 or 중단
/// </summary>
public class ResearchWorkbenchUIPanel : BasePanel
{
    #region UI 참조

    [Header("상태 표시")]
    [SerializeField] private TextMeshProUGUI workerNameText;
    [SerializeField] private TextMeshProUGUI researchSpeedText;
    [SerializeField] private TextMeshProUGUI totalPointsText;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("버튼")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button stopButton;
    [SerializeField] private Button closeButton;

    #endregion

    #region 상태

    private ResearchWorkbench _currentBench;

    #endregion

    #region 초기화

    void Awake()
    {
        if (startButton != null)
            startButton.onClick.AddListener(OnStartClicked);

        if (stopButton != null)
            stopButton.onClick.AddListener(OnStopClicked);

        if (closeButton != null)
            closeButton.onClick.AddListener(OnClose);
    }

    #endregion

    #region 패널 열기 / 닫기

    /// <summary>
    /// 연구 작업대와 연결하여 패널을 엽니다.
    /// ResearchWorkbench.OnMouseDown()에서 호출합니다.
    /// </summary>
    public void Setup(ResearchWorkbench bench)
    {
        _currentBench = bench;
    }

    /// <summary>패널 열기 — 상태를 즉시 갱신합니다.</summary>
    public override void OnOpen()
    {
        base.OnOpen();
        RefreshUI();
    }

    /// <summary>패널 닫기 — 연결된 작업대를 해제합니다.</summary>
    public override void OnClose()
    {
        _currentBench = null;
        base.OnClose();
    }

    #endregion

    #region 매 프레임 갱신

    void Update()
    {
        if (!gameObject.activeSelf || _currentBench == null) return;

        // 연구 포인트만 매 프레임 갱신 (나머지는 이벤트로 처리)
        if (totalPointsText != null && ResearchManager.instance != null)
        {
            totalPointsText.text =
                $"누적 포인트: {ResearchManager.instance.TotalResearchPoints:F1}";
        }
    }

    #endregion

    #region UI 갱신

    /// <summary>현재 연구 작업대 상태에 맞게 UI를 갱신합니다.</summary>
    private void RefreshUI()
    {
        if (_currentBench == null) return;

        bool isResearching = _currentBench.IsResearching;
        Employee worker    = _currentBench.CurrentWorker;

        // ── 상태 텍스트 ──
        if (statusText != null)
            statusText.text = isResearching ? "연구 진행 중" : "대기 중";

        // ── 작업자 이름 ──
        if (workerNameText != null)
            workerNameText.text = isResearching && worker != null
                ? $"연구원: {worker.DisplayName}"
                : "연구원: 없음";

        // ── 연구 속도 ──
        if (researchSpeedText != null)
            researchSpeedText.text = isResearching && worker != null
                ? $"연구 속도: x{worker.GetWorkSpeed(WorkType.Research):F2}"
                : "연구 속도: -";

        // ── 누적 포인트 ──
        if (totalPointsText != null && ResearchManager.instance != null)
            totalPointsText.text =
                $"누적 포인트: {ResearchManager.instance.TotalResearchPoints:F1}";

        // ── 버튼 활성화 ──
        if (startButton != null)
            startButton.interactable = !isResearching;

        if (stopButton != null)
            stopButton.interactable = isResearching;
    }

    #endregion

    #region 버튼 핸들러

    /// <summary>연구 진행 버튼 — WorkAssignment 패널로 직원을 선택합니다.</summary>
    private void OnStartClicked()
    {
        if (_currentBench == null || _currentBench.IsResearching) return;

        // 패널을 닫은 뒤 직원 할당 흐름으로 이동
        OnClose();
        _currentBench.RequestWorkerAssignment();
    }

    /// <summary>연구 중단 버튼 — 현재 연구를 즉시 중단합니다.</summary>
    private void OnStopClicked()
    {
        if (_currentBench == null || !_currentBench.IsResearching) return;

        _currentBench.StopResearch();
        RefreshUI();
    }

    #endregion
}
