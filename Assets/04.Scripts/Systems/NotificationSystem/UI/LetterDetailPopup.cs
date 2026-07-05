using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 레터 상세 팝업. 클릭한 레터의 제목·본문·아이콘을 보여주고, 확인 시 레터를 제거한다.
///
/// 정보형 1차: 효과는 EventManager가 이미 적용했으므로 여기선 표시·확인만.
/// (2차 확장: sourceEvent.HasChoices면 선택지 버튼을 생성해 효과를 지연 적용 — 아래 주석 지점.)
///
/// UIManager에 UIPanelType.LetterDetail로 등록해 사용한다.
/// </summary>
public class LetterDetailPopup : BasePanel
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Image iconImage;
    [SerializeField] private Button confirmButton;

    private Letter _current;

    private void Awake()
    {
        if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);
    }

    /// <summary>레터 내용을 채우고 팝업을 연다.</summary>
    public void Show(Letter letter)
    {
        _current = letter;

        if (titleText != null) titleText.text = letter.title;
        if (bodyText != null)  bodyText.text  = letter.body;

        if (iconImage != null)
        {
            bool hasIcon = letter.icon != null;
            iconImage.enabled = hasIcon;
            if (hasIcon) iconImage.sprite = letter.icon;
        }

        // ── 2차 확장 지점 ──
        // if (letter.sourceEvent != null && letter.sourceEvent.HasChoices)
        //     => 선택지 버튼 생성 후 EventManager.instance.MakeChoice(letter.sourceEvent, idx) 호출(효과 지연 적용)

        OnOpen();
    }

    /// <summary>확인 — 레터를 제거하고(필요 시 게임 재개) 팝업을 닫는다.</summary>
    private void Confirm()
    {
        if (_current != null)
        {
            NotificationManager.instance?.DismissLetter(_current);
            _current = null;
        }
        OnClose();
    }
}
