using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 작업 모드 선택 바.
/// 하단 바 위에 표시되며, 채광/수확/건설/철거/세척 모드를 선택하고 취소할 수 있습니다.
/// </summary>
public class WorkModeBarUI : MonoBehaviour
{
    [Header("모드 버튼")]
    [SerializeField] private Button mineButton;
    [SerializeField] private Button harvestButton;
    [SerializeField] private Button buildButton;
    [SerializeField] private Button demolishButton;
    [SerializeField] private Button cleanButton;

    [Header("취소")]
    [SerializeField] private Button cancelButton;

    private static readonly Color ColActive   = new Color(0.25f, 0.55f, 1.00f);
    private static readonly Color ColInactive = new Color(0.14f, 0.14f, 0.18f);

    /// <summary>상호작용 모드 메시지 구독 핸들</summary>
    private IDisposable modeSubscription;

    private void Awake()
    {
        mineButton?.onClick.AddListener(()     => EnterMode(InteractionManager.InteractMode.Mine));
        harvestButton?.onClick.AddListener(()  => EnterMode(InteractionManager.InteractMode.Harvest));
        buildButton?.onClick.AddListener(()    => EnterMode(InteractionManager.InteractMode.Build));
        demolishButton?.onClick.AddListener(() => EnterMode(InteractionManager.InteractMode.Demolish));
        cleanButton?.onClick.AddListener(()    => EnterMode(InteractionManager.InteractMode.Clean));
        cancelButton?.onClick.AddListener(Hide);

        // 여기서 자신을 끄면 안 된다.
        // 씬에 비활성으로 저장되어 있으므로 Awake는 '첫 활성화 시점'에 실행되는데,
        // 그때 다시 꺼버리면 첫 클릭이 통째로 무시되어 두 번 눌러야 열렸다.
        // 시작 시 닫힌 상태는 씬에 비활성으로 저장해 두는 것으로 보장한다.
    }

    private void OnEnable()
    {
        modeSubscription = GameMessageBus.Subscribe<InteractionModeChangedMessage>(m => OnModeChanged(m.mode));
        RefreshHighlights();
    }

    private void OnDisable()
    {
        modeSubscription?.Dispose();
        modeSubscription = null;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            Hide();
    }

    public void Toggle()
    {
        if (gameObject.activeSelf) Hide();
        else gameObject.SetActive(true);
    }

    private void EnterMode(InteractionManager.InteractMode mode)
    {
        InteractionManager.instance?.SetMode(mode);
        RefreshHighlights();
    }

    private void Hide()
    {
        InteractionManager.instance?.SetMode(InteractionManager.InteractMode.Normal);
        gameObject.SetActive(false);
    }

    private void OnModeChanged(InteractionManager.InteractMode mode)
    {
        if (mode == InteractionManager.InteractMode.Normal)
            gameObject.SetActive(false);
        else
            RefreshHighlights();
    }

    private void RefreshHighlights()
    {
        var mode = InteractionManager.instance?.GetCurrentMode()
                   ?? InteractionManager.InteractMode.Normal;
        SetHighlight(mineButton,     mode == InteractionManager.InteractMode.Mine);
        SetHighlight(harvestButton,  mode == InteractionManager.InteractMode.Harvest);
        SetHighlight(buildButton,    mode == InteractionManager.InteractMode.Build);
        SetHighlight(demolishButton, mode == InteractionManager.InteractMode.Demolish);
        SetHighlight(cleanButton,    mode == InteractionManager.InteractMode.Clean);
    }

    private static void SetHighlight(Button btn, bool active)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = active ? ColActive : ColInactive;
    }
}
