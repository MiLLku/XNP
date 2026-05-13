using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 하단 HUD 바. 림월드 방식으로 패널을 토글하는 버튼들을 관리합니다.
///
/// 계층 구조:
///   BottomBar
///   └── ButtonContainer  [HorizontalLayoutGroup]
///       ├── WorkBtn      → WorkModeBar 토글
///       └── ResearchBtn  → ResearchTreeUI 토글
/// </summary>
public class BottomBarUI : MonoBehaviour
{
    [Header("버튼 참조")]
    [SerializeField] private Button researchButton;
    [SerializeField] private Button workButton;

    [Header("패널 참조")]
    [SerializeField] private WorkModeBarUI workModeBar;

    private void Awake()
    {
        researchButton?.onClick.AddListener(OnResearchClicked);
        workButton?.onClick.AddListener(OnWorkClicked);
    }

    private void OnResearchClicked()
    {
        UIManager.instance?.TogglePanel(UIPanelType.ResearchTreeUI, isPopup: false);
    }

    private void OnWorkClicked()
    {
        workModeBar?.Toggle();
    }
}
