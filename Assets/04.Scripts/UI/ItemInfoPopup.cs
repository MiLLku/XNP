using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 아이템 정보 오버레이 — 이름·설명·파종조건과 파종 버튼.
///
/// 바닥 아이템 클릭(InteractionManager)과 인벤토리 행 클릭(ResourceInventoryUI)이 함께 씁니다.
/// <b>위치</b>: 기준 영역의 오른쪽에 붙이고, 화면 오른쪽에 공간이 없으면 왼쪽에 붙입니다.
/// ESC 또는 X 버튼으로 닫습니다.
/// </summary>
public class ItemInfoPopup : BasePanel
{
    private const float GAP = 8f;

    [Header("UI 요소")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private TextMeshProUGUI conditionText;
    [SerializeField] private Button sowButton;
    [SerializeField] private Button closeButton;

    private ItemData item;

    /// <summary>표시 중인 아이템</summary>
    public ItemData CurrentItem => item;

    private void Awake()
    {
        // 직렬화 참조는 가짜 null일 수 있어 ?. 대신 명시 비교
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (sowButton != null) sowButton.onClick.AddListener(OnSowClicked);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    #region 열기

    /// <summary>바닥 아이템 기준으로 엽니다 (월드 좌표를 화면 영역으로 바꿔 붙임).</summary>
    public static void ShowForWorld(ItemData item, Vector3 worldPos)
    {
        var cam = Camera.main;
        if (cam == null) return;

        Vector2 p = cam.WorldToScreenPoint(worldPos);
        float half = Mathf.Abs(cam.WorldToScreenPoint(worldPos + Vector3.right * 0.5f).x - p.x);
        Show(item, new Rect(p.x - half, p.y - half, half * 2f, half * 2f));
    }

    /// <summary>UI 요소(인벤토리 행 등) 기준으로 엽니다.</summary>
    public static void ShowForRect(ItemData item, RectTransform anchor)
    {
        var canvas = anchor.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

        var corners = new Vector3[4];
        anchor.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
        Show(item, Rect.MinMaxRect(min.x, min.y, max.x, max.y));
    }

    /// <summary>화면 좌표 영역 옆에 엽니다.</summary>
    public static void Show(ItemData item, Rect screenAnchor)
    {
        if (item == null || UIManager.instance == null) return;

        var popup = UIManager.instance.GetPanel<ItemInfoPopup>(UIPanelType.ItemInfo);
        if (popup == null)
        {
            Debug.LogWarning("[ItemInfoPopup] UIManager에 ItemInfo 패널이 등록되지 않았습니다.");
            return;
        }

        popup.Fill(item);
        UIManager.instance.ShowPanel(UIPanelType.ItemInfo);
        popup.PlaceNextTo(screenAnchor);
    }

    #endregion

    #region 내용

    private void Fill(ItemData data)
    {
        item = data;

        if (nameText != null) nameText.text = data.itemName;
        if (descriptionText != null)
            descriptionText.text = string.IsNullOrWhiteSpace(data.description) ? "설명 없음" : data.description;

        if (conditionText != null)
        {
            conditionText.text = BuildConditionText(data);
            conditionText.gameObject.SetActive(conditionText.text.Length > 0);
        }

        if (sowButton != null) sowButton.gameObject.SetActive(data.IsSowable);
    }

    /// <summary>파종 가능한 아이템의 성장 조건 문구. 아니면 빈 문자열.</summary>
    public static string BuildConditionText(ItemData data)
    {
        if (data == null || !data.IsSowable) return "";

        var def = data.plantEntity;
        var lines = PlantConditionChecker.Describe(def.growthConditions);

        var prefabPlant = def.prefab != null ? def.prefab.GetComponentInChildren<PlantBase>() : null;
        string space = prefabPlant != null ? prefabPlant.DescribeSpaceNeed() : null;
        if (!string.IsNullOrEmpty(space)) lines.Add(space);

        return lines.Count == 0 ? "성장 조건: 없음" : "성장 조건: " + string.Join(", ", lines);
    }

    #endregion

    #region 배치

    /// <summary>기준 영역 오른쪽 우선, 공간이 없으면 왼쪽. 세로는 기준 위쪽에 맞추고 화면 안으로 자릅니다.</summary>
    private void PlaceNextTo(Rect anchor)
    {
        var rt = (RectTransform)transform;
        var parent = rt.parent as RectTransform;
        var canvas = GetComponentInParent<Canvas>();
        if (parent == null || canvas == null) return;

        Canvas.ForceUpdateCanvases();
        float scale = canvas.scaleFactor;
        Vector2 size = rt.rect.size * scale; // 화면 픽셀 크기

        bool right = anchor.xMax + GAP + size.x <= Screen.width;
        float x = right ? anchor.xMax + GAP : anchor.xMin - GAP - size.x;
        x = Mathf.Clamp(x, 0f, Mathf.Max(0f, Screen.width - size.x));
        float top = Mathf.Clamp(anchor.yMax, size.y, Screen.height);

        // 좌상단 피벗 + 부모 중앙 앵커 → anchoredPosition = 부모 로컬 좌표 - 부모 중심
        rt.pivot = new Vector2(0f, 1f);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(x, top), cam, out Vector2 local))
            rt.anchoredPosition = local - parent.rect.center;
    }

    #endregion

    private void OnSowClicked()
    {
        if (item == null || !item.IsSowable) return;
        if (InteractionManager.instance != null) InteractionManager.instance.BeginSow(item);
        Close();
    }

    private void Close()
    {
        if (UIManager.instance != null) UIManager.instance.HidePanel(UIPanelType.ItemInfo);
    }
}
