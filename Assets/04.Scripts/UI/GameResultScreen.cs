using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUI;

/// <summary>
/// 승리·패배 결과 화면 — 화면 전체를 어둡게 덮고 가운데에 결과를 띄웁니다.
/// [계속 보기]를 누르면 닫히고 일시정지가 풀립니다 (판정은 다시 하지 않는다).
/// <see cref="PurificationManager"/>가 런타임에 만듭니다 (씬 배선 없음).
/// </summary>
public class GameResultScreen : MonoBehaviour
{
    #region 상수

    private const float WIDTH = 420f;
    private const int CANVAS_ORDER = 200; // 다른 모든 UI 위

    private static readonly Color DIM_BG = new Color(0f, 0f, 0f, 0.6f);
    private static readonly Color VICTORY_COLOR = new Color(0.45f, 0.9f, 0.6f, 1f);
    private static readonly Color DEFEAT_COLOR = new Color(0.95f, 0.4f, 0.4f, 1f);

    #endregion

    private GameObject _root;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _bodyText;

    public static GameResultScreen Create()
    {
        var canvasGo = new GameObject("GameResultScreen", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CANVAS_ORDER;

        var screen = canvasGo.AddComponent<GameResultScreen>();
        screen.Build();
        screen.Hide();
        return screen;
    }

    private void Build()
    {
        // 전체 덮개 — raycastTarget을 켜 두어 결과 화면 뒤의 월드·UI 클릭을 막는다
        _root = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        _root.transform.SetParent(transform, false);
        var dimRt = (RectTransform)_root.transform;
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;
        _root.GetComponent<Image>().color = DIM_BG;

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(_root.transform, false);
        var rt = (RectTransform)panel.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        SetupPanel(rt, WIDTH);

        _titleText = Text(panel.transform, "", 30);
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.alignment = TextAlignmentOptions.Center;

        _bodyText = Text(panel.transform, "", 15);
        _bodyText.alignment = TextAlignmentOptions.Center;

        Btn(panel.transform, "계속 보기", 0, OnContinueClicked, BTN);
    }

    public void Show(bool victory, string body)
    {
        _titleText.text = victory ? "정화 완료 — 승리" : "기지 전멸 — 패배";
        _titleText.color = victory ? VICTORY_COLOR : DEFEAT_COLOR;
        _bodyText.text = body;
        _root.SetActive(true);
    }

    public void Hide()
    {
        if (_root != null) _root.SetActive(false);
    }

    private void OnContinueClicked()
    {
        Hide();
        TimeManager.instance?.Resume();
    }
}
