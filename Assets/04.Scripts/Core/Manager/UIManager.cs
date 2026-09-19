using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// UI 패널 등록 데이터.
/// </summary>
[System.Serializable]
public class UIPanelData
{
    /// <summary>패널 타입</summary>
    public UIPanelType type;

    /// <summary>패널 컴포넌트</summary>
    public BasePanel panel;

    /// <summary>항상 활성화 상태를 유지하는지 여부</summary>
    public bool alwaysActive;
}

/// <summary>
/// UI 매니저.
/// 모든 UI 패널을 관리하며, 열기/닫기/토글/팝업 스택 관리를 담당합니다.
/// </summary>
public class UIManager : DestroySingleton<UIManager>
{
    [SerializeField] private List<UIPanelData> uiList;

    private Dictionary<UIPanelType, BasePanel> _uiDictionary;
    private List<BasePanel> _activePopupList = new List<BasePanel>();

    /// <summary>IsPointerOverUI가 매 클릭마다 재사용하는 버퍼</summary>
    private readonly List<RaycastResult> _raycastResults = new List<RaycastResult>();

    protected override void Awake()
    {
        base.Awake();
        InitializeUIDictionary();
    }

    #region 초기화

    /// <summary>
    /// 패널 리스트를 딕셔너리로 변환하고 초기 상태를 설정합니다.
    /// </summary>
    private void InitializeUIDictionary()
    {
        _uiDictionary = new Dictionary<UIPanelType, BasePanel>();
        foreach (var data in uiList)
        {
            if (data.panel != null && !_uiDictionary.ContainsKey(data.type))
            {
                _uiDictionary.Add(data.type, data.panel);
                if (!data.alwaysActive)
                {
                    data.panel.gameObject.SetActive(false);
                }
            }
        }
    }

    #endregion

    #region 조회

    /// <summary>
    /// 패널 컴포넌트를 타입으로 조회합니다.
    /// </summary>
    /// <typeparam name="T">패널 타입</typeparam>
    /// <param name="type">패널 종류</param>
    /// <returns>패널 컴포넌트 (없으면 null)</returns>
    public T GetPanel<T>(UIPanelType type) where T : BasePanel
    {
        if (_uiDictionary.TryGetValue(type, out BasePanel panel))
        {
            return panel as T;
        }
        return null;
    }

    /// <summary>
    /// 패널이 현재 활성화 상태인지 확인합니다.
    /// </summary>
    /// <param name="type">확인할 패널 종류</param>
    public bool IsPanelActive(UIPanelType type)
    {
        if (_uiDictionary.TryGetValue(type, out BasePanel panel))
        {
            return panel.gameObject.activeSelf;
        }
        return false;
    }

    /// <summary>
    /// 포인터가 UI 위에 있는지 — <b>월드 클릭을 막아야 하는지 묻는 단일 창구</b>입니다.
    /// InteractionManager가 모든 클릭 처리 앞에서 이것을 봅니다.
    ///
    /// raycastTarget이 켜진 UI 그래픽에 하나라도 맞으면 UI로 칩니다.
    /// 예전에는 버튼(Selectable)과 UIClickBlocker만 UI로 쳐서, 패널의 빈 배경을 누르면
    /// 클릭이 월드로 새 뒤에 있던 창고가 열리는 일이 있었습니다.
    ///
    /// 화면을 덮는 장식용 이미지를 새로 넣을 때는 <b>raycastTarget을 꺼야 합니다</b> —
    /// 켜두면 맵 클릭이 전부 막힙니다.
    /// </summary>
    /// <summary>
    /// 포인터가 UI 위인지 (UIManager가 없어도 안전).
    ///
    /// Unity의 <c>OnMouseDown</c>류는 EventSystem을 거치지 않아 UI에 가려져도 그대로 불립니다.
    /// 그런 콜백 첫 줄에서 이걸 보고 빠져나와야 패널 위를 눌렀는데 뒤의 건물이 열리지 않습니다.
    /// </summary>
    public static bool PointerOverUI => instance != null && instance.IsPointerOverUI();

    public bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;

        var eventData = new PointerEventData(EventSystem.current) { position = Input.mousePosition };

        _raycastResults.Clear();
        EventSystem.current.RaycastAll(eventData, _raycastResults);

        foreach (var result in _raycastResults)
        {
            // 카메라에 PhysicsRaycaster가 붙어 있으면 월드 콜라이더도 결과에 섞인다.
            // 그래픽이 붙은 것만 UI로 친다
            if (result.gameObject.GetComponent<UnityEngine.UI.Graphic>() != null) return true;
        }

        return false;
    }

    #endregion

    #region 패널 조작

    /// <summary>
    /// 패널을 엽니다.
    /// </summary>
    /// <param name="type">열 패널 종류</param>
    /// <param name="isPopup">팝업 스택에 추가할지 여부</param>
    public void ShowPanel(UIPanelType type, bool isPopup = true)
    {
        if (_uiDictionary.TryGetValue(type, out BasePanel panel))
        {
            panel.OnOpen();

            if (isPopup && !_activePopupList.Contains(panel))
            {
                _activePopupList.Add(panel);
            }
        }
    }

    /// <summary>
    /// 패널을 닫습니다.
    /// </summary>
    /// <param name="type">닫을 패널 종류</param>
    public void HidePanel(UIPanelType type)
    {
        if (_uiDictionary.TryGetValue(type, out BasePanel panel))
        {
            panel.OnClose();
        }
    }

    /// <summary>
    /// 패널의 표시 상태를 토글합니다.
    /// </summary>
    /// <param name="type">토글할 패널 종류</param>
    /// <param name="isPopup">팝업 스택에 추가할지 여부</param>
    public void TogglePanel(UIPanelType type, bool isPopup = true)
    {
        if (IsPanelActive(type))
        {
            HidePanel(type);
        }
        else
        {
            ShowPanel(type, isPopup);
        }
    }

    /// <summary>
    /// 직원 스킬 트리 패널을 엽니다.
    /// SkillTreePanel에 직접 싱글턴이 없어도 UIManager를 통해 호출할 수 있습니다.
    /// </summary>
    /// <param name="employee">스킬 트리를 열 대상 직원</param>
    public void ShowSkillTree(Employee employee)
    {
        var panel = GetPanel<SkillTreePanel>(UIPanelType.SkillTreeUI);
        if (panel == null)
        {
            Debug.LogWarning("[UIManager] SkillTreePanel이 uiList에 등록되지 않았습니다.");
            return;
        }
        panel.Setup(employee);
        // Setup() 내부에서 OnOpen()을 호출하므로 ShowPanel()은 중복 호출하지 않음
    }

    /// <summary>
    /// 가장 최근에 열린 팝업을 닫습니다 (ESC 입력 시 사용).
    /// </summary>
    /// <returns>닫힌 팝업이 있으면 true</returns>
    public bool CloseTopPopup()
    {
        if (_activePopupList.Count > 0)
        {
            int lastIndex = _activePopupList.Count - 1;
            BasePanel topPanel = _activePopupList[lastIndex];

            topPanel.OnClose();
            _activePopupList.RemoveAt(lastIndex);
            return true;
        }
        return false;
    }

    #endregion
}
