using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 하단 HUD 바. 림월드 방식으로 패널을 토글하는 버튼들을 관리합니다.
///
/// 계층 구조:
///   BottomBar
///   └── ButtonContainer  [HorizontalLayoutGroup]
///       ├── WorkBtn        → WorkModeBar 토글
///       ├── ResearchBtn    → ResearchTreeUI 토글
///       └── SpawnEventBtn  → XenopsAppearance 이벤트 강제 발동 (디버그/테스트용)
/// </summary>
public class BottomBarUI : MonoBehaviour
{
    [Header("버튼 참조")]
    [SerializeField] private Button researchButton;
    [SerializeField] private Button workButton;

    [Tooltip("직원 작업 일정(스케줄) 편집 패널 토글 버튼")]
    [SerializeField] private Button scheduleButton;

    [Header("이벤트 강제 발동 버튼 (테스트용)")]
    [Tooltip("클릭 시 XenopsAppearance 카테고리 이벤트를 조건 무시하고 즉시 발동합니다.")]
    [SerializeField] private Button spawnEventButton;

    [Header("패널 참조")]
    [SerializeField] private WorkModeBarUI workModeBar;

    private void Awake()
    {
        researchButton?.onClick.AddListener(OnResearchClicked);
        workButton?.onClick.AddListener(OnWorkClicked);
        scheduleButton?.onClick.AddListener(OnScheduleClicked);
        spawnEventButton?.onClick.AddListener(OnSpawnEventClicked);
    }

    private void OnResearchClicked()
    {
        UIManager.instance?.TogglePanel(UIPanelType.ResearchTreeUI, isPopup: false);
    }

    private void OnScheduleClicked()
    {
        UIManager.instance?.TogglePanel(UIPanelType.ScheduleUI, isPopup: false);
    }

    private void OnWorkClicked()
    {
        workModeBar?.Toggle();
    }

    /// <summary>
    /// XenopsAppearance 이벤트를 즉시 강제 발동합니다.
    /// EventManager.allEvents 에 등록된 이벤트가 없으면 폴백으로 랜덤 제노프스를 스폰합니다.
    /// </summary>
    private void OnSpawnEventClicked()
    {
        if (EventManager.instance == null)
        {
            Debug.LogWarning("[BottomBarUI] EventManager가 없습니다.");
            return;
        }
        EventManager.instance.ForceSpawnXenopsEvent();
    }
}
