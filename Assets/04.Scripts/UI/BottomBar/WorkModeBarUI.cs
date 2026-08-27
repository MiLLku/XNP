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

    private void Awake()
    {
        mineButton?.onClick.AddListener(()     => EnterMode(InteractionManager.InteractMode.Mine));
        harvestButton?.onClick.AddListener(()  => EnterMode(InteractionManager.InteractMode.Harvest));
        buildButton?.onClick.AddListener(()    => EnterMode(InteractionManager.InteractMode.Build));
        demolishButton?.onClick.AddListener(() => EnterMode(InteractionManager.InteractMode.Demolish));
        cleanButton?.onClick.AddListener(()    => EnterMode(InteractionManager.InteractMode.Clean));
        cancelButton?.onClick.AddListener(Hide);
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (InteractionManager.instance != null)
            InteractionManager.instance.OnModeChanged += OnModeChanged;
        RefreshHighlights();
    }

    private void OnDisable()
    {
        if (InteractionManager.instance != null)
            InteractionManager.instance.OnModeChanged -= OnModeChanged;
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
