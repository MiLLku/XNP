using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 가이드라인 패널 (좌상단, 보기 토글 바 아래).
/// 지금 장의 제목과 목표 목록을 보여 줍니다. 제목을 누르면 목표 목록을 접고, 보기 토글 바의 '안내' 버튼으로 패널 전체를 끕니다.
///
/// 켜고 끄는 설정은 게임이 아니라 <b>플레이어의 것</b>이라 세이브가 아닌 PlayerPrefs에 둡니다.
/// </summary>
public class GuidelineUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Button headerButton;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private GameObject body;
    [Tooltip("목표 한 줄의 원본. 목표 수만큼 복제해서 씁니다 (원본은 숨깁니다).")]
    [SerializeField] private TMP_Text lineTemplate;

    private const string VISIBLE_PREF = "Guideline.Visible";
    private const float REFRESH_INTERVAL = 0.5f;
    private const string DONE_COLOR = "#7CD67C";

    private readonly List<TMP_Text> lines = new List<TMP_Text>();
    private bool collapsed;
    private float timer;

    /// <summary>패널을 보일지. 모든 게임에 공통으로 적용되는 플레이어 설정입니다.</summary>
    public static bool Visible
    {
        get => PlayerPrefs.GetInt(VISIBLE_PREF, 1) == 1;
        set { PlayerPrefs.SetInt(VISIBLE_PREF, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    private void Awake()
    {
        headerButton?.onClick.AddListener(() => { collapsed = !collapsed; Refresh(); });
        if (lineTemplate != null) lineTemplate.gameObject.SetActive(false);
    }

    private void OnEnable() => Refresh();

    private void Update()
    {
        timer += Time.unscaledDeltaTime;
        if (timer < REFRESH_INTERVAL) return;
        timer = 0f;
        Refresh();
    }

    private void Refresh()
    {
        var manager = GuidelineManager.instance;
        bool show = Visible && manager != null && manager.ChapterCount > 0;
        if (panel != null && panel.activeSelf != show) panel.SetActive(show);
        if (!show) return;

        GuidelineChapter chapter = manager.CurrentChapter;
        if (chapter == null)
        {
            if (titleText != null) titleText.text = "안내를 모두 마쳤습니다";
            if (body != null) body.SetActive(false);
            return;
        }

        if (titleText != null)
            titleText.text = $"{manager.CurrentChapterIndex + 1}/{manager.ChapterCount}  {chapter.title}  {(collapsed ? "+" : "-")}";

        if (body != null) body.SetActive(!collapsed);
        if (collapsed || lineTemplate == null) return;

        for (int i = 0; i < chapter.objectives.Count; i++)
        {
            if (i >= lines.Count) lines.Add(Instantiate(lineTemplate, lineTemplate.transform.parent));

            GuidelineObjective objective = chapter.objectives[i];
            string text = objective.text;
            bool done = manager.IsCompleted(objective);
            if (!done)
            {
                string progress = manager.GetProgressLabel(objective);
                if (progress.Length > 0) text += $" ({progress})";
            }

            lines[i].text = done ? $"<color={DONE_COLOR}><s>{text}</s></color>" : text;
            lines[i].gameObject.SetActive(true);
        }
        for (int i = chapter.objectives.Count; i < lines.Count; i++)
            lines[i].gameObject.SetActive(false);
    }
}
