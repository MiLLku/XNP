using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 메시지 로그 레터 1개(HUD). MessageLogUI가 풀로 관리한다.
/// 클릭 시 상세 팝업을 연다(콜백 위임).
///
/// 계층 예:
///   MessageLogItem [Button]
///   ├── Accent (Image, 좌측 색 바)
///   ├── Icon   (Image, 선택)
///   └── Title  (TMP_Text)
/// </summary>
public class MessageLogItem : MonoBehaviour
{
    [SerializeField] private Image accent;       // 좌측 색 바 (LetterType 색)
    [SerializeField] private Image iconImage;    // 선택
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private Button button;

    private Letter _letter;
    private Action<Letter> _onClick;

    private void Awake()
    {
        if (button != null) button.onClick.AddListener(() => _onClick?.Invoke(_letter));
    }

    public void Bind(Letter letter, Color accentColor, Action<Letter> onClick)
    {
        _letter = letter;
        _onClick = onClick;

        if (titleText != null) titleText.text = letter.title;
        if (accent != null) accent.color = accentColor;

        if (iconImage != null)
        {
            bool hasIcon = letter.icon != null;
            iconImage.enabled = hasIcon;
            if (hasIcon) iconImage.sprite = letter.icon;
        }

        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }
}
