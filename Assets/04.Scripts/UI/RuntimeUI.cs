using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 코드로 짓는 창(계획 창·침대 배정 창 등)의 공용 부품 — 행·열·글자·버튼과 색.
/// 사용하는 쪽에서 <c>using static RuntimeUI;</c>.
/// </summary>
public static class RuntimeUI
{
    public const float ROW_H = 30f;

    public static readonly Color PANEL_BG = new Color(0.11f, 0.12f, 0.16f, 0.97f);
    public static readonly Color ROW_BG   = new Color(0.16f, 0.17f, 0.22f, 1f);
    public static readonly Color BTN      = new Color(0.2f, 0.22f, 0.28f, 1f);
    public static readonly Color BTN_ON   = new Color(0.086f, 0.373f, 0.502f, 1f);
    public static readonly Color BTN_OFF  = new Color(0.32f, 0.16f, 0.16f, 1f);
    public static readonly Color DIM      = new Color(0.6f, 0.6f, 0.65f, 1f);
    public static readonly Color BTN_DIM  = new Color(0.15f, 0.16f, 0.2f, 1f);
    public static readonly Color ROW_SEL  = new Color(0.07f, 0.26f, 0.35f, 1f);


    /// <summary>패널 배경 + 세로 배치 + 내용 높이 맞춤</summary>
    public static void SetupPanel(Component panel, float width)
    {
        var rt = (RectTransform)panel.transform;
        rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
        if (!panel.TryGetComponent(out Image bg)) bg = panel.gameObject.AddComponent<Image>();
        bg.color = PANEL_BG;

        if (!panel.TryGetComponent(out VerticalLayoutGroup vlg)) vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 10, 12);
        vlg.spacing = 6;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        if (!panel.TryGetComponent(out ContentSizeFitter fitter)) fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    public static void Clear(Transform t)
    {
        // Destroy는 프레임 끝에 지워지므로 먼저 떼어 내 — 같은 프레임의 레이아웃에 옛 줄이 끼지 않게
        for (int i = t.childCount - 1; i >= 0; i--)
        {
            var child = t.GetChild(i).gameObject;
            child.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(child);
        }
    }

    public static Transform Row(Transform parent, float spacing)
    {
        var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var h = go.GetComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;
        go.GetComponent<LayoutElement>().minHeight = ROW_H;
        return go.transform;
    }

    public static Transform Column(Transform parent, float spacing)
    {
        var go = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup));
        go.transform.SetParent(parent, false);
        var v = go.GetComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return go.transform;
    }

    public static TextMeshProUGUI Text(Transform parent, string s, float size, bool flexible = false)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = s;
        t.fontSize = size;
        t.color = Color.white;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        var le = go.GetComponent<LayoutElement>();
        le.minHeight = size + 6;
        if (flexible) le.flexibleWidth = 1;
        return t;
    }

    public static Button Btn(Transform parent, string label, float width, UnityAction onClick, Color color)
    {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        var b = go.GetComponent<Button>();
        b.onClick.AddListener(onClick);
        var le = go.GetComponent<LayoutElement>();
        if (width > 0) le.preferredWidth = le.minWidth = width;
        le.minHeight = ROW_H;

        var t = Text(go.transform, label, 14);
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        var trt = (RectTransform)t.transform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        return b;
    }

}
