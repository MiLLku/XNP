using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 좌상단 보기(오버레이) 토글 바.
/// 손잡이 버튼으로 화면 왼쪽 밖으로 밀어 넣었다 꺼내며, 안의 버튼으로 표시 레이어를 켜고 끈다.
/// 토글: '침식 안개'(월드 표시 레이어), '안내'(가이드라인 패널). 새 토글은 버튼을 복제해 같은 방식으로 붙이면 된다.
/// </summary>
public class OverlayToggleBarUI : MonoBehaviour
{
    [Header("슬라이드")]
    [Tooltip("밀려 들어가는 바. 피벗이 왼쪽이어야 하며, 닫히면 자기 너비만큼 왼쪽으로 나간다.")]
    [SerializeField] private RectTransform bar;
    [SerializeField] private Button handleButton;
    [SerializeField] private TMP_Text handleText;
    [SerializeField] private float slideSpeed = 14f;
    [SerializeField] private bool startOpen = false;

    [Header("토글")]
    [SerializeField] private Button erosionFogButton;
    [SerializeField] private Button guidelineButton;

    private static readonly Color ColOn  = new Color(0.25f, 0.55f, 1.00f);
    private static readonly Color ColOff = new Color(0.14f, 0.14f, 0.18f);

    private bool open;

    private void Awake()
    {
        handleButton?.onClick.AddListener(() => SetOpen(!open));
        erosionFogButton?.onClick.AddListener(OnErosionFogClicked);
        guidelineButton?.onClick.AddListener(() => { GuidelineUI.Visible = !GuidelineUI.Visible; RefreshButtons(); });
    }

    private void Start()
    {
        SetOpen(startOpen);
        if (bar != null) bar.anchoredPosition = new Vector2(TargetX, bar.anchoredPosition.y); // 시작은 애니메이션 없이
        RefreshButtons();
    }

    private float TargetX => open || bar == null ? 0f : -bar.rect.width;

    private void SetOpen(bool value)
    {
        open = value;
        if (handleText != null) handleText.text = open ? "<" : ">";
    }

    private void Update()
    {
        if (bar == null) return;
        Vector2 pos = bar.anchoredPosition;
        pos.x = Mathf.Lerp(pos.x, TargetX, 1f - Mathf.Exp(-slideSpeed * Time.unscaledDeltaTime));
        bar.anchoredPosition = pos;
    }

    private void OnErosionFogClicked()
    {
        var fog = FogOfWarManager.instance;
        if (fog == null) return;
        fog.ErosionFogVisible = !fog.ErosionFogVisible;
        GuidelineManager.MarkFlag("erosion_fog_filter");
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        var fog = FogOfWarManager.instance;
        if (erosionFogButton != null && erosionFogButton.image != null)
            erosionFogButton.image.color = fog != null && fog.ErosionFogVisible ? ColOn : ColOff;
        if (guidelineButton != null && guidelineButton.image != null)
            guidelineButton.image.color = GuidelineUI.Visible ? ColOn : ColOff;
    }
}
